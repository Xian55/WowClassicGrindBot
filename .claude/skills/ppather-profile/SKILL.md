---
name: ppather-profile
description: Attach .NET diagnostics (dotnet-trace / counters / gcdump) or run the Benchmarks harness against PathingAPI or a bake — for CPU and allocation profiling of the navmesh pipeline
user-invocable: true
---

# PPather Profile

Attach `dotnet-trace` / `dotnet-counters` / `dotnet-gcdump` to a running `PathingAPI` or `Benchmarks` process to find where navmesh bake and pathing time actually goes, plus a wrapper around the `Benchmarks` project's own harnesses.

Sample **Release builds only**. Debug flattens inlining and turns the flamegraph into stack-frame setup.

## Subcommands

```
/ppather-profile install                       # one-time: install the CLI tools
/ppather-profile cpu [seconds=30] [--detailed] # CPU sampling -> trace.nettrace + speedscope JSON
/ppather-profile counters [seconds=60]         # live runtime counters -> counters.csv
/ppather-profile heap                          # heap snapshot -> trace.gcdump + text report
/ppather-profile bench --bake [label] [iters]  # 6-tile bake corpus (stage table + tile hashes)
/ppather-profile bench --throughput            # 40-tile virgin corridor
/ppather-profile bench --engines Navmesh       # pathing A/B suite (needs PathingAPI running)
/ppather-profile bench --filter *Navmesh*      # a BenchmarkDotNet filter
/ppather-profile pid                           # print the detected PID
```

### Dispatch

| Subcommand | Script |
|---|---|
| `install` | `pwsh .claude\skills\ppather-profile\scripts\install.ps1` |
| `cpu` | `pwsh ...\cpu.ps1 [-Seconds <int>] [-Detailed] [-Target PathingAPI\|Benchmarks] [-TargetPid <int>]` |
| `counters` | `pwsh ...\counters.ps1 [-Seconds <int>] [-Counters <list>] [-TargetPid <int>]` |
| `heap` | `pwsh ...\heap.ps1 [-TargetPid <int>]` |
| `bench` | `pwsh ...\bench.ps1 [-Bake] [-Throughput] [-Engines <list>] [-Filter <pat>] [-Label <s>] [-Iterations <n>] [-Rebuild]` |
| `pid` | `pwsh ...\pid.ps1 [-Target <name>]` |

Output goes to `artifacts/profile/<UTC-timestamp>/`. Benchmark reports go to `local/benchmark_results/`.

## The one guard that matters

`bench.ps1` refuses to run against a **stale Debug DotRecast**.

DotRecast is a submodule and is **not** in `MasterOfPuppets.sln`, so
`dotnet build MasterOfPuppets.sln -c Release` copies the *Debug* DotRecast
assemblies into the Benchmarks output. Every stage then measures 3-5x slower —
including stages nothing touched — and it reads as a catastrophic regression
rather than a build problem. This has burned real measurement rounds twice.

`-Rebuild` does the correct dance:

```
rm -f Benchmarks/bin/Release/net10.0*/DotRecast.*.dll
dotnet build Benchmarks -c Release        # project-level, not the .sln
```

Sanity check by size: Release `DotRecast.Recast.dll` is ~112-113 KB, Debug ~127 KB.

## Typical workflows

**Where is bake time going?**
1. Terminal A: `dotnet run --project PathingAPI -c Release` and start a bake (map sidebar, or `POST /api/PPather/Bake?continent=Azeroth`).
2. Terminal B: `/ppather-profile cpu 60 -Target PathingAPI`.
3. Open `trace.speedscope.json` at <https://www.speedscope.app>. Sort by self time.

Expect the split to be roughly MPQ/ADT parse versus recast stages. A single ADT is ~9 s, most of it *not* recast — which is why bake throughput improved far more from extraction and worker-count work than from recast micro-optimisation.

**Is a stage allocation-bound?**
`/ppather-profile counters 60` then watch `alloc-rate` and `gen-0-gc-count`, or `/ppather-profile heap` for what is actually retained.

**Did a change help, and is it still correct?**
`/ppather-profile bench --bake before -Rebuild`, make the change, `bench --bake after`. Compare the per-stage table *and* the tile hashes.

## Reading the numbers honestly

- **Sampling shows elapsed time, not work.** A thread blocked on file I/O dominates the flamegraph; that is not a bug.
- **Ambient load on this box swings +-10%**, which is wider than most individual wins. Interleave A and B runs; never compare "before lunch" with "after lunch".
- **Process-local state contaminates A/B.** Resident navmesh tiles from an earlier query make a later route look faster or even *succeed* where it otherwise fails. Restart the server between arms when tile residency could matter.
- **A first run on a fresh server is not a measurement.** Cold ADT loads and cold tile cache dominate. Warm up, then measure.
- The corridor throughput bench is noisy (observed 4744 vs 5222 ms back-to-back). Prefer the per-tile corpus numbers for anything subtle.

## See also

- `/dotnet-performance` — the .NET-side patterns to reach for once you know what is hot.
- `/recast-perf` — the correctness gate and workflow for changing the recast pipeline itself.
