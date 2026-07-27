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

### Re-measured on Apple silicon

Everything above was measured on a Windows / Intel i7-6700K box whose run-to-run
spread was ±10-15% - wider than most of the individual wins being claimed. The
whole A/B was therefore repeated on a quieter machine: **Mac mini M4, 10 cores,
macOS 26.5.2, arm64**, .NET 10.0.110, three samples per arm in the same
drift-cancelling order, `ba16b1c` (the same baseline as above) against `eae9562`,
five bake iterations per tile.

> **`BakeProfiler` had to be fixed first, and this invalidates the per-stage
> tables above.** The stage breakdown used to come from a *single* instrumented
> bake, while only the warm-bake column was averaged. At n=1 the small stages are
> dominated by JIT tier state, and because the instrumented pass runs *after* the
> warm loop, its tier state depended on the `iterations` argument rather than on
> the code. Across eight identical runs at the same commit, `BUILD_POLYMESH`
> landed anywhere in 19.8-27.4 ms, and 11.0 ms at 20 iterations;
> `RASTERIZE_TRIANGLES` ranged 61.9-110.7 ms (75% spread);
> `BUILD_CONTOURS_SIMPLIFY` moved 102%. The profiler now averages stage timers
> over N bakes after a separate allocation pass. Re-testing four identical runs at
> the tip: `RASTERIZE_TRIANGLES` 75% → 5%, `BUILD_POLYMESH` 37% → 5%,
> `BUILD_CONTOURS_WALK` 36% → 7%, `BUILD_REGIONS_EXPAND` 15% → 2%. Two stages are
> still not trustworthy because they are simply too small -
> `BUILD_CONTOURS_SIMPLIFY` (2-3 ms, 45%) and `BUILD_REGIONS_FLOOD` (4-6 ms, 30%);
> ignore both unless a change targets them directly. **Every per-stage number in
> the 6700K tables above was produced by the old n=1 path**, so treat those columns
> as indicative only - the totals and the allocation figures were always sound.

| stage | baseline | now | M4 change | 6700K change |
|---|---|---|---|---|
| `RASTERIZE_TRIANGLES` | 232.4 | 63.1 | **−73%** | −77% |
| `BUILD_COMPACTHEIGHTFIELD` | 150.4 | 48.5 | **−68%** | −73% |
| `MEDIAN_AREA` | 79.1 | 27.6 | **−65%** | −77% |
| `BUILD_POLYMESHDETAIL` | 109.2 | 45.2 | −59% | −57% |
| `BUILD_CONTOURS_TRACE` | 16.5 | 9.2 | −44% | −66% |
| `BUILD_REGIONS_WATERSHED` | 127.1 | 84.9 | −33% | −47% |
| `BUILD_REGIONS` | 153.9 | 104.4 | −32% | −46% |
| `BUILD_CONTOURS` | 23.7 | 17.1 | −28% | −52% |
| `BUILD_REGIONS_EXPAND` | 51.8 | 42.9 | −17% | −43% |
| `BUILD_POLYMESH` | 17.1 | 14.7 | −14% | −29% |
| `BUILD_DISTANCEFIELD` | 57.1 | 55.6 | −3% | −20% |
| `ERODE_AREA` | 39.8 | 42.9 | +8% | −36% |
| `BUILD_CONTOURS_SIMPLIFY` | 1.7 | 2.2 | +29% | - |
| `FILTER_BORDER` | 60.9 | 89.2 | **+46%** | +29% |

| overall | baseline | now | 6700K |
|---|---|---|---|
| total warm bake | 936 ms | **527 ms (−44%)** | −52% |
| allocation | 708 MB | **84 MB (−88%)** | −89% |
| gen0 collections | 72 | **8 (−89%)** | −90% |
| run-to-run spread | 1.1% | **0.9%** | ±10-15% |

Three things to take from this.

**The variance is an order of magnitude tighter** - 1.1% and 0.9% against
±10-15%. At ±10-15% the 6700K could not resolve a 5% win at all; here it can.

