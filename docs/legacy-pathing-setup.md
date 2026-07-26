# Legacy pathing setup (deprecated)

> **Deprecated.** Nothing needs these any more. The in-process **DotRecast navmesh**
> engine is the default on every supported client, runs from pre-baked tiles, and needs
> no game archives and no external service. See
> [Pathfinders](../README.md#pathfinders) in the README.

These instructions are kept for two reasons:

* the **game archives** section is still how you get MPQs if you want to *bake* your own
  navmesh tiles (a custom agent config, or an era with no published bundle);
* the **V1/V3 backends** still exist and can be selected with `Pathing:Mode`, so the
  setup steps should not simply vanish.

If you are setting the bot up for the first time, you want
`.\scripts\download-navmesh.ps1` and nothing on this page.

---

## V1 — Local / Remote (MPQ archives)

Where to get the game archives. **They are not required to run the bot** — only to
*bake* navmesh tiles yourself, or to run the legacy `Pathing:Engine=SpotAStar`.

**Vanilla:**
[**common-2.MPQ**](https://mega.nz/file/vXQCBCha#m7COhB9HQd86a5iNAT0-fMLsc-BtoTRO1eIBJNrdTH8) (1.7Gb)

**TBC:**
[**expansion.MPQ**](https://mega.nz/file/Of4i2YQS#egDGj-SXi9RigG-_8kPITihFsLom2L1IFF-ltnB3wmU) (1.8Gb)

**WOTLK:**
[**lichking.MPQ**](https://mega.nz/file/vDYWSTrK#fvaiuHpd-FTVsQT4ghGLK6QJLZyA87c1rlBEeu1_Btk) (2.5Gb)

Copy these files under the **\Json\MPQ** folder (e.g., `C:\WowClassicGrindBot\Json\MPQ`)

**Cataclysm 4.3.4 (2011):** no separate download — copy the archives from your own client's
`Data\` folder (`world.MPQ`, `world2.MPQ`, `art.MPQ`, `expansion1-3.MPQ`, `alternate.MPQ`).
`Data\enUS\*.MPQ` holds locale data only and is not needed for geometry. Only one client's
archives may sit in `Json\MPQ` at a time — mixing eras makes ADT lookups resolve against
whichever archive sorts first.

> Most users can skip the archives entirely and just run `download-navmesh.ps1` — the MPQs
> are only needed to *bake* tiles or to run the legacy `SpotAStar` engine.

Technical details about **V1:**
- Precompiled x86, x64 and arm64 [Stormlib](https://github.com/ladislav-zezula/StormLib)
- Source code accessible, written in **C#**
- Uses `*.mpq` files as source
- Extracts the geometry on demand during runtime
- Loads those `*.adt` files which are in use. Lower memory usage compared to V3
- After calculating a path successfully, caches it under `Json\PathInfo\_CONTINENT_NAME_\`
- Easy to visualize path steps and development iteratively

## V3 — AmeisenNavigation (external server)

Since [PR 585](https://github.com/Xian55/WowClassicGrindBot/issues/585) using a different branch!

- Download the navmesh files.

[**Vanilla + TBC**](https://mega.nz/file/7HgkHIyA#c_gzUeTadecWY0JDY3KT39ktfPGLs2vzt_90bMvhszk)

[**Vanilla + TBC + Wrath**](https://mega.nz/file/zWQ2XIKI#9EKWOPyyTMfY1LACkcP_wioZ0poVIuaGh2xcRh4V9dw)

[**Vanilla + TBC + Wrath + Cataclysm** - work in progress](https://mega.nz/file/7Og32TDA#5HpxZ8Sh1XvDNCmWbI8H-cOFEJzDmh97Z6FGrO2p3X4)

1. Extract the `mmaps` and copy anywhere you want, like `C:\mmaps`
1. Get the [multi-version-guess-z-coord branch](https://github.com/Xian55/AmeisenNavigation/tree/feature/multi-version-guess-z-coord)
1. Open the solution file.
1. Unload **AmeisenNavigation.Exporter** project(right click -> unload project)
1. ![image](https://github.com/Xian55/WowClassicGrindBot/assets/367101/df443648-bb57-4200-ac99-ee26e723f120)
1. Select **AmeisenNavigation.Server** Press rebuild.
1. Navigate to the `AmeisenNavigation.Server` build(ex. `AmeisenNavigation.Server\build\x64\Release`) location and find `config.cfg`
1. Edit the last line of the file to look like `sMmapsPath=C:\mmaps`
1. Start `AmeisenNavigation.Server.exe`

Technical details about **V3:**
- Uses another project called [AmeisenNavigation](https://github.com/Xian55/AmeisenNavigation/tree/feature/multi-version-guess-z-coord)
- Under the hood uses [Recast and Detour](https://github.com/recastnavigation/recastnavigation)
- Source code is written in **C++**
- Uses `*.mmap` files as source
- Loads the whole continent navmesh data into memory. Higher base memory usage, at least around *~600mb*
- It's super fast path calculations
- Not always suitable for player movement.
- Requires a considerable amount of time to tweak the navmesh config, then bake it
---

Back to the [README](../README.md) &middot; current pathing is documented under
[Pathfinders](../README.md#pathfinders).
