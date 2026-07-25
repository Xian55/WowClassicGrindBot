# Unifying MPQ and CASC game-data access

## Why

The navmesh bake reads terrain and object geometry (ADT / WMO / M2) straight from the
installed WoW client's data files. Today that path is MPQ-only: `MPQTriangleSupplier` opens
a set of `.MPQ` archives through StormLib (P/Invoke in `PPather/StormDll/`).

MPQ is the archive format up to and including the **original** Mists of Pandaria client
(5.4.8) and WotLK (3.3.5). Blizzard replaced it with **CASC** (Content Addressable Storage
Container) in Warlords of Draenor (6.0, 2014). The catch that matters here: the **modern
Classic re-releases — Cataclysm Classic (2024) and the upcoming MoP Classic — run on the
retail engine and ship CASC, not MPQ.** So although the bot already supports Cata and MoP at
the game-client level, the navmesh cannot be baked for those clients: StormLib cannot open a
CASC storage, so there is no geometry to voxelize.

Closing this needs a storage layer that reads the same logical files (`World\Maps\…\*.adt`
and friends) from **either** an MPQ archive set **or** a CASC storage, chosen by client
version. This document scopes that.

## What exists today

The geometry path is already funnelled through one small storage seam, which is what makes
this tractable.

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
  `DataConfig.Exp` (e.g. `"wrath"`) and `DataConfig.Version`.

The whole geometry stack touches storage through exactly two methods — `GetStream` and
`Exists` — returning a `Stream`. That is the entire seam.

## The design

### Phase 1 — storage abstraction (no behaviour change)

Introduce one interface and make MPQ implement it. Nothing about the bake changes; this is
pure refactoring, gated by the existing byte-identity corpus.

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

- `ArchiveSet` implements `IGameFileSource` — `OpenRead` forwards to `GetStream`, which already
  returns `MpqFileStream : Stream`. `Exists` already exists. `Close` becomes `Dispose`.
- Every consumer signature changes `ArchiveSet` → `IGameFileSource`, and
  `using MpqFileStream mpq = …` → `using Stream s = …`. The consumers never call an
  MPQ-specific method, so this is mechanical.
- `MPQTriangleSupplier` takes an `IGameFileSource` instead of constructing `ArchiveSet`
  directly; a factory (below) builds the right one.

**Verification for Phase 1:** the six-tile bake corpus must stay byte-identical, and the
`CoreTests navmesh` suite green. This phase ships no new capability — it only proves the seam
is clean.

### Phase 2 — CASC backend

Add `CascFileSource : IGameFileSource`. This is where the real work is.

**Library choice — native CascLib via P/Invoke (recommended).** The project already
P/Invokes **StormLib** (Ladislav Zezula) for MPQ. **CascLib** is the same author's CASC
counterpart with a deliberately parallel API, so a `PPather/CascDll/` wrapper mirrors
`StormDll/` almost line for line:

| StormLib (MPQ) | CascLib (CASC) |
|---|---|
| `SFileOpenArchive` | `CascOpenStorage` / `CascOpenOnlineStorage` |
| `SFileOpenFileEx` | `CascOpenFile` (by name or FileDataId) |
| `SFileReadFile` | `CascReadFile` |
| `SFileGetFileSize` | `CascGetFileSize` |
| `SFileSetFilePointer` | `CascSetFilePointer` |
| `SFileCloseFile` | `CascCloseFile` |
| `SFileCloseArchive` | `CascCloseStorage` |

Source: <https://github.com/ladislav-zezula/CascLib>. This keeps one native dependency style,
one build story, and a `CascFileStream : Stream` that mirrors `MpqFileStream` exactly.

*Alternative — managed CASCExplorer (TOM_RUS lineage)*, the C# reader vendored in TheNoobBot's
`meshReader/CascLib`. No native dependency, but a much larger managed surface (BLTE decode,
encoding/root/download handlers, DB2 readers) to carry and keep current. Prefer native CascLib
for symmetry with the existing StormLib path; fall back to managed only if shipping a second
native DLL per-RID is a problem.

**The naming problem — the one genuinely new concept.** MPQ addresses files by path string.
CASC addresses them by **FileDataId** (an integer), with an optional name→id mapping:

- Modern clients ship a **root** file mapping a Jenkins96 hash of the path → FileDataId.
  CascLib resolves `CascOpenFile(name, …)` through it when the storage has name support.
- Where names are unavailable, a **community listfile** (path ↔ FileDataId) is needed.
  CascLib accepts one via `CascOpenStorageEx` / a listfile path.

`CascFileSource.OpenRead(name)` therefore resolves the client path to a FileDataId (via root
or listfile) and calls `CascOpenFile`. Keep the *interface* path-based so consumers are
unchanged; the id resolution is entirely inside the CASC backend.

**Storage construction differs from MPQ.** MPQ opens a *list of archive files*
(`Directory.GetFiles(MPQ, "*.MPQ")`). CASC opens *one storage rooted at the install
directory* (the folder containing `.build.info` / `Data/`), via `CascOpenStorage(installDir,
localeMask)`. So the factory's inputs differ by backend — see below. Local storage only; no
CDN/online path is needed for a locally installed client.

