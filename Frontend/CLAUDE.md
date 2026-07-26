# Frontend — shared Blazor UI

A razor class library shared by `BlazorServer` (the bot) and `PathingAPI` (the pathing
server). Both host the same pages, so **anything added here must tolerate the other
host**: PathingAPI has no `PlayerReader` and no game client, BlazorServer has no bake
API.

## Layout

```
DependencyInjection.cs   static file routes (/path, /tiles, /dbc, /area, ...)
Pages/                   Leaflet, RecordPath, ClassConfig, ... (routable)
Shared/MainLayout.razor  nav menu, incl. which items a client version gets
Controllers/             Road / DangerZone / Path authoring endpoints
wwwroot/script/
  leaflet-watch.js       the map: eras, tile bases, zone/subzone resolution, POIs
  leaflet-navmesh.js     navmesh tile overlay
  leaflet-bake.js        bake controls
  leaflet-zones.js       zone polygons
  babylonjs.js           3D route view (PathingAPI only)
```

## Things that will bite you

**Static file routes are order-sensitive.** `DependencyInjection` registers
`/tiles/<continent>` for shared-art continents *before* the general `/tiles`. Reversing
that serves the wrong era's art.

**`PhysicalFileProvider` throws on a missing directory** — it does not create one. Use
the `UseDataFolder` helper, which `CreateDirectory`s first. A missing folder here does
not break one route, it kills host startup.

**Never gate UI on a hardcoded client-version list.** Use `DataConfig.HasLeaflet(...)`
(era-derived). The map and its nav entry were stuck on `SoM or TBC` long after wrath,
cata and mop worked, which is exactly how that goes stale.

**The JS mirrors `DataConfig` and must be kept in step.** `clientEra()`, `tileEra()`,
`TILE_SHARED_CONTINENTS` and the `Configs` blocks in `leaflet-watch.js` duplicate
`ClientEra` / `TileEra` / `TileSharedContinents` / `LeafletEras`. Change one, change both.

**Tiles fall back to the CDN automatically.** `resolveTileBase()` HEAD-probes a few
low-zoom local tiles per continent and uses `https://bot.tortoiseclothing.org/<era>`
when they are absent. The candidate list has five entries because offset-cropped
continents have no `z2x0y0`. The choice is made once at map load —
`localStorage.leafletTilesLocal = '0'` forces the CDN.

**Area names come from `WorldMapArea.json`, not `AreaTable.json`.** The worldmaparea
carries subzone rows (name + `ParentAreaId` + bounds) when `Json/subzones/<exp>` existed
at generation time. `AreaTable` is only a fallback for an era whose file predates that.

**PathingAPI decides the era, not the URL.** `Pages/Leaflet.razor` in this project reads
`PlayerReader.Version`; PathingAPI's copy reads `DataConfig.Exp`. Neither should trust
the `{expansion}` route segment.

## Editing JS

`wwwroot/script/*.js` is served straight from disk — no bundler, no build step. A
browser reload is enough; only C# changes need a rebuild. Cache-bust query strings
(`?v=…`) are appended by the host.
