# WowheadDB — zone data model

Three POCOs and nothing else. They describe the per-zone data scraped from Wowhead:
service NPCs, herb/vein nodes, and which mobs are skinnable/minable/etc.

```
Area/Area.cs   one zone: flightmaster, innkeeper, repair, vendor, trainer,
               herb, vein, skinnable, gatherable, minable, salvegable
NPC/NPC.cs     coords, level, name, type, id, reacthorde, reactalliance, description
Herb/Node.cs   coords, level, name, type, id
```

**Producer:** `Utilities/WowheadDB_Extractor` writes `Json/area/<client>/<areaId>.json`.
**Consumer:** `Core/Database/AreaDB.cs` deserializes the file for the zone the player is
in, on a background thread, whenever the area changes. `Core/Goals/LootGoal.cs` and
`Core/Path/RouteInfo.cs` read the result.

## Things that will bite you

**These are public *fields*, not properties, matched by name against the JSON.** There
are no `[JsonProperty]` attributes — Newtonsoft binds on the identifier, so **renaming a
field silently breaks deserialization** into a null/empty collection rather than an
error. Same for casing: the JSON keys are lowercase and the fields are lowercase to
match.

**`salvegable` is misspelled, and that spelling is the wire format.** It is the key in
every generated `Json/area/**/*.json`, so "fixing" the field name breaks every file on
disk and every published data set. Correcting it means regenerating all area data across
all clients — treat it as a data migration, not a typo fix.

**`coords` are map coordinates, not world coordinates.** They are `[[x, y]]` pairs in the
0-100 zone space (e.g. `[[30.0, 71.4]]`), which is why `MapCoords` is named that way.
Convert through `WorldMapAreaDB` before handing them to a pathfinder — passing them
straight to a world-coordinate API yields a point near the map origin.

**`coords` is a list of lists because one entry can have many spawns.** A single vendor
id may appear at several points; do not assume `coords[0]` is the only one.

**Data is partitioned per client, not per era** (`DataConfig.ExpArea` →
`Json/area/<Exp>`), because zone and area ids differ between clients. A missing
`<areaId>.json` is normal — not every area has scraped data — so `AreaDB` must tolerate
it rather than throw.
