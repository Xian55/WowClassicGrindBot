# PPather — pathfinding engine

Two engines behind one service. **Navmesh (DotRecast) is the default and the one to
work on**; the spot-grid A* is legacy and kept only as a fallback.

TFM is plain **`net10.0`** (not `net10.0-windows`) so the pathing chain runs on
macOS/Linux — see `docs/headless-pathing-server-design.md`. Do not add a Windows-only
dependency here.

## Layout

```
Navmesh/       DotRecast engine (the current one)
  NavmeshPathfinder.cs     query entry: FindPath, TryGetHeight, corridor widening, retarget
  NavmeshTileCache.cs      tile residency + on-demand bake; accepts a NULL world = disk-only
  NavmeshTileBuilder.cs    geometry -> DtMeshData
  TileGeometryExtractor.cs triangle soup -> tile geometry (MinWorldZ filter lives here)
  NavmeshSettings.cs       MeshEra() + ComputeSettingsHash() -> the cache directory name
  NavmeshCoords.cs         wow <-> recast conversion, tile indexing
  NavmeshEndpointResolver.cs  snap a world point to a poly (tight vertical extents on purpose)
  CostZones.cs/CostZoneLoader.cs  authored road/danger costs, query-time only
  AreaGrid.cs              standalone area-id grid, answers without game files
  CatmullRom.cs            path smoothing
StormDll/      MPQ archive access via StormLib P/Invoke
  ArchiveSet.cs            base archives + PTCH patch chaining (build-order ascending)
  Archive.cs               one archive; empty (listfile) is tolerated, not fatal
  StormDll.cs              [LibraryImport]; OS+arch resolver, W/A marshalling split
Triangles/     ADT/WMO/M2 geometry -> triangle soup
  MPQTriangleSupplier.cs   per-continent supplier; opens the archive set + WDT eagerly
  ChunkedTriangleCollection.cs  LRU ADT cache (maxCache=128)
  Game/MapTileFile.cs      ADT parsing, incl. split (Cata+) root/_obj0 feature detection
  Game/WDTFile.cs          WDT MAIN; bit 0 = has_adt
Search/PPatherService.cs   the facade everything else uses (engine choice, bake, area grid)
Graph/         legacy spot-grid A* (PathGraph, Spot, SpotManager)
MPQ/           bundled StormLib natives (x64/x86/arm64 .dll, .dylib/.so are gitignored)
```

## Things that will bite you

**Settings hash = cache directory.** `ComputeSettingsHash(era, version, agentRadius,
agentMaxClimb, walkableSlope, minWorldZ)` names the tile dir. Change any of those and
every previously baked tile is invisible — not wrong, *invisible*. Outland is the live
case: it only resolves with `MinWorldZ["Expansion01"] = -700`.

**Disk-only is a supported mode, not a degraded one.** `NavmeshTileCache` takes a
nullable world; null means "answer from baked tiles, never bake". `PPatherService`
falls back to it per continent when archives are missing. Baking, area-grid extraction
and SpotAStar are the only things that genuinely need `Json/MPQ` — they call
`RequireGeometry()` and fail with a stated reason.

**Paths inside an MPQ are always backslash-separated.** Never `Path.Join` them; it
yields `World/Maps/...` on macOS and matches nothing. Use interpolation with `\\`.

**Geometry eras never share tiles.** `MeshEra()` maps vanilla…wrath to `precata`, and
`cata`/`mop` to themselves. A stale mesh routes the bot through solid geometry, so this
is deliberately stricter than the minimap-art sharing in `DataConfig.TileEra`.

**`PPather.Graph.Path` collides with `System.IO.Path`.** Fully qualify it in any file
doing I/O.

**`PPatherService` is a singleton with a two-call search** (`SetLocations` then
`DoSearch`). It is not re-entrant; concurrent callers must serialize.

**`GetAreaIdAndZ` leaves the tile flagged loaded**, so a later `GetTriangles` on the
same tile returns zero triangles. Use a fresh supplier when doing both.

## Verifying a change

Geometry must stay byte-identical for the old eras. The gate:

```powershell
dotnet run --project Benchmarks -c Release -- --bake-profile <label> 1
```

Writes `local/benchmark_results/bake_<label>_*.md` with a per-tile SHA-256. Wrath
6-tile baseline, which must not move:

| tile | hash |
|---|---|
| elwynn-open | `6d6edc442008b805` |
| stormwind-wmo | `c3f074b363b36063` |
| dunmorogh-indoor | `34a1a2d4f25ad55b` |
| barrens-open | `12c83529658aba79` |
| durotar-water | `fb06c81c3603f8e4` |
| orgrimmar-wmo | `a64ee4ee73bb3e33` |

`delaunayHull: Removing dangling face` on stderr is normal DotRecast noise — real
failures go to stdout.
