# Why WowClassicGrindBot forks DotRecast

The bot needs a navmesh for a 34,000-yard-wide world, baked on the player's own
machine, on first visit to an area, while the game client is running on the same
box. Nothing about that is a normal recast workload - the usual one is "bake
offline on a build server, ship the result".

That constraint is what drove every change below. This page walks the fork
commit by commit, with what was measured and how.

Fork: [`Xian55/DotRecast @ wow-mods`](https://github.com/Xian55/DotRecast/tree/wow-mods)

---

## Read this before the numbers

Three things make naive benchmark numbers misleading here, and all three bit us
during this work.

**Run-to-run spread is large.** The dev box swings roughly ±10–15% depending on
what else is running. The clearest proof is in our own data: the `poolbatch` and
`rehistory` runs are the *same code* and differ by 16% on total bake time
(1575 ms vs 1823 ms). So a single before/after pair across two sittings proves
nothing. Per-change numbers below come from **interleaved A/B** runs on one idle
machine - see "On how these were measured" for the protocol and the specific
ways the naive version misled us.

**A stale build silently poisons everything.** DotRecast is a submodule and is
not in `MasterOfPuppets.sln`, so a solution-level Release build copies the
*Debug* DotRecast into the benchmark output. Every stage then reads 3–5× slower,
including stages nothing touched. This cost us an entire invalidated measurement
round. There is now a guard script that refuses to benchmark in that state.

**Correctness is checked separately from speed.** Every tile is hashed
(SHA-256 of the serialized `DtMeshData`) across a 6-tile corpus covering open
terrain, a WMO city, an indoor area and water. Unless a change is deliberately
geometry-affecting, **all six hashes must be identical** - otherwise every user
would have to rebake. All 26 commits below pass that gate.

A passing hash gate means a change is *legal*, not that it is *good*. One
change passed all six hashes and was 50% slower; see "What didn't work".

---

## The measured result

6-tile corpus, warm bake. `baseline` is upstream DotRecast plus the three
pre-existing fork commits. The work came in three rounds: CPU time, then
allocation, then memory layout.

| stage | baseline | now | change |
|---|---|---|---|
| `RASTERIZE_TRIANGLES` | 779.0 | 177.9 | **−77%** |
| `MEDIAN_AREA` | 253.3 | 57.0 | **−77%** |
| `BUILD_COMPACTHEIGHTFIELD` | 371.3 | 100.8 | **−73%** |
| `BUILD_CONTOURS_TRACE` | 46.9 | 16.1 | **−66%** |
| `BUILD_POLYMESHDETAIL` | 293.3 | 125.4 | −57% |
| `BUILD_CONTOURS` | 70.3 | 33.6 | −52% |
| `BUILD_REGIONS_WATERSHED` | 351.2 | 187.3 | −47% |
| `BUILD_REGIONS` | 403.1 | 218.8 | −46% |
| `BUILD_REGIONS_EXPAND` | 201.5 | 114.7 | −43% |
| `ERODE_AREA` | 105.3 | 67.7 | −36% |
| `BUILD_POLYMESH` | 49.3 | 35.2 | −29% |
| `BUILD_DISTANCEFIELD` | 135.8 | 109.0 | −20% |
| `FILTER_BORDER` | 117.8 | 152.0 | **+29%** |

| overall | baseline | now |
|---|---|---|
| total warm bake | 2515 ms | **1196 ms (−52%)** |
| allocation | 751 MB | **85 MB (−89%)** |
| gen0 collections | 116 | **12 (−90%)** |

Allocation and GC counts are the most trustworthy figures here - they are far
less sensitive to machine state than wall-clock.

`FILTER_BORDER` is the one stage that ends up *slower* than upstream. That is a
deliberate, documented trade in [`66300b0`](https://github.com/Xian55/DotRecast/commit/66300b0) - see below.

### On how these were measured

Everything after the allocation round was measured as **three interleaved A/B
pairs run in drift-cancelling order** (new, old, old, new, new, old) on an
otherwise idle machine, quoting all three samples rather than a mean. That
protocol exists because the naive one lied to us repeatedly:

- A single before/after pair on a busy machine reported a uniform +11% on
  stages the change could not touch. It was ambient load.
- Running new-then-old each round made "old" always the later, slower slot on a
  thermally drifting box, manufacturing a win out of nothing.
- Even with clean separation between the two sets, stages that the change
  provably cannot reach still moved by 4-6%. Non-overlapping samples are not
  proof of causation; a mechanism is required as well.

---

## The commits

### Allocation removal

#### [`e6472d7`](https://github.com/Xian55/DotRecast/commit/e6472d734d2f63917cb44d52a20c28e72e47382c) - `core: add a Span<int> overload to InsertSort`
Enables the next one. The array overload forwards to it, so nothing changes.

#### [`1b555ad`](https://github.com/Xian55/DotRecast/commit/1b555ad4374df307715e1e574b2a67d16daaa963) - `recast: build polygon mesh adjacency without per-edge objects`
`BuildMeshAdjacency` allocated an `RcEdge` plus its three `int[2]` fields per
edge - four objects each, on the order of 80k per tile. Replaced with one flat
`int[]` of stride 6.
→ `BUILD_POLYMESH` −11…−17%, `BUILD_CONTOURS` −15…−18%.

#### [`a377ba4`](https://github.com/Xian55/DotRecast/commit/a377ba4f7fa0b8719193e7f365170dd94493bb19) - `recast: stack-allocate the scratch verts in contour hole merging`
`IntersectSegContour` and `InCone` each allocated a 16-int array per call, and
`MergeRegionHoles` calls them O(outline × hole) times.

#### [`d549b24`](https://github.com/Xian55/DotRecast/commit/d549b2455c4989714aa0e784e1a1b4c77018ca15) - `recast: read direction offset tables from the data section`
`GetDirOffsetX/Y` are read tens of millions of times per tile. As
`static ReadOnlySpan<sbyte>` with an inline initializer they live in the
assembly data section - no static field load, foldable bounds check.

#### [`4a313e2`](https://github.com/Xian55/DotRecast/commit/4a313e2420977722fd80802e6ab91c63a1baea8f) - `recast: make RcCompactSpanBuilder a value type`
Pure mutable scratch that was a class, costing one heap object per compact span
(~300k per tile) in the compact build, and again per span in every region
write-back. `SetCon` now takes it by `ref`.

#### [`60a1f2e`](https://github.com/Xian55/DotRecast/commit/60a1f2e50e28289014f0d0cf5cee666f4c2b983e) - `recast: drop LINQ from the compact heightfield build`
Two LINQ pipelines ran once per compact span each - ~300k iterator steps plus a
delegate invocation, for what is an array fill and an array copy.

#### [`99b2b15`](https://github.com/Xian55/DotRecast/commit/99b2b152b405f9ee5d7c960fed707637c4a66e8c) - `recast: hoist per-column invariants out of the neighbour connection loop`
The four neighbour cells depend on the column, not the span, but were resolved
per span: four bounds tests, four table reads, four multiplied `cells[]` loads,
×300k spans. Also `MathF.Abs`→`Math.Abs` on ints in the innermost loop, and an
early break once a neighbour climbs past `walkableClimb` (spans are stored
bottom-up, so no later one can qualify).

#### [`c54cfa0`](https://github.com/Xian55/DotRecast/commit/c54cfa0e0e16cc54eeefe46a87d1df8fb02064f7) - `recast: reuse one scratch window in the median area filter`
A 9-element `int[]` was allocated per compact span for a window that gets
overwritten anyway. Now one `stackalloc` reused across the pass.

### Algorithmic

#### [`c5f21c5`](https://github.com/Xian55/DotRecast/commit/c5f21c5ca599c7673a0b7243145e3fecc53f4e23) - `recast: skip the median sort when the 3x3 neighbourhood is uniform`
All nine slots start as copies of the centre area and are only overwritten where
a neighbour differs. On open terrain almost every neighbourhood is a single area
id, so `InsertSort` was sorting nine identical values - up to 36 compare-and-shift
steps - to pick a value already known.

> **Isolated interleaved A/B**, corpus `MEDIAN_AREA`:
> forced sort **98.6 / 100.0 ms** → fast path **82.2 / 73.1 ms**

#### [`040531b`](https://github.com/Xian55/DotRecast/commit/040531be076515bcbd4c7cf3dd0495a62ac1e9b6) - `recast: fold erode's fill and threshold passes into their neighbours`
Three full sweeps of the span set become two. The cells partition every span
index exactly once, so the `Array.Fill(255)` folds into the boundary pass; and
pass 2 finalises `distanceToBoundary[i]` per iteration and never reads `areas`,
so the thresholding folds in there. ~2.4 MB less array traffic per tile.

#### [`3351e31`](https://github.com/Xian55/DotRecast/commit/3351e3193bcee4c2b20c3789987f028bf6bd3e4e) - `recast: fold the distance field's init and max passes into their neighbours`
Same shape: the init-to-`0xffff` sweep folds into boundary marking, and the max
reduction folds into pass 2.

#### [`1ec623d`](https://github.com/Xian55/DotRecast/commit/1ec623d5ad00f0db0d9a13a0f7a4992e52db1a8d) - `core+recast: time the watershed level bucketing and the final expansion`
Instrumentation only. The `EXPAND` timer opens and closes *inside* the level
loop, so the final expansion is untimed, and the `DIVIDE_TO_LEVELS` calls are
commented out with no label defined - leaving ~half of watershed unattributed.

> This immediately attributed a **155 ms blind spot**: level bucketing is
> **183 ms of the 383 ms watershed total (48%)**, while the final expansion that
> looked pathological is **4.6 ms (1%)**. [`d5d4914`](https://github.com/Xian55/DotRecast/commit/d5d491408ca9684c89dab68a82c2a7dfe40a0d06) exists because of this one.

#### [`3c3745f`](https://github.com/Xian55/DotRecast/commit/3c3745f7e8b37be39e8733d7c51a945d9c476b83) - `recast: reuse one dirty-entry list across watershed expansion`
`ExpandRegions` allocated its write-back list per call - ~250 calls per tile,
regrowing to stack size each time.

#### [`d5d4914`](https://github.com/Xian55/DotRecast/commit/d5d491408ca9684c89dab68a82c2a7dfe40a0d06) - `recast: bucket watershed cells by level instead of rescanning every column`
`SortCellsByLevel` walks the entire heightfield to collect one 8-level band, and
the level loop calls it once per wrap: ~30 full sweeps, ~15M span visits per
tile. Now bucketed by level window once up front.

Safe because emission order is preserved exactly - the index is built in scan
order, which is ascending span index, and every list stays index-sorted through
the merges. Stack order matters: `FloodRegion` assigns region ids in it.

> **Isolated interleaved A/B**:
> `BUILD_REGIONS_LEVELS` **156.9 / 147.4 ms** → **61.9 / 39.9 ms**
> total warm bake **1815 / 1816 ms** → **1746 / 1718 ms**
>
> Note `EXPAND` *rises* ~45 ms - the old full scan was inadvertently prefetching
> `srcReg`/`areas` for it. Net is still ≈−4%.

#### [`5c94bc0`](https://github.com/Xian55/DotRecast/commit/5c94bc02501602bc4ab0eef162d3bc324631b157) - `recast: pool the large per-tile scratch arrays`
Four large-object allocations per tile - `tempSpans` ~5 MB, `distanceToBoundary`
~1.2 MB, `srcReg` and `srcDist` ~1.2 MB each - now rented from `ArrayPool`.
`tempSpans` needs `reg`/`con` written explicitly since it no longer arrives
zeroed; `srcReg`/`srcDist` genuinely need clearing because `srcReg == 0` is the
"no region yet" marker.

### Parallelism

#### [`9f46bb0`](https://github.com/Xian55/DotRecast/commit/9f46bb0ce6c3f7e5ccc428ff00cd3245b8b8b1cc) - `recast: recycle spans through a pool during rasterization`
`AddSpan` called `new RcSpan()` per (triangle, cell) incidence - 3×10⁵ to 1.5×10⁶
objects, 12–60 MB per tile - and the merge path dropped absorbed spans instead of
reusing them. Upstream already declared a pool, freelist, `AllocSpan` and
`FreeSpan`, but **nothing ever called them**; the mechanism was dead code.

#### [`0d8641b`](https://github.com/Xian55/DotRecast/commit/0d8641bd9aedcf9742c7934a7f38332c1646eabe) - `recast: rasterize triangles in parallel z-bands`
Splits the heightfield into row bands, one thread each, each with its own
allocator so no locking is needed. A triangle only touches columns in its own z
range and each band clamps to its own rows, so no two bands write the same
column. Falls back to sequential below 2048 triangles.

#### [`51b96e6`](https://github.com/Xian55/DotRecast/commit/51b96e602b803de7d1311a10463481e1dfe2435c) - `recast: build the polygon detail mesh in parallel`
The per-polygon loop reads its inputs read-only and writes only per-iteration
scratch. Each worker gets its own scratch set; results are collected by polygon
index and concatenated in order, so numbering matches the sequential version.
→ `BUILD_POLYMESHDETAIL` −41…−48%.

[`0d8641b`](https://github.com/Xian55/DotRecast/commit/0d8641bd9aedcf9742c7934a7f38332c1646eabe) and [`51b96e6`](https://github.com/Xian55/DotRecast/commit/51b96e602b803de7d1311a10463481e1dfe2435c) buy **single-tile latency, not throughput** - they compose
poorly with a caller that already bakes several tiles concurrently. Worth gating
behind a switch if you do that.

### Memory layout

This round exists because a native comparison (below) showed rasterization was
the largest remaining gap against C++, and an allocation profile showed the same
three stages dominating managed allocation. Both had one root cause: DotRecast
allocates where C++ owns storage.

#### [`66300b0`](https://github.com/Xian55/DotRecast/commit/66300b0) - `recast: store heightfield spans as pooled value types`
`RcSpan` was a class, so `AddSpan` produced one ~40-byte heap object per
(triangle, cell) incidence - 3×10⁵ to 1.5×10⁶ per tile. [`9f46bb0`](https://github.com/Xian55/DotRecast/commit/9f46bb0ce6c3f7e5ccc428ff00cd3245b8b8b1cc) had already
revived the dead pool; this goes the rest of the way to the C++ layout: 16-byte
value-type spans in fixed-size pages, addressed by `int` index.
`RcHeightfield.spans` becomes `int[]` column heads and `RcSpan.next` an index.

Pages are never resized or moved, so an index stays valid however much the store
grows - which is what lets each parallel rasterization band keep its own bump
allocator and free list, taking the page lock only to claim a fresh page.

> `RASTERIZE_TRIANGLES` **554 → 246 ms (−56%)**, total bake −18%,
> allocation −18%, gen0 **−59%**

**This is also the one regression in the fork.** `FILTER_BORDER` goes
**118 → 162 ms**. A span field read is now three dependent loads - `pages` field,
page array, element - where a reference was one, and the ledge filter walks four
neighbour columns per span. Flattening the store into one column-ordered array
afterwards would recover it, at the cost of a multi-megabyte large-object
allocation per tile, which is the exact pressure the commit exists to remove.
The trade is +330 ms elsewhere for −44 ms here.

#### [`207489c`](https://github.com/Xian55/DotRecast/commit/207489c) - `recast: bucket the watershed level index with a counting sort`
The level index from [`d5d4914`](https://github.com/Xian55/DotRecast/commit/d5d491408ca9684c89dab68a82c2a7dfe40a0d06) kept one `List` per level window. Every
unassigned walkable span produces an entry - 2×10⁵ to 6×10⁵ per tile - so those
lists grew by repeated doubling, copying the whole bucket each time. That was the
largest single allocator left in the region build.

The windows are write-once, so: count, prefix-sum the offsets, then place. One
exactly-sized pooled array instead of N growing ones. The placement pass walks
spans in the original order, so each window stays sorted by span index, which the
merges depend on.

> allocation **249 → 191 MB (−24%)**

#### [`2bc9163`](https://github.com/Xian55/DotRecast/commit/2bc9163) - `recast: pool the compact heightfield's bulk arrays`
`cells`, `spans`, `dist` and `areas` are ~10 MB per tile and dead once the meshes
are built - but several tiles bake concurrently, so that garbage is all live at
once and most of it lands on the large object heap.
`RcCompactHeightfield` becomes disposable and rents the four arrays.

Disposing is optional - skip it and they are collected as before, which is what
keeps this source-compatible. Two invariants a pooled array forces, both now
documented on the type:

- it may be **longer than requested**, so nothing may bound a loop by `Length`.
  Audited: every consumer already bounds by `spanCount` or `width * height`.
- it arrives **dirty**. `cells` is the only array not written in full - columns
  with no spans are left at `index=0, count=0` - so it is cleared explicitly.

> allocation **191 → 85 MB (−55%)**. `ERODE_AREA`, `MEDIAN_AREA` and the whole
> distance field now allocate **nothing** in steady state.

### Instruction-level

With allocation handled, the native comparison put the remaining gaps in the
contour and polygon builds. Both turned out to be plain wasted work.

#### [`8174bf3`](https://github.com/Xian55/DotRecast/commit/8174bf3) - `recast: tighten the contour boundary-marking sweep`
`RC_TIMER_BUILD_CONTOURS_TRACE` does not time contour tracing - it wraps the
flat sweep that marks which span edges border another region, visiting every
span and testing four directions. `chf` is a class, so each `chf.spans[..]` is a
field load the JIT cannot keep across the store to `flags[i]`; the loop did six
per span. `reg` was re-read five times for a value that does not change. And the
neighbour cell was addressed as `chf.cells[ax + ay * w]` after reading both
direction tables, where the four neighbours are just the current cell index
plus −1, +w, +1, −w.

> `BUILD_CONTOURS_TRACE` **39.1/39.5 → 17.9/17.2 ms (−56%)**,
> `BUILD_CONTOURS` −37%

#### [`a6c10b7`](https://github.com/Xian55/DotRecast/commit/a6c10b7) - `recast: drop the integer divides and repeated determinants from the polygon build`
`GetPolyMergeValue` wrapped indices with `% na` / `% nb`. Neither bound is a
constant, so each is a hardware integer divide - ~25 cycles, not pipelined - in
a scan called O(npolys³) times per contour. Every index is already below twice
its bound, so a compare-and-subtract is exact.

`Intersect` decided segment crossing from four determinants but reached them via
`Collinear`, then `Left`, then `Between` - evaluating each up to three times,
which made `Area2` the hottest routine in the polygon build.

> `BUILD_POLYMESH` **58.9/56.4 → 47.6/30.5 ms**

#### [`1d1a8ae`](https://github.com/Xian55/DotRecast/commit/1d1a8ae) - `recast: trim the per-vertex work in the contour walk`
`GetCon` called twice per direction in six places, `chf` arrays reloaded per
access, `DistancePtSeg` recomputing the segment's own terms for every raw point
tested against it, and a four-field point insert doing four `List.Insert` calls
that each memmove the whole tail.

> `BUILD_CONTOURS_WALK` **12.3/13.6 → 11.6/9.7 ms**. Only the walk moved beyond
> the noise - about 2 ms of a 1300 ms bake. Kept because the result is
> consistent, not because it is significant.

### Narrowing the value types

This is the round the "What C++ actually says" section below is about. Four
candidates, all pure layout changes; **two paid and two did not**, and the two
that did not were reverted after being measured.

#### [`de7f32a`](https://github.com/Xian55/DotRecast/commit/de7f32a) - `recast: store compact heightfield areas one byte per span` ✅
`chf.areas` held an `int` per span where C++ holds an `unsigned char`. Area ids
are small - `RC_WALKABLE_AREA` is 63 - so three of every four bytes were padding
being dragged through the cache. It matters because `ExpandRegions` reads it once
per span per direction, making it the array's heaviest consumer.

> `BUILD_REGIONS_EXPAND` **−16%**, `BUILD_REGIONS_WATERSHED` **−12%**,
> `BUILD_REGIONS` **−10%** - no sample overlapping between the two sets.
>
> `MEDIAN_AREA`, which sweeps `areas` in a 3×3 stencil and was the stage we
> predicted would win most, **did not move**.

#### [`eae9562`](https://github.com/Xian55/DotRecast/commit/eae9562) - `recast: pack the compact span into eight bytes` ✅
`RcCompactSpan` was four ints; C++ packs `ushort y; ushort reg; uint con:24, h:8`.
At hundreds of thousands of spans per tile this is the largest single part of the
working set. `con` and `h` became properties over a packed `uint`, so **no call
site changed**.

The widths are ones the algorithm already assumes: `RC_BORDER_REG` is `0x8000`,
so a region id must fit in fifteen bits anyway, and `con` is four six-bit slots.

`h` is the one real semantic change - it now saturates at 255 like C++ instead of
at `RC_SPAN_MAX_HEIGHT`. Our corpus reaches 1,048,558, so this genuinely
truncates. It is safe because `h` is clearance *above* a span and no walkability
test can distinguish 255 voxels of headroom from more. Verified rather than
argued: applying the clamp alone, with the field widths untouched, left all six
hashes identical.

> `BUILD_REGIONS_EXPAND` **−12%**, `BUILD_CONTOURS_TRACE` **−8%**,
> `BUILD_DISTANCEFIELD_BLUR` **−8%**, `BUILD_DISTANCEFIELD` **−6%**,
> total bake **−3%**. None of those four overlap.
>
> `BUILD_POLYMESH` went the *other* way, 32.5 → 44.3 ms, and it never reads the
> compact heightfield. Most likely contention - tiles bake concurrently, so
> speeding up the memory-bound stages puts more of them in flight at once.

#### `chf.dist` → `ushort[]` ❌ reverted
Distance values saturate at `0xffff` by construction, so the width is free on
paper. Measured, the distance field got **worse**: `BUILD_DISTANCEFIELD` +2.7%,
`_DIST` +1.9%, `_BLUR` +4.0%, none overlapping. 16-bit loads need zero-extension
and stores need truncation while the arithmetic still happens in `int`; the
1.2 MB saved bought nothing.

#### `RcCompactCell` → 4 bytes ❌ reverted
C++ packs `index:24, count:8`. Measured: `BUILD_CONTOURS_TRACE` +8.8%,
`BUILD_DISTANCEFIELD` +6.5%, `_BLUR` +7.0%, total +0.3%. `index` and `count` now
need a shift and mask on every access, and the stencil passes read five cells per
span.

---

## What didn't work

**A monotone cursor for the ledge filter.** `FilterLedgeSpans` restarts its
neighbour walk at the column head for every span, making it O(S²) in stacked-span
depth - which is why WMO tiles cost 4× what their triangle count suggests. The
fix was provably output-identical and **all six tile hashes passed**.

It was also **50% slower**:

| | `FILTER_BORDER`, corpus |
|---|---|
| upstream | 117.3 / 122.3 ms |
| monotone cursor | 175.3 / 189.2 ms |

Real geometry has S ≈ 1.2–3.5, so the quadratic never bites, while the per-column
cursor setup taxes all 260k columns. Reverted - which is why `FILTER_BORDER` was
untouched through round 1, and why it is the one stage still open.

**Copying the span struct into a local.** After spans became value types in a
paged store, every `store[span].field` is three dependent loads where a reference
was one, and `FilterLedgeSpans` reads four fields per span. Hoisting one copy per
span looked certain. Measured: no change on the target, and both *smaller*
filters got consistently ~15% worse. The JIT was already common-subexpression-
eliminating the repeated lookups, and a 16-byte struct copy costs more than
re-reading a hot cache line.

**Narrowing `chf.dist` and `RcCompactCell`.** Both are exactly the change that
made `areas` and `RcCompactSpan` pay, applied to smaller arrays - and both lost.
See the next section; this is the one where we also falsified our own
explanation for *why* the winners won.

**Two conclusions that were wrong in this document.** An earlier version said the
narrowing was disproven and would not be attempted; two of the four turned out to
be among the largest wins here. It also explained the wins with an L3 cache
cliff, which a direct test then falsified. Both are corrected in place below
rather than quietly deleted, because the way they were wrong is the useful part.

The lesson is the one worth taking from this whole exercise: **the correctness
gate tells you a change is allowed; only measurement tells you it's worth
shipping** - and a plausible mechanism is not measurement. Seven confident
diagnoses died during this work, including two of our own conclusions.

---

## What C++ actually says

To stop guessing, we built a native harness (`native/rcbench`) that links
recastnavigation directly and bakes the *same* geometry: the corpus tiles are
dumped to a binary file with their full config, and the harness mirrors our bake
step for step. Polygon counts come out identical on all six tiles
(173/365/284/345/258/355), so the two pipelines are genuinely comparable.

DotRecast is a port of recastnavigation **and recast4j** - a port of a port - and
the Java hop widened value types that C++ packs into bitfields:

| structure | C++ | DotRecast | ratio |
|---|---|---|---|
| `rcSpan` | bitfields + `next`, 16 B, pooled contiguous | 16 B struct, paged ✅ | fixed by [`66300b0`](https://github.com/Xian55/DotRecast/commit/66300b0) |
| `rcCompactSpan` | `ushort y, reg` + `con:24, h:8` = 8 B | 4×`int` = 16 B | 2× |
| `rcCompactCell` | `index:24, count:8` = 4 B | 2×`int` = 8 B | 2× |
| `chf.dist` | `unsigned short*` | `int[]` | 2× |
| `chf.areas` | `unsigned char*` | `int[]` | **4×** |

The size gap is real and consistent: measured `sizeof` plus live span counts give
a compact working set of **3.5–7.4 MB in C++ against 7.4–16.0 MB here**, a steady
**2.13–2.16×**. C++ fits inside the dev box's 8 MB L3 on every tile; we exceed it
on four of six.

**What it costs is not what we first concluded.** An earlier version of this
page said the layout theory was disproven, because the stencil stages measured
at parity. That reading was taken *before* the allocation and instruction-level
rounds, and it was an artifact: CPU-bound work in those stages was masking the
memory-bound part. Once that work was gone, the five worst ratios were exactly
the five memory-bound sweeps, ranked by how much wider our storage was. So we
tested the narrowing rather than arguing about it.

**Two of the four candidates paid.** `areas` (4× narrower) and `RcCompactSpan`
(2× narrower, 4.8 MB per tile) both won clearly. `dist` (2×, 1.2 MB) and
`RcCompactCell` (2×, 1.0 MB) both *lost*, and were reverted.

The tempting explanation was a cache cliff: the box has 8 MB of L3, and only the
full set of four takes the largest tile's working set from 16.0 MB to 7.4 MB.
Under that theory `dist` and `cell` fail alone but should pay together, since the
pair is what crosses 8 MB. **We tested that directly, and it is false** - both
applied at once still measured +1.0% on total bake with no overlap between the
sets.

So the rule is not "get under L3". It is **bytes saved versus unpack cost**.
`areas` needed no unpacking at all - a byte load is just a byte load - and
`RcCompactSpan` saved enough per tile to pay for its shift-and-mask several times
over. `dist` and `cell` save around a megabyte each and charge zero-extension or
shift-and-mask on every single access, which is a losing trade at any footprint.

### Where the two stand now

| stage | C++ ms | C# ms | C# ÷ C++ |
|---|---|---|---|
| `BUILD_REGIONS_FILTER` | 67.6 | 27.7 | **0.41×** |
| `BUILD_REGIONS` | 337.9 | 218.8 | **0.65×** |
| `BUILD_POLYMESHDETAIL` | 187.7 | 125.4 | **0.67×** |
| `BUILD_REGIONS_WATERSHED` | 267.2 | 187.3 | **0.70×** |
| `BUILD_REGIONS_EXPAND` | 149.3 | 114.7 | **0.77×** |
| `BUILD_CONTOURS_TRACE` | 20.5 | 16.1 | **0.79×** |
| `RASTERIZE_TRIANGLES` | 217.7 | 177.9 | **0.82×** |
| `BUILD_COMPACTHEIGHTFIELD` | 101.3 | 100.8 | 1.00× |
| `ERODE_AREA` | 61.6 | 67.7 | 1.10× |
| `BUILD_CONTOURS` | 28.9 | 33.6 | 1.16× |
| `BUILD_DISTANCEFIELD_DIST` | 48.6 | 68.5 | 1.41× |
| `BUILD_POLYMESH` | 23.7 | 35.2 | 1.49× |
| `MEDIAN_AREA` | 36.8 | 57.0 | 1.55× |
| `BUILD_DISTANCEFIELD_BLUR` | 25.0 | 40.3 | 1.61× |
| `FILTER_BORDER` | 71.0 | 152.0 | **2.14×** |
| **total** | **1173.6** | **1196.2** | **1.02×** |

Within 2% overall, from 2.1× at the start of this work. Two caveats worth
stating: the harness is **single-threaded throughout**, so our wins in
`RASTERIZE_TRIANGLES` and `BUILD_POLYMESHDETAIL` are partly thread count rather
than per-core efficiency; and the working set is still 9.4 MB against C++'s
7.4 MB, because two of the four narrowings were not worth taking.

## Still on the table

- **`FILTER_BORDER`**, at 2.14× the worst remaining gap and the one stage this
  fork made *worse*. Two attempts have failed: a monotone cursor (50% slower,
  see above) and copying the span struct into a local to cut the paged-store
  load chain (no gain on the target, and both smaller filters got worse). The
  remaining idea is flattening the span store into one column-ordered array
  after rasterization, which would cost a multi-megabyte pooled allocation per
  tile.
- **`MEDIAN_AREA` at 1.55× and the distance field at ~1.5×.** Both are memory-
  bound sweeps where the narrowing that would help is exactly the one measured
  to lose.
- **`BUILD_POLYMESH` at 1.49×.** The `BuildPolyMesh` merge loop re-scans all
  polygon pairs after every merge, an O(npolys³) per contour; only the pairs
  touching the two merged polygons actually change. Caching merge values with
  row/column invalidation makes it O(npolys²), and stays byte-identical provided
  the ascending scan order and the strict `>` tie-break are preserved.
- **`RemoveVertex`/`CanRemoveVertex`** make five full polygon passes per flagged
  vertex, and border tiles flag a whole perimeter's worth.