### Phase 3 — version-aware chunk parsing

Storage is necessary but not sufficient: the ADT/WMO/M2 **chunk formats evolved**, and the
current parsers (`MapTileFile`, `WmoGroupFile`, `ModelFile`) assume the WotLK layout.

- **Split ADTs.** From Cataclysm on, an ADT is split across `…_x_y.adt`, `…_x_y_tex0.adt`,
  `…_x_y_obj0.adt` (and `_lod` later). Terrain geometry (MCNK/MCVT) and object placement
  (MDDF/MODF) move between the root and `_obj0`. `MapTileFile.Read` must load the right
  companion files for the client version.
- **M2 header** gained fields; **WMO** group/root chunks changed. The bounding-collision
  reads the geometry cares about (`MOPY`, bounding vertices/triangles) are stable in shape but
  offset-sensitive.

Gate this behind the client version already in `DataConfig`. This is the largest and least
mechanical phase; scope it per-format with the existing geometry as the reference (a Cata/MoP
tile that bakes to sane polys, eyeballed in the Leaflet viz).

### Phase 4 — DB2/DB6 for map metadata

`WorldMapAreaDB` and the coordinate conversions read **DBC** tables. Modern clients replaced
DBC with **DB2** (and DB6). This is off the geometry hot path — it feeds map↔world coordinate
conversion, not the bake — so it can land independently, but Cata/MoP navmesh is not *usable*
without correct `WorldMapArea` bounds. CascLib's managed cousin ships DB2 readers; the native
route needs a small DB2 parser (the format is well documented in the wowdev wiki).

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

`StorageKind` derives from the configured expansion/version — MPQ for vanilla…original-MoP
and WotLK, CASC for the modern re-releases. Add `WowInstallDir` / `Locale` / `ListfilePath`
to `DataConfig` for the CASC case (MPQ ignores them).

## Phasing summary

| phase | deliverable | risk | ships capability? |
|---|---|---|---|
| 1 | `IGameFileSource`, `ArchiveSet` implements it, consumers retargeted | low — byte-identity gated | no (refactor) |
| 2 | `CascDll/` P/Invoke + `CascFileSource`, factory, config | medium — naming/FileDataId, native DLL packaging | opens CASC files |
| 3 | version-aware ADT/WMO/M2 parsing (split ADTs) | high — format archaeology | Cata/MoP geometry |
| 4 | DB2/DB6 for `WorldMapArea` metadata | medium | usable Cata/MoP coords |

Phase 1 stands alone and is worth doing regardless — it isolates storage behind an interface
and is the cheapest, lowest-risk step. Phases 2–4 are only for actually baking the modern
Classic clients and can be sequenced later.

## Verification

- **Phase 1:** six-tile bake corpus byte-identical (`bench.ps1 -Bake`), `CoreTests navmesh`
  green, whole-solution build clean. No new behaviour.
- **Phase 2:** open a real Cata-Classic install, list and read one known ADT by path, confirm
  bytes match the client (spot-check against a known tool, e.g. a wow.tools export). No bake
  yet — just prove the storage backend reads the right bytes.
- **Phase 3:** bake one Cata/MoP tile, render it in the Leaflet/Babylon viz, confirm the
  walkable surface matches the terrain (no holes, no runaway polys). Compare poly counts to a
  reference tool if available.
- **Phase 4:** a known landmark's world↔map round-trip matches the client's map, the same way
  `NavmeshCoords` is validated for WotLK today.

## Open questions / unknowns

- **Listfile dependency.** Do the Cata/MoP-Classic roots carry usable name hashes, or is a
  community listfile required? If required, it becomes a shipped/managed data file with its own
  update story. Determine before committing to Phase 2's scope.
- **Native DLL packaging.** CascLib must be built and shipped per-RID alongside the existing
  StormLib native. Confirm the build/pack story (the DotRecast submodule precedent shows the
  repo already handles native-ish externals, but CascLib is a classic C++ DLL).
- **Locale.** CASC storages are locale-partitioned; the extractor must open the installed
  locale. Read it from the install's `.build.info` rather than hard-coding.
- **Format drift within "Cata/MoP Classic."** These re-releases are patched on the retail
  engine, so their file/table versions may track *current retail* rather than the historical
  4.3.4/5.4.8 formats. Phase 3/4 parsing should target the shipped Classic client version, not
  the original expansion's documented formats.

## Reference

- CascLib (native, recommended): <https://github.com/ladislav-zezula/CascLib>
- StormLib (already used, same author): the `PPather/StormDll/` wrapper is the template
- TheNoobBot `meshReader/CascLib` — a working managed CASC reader (CASCExplorer lineage) and
  a `MpqManager` that already unifies MPQ + CASC; useful as a reference implementation
- wowdev wiki — ADT/WMO/M2 and DB2 format documentation across versions
