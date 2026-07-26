# Utilities — offline data tools

One-shot console tools that generate data into `Json/`. **None of these are in
`MasterOfPuppets.sln`**, which has two consequences worth internalising:

* `dotnet build MasterOfPuppets.sln` does **not** build them.
* `dotnet run --no-build` therefore silently runs a **stale binary**. Build the project
  directly (`dotnet build Utilities/<Tool>`) or omit `--no-build`.

That trap has already cost a wasted run: a new `BakeTool` flag appeared to do nothing
because the old binary ran and fell through to a different code path.

| project | purpose |
|---|---|
| `BakeTool` | cross-platform navmesh + area-grid bake CLI (the only bake entry point that is not `net10.0-windows`) |
| `ReadDBC_CSV` | download wago.tools CSVs and emit `Json/dbc/<client>/*.json` |
| `WowheadDB_Extractor` | zone NPC/herb/vein data from Wowhead |
| `ClassifyVendorFlags`, `TransferTrainerFlags` | NPC flag maintenance |
| `DangerZoneGenerator` | authored danger-zone data |
| `PathMaker`, `ReadItems` | loose scripts, no csproj |

## BakeTool

```powershell
dotnet build Utilities/BakeTool -c Release
dotnet run --project Utilities/BakeTool -c Release -- --exp legacy_mop --root ~/wcgb-data --continent HawaiiMainLand
dotnet run --project Utilities/BakeTool -c Release -- --exp legacy_mop --root ~/wcgb-data --area-grid
```

`--root` is what makes it usable against a client other than the one `Json/MPQ` holds:
point it at a directory whose `MPQ` is that client's `Data` (a junction works) and whose
other subdirs junction back to the repo, so output lands in the real tree.

**`--min-world-z` feeds the settings hash.** It defaults to `Expansion01=-700` to match
`appsettings.json`; baking Outland without it writes to a different cache dir *and*
bakes the floating-landmass death floor.

**`--area-grid` is per client, not per era.** `subzones` is `Join(Root,"subzones",Exp)`,
so `--exp legacy_mop` does not populate `subzones/mop`. Run it for each `Exp` you care
about — `ReadDBC_CSV`'s worldmap extractor reads that directory.

## ReadDBC_CSV

```powershell
dotnet run --project Utilities/ReadDBC_CSV -c Release -- -v legacy_mop            # all extractors
dotnet run --project Utilities/ReadDBC_CSV -c Release -- -v cata worldmap --clean # one extractor
```

**The CSV cache is keyed by filename, not by build.** Switching `-v` reuses the previous
build's CSVs and silently produces a file built from the wrong expansion — it prints
"exists, skipping download" while doing it. **Always pass `--clean` when changing `-v`.**
Validate by content, not row count: a cata worldmaparea must contain Vashj'ir and Uldum
and must not contain The Jade Forest.

**`Legacy_Cata` is pinned to `8.1.0.27826`, which now serves empty `Map`/`SpellName`
tables.** Regenerating it produces a worse file than the committed one. If it ever needs
a refresh, move it to Cata Classic `4.4.2.60895` — but that shifts the `UiMapId` space
the addon's `WorldMapAreaIDToUiMapID.lua` maps against, so it is a deliberate decision,
not a routine regeneration.

**The `legacy_*` builds point at modern Classic re-releases on purpose.** The addons are
written against modern clients and bridge legacy ones through that Lua table, so the
json must be keyed by `UiMapID`s that exist.
