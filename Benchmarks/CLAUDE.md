# Benchmarks

BenchmarkDotNet suites plus several custom CLI modes. Always Release.

```powershell
dotnet run --project Benchmarks -c Release                       # BenchmarkDotNet switcher
dotnet run --project Benchmarks -c Release -- --bake-profile <label> [iterations]
dotnet run --project Benchmarks -c Release -- --alloc-profile
dotnet run --project Benchmarks -c Release -- --dump-geometry [outdir]
dotnet run --project Benchmarks -c Release -- --pather-benchmark <baseUrl> [iters] [label] --engines SpotAStar,Navmesh
```

## `--bake-profile` is the byte-identity gate

This is the regression test for any change under `PPather/Triangles` or
`PPather/Navmesh`. It bakes a fixed 6-tile corpus and writes
`local/benchmark_results/bake_<label>_*.md` with cold/warm timings, triangle counts,
allocations **and a per-tile SHA-256**. Those hashes must not move for the precata era:

| tile | hash |
|---|---|
| elwynn-open | `6d6edc442008b805` |
| stormwind-wmo | `c3f074b363b36063` |
| dunmorogh-indoor | `34a1a2d4f25ad55b` |
| barrens-open | `12c83529658aba79` |
| durotar-water | `fb06c81c3603f8e4` |
| orgrimmar-wmo | `a64ee4ee73bb3e33` |

`a64ee4ee73bb3e33` is orgrimmar's tile hash, not a digest of the whole corpus — all six
are the gate.

## Layout

```
Navmesh/NavmeshCorpus.cs      the 6 corpus tiles + GeometryFixture (per-continent world)
Navmesh/BakeProfiler.cs       --bake-profile
Navmesh/AllocProfiler.cs      --alloc-profile
Navmesh/GeometryDumper.cs     --dump-geometry (rcdump for external inspection)
Navmesh/Navmesh_*.cs          BenchmarkDotNet tile extract / bake / MPQ load
PathingAPIBenchmark.cs        --pather-benchmark, drives a running server over HTTP
```

## Things that will bite you

**BenchmarkDotNet runs from a generated temp directory**, so `DataConfig`'s relative
Root (`..\json`) resolves to nothing. `NavmeshCorpus.SetWorkingDirectory()` chdirs into
`HeadlessServer` first; every later relative path resolves against *that*, so use
`SolutionRoot()` for anything written back to the repo.

**`GeometryFixture` defaults to `wrath`** and needs that client's archives in
`Json/MPQ`. It deliberately reuses one triangle world across iterations — call `Evict`
for a cold ADT read without paying archive-open again.

**`delaunayHull: Removing dangling face` is normal.** DotRecast emits it on healthy
tiles; real failures are on stdout, not stderr.

**Getting a baseline without mutating git:** `git show HEAD:<path>` into the scratchpad,
swap files, re-run, swap back — back up your edits first.
