# Unifying MPQ and CASC game-data access

> **Status note (2026-07-25).** This document originally assumed that reaching Cataclysm/MoP
> geometry required CASC. That is only true for the *modern re-releases*. The **original
> Cataclysm 4.3.4.15595 client ships MPQ**, and its archives were probed directly (see
> [Measured ground truth](#measured-ground-truth)). The result: **CASC is not on the critical
> path for Cata-era navmesh**, and the DB2 phase is already solved by an existing tool. The
> phasing below has been re-sequenced accordingly.
>
> **Phase A is implemented and shipped** (2026-07-25/26) — see
> [Phase A](#phase-a--cata-era-adt-parsing-the-only-blocker) and [Verification](#verification).
> Vanilla/TBC/WotLK remain byte-identical; the original 4.3.4 Cataclysm client is supported
> end to end: split-ADT parsing, all four continents baked (53,818 navmesh tiles), Leaflet
> tiles for the rebuilt continents with Northrend/Outland inheriting precata art, and both
> published to R2. Phases B (`IGameFileSource`) and C (CascLib, for the 4.4.x/5.5.x
> re-releases) remain open. MoP 5.4.8 is MPQ and split-ADT too but is **untested** — it adds
> `holes_high_res` over `ofsHeight` in the MCNK header.

## Why

The navmesh bake reads terrain and object geometry (ADT / WMO / M2) straight from the
installed WoW client's data files. Today that path is MPQ-only: `MPQTriangleSupplier` opens
a set of `.MPQ` archives through StormLib (P/Invoke in `PPather/StormDll/`).

MPQ is the archive format up to and including the **original** Mists of Pandaria client
(5.4.8), the original Cataclysm client (4.3.4) and WotLK (3.3.5). Blizzard replaced it with
**CASC** (Content Addressable Storage Container) in Warlords of Draenor (6.0, 2014). The
**modern Classic re-releases — Cataclysm Classic (4.4.x) and MoP Classic — run on the retail
engine and ship CASC, not MPQ.**

So there are two separate gaps, and the original document conflated them:

1. **A format gap.** From Cataclysm on, an ADT is split across several files and the root ADT
   lost its chunk-offset table. The current parser cannot read a Cata-era ADT *from any
   container*. This blocks Cata/MoP geometry.
2. **A container gap.** StormLib cannot open a CASC storage. This blocks reading a
   Cata-Classic / MoP-Classic *install*, but only those.

Gap 1 is the blocker. Gap 2 is a convenience: with an original 4.3.4 client on disk, the
Cata-era navmesh can be baked entirely over MPQ. And because `DataConfig.ClientEra` folds
`cata` and `legacy_cata` into one `"cata"` era, a mesh baked from 4.3.4 is the artifact
Cataclysm Classic consumes too. (`mop`/`legacy_mop` are a separate `"mop"` era — see
[MoP implementation](#mop-implementation--patch-chain--high-res-holes).)

## What exists today

The geometry path is already funnelled through one small storage seam.

- **`PPather/StormDll/`** — the StormLib P/Invoke wrapper.
  - `Archive` — one open `.MPQ`; `SFileOpenArchive` / `SFileOpenFileEx` / `SFileReadFile`.
  - `ArchiveSet` — a priority-ordered set of archives. The public surface is tiny:
    - `MpqFileStream GetStream(ReadOnlySpan<char> name)`
    - `bool Exists(ReadOnlySpan<char> name)`
    - `void Close()`
  - `MpqFileStream : Stream` — a read-only, seekable `Stream` over one open MPQ file.
- **Consumers** all take `ArchiveSet` and call only `GetStream` / `Exists`:
  - `MapTileFile.Read(ArchiveSet, …)` — ADT
  - `WDTFile(ArchiveSet, …)` — WDT
  - `ModelFile.Read(ArchiveSet, …)`, `ModelManager(ArchiveSet)` — M2
  - `WmoGroupFile.Load(ArchiveSet, …)`, `WMOManager(ArchiveSet)` — WMO
- **Selection** — `MPQTriangleSupplier.GetArchiveNames(DataConfig)` is
  `Directory.GetFiles(dataConfig.MPQ, "*.MPQ")`. The client/expansion is known from
  `DataConfig.Exp`.
- **Client classification already exists.** `StartupClientVersion` maps
  `{Major: 4, Minor: <= 3}` → `legacy_cata` and `{Major: 5, Minor: <= 4}` → `legacy_mop`;
  `{Major: 4, Minor: >= 4}` → `cata`. `DataConfig.ClientEra` maps the two Cataclysm names to
  the `"cata"` geometry era and the two Mists names to `"mop"`. Nothing new is needed to
  *recognise* the client.

The whole geometry stack touches storage through exactly two methods — `GetStream` and
`Exists` — returning a `Stream`. That is the entire seam.

## Measured ground truth

Probed against a real install at `F:\Game\Cataclysm-4.3.4.15595-enUS-x64`, opening the
archives with the project's own StormLib binary, with WotLK 3.3.5 (`json/MPQ`) as the
control. These are observations, not format-wiki claims.

### Archives

`Data\*.MPQ` holds all world geometry (14 archives; `world.MPQ`, `world2.MPQ`, `art.MPQ`,
`expansion1-3.MPQ`, …). `Data\enUS\*.MPQ` holds locale data including all DBCs.

- `World\Maps\Azeroth\Azeroth_32_48.adt` resolves in exactly one archive (`world.MPQ`) —
  **no cross-archive duplicate**, so `ArchiveSet`'s first-match-wins order is not a
  correctness hazard for geometry.
- The `wow-update-base-*.MPQ` patch archives carry creature/sound/interface assets only —
  no ADT, WMO or WDT overrides.
- **`OldWorld.MPQ` is a 1 KB stub with an empty `(listfile)`.** It opens fine but lists
  nothing. This crashed `ArchiveSet` construction outright (see Phase A step 4).

### ADT layout — the actual delta

Every tile exists as a 1:1 family of four files (7192 of each: root, `_obj0`, `_tex0`,
`_tex1`). There is no `_obj1` and no `_lod` in 4.3.4.

| | WotLK 3.3.5 root `.adt` (control) | Cata 4.3.4 |
|---|---|---|
| root | `MVER, MHDR, `**`MCIN`**`, MTEX, `**`MMDX, MMID, MWMO, MWID, MDDF, MODF`**`, MH2O, MCNK×256` | `MVER, MHDR, MH2O, MFBO, MCNK×256` |
| `_obj0` | — | `MVER, `**`MMDX, MMID, MWMO, MWID, MDDF, MODF`**`, MCNK×256` |
| `_tex0` | — | `MVER, MAMP, MTEX, MCNK×256` |
| `_tex1` | — | (texture data) |

Consequences:

- **`MCIN` is gone from the root ADT.** Its `MHDR` slot reads `0`. This is the hard breakage:
  `MapTileFile.Read` populates its `mcin` span only inside `case ChunkReader.MCIN`, so on a
  Cata ADT every one of the 256 entries stays `offset == 0` and the MCNK loop seeks all 256
  chunks to file offset 0. `ReadAreaIds` has a `haveMcin` guard and degrades to all-zero area
  ids instead of corrupting, but produces no data.
- **Model and WMO placement moved to `_obj0`.** `MMDX`/`MMID`/`MWMO`/`MWID`/`MDDF`/`MODF` are
  no longer in the root.
- **`MH2O` stayed in the root.** Water parsing is unaffected.
- **`_tex0` / `_tex1` are irrelevant to the bake** — the voxelizer never reads textures. They
  can be ignored entirely.
- `MFBO` (flight bounds) is new in the root and is simply skipped by the existing
  `switch` default.

### MCNK header — unchanged

The 128-byte MCNK header layout is **identical** to WotLK. Verified on `Azeroth_32_48`
chunk 0:

- `ofsHeight` = 136 → `MCVT` at MCNK-chunk-start + 136 (same +8-for-header convention).
- `ofsNormal` = 724 → `MCNR`. `MCVT` data is 580 bytes (145 floats), `MCNR` 448 — both as
  in WotLK.
- **`areaid` is still header index 13** (byte 0x34) and reads `12` = Elwynn Forest, correct
  for tile 32_48. `ReadAreaIds`' `+ sizeof(uint) * 15` offset arithmetic stays valid.
- `holes` still at index 15; position floats still at indices 26–28.
- `nLayers`/`ofsLayer`/`ofsRefs`/`nDoodadRefs` are `0` in the root — that data lives in the
  `_tex0` / `_obj0` MCNKs. The bake does not use any of it (`MPQTriangleSupplier` does its
  own spatial culling in `MCNKHelper`), so the `_obj0` MCNKs can be skipped too.
- Root MCNK sub-chunks are `MCVT`, `MCNR`, `MCSE` only.

### M2 — no change needed

Cata M2s are `MD20` **version 272** (WotLK is 264). Despite the version bump, the header
fields the bake reads sit at the same offsets. `ModelFile.Read`'s hard-coded skips
(`4 + 14×uint32` → `nVertices`/`ofsVertices`, then `23×uint32 + 14×float` →
`nBoundingTriangles`/`ofsBoundingTriangles`/`nBoundingVertices`/`ofsBoundingVertices`) were
run against 500 Cata M2s and 500 WotLK M2s:

```
WotLK 3.3.5 (control) : versions 264(0x108)x500 — parsed OK=500 FAILED=0
Cataclysm 4.3.4       : versions 272(0x110)x500 — parsed OK=500 FAILED=0
```

Validation was not just bounds-checking: every bounding-triangle index was confirmed to
address a real bounding vertex (`max(index) < nBoundingVertices`), which a mis-aligned
header would not survive. **`ModelFile.cs` needs no changes.** (186 of the 500 Cata models
carry no collision mesh at all, versus 27 in WotLK — that is a content difference, and
`ModelFile` already returns empty arrays for it.)

### WMO — no change needed

Root and group chunk sets are identical to WotLK, `MVER` = 17 in both:

- root: `MVER, MOHD, MOTX, MOMT, MOGN, MOGI, MOSB, MOPV, MOPT, MOPR, MOVV, MOVB, MOLT, MODS, MODN, MODD, MFOG`
- group: `MVER, MOGP{ MOPY, MOVI, MOVT, MONR, MOTV, MOBA, MOLR, MOBN, MOBR }`

### WDT — no change needed

`Azeroth.wdt` is `MVER, MPHD, MAIN` — `MPHD` flags `0x5E`, bit 0 clear, so no global WMO,
hence no `MWMO`/`MODF`. `WDTFile` switches on chunk type and tolerates their absence.
`MAIN` is still 32768 bytes (64×64 × 8).

### MoP 5.4.8 — measured, both blockers now fixed

> **Update:** both blockers below are implemented and verified. Vanilla/TBC/WotLK stayed
> byte-identical and Cata re-baked byte-identical (32/32 tiles), so nothing already shipped
> moved. MoP now reads patched geometry and extracts Pandaria. See
> [MoP implementation](#mop-implementation--patch-chain--high-res-holes) for what changed and
> what is still missing before MoP can be called supported.

Probed against `F:\Game\Mist of Pandaria` (5.4.8.18414). The **chunk formats are effectively
the same as Cata 4.3.4**, so Phase A's parser already handles them — but two client-level
differences block support, and neither is about chunk layout.

What already matches Cata (no parser work needed):

- Split ADT with the same root layout — `MVER, MHDR, MH2O, MFBO, MCNK×256`. It adds `_obj1`
  (9764 files) alongside `_obj0`/`_tex0`/`_tex1`; still no `_lod`.
- MCNK header identical: `areaid` at index 13 reads 12 (Elwynn) on `Azeroth_32_48`,
  `ofsHeight` 0x88 / `ofsNormal` 0x2D4, sub-chunks `MCVT` 580 / `MCNR` 448 / `MCSE`.
- `MD20` version **272** and WMO `MVER` **17** — same as Cata, so `ModelFile`/WMO need nothing.
- WDT `MAIN` histogram `{1: 839, 2: 3257}` — identical to Cata, so the bit-0 mask fix
  (Phase A step 4) is required here too and already covers it.
- Minimaps use the Cata path form (`World\Minimaps\...`, 23,889 entries, no `.trs`), so the
  extractor's listfile-scan fallback works unchanged.

**Blocker 1 — MPQ patch chains (`PTCH`).** This is the serious one. MoP ships `world.MPQ` /
`expansion1-4.MPQ` as a 5.0.x base plus **23 `wow-update-base-*.MPQ`** archives, several
500 MB-1 GB. Those patch archives do *not* contain whole files; they hold **incremental
`PTCH` deltas** that StormLib applies over the base via `SFileOpenPatchArchive`. Cata's
three update archives carried no world data at all, so this never came up.

`ArchiveSet` opens every archive as an independent peer and resolves by
`Directory.GetFiles` alphabetical order, first match wins. `world.MPQ` sorts before every
`wow-update-base-*`, so the base always wins and **every patch is ignored**:

| file kind | patched | total | |
|---|---|---|---|
| root `.adt` | 5041 | 13,558 | **37.2%** |
| `_obj0.adt` | 1811 | 9882 | 18.3% |
| `_obj1.adt` | 1266 | 9764 | 13.0% |
| `_tex0/_tex1` | 1245 | 19,563 | ~6% |

Worst hit is `HawaiiMainLand` — Pandaria itself — at 1584 of 3420 ADTs patched, then
Kalimdor 1366/5341 and Azeroth 827/4378. Baking MoP today would silently produce
**pre-5.4.8 geometry for over a third of the world**, and reversing the sort order would be
worse: it would feed a raw `PTCH` blob to the ADT parser.

`PPather/StormDll/StormDll.cs` exposes no `SFileOpenPatchArchive` (confirmed absent), so the
fix is real work: add the P/Invoke and restructure `ArchiveSet` from "peer archives, first
match wins" into "base archives + patch chain attached in ascending build order".

**Blocker 2 — `holes_high_res` is actually used.** 768 of 15,360 sampled MCNKs (5%, across
3 of 60 sampled root ADTs) set flag `0x10000`, which repurposes the 8 bytes at header 0x14
(`ofsHeight`/`ofsNormal`) as a 64-bit hole bitmask. Geometry still parses — `ReadMapChunk`
walks sub-chunks rather than trusting `ofsHeight` — but the low-res `holes` field at index 15
is then meaningless, so `MapChunk.IsHole` reports no holes and the bake floors over gaps that
should be open. Localized, but it produces walkable surface where the world has none.

### MoP implementation — patch chain + high-res holes

**Patch chain.** `StormDll` gained `SFileOpenPatchArchive`; `Archive` gained `AttachPatch`;
`ArchiveSet` now splits its input into base archives and `wow-update-<scope>-<build>.MPQ`
patches (regex on the filename), sorts patches by **build number ascending**, and chains them
onto each base they overlap. Overlap is checked against the patches' listfiles first, because
attaching blindly would reopen MoP's multi-hundred-MB patch archives once per base
(23 x 14 = 322 opens).

Patch archives are still opened standalone and searched **last**, since an update can add a
wholly new file that no base lists - those are stored complete rather than as a PTCH delta
(e.g. Cata's `Creature\FlyingPanther\FlyingPanther.M2`, magic `MD20`). Bases are searched
first and resolve to patched content, which preserves the previous alphabetical resolution
order: `wow-update-*` sorted after every base archive name anyway.

Verified end to end - the same ADT read through `ArchiveSet`:

| source | bytes | sha256 |
|---|---|---|
| raw `world.MPQ` base | 381,610 | `e90e3032e265397c` |
| through the patch chain | 381,610 | **`df788149c15f69bd`** |

Identical length, different content: the delta is applied. (Length alone would have been a
misleading check.)

**High-res holes.** `MapChunk` gained `holesHighRes`, and `MapTileFile.ReadMapChunk` now reads
the MCNK header words rather than blind-skipping them, taking the 64-bit mask from words 5-6
when flag `0x10000` is set. `IsHole` tests that mask directly (bit `j*8+i`) and otherwise
falls through to the legacy 4x4 field, so every pre-Mists client is bit-for-bit unaffected.

Worth knowing: this is **defensive, not load-bearing**. Sampling 25 root ADTs per map dir
across all 190 of them, `high_res_holes` carries real holes only in scenario/BG/raid maps -
`GoldRushBG` (315 chunks), `HalfhillScenario` (139), `OrgrimmarRaid` (41),
`ValeofEternalBlossoms_17116` (27) and a few more. **All five world continents the bot bakes
report zero**, so world terrain still uses the legacy field. Pandaria holes do get applied
through that legacy path - `HawaiiMainLand_21_19` extracts 65,520 terrain triangles rather
than 65,536, i.e. four hole cells.

**Mists now has its own era.** `ClientEra` previously folded `mop`/`legacy_mop` in with
Cataclysm. That was unsafe: comparing 40 Azeroth ADTs between a 4.3.4 and a 5.4.8 client, 39
hash identically but one (`37_23`) does not, so the two clients' bakes would have shared
`cata/<continent>/<hash>/` and cross-served tiles - and Pandaria had no era at all. `mop` is
now a separate era end to end (navmesh, leaflet, area grid, cost zones), verified per client:

| client | cache directory |
|---|---|
| `wrath` | `navmesh/precata/Azeroth/b1473dae` (unchanged) |
| `legacy_cata` | `navmesh/cata/Azeroth/bf03fdb6` (unchanged - shipped bundles still match) |
| `mop` | `navmesh/mop/Azeroth/675b9b8a` |

`TileEra`'s shared-art fallback deliberately stays cata-only: Mists' minimap art has not been
compared against precata the way Cataclysm's was, and guessing would serve the wrong world.

*Latent startup bug this surfaced:* `PhysicalFileProvider` throws `DirectoryNotFoundException`
on a missing root, so registering `/tiles` for a brand-new era with no tiles folder took down
host startup entirely rather than 404ing that one route. Any fresh install without generated
leaflet tiles had the same exposure. `Frontend/DependencyInjection` now creates each data
folder before serving it.

**Still missing before MoP can be called supported:**

- ~~`Json/dbc/legacy_mop/` does not exist~~ **done** - generated with
  `ReadDBC_CSV -v legacy_mop` (524 zones). `legacy_mop` now starts and bakes.
- No MoP leaflet tiles yet: `Configs['mop']` in `leaflet-watch.js` is an empty placeholder.
  Generate with `LEAFLET_ERA=mop WOW_MPQ=<mop client>\Data python scripts/extract-minimap.py`
  and paste the manifest output.
- No MoP navmesh baked or published.

#### Sourcing the legacy DBC data

The addons target the modern Classic clients and bridge legacy ones through
`Addons/DataToColor/Legacy/WorldMapAreaIDToUiMapID.lua`, which maps a legacy client's
`WorldMapAreaID` to a modern `UiMapID`. `worldmaparea.json` therefore has to be keyed by
UiMapIDs that agree with that table - which is why the `legacy_*` entries in `ReadDBC_CSV`
point at a re-release build rather than the original client.

`legacy_mop` is sourced from **MoP Classic `5.5.4.68806`**, not `legacy_cata`'s `8.1.0.27826`.
Two reasons. It is the same expansion's content, and it verifies clean against the mapping
table: 261 zone names match with 2 cosmetic differences, all Pandaria zones arrive on map 870
(`HawaiiMainLand`) with real bounds, and of the 179 mapped UiMapIDs it lacks, 176 are
Warlords-or-later zones a 5.4.8 client cannot reach (the three older ones are `Undercity`,
`Outland` and `Dire Maul`, the last of which `legacy_cata` also lacks).

The second reason is that **8.1.0.27826 no longer works**. wago.tools now answers `200` with an
**empty body** for `Map`, `SpellName` and `ManifestInterfaceData` at that build and `404`s
`TalentTab`. The first `legacy_mop` run against it produced no `worldmaparea.json`, no
`spells.json`, no icons and no talents, failing with a cryptic
`The given key 'ID' was not present in the dictionary` - because the extractor cached the
zero-byte responses and only tripped over them later. The downloader now rejects an empty
response instead of caching it. `Json/dbc/legacy_cata/` predates the regression; refreshing it
would need the Cata Classic build (`4.4.2.60895`).

### DBC — already solved, not a phase

The original Phase 4 assumed a DB2/DB6 parser was needed. It is not, for two reasons:

1. The 4.3.4 client still ships **`WDBC`**, not DB2: `WorldMapArea.dbc` (211 rows, 14 cols,
   56-byte rows), `Map.dbc`, `AreaTable.dbc`, `LiquidType.dbc` — 328 `.dbc` versus only 5
   `.db2` (all item-related, none map-related), in `Data\enUS\locale-enUS.MPQ`.
2. **More importantly, the bot never reads DBC from the client at all.**
   `Utilities/ReadDBC_CSV` pulls CSVs from [wago.tools](https://wago.tools) and emits JSON
   into `json/dbc/{version}/`. `json/dbc/legacy_cata/WorldMapArea.json` **already exists**,
   alongside `cata` and `mop`.

So map metadata for the Cata era is done. No DB2 work is required for the navmesh.

### Minimap / Leaflet tiles — the index file is gone

`scripts/extract-minimap.py` stitches the client's per-ADT minimap BLPs into the Leaflet tile
pyramid. Cataclysm changed how those blocks are addressed, and the script assumed the old
form:

| | vanilla…WotLK | Cata 4.3.4 |
|---|---|---|
| block location | `textures\Minimap\<md5>.blp` | `World\Minimaps\<MapDir>\map<col>_<row>.blp` |
| index | `textures\Minimap\md5translate.trs` | **none — no `.trs` anywhere in the client** |

The script `sys.exit`ed on the missing `.trs`. Fixed the same way as the ADT reader — detect
from the client, not from config: use the `.trs` when present, otherwise discover the blocks
by scanning the archive listfiles for `World\Minimaps\<MapDir>\map<col>_<row>.blp`. Both
generations produce identical pyramid geometry (a block is 256×256 in both and is upscaled to
the script's 512 `BLP` constant, unchanged).

Cata ships *more* minimap coverage than WotLK, as expected from the new zones — Azeroth 839
blocks vs 687, Kalimdor 1105 vs 1018 — with Expansion01 (826) and Northrend (1131) also
present, so one Cata client covers the whole `cata` era. Run it with `LEAFLET_ERA=cata`.

### What Cataclysm actually changed, per continent

Cataclysm left Northrend and Outland *almost* alone, which is worth exploiting — but "almost"
is doing real work, and it lands differently for art than for geometry. Measured against the
4.3.4 client with 3.3.5 as the control.

**Minimap imagery** (40-block sample per continent, decoded and pixel-hashed):

| continent | blocks | identical | different | cata-only blocks |
|---|---|---|---|---|
| Northrend | 1131 both | 34/40 | 6 | 0 |
| Expansion01 | 800 common | 38/40 | 2 | 26 |
| Azeroth *(control)* | 687 common | 8/40 | 32 | 152 |

**Geometry** (triangle-soup hash per ADT, via `MPQTriangleSupplier`):

- **Northrend** — 52 of 60 ADTs hash identically. Of the 8 that differ, 6 have identical
  triangle *counts* but different vertex data (sub-voxel float drift from Cata re-exporting
  the ADTs), while **2 carry genuinely new collision geometry**: `29_11` gains 2454 M2
  triangles and `30_11` gains 5030.
- **Expansion01** — 4 of 5 sampled ADTs differ, mostly in liquid (water triangles 32768 →
  32644, 22528 → 20312) plus WMO/M2 counts. Outland was *not* left alone.

So the two assets get different treatment, and only one of them is shared:

- **Leaflet tiles are shared** for Northrend and Expansion01 (`DataConfig.TileEra`). A stale
  minimap block is cosmetic, the art is 85–95% identical, and it saves regenerating ~1950
  blocks (~10k tiles) per era.
- **The navmesh is never shared.** A stale mesh is not cosmetic — the bot would route
  straight through those thousands of new Northrend collision triangles. `NavmeshCacheDir`
  stays keyed on `MeshEra`, so Cata bakes its own.

`TileEra` deliberately whitelists rather than falling back on "tiles missing". A blanket
fallback would silently serve precata Azeroth art whenever the cata tiles had not been
generated yet — the wrong world, rendered confidently. Kalimdor with no cata tiles 404s
instead, which is the correct failure.

Implementation: `DataConfig.TileSharedContinents` / `TileEra` / `LeafletFor` are the single
source of truth; `Frontend/DependencyInjection` registers a `/tiles/<continent>` static route
per shared continent *before* the general `/tiles` route (static file middleware is
order-sensitive, so the specific path has to come first); `leaflet-watch.js` mirrors it with
`TILE_SHARED_CONTINENTS` / `tileEra()` / `continentConfig()` / `eraContinents()`. Verified: a
`legacy_cata` client serves Northrend and Expansion01 tiles from `precata` (Northrend renders
35/35 tiles and appears in the continent selector), Kalimdor correctly 404s, and a `wrath`
client still serves all four continents unchanged.

Also fixed while verifying: `glob("*.MPQ") + glob("*.mpq")` returns every archive twice on
Windows (case-insensitive glob), so the script opened and searched each archive twice.

`CONTINENTS` is deliberately left at the four world continents that `PPatherService.WorldContinents`
bakes; Cata's Deepholm (`Deephome`, map 646) is present in the client but out of scope.

## The design

### Phase A — Cata-era ADT parsing (the only blocker)

**Status: implemented 2026-07-25.** Scope came in close to the estimate — two methods in
`PPather/Triangles/Game/MapTileFile.cs`, plus one `ChunkReader` constant and an unrelated
robustness fix in the archive layer (step 4, found only by running against a real install).
Everything else in the geometry stack was verified unchanged and needed no edits.

**1. Make `MCIN` optional.** Walk the root ADT's chunk stream and record the file offset of
each `MCNK` in encounter order, then use `MCIN` offsets when the chunk is present and the
walked offsets when it is not.

Prefer **feature detection over version gating**: branch on "did this ADT have an `MCIN`?"
rather than on `DataConfig.Exp`. That keeps one code path for both eras, needs no config
plumbing, and — because a WotLK ADT still takes the `MCIN` branch — preserves the byte-
identity bake corpus as the regression gate.

**2. Read placement chunks from `_obj0`.** If `<tile>_obj0.adt` exists, parse
`MMDX`/`MMID`/`MWMO`/`MWID`/`MDDF`/`MODF` from it; otherwise from the root as today. The
existing `HandleMDDF`/`HandleMODF`/`ChunkReader.ExtractFileNames` bodies are reusable as-is —
only the stream they read from changes. Skip the `_obj0` `MCNK` chunks and both `_tex*`
files entirely.

**3. Apply the same `MCIN`-optional walk to `ReadAreaIds`**, so `BuildAreaData` produces a
real `AreaGrid` for the Cata era instead of silently returning zeros.

**4. Mask the WDT `MAIN` has-ADT flag.** `WDTFile.HandleMAIN` recorded a grid cell as having
terrain with `file.ReadInt32() != 0`. Pre-Cata WDTs only ever store 0 or 1 there so that
worked by luck, but **Cataclysm marks its open-ocean cells with flag `2`** — 3257 of Azeroth's
4096 and 3085 of Kalimdor's. Testing the whole word therefore claims all 4096 cells have an
ADT, and the loader throws `FileNotFoundException` on the first one, so any full-continent
Cata bake died immediately. Now masks bit 0 (`AreaInfoHasAdt`), which leaves pre-Cata
behaviour byte-identical (corpus re-verified) and yields exactly the 839 / 1011 real ADTs.

**5. Tolerate a stub archive.** `Archive`'s constructor threw
`InvalidOperationException("File contains no lines.")` when an archive's `(listfile)` was
empty. Cataclysm's 1 KB `OldWorld.MPQ` placeholder is exactly that, and because the throw
happened *inside* the constructor it bypassed `ArchiveSet`'s existing
"skip archives that fail to open" guard and took down the whole set — no Cata client could
be opened at all. Now an empty or missing `(listfile)` yields an empty file list (a missing
one is caught as `IOException`), `Archive` exposes `FileCount`, and `ArchiveSet` closes and
skips any archive listing nothing rather than searching it on every lookup. The diagnostic
value of the original throw is preserved by `ArchiveSet`'s existing "no archive could be
opened" error.

Note that `ArchiveSet.Exists` already provides the `_obj0` probe, and `MPQTriangleSupplier`
needs no change — it derives paths the same way for both eras.

**Files touched:** `PPather/Triangles/Game/MapTileFile.cs` (`Read`, `ReadAreaIds`, new
`ReadObjectFile`), `PPather/Triangles/Game/ChunkReader.cs` (`MCNK` fourcc),
`PPather/StormDll/Archive.cs`, `PPather/StormDll/ArchiveSet.cs`.

**Known quirk, pre-existing and untouched:** `MPQTriangleSupplier.GetAreaIdAndZ` leaves
`wdt.loaded[index]` set, while `GetChunkData` early-returns on an already-loaded tile and
clears the flag itself. Calling `GetAreaIdAndZ` before `GetTriangles` for the same tile
therefore yields zero triangles. Not a Cata issue — worth a look independently.

### Phase B — storage abstraction (no behaviour change)

Worth doing regardless, but no longer load-bearing: it is prep for Phase C, not for Cata
geometry.

```csharp
// PPather/Storage/IGameFileSource.cs
public interface IGameFileSource : IDisposable
{
    /// Opens a logical game file by its client path
    /// (e.g. "World\Maps\Azeroth\Azeroth_32_48.adt").
    /// Returns a read-only, seekable stream, or throws if absent.
    Stream OpenRead(ReadOnlySpan<char> name);

    bool Exists(ReadOnlySpan<char> name);
}
```

- `ArchiveSet` implements it — `OpenRead` forwards to `GetStream`, which already returns
  `MpqFileStream : Stream`. `Exists` already exists. `Close` becomes `Dispose`.
- Every consumer signature changes `ArchiveSet` → `IGameFileSource`, and
  `using MpqFileStream mpq = …` → `using Stream s = …`. No consumer calls an MPQ-specific
  member, so this is mechanical.

**Verification:** six-tile bake corpus byte-identical, `CoreTests navmesh` green. Ships no
capability.

### Phase C — CASC backend (deferred; only for the re-releases)

Needed to bake *from* a Cata-Classic / MoP-Classic install directly. Not needed to *have* a
Cata-era mesh, since Phase A + a 4.3.4 client produces the `"cata"`-era artifact that those
clients consume.

**Library choice — native CascLib via P/Invoke.** The project already P/Invokes StormLib
(Ladislav Zezula); **CascLib** is the same author's CASC counterpart with a deliberately
parallel API, so a `PPather/CascDll/` wrapper mirrors `StormDll/` almost line for line:

| StormLib (MPQ) | CascLib (CASC) |
|---|---|
| `SFileOpenArchive` | `CascOpenStorage` / `CascOpenOnlineStorage` |
| `SFileOpenFileEx` | `CascOpenFile` (by name or FileDataId) |
| `SFileReadFile` | `CascReadFile` |
| `SFileGetFileSize` | `CascGetFileSize` |
| `SFileSetFilePointer` | `CascSetFilePointer` |
| `SFileCloseFile` | `CascCloseFile` |
| `SFileCloseArchive` | `CascCloseStorage` |

Source: <https://github.com/ladislav-zezula/CascLib>. One native dependency style, one build
story, and a `CascFileStream : Stream` mirroring `MpqFileStream`.

*Alternative — managed CASCExplorer (TOM_RUS lineage)*, as vendored in TheNoobBot's
`meshReader/CascLib`. No native dependency, but a much larger managed surface (BLTE decode,
encoding/root/download handlers, DB2 readers) to carry and keep current. Prefer native
CascLib for symmetry; fall back to managed only if shipping a second native DLL per-RID is a
problem.

**The naming problem.** MPQ addresses files by path string; CASC addresses them by
**FileDataId**, with an optional name→id mapping. Modern clients ship a **root** file mapping
a Jenkins96 hash of the path → FileDataId, and CascLib resolves `CascOpenFile(name, …)`
through it where the storage has name support; otherwise a community **listfile** is needed.
Keep `IGameFileSource` path-based so consumers are unchanged; id resolution stays inside the
CASC backend.

**Construction differs.** MPQ opens a *list of archive files*. CASC opens *one storage rooted
at the install directory* (the folder holding `.build.info`), via
`CascOpenStorage(installDir, localeMask)`. Local storage only; no CDN path is needed.

**Format caveat.** Cata Classic runs on the retail engine, so its ADT/M2/WMO versions may
track *current retail* rather than 4.3.4 — expect `_obj1`/`_lod` ADTs, chunked `MD21` M2s and
`FileDataId`-based `MDDF`/`MODF` variants that Phase A does not cover. Phase A validates the
*4.3.4* formats specifically.

## Factory and selection

```csharp
// PPather/Storage/GameFileSourceFactory.cs
public static IGameFileSource Open(DataConfig cfg, ILogger logger)
{
    return cfg.StorageKind switch
    {
        StorageKind.Mpq  => new ArchiveSet(logger, Directory.GetFiles(cfg.MPQ, "*.MPQ")),
        StorageKind.Casc => new CascFileSource(logger, cfg.WowInstallDir, cfg.Locale, cfg.ListfilePath),
        _ => throw new NotSupportedException(...)
    };
}
```

`StorageKind` derives from the configured client: MPQ for vanilla…4.3.4/5.4.8 (including
`legacy_cata` / `legacy_mop`), CASC for the modern re-releases. Add `WowInstallDir` /
`Locale` / `ListfilePath` to `DataConfig` for the CASC case (MPQ ignores them).

## Phasing summary

| phase | deliverable | risk | ships capability? |
|---|---|---|---|
| **A** ✅ | `MCIN`-optional MCNK walk + `_obj0` placement chunks in `MapTileFile`, stub-archive tolerance in `Archive`/`ArchiveSet` | **done** — corpus byte-identical, Cata verified against 3 zones | **yes — Cata-era geometry from a 4.3.4 MPQ client** |
| B | `IGameFileSource`, `ArchiveSet` implements it, consumers retargeted | low — byte-identity gated | no (refactor) |
| C | `CascDll/` P/Invoke + `CascFileSource`, factory, config | medium — FileDataId naming, native DLL packaging, retail-era format drift | bakes directly from a Cata/MoP Classic install |
| ~~D~~ | ~~DB2/DB6 for `WorldMapArea`~~ | — | **already done** — `json/dbc/legacy_cata/WorldMapArea.json` via `ReadDBC_CSV` |

Phase A is now the whole story for Cata-era navmesh. B and C are only needed to bake from a
CASC install rather than from an original 4.3.4 one.

## Verification

- **Phase A — results (2026-07-25):**

  *Regression — vanilla/TBC/WotLK unchanged.* Six-tile bake corpus
  (`Benchmarks --bake-profile`), pristine `HEAD` vs modified, all **byte-identical**,
  including after the `Archive`/`ArchiveSet` change:

  | tile | hash (both runs) |
  |---|---|
  | elwynn-open | `6d6edc442008b805` |
  | stormwind-wmo | `c3f074b363b36063` |
  | dunmorogh-indoor | `34a1a2d4f25ad55b` |
  | barrens-open | `12c83529658aba79` |
  | durotar-water | `fb06c81c3603f8e4` |
  | orgrimmar-wmo | `a64ee4ee73bb3e33` |

  Ground/liquid triangle counts and poly counts also identical. Whole-solution Release build
  clean; `CoreTests navmesh` green ("all landmark round-trips, tile bounds and 2601 grid
  inversions OK").

  *New capability — Cata 4.3.4 geometry loads.* Driven through the real stack
  (`MPQTriangleSupplier` over the 4.3.4 `Data` folder, `Exp=legacy_cata`), with the WotLK
  client as a control at the same world positions:

  | | Cata 4.3.4 | WotLK 3.3.5 |
  |---|---|---|
  | **Elwynn** (`Azeroth_31_48`) terrain / water tris | 65472 / 5376 | 65472 / 5376 |
  | areaId / z | 9 / 81.83 | 9 / 81.83 |
  | **Auberdine** (`Kalimdor_30_19`) terrain / water | 65536 / 30848 | 65536 / 32768 |
  | areaId / z | 2078 / 9.67 | 2078 / 10.04 |
  | **Westfall** (`Azeroth_29_51`) terrain / water | 65504 / 0 | 65504 / 0 |
  | areaId / z | 108 / 34.03 | 108 / 34.47 |

  Terrain triangle counts (hence hole masks) and area ids match the control exactly at all
  three, confirming the split-ADT MCNK walk and the unchanged MCNK header offsets. WMO/M2
  counts and water differ where Cataclysm actually changed the world — Auberdine's flooded,
  wreckage-strewn coast (M2 1587 vs 777) and Westfall's added encampment (WMO 12803 vs
  10863) — which is the content differing, not the parser.

  *Leaflet tiles.* `scripts/extract-minimap.py` now handles both client generations
  (see [above](#minimap--leaflet-tiles--the-index-file-is-gone)). Verified by exercising
  discovery + `build_pyramid` on both clients: WotLK resolves via `md5translate.trs`, Cata via
  the listfile scan, each producing 16 z6 tiles from a 4-block patch plus the same pyramid
  levels above it, with all four continents discovered on both.

  *End-to-end bake + viz (done).* Ran PathingAPI with `--exp=legacy_cata` against the 4.3.4
  client and baked 6 ADTs around Elwynn via `POST api/PPather/Bake`, ~1s per ADT (16 tiles
  each). Artifacts landed in the era-partitioned folders, leaving the precata set untouched:

  | artifact | path | result |
  |---|---|---|
  | navmesh | `Json/PathInfo/navmesh/cata/Azeroth/bf03fdb6/tile_*.dnm` | 105 tiles, 4.5 MB |
  | leaflet | `Json/leaflet/cata/Azeroth/z{z}x{x}y{y}.webp` | 4505 tiles, 18.4 MB |

  `GET api/PPather/WorldRoute` across the baked mesh returned a **304-point path** from
  (-8900,-100) to (-9400,300) with z resolved to 82.4 → 61.1 — plausible Elwynn terrain
  heights. The Leaflet coverage overlay reports "ADT 32,14 - 16/16 tiles baked" over Cata-era
  Stormwind/Elwynn art, and the Babylon view renders green walkable surface with blue liquid
  tracking Elwynn's rivers and holes where trees and buildings block.

  *Frontend changes this needed:* `leaflet-watch.js` had no `Configs['cata']` block (the code
  even commented "Cata+ not yet"), and its `clientEra()` did not map `legacy_cata`/`legacy_mop`
  even though `DataConfig.ClientEra` does — so the era resolved to a key with no config and
  the map stayed blank. Added the cata block (Azeroth `resX 14848, resY 21504, offset
  {x:20,y:17}` from the extractor manifest) and the missing `legacy_*` cases. Also made the R2
  CDN base era-aware; it was hardcoded to `/precata`, so a cata client without local tiles
  would have fetched precata art.

  *Full continent bake (done).* Azeroth and Kalimdor baked end to end from the 4.3.4 client:

  | continent | ADTs | tiles baked | written | time |
  |---|---|---|---|---|
  | Azeroth | 841/841 | 13,298 | 12,912 (263 MB) | 12.7 min |
  | Kalimdor | 1013/1013 | 16,207 | 15,484 (305 MB) | 14.0 min |
  | **total** | **1854** | **29,505** | **28,396 (568 MB)** | **~27 min** |

  Sustained ~70 ADTs/min, peak process memory 2.9 GB, `precata` untouched at 51,113 tiles.
  The 1109-tile gap between "baked" and written is the empty-tile accounting noted above -
  ocean tiles with no geometry count as done and are never written. **One** tile genuinely
  failed, Kalimdor `(68,159)`, with DotRecast's `rcBuildContours: Bad outline for region 9`;
  that is a known upstream robustness edge case rather than anything Cata-specific, and at 1
  in 29,505 the runtime simply routes around the hole. Everything else on stderr was the
  usual `delaunayHull: Removing dangling face` chatter (7828 lines, all benign - the wrath
  corpus emits the same).

  Both continents verified queryable afterwards: Elwynn 304 points (z 82.4 → 61.1), Barrens
  300 points (z 12 → 0.2).

  Leaflet tiles now cover the cata era too - Azeroth 4505 and Kalimdor 5943 (46 MB total),
  with `Configs['cata']` holding both; Northrend and Expansion01 are deliberately absent
  there because they inherit precata art. All four continents render on a `legacy_cata`
  client.

  *Outland and Northrend baked as well*, since the navmesh is never shared across eras even
  where the tiles are:

  | continent | hash | ADTs | tiles baked | written | time |
  |---|---|---|---|---|---|
  | Azeroth | `bf03fdb6` | 841 | 13,298 | 12,912 (263 MB) | 12.7 min |
  | Kalimdor | `bf03fdb6` | 1013 | 16,207 | 15,484 (305 MB) | 14.0 min |
  | Expansion01 | `c480316e` | 780 | 12,480 | 8,399 (160 MB) | 7.8 min |
  | Northrend | `bf03fdb6` | 1131 | 18,096 | 17,023 (187 MB) | 17.3 min |
  | **total** | | **3765** | **60,081** | **53,818 (915 MB)** | **~52 min** |

  Expansion01 lands in its **own hash directory** because `NavmeshBakeOptions.MinWorldZ`
  sets a -700 floor for it (the floating-landmass filter that drops Outland's unreachable
  base terrain); every other continent uses the default settings hash. That also explains its
  larger baked-vs-written gap - 4081 tiles voxelize to nothing once the base terrain is
  filtered out.

  Outland and Northrend baked with **zero** tile failures; the only one across all four
  continents remains Kalimdor `(68,159)`. Routes verified on both new continents: Northrend
  243 points (z 53.4 → 99.1), Outland 282 / 785 / 52 points (z ~20-24 in Hellfire).
  Peak process memory 3.4 GB, `precata` still untouched at 51,113 tiles.

  *Published to R2 and validated on the CDN.* `scripts/upload-navmesh-r2.py --era cata` and
  `scripts/upload-leaflet-r2.py --era cata`, both of which were already era-aware:

  - `navmesh/index.json` now lists **8** bundles (4 precata + 4 cata). All 8 zips serve `206`
    with `Content-Range` totals matching their index `bytes` exactly.
  - Leaflet: a random 40-tile sample of the 10,448 cata tiles all served; the
    `resolveTileBase` probe tiles resolve for both Azeroth and Kalimdor, so a remote user
    with no local tiles falls through to the CDN correctly.
  - Northrend and Expansion01 correctly **404 under `/cata/`** and serve under `/precata/`,
    which is the shared-art fallback working end to end on the CDN rather than only locally.
  - Both offline bundles (`cata-tiles.zip`, `precata-tiles.zip`) are present.

  **One bug had to be fixed before publishing.** `upload-navmesh-r2.py` built
  `index = {"continents": []}` from only the bundles it had just uploaded and `put_object`'d
  it wholesale. An era-scoped run (`--era cata`) would therefore have **erased the four live
  precata entries**, breaking `download-navmesh.ps1` for every existing user - the tiles
  themselves would still be in the bucket, but nothing would reference them. It now GETs the
  current index, merges by `(era, continent, hash)`, and refuses to overwrite an index it
  cannot parse rather than silently replacing it.

  *Wart found on the way:* `POST api/PPather/Bake`'s `adtX`/`adtY` are
  `ChunkedTriangleCollection` grid indices, which are **transposed** relative to ADT file
  naming (`MPQTriangleSupplier` derives chunk_x from world Y). `adtX=31&adtY=48` looks up
  `Azeroth_48_31.adt`. Worse, when the ADT does not exist the bake still reports
  "16 tiles baked" — empty tiles count as done and are never written — so a wrong-way-round
  request looks like a success with an empty output directory.
- **Phase B:** corpus byte-identical, whole-solution build clean. No new behaviour.
- **Phase C:** open a real Cata-Classic install, read one known ADT by path, confirm the
  bytes match a reference extraction. No bake yet.

## Open questions / unknowns

- **Cata content drift 4.3.4 → 4.4.x.** `ClientEra` asserts `legacy_cata` and `cata` share
  geometry. They share a world (both post-Shattering), but Cata Classic has had content
  patches. Worth spot-checking a few tiles before treating a 4.3.4-baked mesh as
  authoritative for Cata Classic.
- **MoP 5.4.8 — probed 2026-07-26, NOT supported; two concrete blockers.** See
  [MoP findings](#mop-548--measured-not-yet-supported).
- **Listfile dependency (Phase C).** Do the Cata/MoP-Classic roots carry usable name hashes,
  or is a community listfile required? If required, it becomes a shipped data file with its
  own update story. Determine before committing to Phase C's scope.
- **Native DLL packaging (Phase C).** CascLib must be built and shipped per-RID alongside
  StormLib.
- **Locale (Phase C).** CASC storages are locale-partitioned; read the installed locale from
  `.build.info` rather than hard-coding.

## Reference

- CascLib (native, for Phase C): <https://github.com/ladislav-zezula/CascLib>
- StormLib (already used, same author): `PPather/StormDll/` is the wrapper template
- TheNoobBot `meshReader/CascLib` — a working managed CASC reader (CASCExplorer lineage) and
  an `MpqManager` that already unifies MPQ + CASC; useful as a reference implementation
- wowdev wiki — ADT/WMO/M2 format documentation across versions
