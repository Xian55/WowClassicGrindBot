# Draft issue for ikpil/DotRecast

**Suggested title:** Recast bake performance: 26 output-identical changes from a
downstream fork, with measurements

---

Hi — first, thanks for DotRecast. Having a maintained C# port meant we could do
navmesh work entirely in-process instead of shelling out to a native
recastnavigation build, which is the whole reason our project could go this
route at all.

**This is a findings report, not a contribution request.** I'm not asking you to
merge anything. It's your library and your roadmap, and a large diff to the core
of it — driven by one downstream project's unusual constraints — isn't a fair
thing to put in your review queue.

Everything below is measurements and reasoning. If any of it is useful, the
easiest path for you is almost certainly to **implement it yourself** in
whatever way fits your design; the numbers and the failure modes are the part
that took the time, not the code. Branch link included only so you can read the
diffs if that's quicker than my prose.

## The workload these numbers come from

Baking a navmesh for a ~34,000 yard world **on the end user's machine, on first
visit to an area, while a 3D application runs on the same box**. That's an
unusual recast workload — the normal one is baking offline and shipping the
result — and it leans hard on `RcRecast` bake throughput and on allocation
behaviour, since the GC competes with the foreground app for the same cores.

Worth stating plainly so you can judge relevance: the project is
[WowClassicGrindBot](https://github.com/Xian55/WowClassicGrindBot), a World of
Warcraft automation tool. If that's not something you want associated with the
project, that's entirely fair — the measurements below stand on their own and
you're welcome to use them without any attribution or connection.

Diffs, if useful: <https://github.com/Xian55/DotRecast/tree/wow-mods>
Full write-up with methodology: **https://github.com/Xian55/DotRecast/wiki/Why-WowClassicGrindBot-forks-DotRecast**

## Correctness

Every change is **output-identical**. We gate on a SHA-256 of the serialized
`DtMeshData` for a 6-tile corpus (open terrain, WMO city, indoor, water) — all
six hashes must match, otherwise our users would have to rebake. All 26 commits
pass that gate, and your test suite (142 tests) stays green throughout.

I mention this because it's also how I caught myself being wrong: one change
passed all six hashes and was **50% slower**. Details below.

## Headline numbers

6-tile corpus, warm bake, on one dev box (i7-6700K, 4 physical cores):

| | before | after |
|---|---|---|
| total warm bake | 2515 ms | 1196 ms |
| allocation | 751 MB | 85 MB |
| gen0 collections | 116 | 12 |

Run-to-run spread on this machine is ±10–15% — at one point two runs of the
*same code* differed by 16% on wall clock — so everything after the first round
was measured as three interleaved A/B pairs in drift-cancelling order on an idle
box, quoting all samples. Per-stage, the largest movers were
`RASTERIZE_TRIANGLES` (−77%), `MEDIAN_AREA` (−77%),
`BUILD_COMPACTHEIGHTFIELD` (−73%) and `BUILD_CONTOURS_TRACE` (−66%).

One stage went the **wrong** way: `FILTER_BORDER` +29%, a deliberate trade
described under "How I'd triage" below.

For calibration, we also built a native harness that links recastnavigation and
bakes the identical geometry (polygon counts match exactly on all six tiles).
Total bake is now **1196 ms against that harness's 1174 ms** — within 2%, from
2.1× when this started. Two caveats: the harness is single-threaded, so our wins
in `RASTERIZE_TRIANGLES` and `BUILD_POLYMESHDETAIL` are partly thread count; and
our compact-heightfield working set is still 9.4 MB against its 7.4 MB.

## Two things you may want to know regardless

**1. The span pool is dead code.** `RcHeightfield.pools` / `freelist` and the
`AllocSpan` / `FreeSpan` helpers exist but have **zero call sites** — `AddSpan`
does `new RcSpan()` on every call, which is once per (triangle, cell)
incidence. On our tiles that's 3×10⁵–1.5×10⁶ objects and 12–60 MB per tile. The
merge path also unlinks absorbed spans and drops them rather than reusing them.
Whether or not you want our pooling change, the dead mechanism is worth either
wiring up or removing.

This turned out to be the single largest item in the whole exercise. Wiring the
pool up helped; going the rest of the way to the C++ layout — `RcSpan` as a
16-byte struct in pooled pages, addressed by `int` index, with
`RcHeightfield.spans` becoming `int[]` column heads — took
`RASTERIZE_TRIANGLES` from **554 ms to 246 ms** and cut gen0 by 59% on its own.
It is also the change that made `FILTER_BORDER` worse; see below.

**2. Roughly half of `RC_TIMER_BUILD_REGIONS_WATERSHED` is unattributed.** The
`EXPAND` timer opens and closes *inside* the level loop, so the final
`ExpandRegions` call is untimed, and the `DIVIDE_TO_LEVELS` calls around
`SortCellsByLevel` are commented out with no matching label defined. Adding two
labels attributed a 155 ms blind spot on our corpus: level bucketing turned out
to be 48% of watershed, while the final expansion — which looks alarming in the
source, since `level == 0` bypasses the `maxIter` guard — is 1%.

That's a pure-instrumentation change and probably the single most useful thing
here even if you take nothing else.

## How I'd triage the changes

**Mechanical, uncontroversial** — allocation removal, LINQ removal in the
compact build, loop-invariant hoisting, folding redundant whole-array passes,
`ArrayPool` for the large per-tile scratch. These are the ones I'd expect to be
straightforwardly acceptable.

**Algorithmic, needs a proper read** — bucketing watershed cells by level
instead of rescanning every column each wrap. `SortCellsByLevel` walks the whole
heightfield once per 8 levels (~30 full sweeps, ~15M span visits/tile) to
collect one band. The rewrite is safe only because emission order is preserved
exactly — stack order determines region id assignment in `FloodRegion` — and
because `srcReg` never returns to 0 between rounds. The commit message spells
both arguments out.

**Opinionated, probably wants a switch** — two `Parallel.For` changes (banded
rasterization, detail mesh). They buy single-tile latency and do very little
when the caller already bakes several tiles concurrently, so defaulting them on
may not suit everyone.

**A real trade, not a free win** — `RcSpan` as a pooled struct. It is the
biggest single improvement here, and it makes one stage *slower*:
`FILTER_BORDER` 118 → 162 ms. A span field read becomes three dependent loads
(pages field, page array, element) where a reference was one, and
`FilterLedgeSpans` walks four neighbour columns per span. For us +330 ms
elsewhere against −44 ms there is obviously worth it, but a caller whose
profile is filter-dominated might feel differently. Flattening the store into
one column-ordered array after rasterization would recover it at the cost of a
multi-megabyte LOH allocation per tile — which defeats the purpose for us, but
might not for you.

**Public API change** — making `RcCompactHeightfield` disposable so its four
bulk arrays (~10 MB/tile) can come from `ArrayPool`. Not disposing still works
and collects as before, so it is source-compatible, but it does add
`IDisposable` to a public type and imposes two invariants: no bounding loops by
`Length` (audited — nothing does), and `cells` must be cleared explicitly since
it is the one array not fully written. Worth **85 MB vs 191 MB** on our corpus.

**Public layout change, and the most interesting result** — narrowing the value
types to the C++ widths. `chf.areas` `int[]` → `byte[]` and `RcCompactSpan`
16 → 8 bytes (`ushort y; ushort reg; uint con:24, h:8`, with `con`/`h` as
properties over a packed field so no call site changes). Those two gave
`BUILD_REGIONS` −10% and `BUILD_REGIONS_EXPAND` −12/−16% respectively.

The catch is `RcCompactSpan.h`, which then saturates at 255 as it does in C++,
rather than at `RC_SPAN_MAX_HEIGHT`. Our corpus reaches 1,048,558, so this
genuinely truncates — it is safe because `h` is clearance *above* a span and no
walkability test distinguishes 255 voxels of headroom from more. I verified that
rather than assuming it: applying the clamp alone, leaving field widths untouched,
kept all six hashes identical. Still, it is a behaviour change and you may
reasonably not want it.

**Do not bother with the other two.** `chf.dist` → `ushort[]` and
`RcCompactCell` → 4 bytes are the same idea applied to smaller arrays, and both
measured *slower* — see below.

## What didn't work

`FilterLedgeSpans` restarts its neighbour walk at the column head for every
span, which is O(S²) in stacked-span depth — and it looked like a clean win,
since WMO tiles cost ~4× what their triangle count suggests. I implemented a
monotone cursor, it was provably output-identical, and all six hashes passed.

It was 50% slower (`FILTER_BORDER` 117/122 ms → 175/189 ms). Real geometry has
S ≈ 1.2–3.5, so the quadratic never bites, while the per-column cursor setup
taxes all 260k columns. Reverted.

Flagging it so nobody else burns a day on the same idea.

**Copying the span struct into a local.** Once spans are value types in a paged
store, `store[span].field` is three dependent loads where a reference was one,
and `FilterLedgeSpans` reads four fields per span. Hoisting one copy per span
looked certain. Measured: nothing on the target stage, and both smaller filters
got consistently ~15% *worse*. RyuJIT was already CSE-ing the repeated lookups,
and a 16-byte struct copy costs more than re-reading a hot cache line.

**Narrowing `chf.dist` to `ushort[]` and `RcCompactCell` to 4 bytes.** Same
change that made `areas` and `RcCompactSpan` pay, applied to smaller arrays.
`dist` made the distance field +2 to +4% (zero-extend on load, truncate on
store, arithmetic still in `int`). `cell` made `BUILD_CONTOURS_TRACE` +8.8% and
`BUILD_DISTANCEFIELD` +6.5% (shift and mask on every access, and the stencil
passes read five cells per span).

I had a tidy explanation for why the winners won — an L3 cliff. This box has
8 MB, and only the full set of four narrowings takes our largest tile's working
set from 16.0 MB to 7.4 MB, so `dist` and `cell` should fail alone but pay
together, since the pair is what crosses 8 MB. **I tested that directly and it
is false**: both applied at once still measured +1.0% on total bake, cleanly.

The rule that actually held is bytes-saved versus unpack cost. `areas` needs no
unpacking at all, and `RcCompactSpan` saved 4.8 MB per tile — enough to pay for
its shift-and-mask many times over. A megabyte does not.

**The other dead end is worth more to you than to me.** DotRecast's value types
are wider than C++'s bitfield-packed ones — `rcCompactSpan` 16 B vs 8 B,
`rcCompactCell` 8 B vs 4 B, `dist` `int[]` vs `unsigned short*`, `areas`
`int[]` vs `unsigned char*`. That is a measured **2.13–2.16× working set**
(3.5–7.4 MB in C++ vs 7.4–16.0 MB here, against 8 MB of L3), and I was
confident it explained a large share of the remaining gap.

Against the native harness it does not. The stages that sweep that working set
repeatedly are exactly the ones at parity or better —
`BUILD_COMPACTHEIGHTFIELD` 1.00×, `BUILD_REGIONS_WATERSHED` 1.00×,
`ERODE_AREA` 1.06×, `BUILD_REGIONS` 0.87×, `BUILD_REGIONS_FILTER` 0.35×,
`BUILD_POLYMESHDETAIL` 0.75×. The real remaining gaps are `BUILD_CONTOURS` and
`BUILD_POLYMESH` (~2×) and `FILTER_BORDER` (1.5×), none of which the layout
theory predicted.

So if anyone ever proposes narrowing those types to match C++ — a change that
would touch 60+ call sites and break public types — this is evidence that it
would not pay for itself. We are not doing it either.

## No obligation, and no ask

Nothing here needs a reply. Reimplement any of it independently, cherry-pick
under the same zlib terms, or ignore it entirely — all three are fine outcomes.

If you'd rather have a focused PR for some specific subset, say so and I'll
prepare exactly that. But the default assumption is that you won't want one, and
that's the expected answer, not a disappointing one.
