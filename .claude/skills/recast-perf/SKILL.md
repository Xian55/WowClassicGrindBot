---
name: recast-perf
description: Workflow for optimising the DotRecast navmesh pipeline — byte-identity gate, C++ recastnavigation gap analysis, and the measurement traps that invalidate results
user-invocable: true
---

# Recast Performance Work

How to change the navmesh bake pipeline without breaking it, and how to know whether a change was worth making. Those are two separate questions and this skill keeps them separate.

The fork is `external/DotRecast` (submodule, `Xian55/DotRecast @ wow-mods`). We consume `DotRecast.Core`, `Recast` and `Detour` only — **not** `DetourCrowd`, `DetourTileCache` or `Detour.Dynamic`.

## The correctness gate

Every tile is hashed (SHA-256 of `DtMeshDataWriter` output) by the bake profiler. A 6-tile corpus covers open terrain, WMO city, indoor, and water:

```
/ppather-profile bench --bake baseline -Rebuild
# ...make the change...
/ppather-profile bench --bake after
```

**All six hashes must match, unless the change is deliberately geometry-affecting.** If they change, the tile cache is invalid and every user must rebake — so that is a `NavmeshSettings.FormatVersion` decision, not an accident.

This gate is what makes aggressive change possible. It is why rewriting `SortCellsByLevel` — a watershed ordering algorithm where a wrong stack order silently renumbers regions and propagates into polygon output — was a safe thing to attempt.

Also run:
- `dotnet test external/DotRecast/DotRecast.Wow.slnf -c Release -f net10.0` (142 tests)
- `cd CoreTests && pwsh -File ./run.ps1 navmesh` (coordinate + cost-zone suites)

**A passing gate is permission to consider a change, not evidence it is good.** The ledge-filter cursor rewrite passed all six hashes and was 50% *slower*.

## Finding work: diff against the C++ original

DotRecast is a port of **recastnavigation and recast4j** — a port of a port. The Java hop widened value types and replaced arrays with collections, and the C# inherited it. Reference checkout: `X:\Programming\recastnavigation` (v1.6.0+).

Confirmed layout gaps (measured, not guessed):

| structure | C++ | DotRecast | ratio |
|---|---|---|---|
| `rcSpan` | bitfields `smin:13,smax:13,area:6` + `next` = 16 B, pooled contiguous | class, ~40 B, scattered heap | 2.5x + locality |
| `rcCompactSpan` | `ushort y, reg` + `con:24, h:8` = 8 B | 4x `int` = 16 B | 2x |
| `rcCompactCell` | `index:24, count:8` = 4 B | 2x `int` = 8 B | 2x |
| `chf.dist` | `unsigned short*` | `int[]` | 2x |
| `chf.areas` | `unsigned char*` | `int[]` | **4x** |

At ~300k spans / 260k columns per tile that is a **4.3 MB compact working set in C++ versus 9.3 MB here** — and the dev box has 8 MB of L3, so C++ fits and we do not. Roughly five full traversals cross that set per tile (erode, median, distance field's two sweeps, box blur, region expansion), which is why the stencil stages measure memory-bound.

Structural gaps like these are translation artifacts, not version drift, so exact C++ version alignment does not matter for them. It only matters when comparing *algorithms*.

## Traps that invalidate results

1. **Stale Debug DotRecast.** `dotnet build MasterOfPuppets.sln -c Release` copies the *Debug* submodule DLLs into Benchmarks output. Everything measures 3-5x slower including untouched stages. `bench.ps1` refuses to run when it detects this; use `-Rebuild`. Release `DotRecast.Recast.dll` ~112-113 KB, Debug ~127 KB.
2. **Ambient load swings +-10%**, wider than most individual wins. Interleave arms; never compare across hours.
3. **Process-local tile residency contaminates A/B.** A route can *succeed* in one process and truncate in a fresh one purely because earlier queries left tiles resident. Restart between arms when residency could matter.
4. **First run on a fresh server is not a measurement** — cold ADT loads dominate.
5. **A/B via a toggle, not a rebuild.** Prefer an env-var switch so both arms share one binary and one warm cache.

## Where the time actually is

Measured on the 6-tile corpus (after the current round of work):

```
RASTERIZE_TRIANGLES  468 ms      BUILD_REGIONS        317 ms
  (banded, parallel)               LEVELS  86  EXPAND 152
BUILD_POLYMESHDETAIL 153 ms      COMPACTHEIGHTFIELD   113 ms
DISTANCEFIELD        116 ms      FILTER_BORDER        114 ms
MEDIAN_AREA           56 ms      ERODE_AREA            72 ms
```

Bake is **not** dominated by recast for bulk work: a single ADT takes ~9 s end to end, most of it MPQ parse and ADT extraction. Optimise extraction and worker count for throughput; optimise recast for single-tile latency.

Nothing below the top few stages is worth micro-optimising before profiling — `/ppather-profile cpu`.

## Rules of thumb learned the hard way

- **Measure before believing a theory.** Three confident diagnoses died in one session: a 5 s tile budget, cost zones breaking long routes, and an O(S²) ledge scan. Each was internally consistent and wrong.
- **Amdahl first.** Rasterize + filters + compact + distance field is ~60% of bake; even a perfect GPU port of that half caps out near 2.6x.
- **Parallelism composes badly with the tile worker pool.** Intra-tile `Parallel.For` helps single-tile latency and does roughly nothing when 4-6 tiles already bake concurrently. The box has 4 physical cores; worker scaling plateaus at ~6.
- **Keep the fork rebase-friendly.** Do not re-indent whole method bodies to wrap them in `try`/`using` — a minimal diff conflicts less with upstream.
- **Never wire `ComputeSettingsHash` to the fork commit SHA.** Output-identical patches would force needless global rebakes.

## See also

- `/ppather-profile` — the profiler and benchmark wrappers.
- `/dotnet-performance` — .NET-side patterns once you know what is hot.