**Allocation reproduces almost exactly** (708 vs 751 MB, 84 vs 85 MB) while
wall-clock does not. That is the expected asymmetry - allocation is a property of
the code, wall-clock is a property of the machine - and it is what confirms both
arms really are the commits they claim to be.

**Every win is directionally confirmed but smaller**, consistent with much of this
fork's benefit being memory-bandwidth and GC-pressure relief: the M4 has less of
both to reclaim. `ERODE_AREA` slipping from −36% to +8% is the one direction
change big enough to be worth a look; `BUILD_CONTOURS_SIMPLIFY` at +29% is 0.5 ms
on a 1.7 ms stage and not worth chasing.

**`FILTER_BORDER` is the only real regression, and it is the documented one.**
+46% here against +29% on the 6700K - the deliberate trade from
[`66300b0`](https://github.com/Xian55/DotRecast/commit/66300b0), reproduced on a
second architecture. Nothing new is broken.

Worth recording what this section originally claimed, because it was wrong: a
**+75% `BUILD_POLYMESH` regression**, blamed on
[`1b555ad`](https://github.com/Xian55/DotRecast/commit/1b555ad4374df307715e1e574b2a67d16daaa963)
and [`a6c10b7`](https://github.com/Xian55/DotRecast/commit/a6c10b7) being x86-tuned.
Bisecting all 28 commits found the stage flat throughout - 18.0, 18.3, 18.5, 17.7,
17.9 ms - and the "regression" was the n=1 sampling artifact described above. With
the profiler fixed it is a −14% *improvement*. The lesson is the one this page
already makes elsewhere: a plausible mechanism plus a non-overlapping sample is
still not evidence. Bisect before believing a regression, especially one whose
stage is small enough to be JIT-tier noise.

**The byte-identity gate does not port across architectures.** All six tiles are
byte-identical *between the two arms* and stable across repeats, so the
optimisation series is identity-preserving here too. But five of the six differ
from the x64 hashes recorded in `Benchmarks/CLAUDE.md`, and the one that matches
(`elwynn-open`) is the only tile with neither WMO placement nor liquid. Since the
pre-optimisation baseline produces the same arm64 hashes, the cause is below this
fork - most likely arm64 FMA contraction in the WMO/liquid transform chains.
Treat the recorded hashes as an **x64-only** invariant; an arm64 bake host needs
its own baseline, and arm64-baked tiles should not be assumed byte-interchangeable
with x64-baked ones for distribution.

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

To stop guessing, we built a native harness that links recastnavigation directly
and bakes the *same* geometry: `Benchmarks --dump-geometry` writes each corpus
tile to a `.rcdump` with its full config, and the harness mirrors our bake step
for step. Polygon counts come out identical on all six tiles, so the two
pipelines are genuinely comparable.

The harness is `rcbench`, kept **outside** this repo (it links recastnavigation
and Detour directly, so it does not belong in the .NET solution). Point its
`RECAST_DIR` at a recastnavigation checkout and give it the dump directory:

```
rcbench <dump-dir> [iterations=2] [--no-detail]
```

Two things about the recorded numbers below have to be flagged, because both
quietly favour C++:

**The poly counts have moved.** This page recorded `173/365/284/345/258/355`;
today's corpus gives `198/398/287/380/262/338`. The config or the extractor
changed in between, so the 6700K C++ column is **not** measured against the same
corpus state as the C# column beside it. Absolute milliseconds across the two
hosts mean nothing; only ratios within one host do.

**`-ffast-math` changes the mesh.** `rcbench`'s CMakeLists builds with
`-ffast-math` / `/fp:fast`, which the .NET JIT never gets. On the M4 that flag is
worth only ~1.6% of wall-clock, so it explains no gap - but it does perturb the
float math enough to change the output: the fast-math build produces
`200/393/290/377/264/341` while a plain `-O2` build produces
`198/398/287/380/262/338`, matching DotRecast exactly. So "polygon counts come out
identical on all six tiles" holds for a plain build and **not** for the one the
recorded numbers came from. Everything below uses plain `-O2`.

**The two sides aggregate differently.** `rcbench` reports the *best* of N
iterations after a warm-up; the C# profiler reports the *mean*
(`bakeSum / iterations`). C++'s run-to-run spread on the M4 is under 1%, so the
distortion is small there, but the asymmetry is real and always favours C++.

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

That "single-threaded throughout" claim does not survive scrutiny, incidentally:
the tip has two `Parallel.For` sites ([`0d8641b`](https://github.com/Xian55/DotRecast/commit/0d8641bd9aedcf9742c7934a7f38332c1646eabe)
and [`51b96e6`](https://github.com/Xian55/DotRecast/commit/51b96e602b803de7d1311a10463481e1dfe2435c))
and there is no switch to disable them, so the C# column above was almost
certainly the parallel build. The M4 numbers below quantify what that was worth.

#### On the Mac mini M4

`recastnavigation` at `9f4ce64` (v1.6.0-367), plain `-O2`, no `-ffast-math`, no
`-march=native`, against DotRecast `eae9562`. Poly counts identical to the C#
bake on all six tiles. C++ is single-threaded; the C# column is the shipping
build, with a single-threaded row added underneath via `DOTNET_PROCESSOR_COUNT=1`.

| stage | C++ ms | C# ms | C# ÷ C++ | 6700K ratio |
|---|---|---|---|---|
| `BUILD_REGIONS_FILTER` | 42.4 | 16.7 | **0.39×** | 0.41× |
| `RASTERIZE_TRIANGLES` | 94.7 | 63.1 | **0.67×** | 0.82× |
| `BUILD_REGIONS` | 142.5 | 104.4 | **0.73×** | 0.65× |
| `BUILD_REGIONS_WATERSHED` | 99.7 | 84.9 | **0.85×** | 0.70× |
| `BUILD_REGIONS_EXPAND` | 45.8 | 42.9 | **0.94×** | 0.77× |
| `BUILD_POLYMESHDETAIL` | 44.8 | 45.2 | 1.01× | 0.67× |
| `BUILD_REGIONS_FLOOD` | 4.0 | 5.9 | 1.48× | - |
| `BUILD_CONTOURS_TRACE` | 6.0 | 9.2 | 1.53× | 0.79× |
| `BUILD_CONTOURS` | 9.4 | 17.1 | 1.82× | 1.16× |
| `BUILD_CONTOURS_SIMPLIFY` | 1.2 | 2.2 | 1.83× | - |
| `FILTER_WALKABLE` | 2.6 | 5.1 | 1.96× | - |
| `ERODE_AREA` | 21.2 | 42.9 | 2.02× | 1.10× |
| `BUILD_COMPACTHEIGHTFIELD` | 23.8 | 48.5 | 2.04× | 1.00× |
| `BUILD_DISTANCEFIELD_DIST` | 17.2 | 37.2 | 2.16× | 1.41× |
| `MEDIAN_AREA` | 12.7 | 27.6 | 2.17× | 1.55× |
| `BUILD_POLYMESH` | 6.5 | 14.7 | 2.26× | 1.49× |
| `FILTER_LOW_OBSTACLES` | 2.5 | 5.7 | 2.28× | - |
| `BUILD_DISTANCEFIELD` | 22.6 | 55.6 | 2.46× | - |
| `FILTER_BORDER` | 33.8 | 89.2 | **2.64×** | 2.14× |
| `BUILD_DISTANCEFIELD_BLUR` | 5.4 | 18.8 | **3.48×** | 1.61× |
| **total** | **419.3** | **527.2** | **1.26×** | 1.02× |
| total, C# forced single-threaded | 419.3 | 902.7 | **2.15×** | - |

The C++ column was cross-checked against an independently written harness (no
Detour, median rather than best, same `.rcdump` input) which totalled 422.9 ms -
**0.8% apart**, so the native side is not the uncertain half of this table.

The gap ordering is broadly the same as the 6700K's, with `FILTER_BORDER` and the
distance field's blur pass at the bad end and the region stages comfortably ahead.
The one genuine change is `BUILD_POLYMESHDETAIL` falling from 0.67× to parity:
that stage is parallel here, so on ten cores it should have improved, and it did
not. Worth a look.

**Parity does not hold on arm64.** 1.26× with parallelism, **2.13× without**.
Intra-tile parallelism is worth 1.69× on ten cores and is doing all the work of
making the total look close; per-core, C++ is more than twice as fast. The 1.02×
on the 6700K was a four-core box where the parallel stages had far less headroom
*and* C++ had far less single-core throughput to exploit - the M4 rewards C++'s
scalar code much more than it rewards ours.

**The scalar stages moved against us**, while the parallel and pooled ones
(`BUILD_REGIONS*`, `RASTERIZE_TRIANGLES`) held or improved. Nothing here is a new
defect: the ordering matches the 6700K's, and the two stages at the bad end -
`FILTER_BORDER` and the distance field's blur pass - are the two this page already
tracks under "Still on the table".

Per-tile, the gap is widest where there is least work to parallelise:

| tile | C++ | C# | ratio |
|---|---|---|---|
| `elwynn-open` | 35.7 | 66.2 | 1.85× |
| `durotar-water` | 49.3 | 63.7 | 1.29× |
| `barrens-open` | 61.3 | 79.2 | 1.29× |
| `dunmorogh-indoor` | 35.7 | 46.0 | 1.29× |
| `orgrimmar-wmo` | 148.9 | 173.1 | 1.16× |
| `stormwind-wmo` | 88.6 | 101.0 | 1.14× |

The gap is widest on the smallest tiles and narrowest on `orgrimmar-wmo`, which is
exactly the ordering intra-tile parallelism predicts: the more work there is per
tile, the more the parallel stages can hide. These ratios are therefore limited by
the C# arm's variance, not the harness's.

## Still on the table

- **`FILTER_BORDER`**, at 2.14× on the 6700K and 2.64× on the M4, and the one
  stage this fork made *worse* on both (+29% and +46%). **Five attempts have now
  failed**, and between them they rule out the whole obvious family of causes.
  `FilterLedgeSpans` is byte-for-byte the same algorithm as C++ with the same
  early-outs, so the gap is per-span-access cost - and it turns out not to be any
  of the three things that cost looks like:

  | attempt | `FILTER_BORDER` | verdict |
  |---|---|---|
  | as-is | 89.1 | - |
  | monotone cursor rewrite | ~134 | 50% slower, reverted |
  | copy span struct to a local | no change | smaller filters got worse |
  | `ref` locals + int `Math.Abs` | 86.9 | −2.5%; below the noise floor, not kept |
  | one 1M-slot page (perfect locality) | 81.0 | −7%, but RASTERIZE 59→71 and allocation 3× - not viable |
  | `Unsafe.Add`, no bounds checks | 86.0 | no change; `FILTER_LOW_OBSTACLES` 5.7→7.8, `FILTER_WALKABLE` 5.2→6.5 |

  So the paged store's **indirection is worth ~2%, its locality ~7%, and its bounds
  checks nothing** - the JIT was already eliding the redundant loads, and forcing
  the issue by hand only disturbed inlining elsewhere.

  **The profile settles it.** `/ppather-profile cpu` over a 60 s bake loop
  (`dotnet-sampled-thread-time`, ~100 Hz; on macOS `cpu-sampling` is Linux-only):

  - `FilterLedgeSpans` is **5.50% of CPU** (9087 ms of 165 s) and ~16% of wall.
  - Its callee breakdown is **99.7% `CPU_TIME`**, 0.3% `RcContext.StopTimer`.
    Nothing else. No allocation, no lock, no helper call.
  - `RcSpanStore.get_Item` **does not appear in the frame table at all** - the
    indexer is fully inlined, which is why removing "redundant" lookups and bounds
    checks by hand bought nothing. There was never any overhead there to remove.

  So the stage is one flat inlined scalar loop with no structural target left in
  it. The remaining 2.6× against C++ is per-instruction codegen over identical
  logic, not a layout or abstraction tax. Flattening into one column-ordered array
  is still untried, but the 1M-page result caps what pure locality can pay at well
  under 2×, and the profile shows nothing else to recover - so it is no longer
  obviously worth its multi-megabyte per-tile allocation. **Treat this stage as
  closed** unless someone wants to attack the codegen itself (SIMD over columns, or
  a different span encoding); five plausible mechanisms have now died here.

  **Nothing was kept.** The closest thing to a win was `ref RcSpan` locals replacing
  nine repeated `store[i]` lookups per span, together with `MathF.Abs` → `Math.Abs`
  on an `int` in the innermost loop - the same int→float round trip
  [`99b2b15`](https://github.com/Xian55/DotRecast/commit/99b2b152b405f9ee5d7c960fed707637c4a66e8c)
  removed elsewhere and missed here. It measured 89.1 → 86.9 ms with total bake
  526.8 → 523.5 ms, hashes unchanged and 142 tests green, and it was still
  abandoned: −2.5% on one stage and −0.6% overall sits inside the 0.7-1.5% run
  spread, and paying for that with a `for`→`while` restructure of two loop headers
  is a bad trade against a file upstream still touches. If someone revisits this,
  the `Math.Abs` half is a one-token change with no rebase cost and is worth taking
  on its own merits; the two halves were never measured separately, so how much of
  the 2.5% it carries is unknown.
- **`MEDIAN_AREA` at 1.55× and the distance field at ~1.5×** on the 6700K; 2.17×
  and 2.16-2.46× on the M4, with `BUILD_DISTANCEFIELD_BLUR` the single worst ratio
  anywhere at **3.48×** (5.4 ms against 18.8 ms - only ~13 ms to win, but the
  cleanest ratio on the board). All are memory-bound sweeps where the narrowing
  that would help is exactly the one measured to lose.
- **`BUILD_POLYMESH` at 1.49× on the 6700K, 2.26× on the M4.** The `BuildPolyMesh` merge loop re-scans all
  polygon pairs after every merge, an O(npolys³) per contour; only the pairs
  touching the two merged polygons actually change. Caching merge values with
  row/column invalidation makes it O(npolys²), and stays byte-identical provided
  the ascending scan order and the strict `>` tie-break are preserved.
- **`RemoveVertex`/`CanRemoveVertex`** make five full polygon passes per flagged
  vertex, and border tiles flag a whole perimeter's worth.
- **`RcSpanStore.ClaimPage` lock contention - 3.12% of all CPU**, surfaced by the
  same profile and previously unnoticed because it is invisible in the per-stage
  table (it bills to `RASTERIZE_TRIANGLES`, which is parallel, so it costs CPU
  rather than wall time). Called 100% from `AddSpan`; **87.4% of it is
  `Lock.EnterAndGetCurrentThreadId` → `TryEnterSlow`**, i.e. the ten rasterization
  band allocators genuinely serialising on `pageLock`, with another 12.2% in the
  copy-on-write `Array.Copy` when the outer `pages` array grows.

  Widening the page is *not* the fix: `PageShift` 11→14 did cut
  `RASTERIZE_TRIANGLES` 65.3 → 58.2 ms (−11%) as predicted, but total bake moved
  only −0.7% against a 0.8-1.5% spread, and 11→12 gave nothing at all. The real
  fix is to remove the lock - pre-size the outer `pages` array and claim with
  `Interlocked.Increment` plus a `Volatile.Write` of the new page - which keeps the
  2048-span granularity and deletes both the contention and the `Array.Copy`.
  Untested.
