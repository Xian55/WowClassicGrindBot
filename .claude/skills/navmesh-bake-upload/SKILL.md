---
name: navmesh-bake-upload
description: How to generate (bake) the DotRecast navmesh from a WoW client and upload the tiles to Cloudflare R2 for distribution
user-invocable: true
---

# Navmesh: bake & host

The pathing engine reads baked DotRecast navmesh tiles from
`Json/PathInfo/navmesh/<era>/<continent>/<settings-hash>/tile_<x>_<z>.dnm` (era =
`DataConfig.ClientEra`, e.g. `precata`). Tiles are
read **server-side in the pathing hot path**, so they are hosted as
**per-continent download bundles** (not fetched per-tile). Two phases: bake from
the client, then upload the bundles.

- **Continents (world only):** Azeroth (0), Kalimdor (1), Expansion01/Outland (530), Northrend (571).
- **Settings hash** (`b1473dae` etc.) = SHA of the bake config (FormatVersion +
  agent radius/climb/slope + MinWorldZ) via `NavmeshSettings.ComputeSettingsHash`
  / `PPatherService.NavmeshCacheDir`. Change the agent config → different hash →
  different dir. A hosted bundle only serves runtimes with the **same** hash.
- **Era-shared:** vanilla…wotlk all map to the same `precata` geometry; bake once
  against a Wrath 3.3.5 client to cover all four continents.

---

## 1. Generate (bake)

**Prereqs**
- WoW **3.3.5a** client archives staged at `Json/MPQ/*.MPQ` (baking needs the game files).
- `StormLib_x64.dll` at `PPather/MPQ/` (shipped).
- `appsettings.json` must carry the same bake settings the runtime uses — notably
  `Navmesh:Bake:MinWorldZ:Expansion01 = -700` (Outland's floating base filter).
  The floor MUST match bake vs run or the hash differs and the tiles are ignored.

**Bake** (background, via PathingAPI CLI):
```powershell
dotnet run -c Release --project PathingAPI -- --exp=wrath --bake=all
#   all      -> the 4 world continents (PPatherService.WorldContinents), skips instances
#   or a single continent:  --bake=Northrend
```
- Progress: `GET api/PPather/Bake/Status`. Completion is logged with duration.
- Only present ADTs are baked (WDT grid mask); the geometry cache is LRU-capped
  (~640 MB peak) and freed after each continent.
- Slow (minutes per continent). Full set ≈ 51k tiles / ~877 MB on disk.

**Verify**
```powershell
foreach ($d in Get-ChildItem Json/PathInfo/navmesh -Recurse -Directory | Where-Object { Test-Path (Join-Path $_ '*.dnm') }) {
  "{0}: {1} tiles" -f $_.FullName, @(Get-ChildItem $_ -Filter *.dnm).Count
}
```
Expected (default config): Azeroth `b1473dae` 10590, Kalimdor `b1473dae` 15481,
Expansion01 `891baffa` 8031 (MinWorldZ), Northrend `b1473dae` 17011.

> Wrath WMO note: baking Northrend/Outland needs the `HandleMODD` 24-bit name-offset
> fix (`WmoRootFile.cs`) — already in the tree. Without it the bake OOM-crashes on
> flagged doodads.

---

## 2. Upload to R2

Bundles: one zip per `<era>/<continent>/<hash>/` → key `navmesh/<era>/<c>/<hash>.zip`,
plus `navmesh/index.json` (era, continent → hash/zip/tiles/bytes) that the downloader reads.

**Credentials** (env, never on the command line or in chat):
- `R2_ACCESS_KEY_ID` | `R2_AC`, `R2_SECRET_ACCESS_KEY` | `R2_SAK` — from an R2 API
  token (Object Read & Write). Set as `$env:` in the upload terminal only.
- `R2_ACCOUNT_ID = 0f9c85dc81f0d1edaee3f6e994f52216`, `R2_BUCKET = tortoise-db-viewer`.

**Run** (needs `pip install boto3`):
```powershell
$env:R2_ACCOUNT_ID='0f9c85dc81f0d1edaee3f6e994f52216'
$env:R2_BUCKET='tortoise-db-viewer'
# R2_AC / R2_SAK come from the terminal env

python scripts/upload-navmesh-r2.py --dry-run     # lists continents/hashes, no upload, no creds
python scripts/upload-navmesh-r2.py               # zips (DEFLATE) + uploads bundles + index.json
python scripts/upload-navmesh-r2.py --continent Northrend   # just one
```
DEFLATE shrinks ~877 MB → ~353 MB. One-time; re-run after a re-bake.

**Verify serving** (public domain `https://bot.tortoiseclothing.org`):
```python
python -c "import urllib.request as u,json; UA={'User-Agent':'Mozilla/5.0 Chrome/124 Safari/537.36'}; B='https://bot.tortoiseclothing.org'; \
idx=json.load(u.urlopen(u.Request(B+'/navmesh/index.json',headers=UA))); print(len(idx['continents']),'continents'); \
[print(c['name'],c['hash'],round(c['bytes']/1048576),'MB', u.urlopen(u.Request(B+'/'+c['zip'],headers={**UA,'Range':'bytes=0-1'})).status) for c in idx['continents']]"
```
> **Cloudflare gotcha:** the default Python/urllib User-Agent gets a **403 error
> 1010** — always send a browser UA when probing the domain.

---

## 3. User download (context)

End users get pre-baked navmesh with **no client, no bake** via a zero-install
PowerShell script (public bucket → no creds):
```
.bat\download-navmesh.bat            # double-click: all continents
scripts\download-navmesh.ps1 -Continent Northrend
```
It resolves the destination from **DataConfig** (`data_config.json` Root →
`<Root>/PathInfo/navmesh`), reads `index.json` for the hash, downloads + extracts,
and skips continents already complete. Zip roundtrip is byte-identical.

**Hash caveat:** the bundle matches the **default** agent config's hash. A user
running a custom agent config computes a different hash → the download lands in a
dir the runtime won't read → they must bake. The existing disk→bake fallback
handles the miss automatically.

---

## Notes
- Sibling flow for leaflet tiles: `scripts/upload-leaflet-r2.py` + `resolveTileBase`
  in `leaflet-watch.js` (browser-fetched, per-tile CDN, local-first probe).
- Companion data (game-file-free area lookups): `--bake-area=all` writes
  `Json/area_grid/*` + `Json/subzones/*`; see the pathfinding project memory.
- `du` over-reports size (block slack across tens of thousands of tiny files);
  the zip byte totals are the real numbers.
