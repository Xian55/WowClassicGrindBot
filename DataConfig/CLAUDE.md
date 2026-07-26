# DataConfig — where every data path comes from

One class, and it is the single source of truth for **every** on-disk location the app
reads or writes. Change a path here, not at a call site.

`Root` comes from `data_config.json` in the **process working directory** (default
`..\json`), so which file wins depends on which project you run. There is no env-var
override; `BakeTool --root` exists precisely because of that.

## Partitioning — three different keys, on purpose

| key | properties | why |
|---|---|---|
| **era** (`ClientEra`) | `Navmesh`, `Leaflet`, `Road`, `AreaGrid` | clients that share a world share these |
| **client** (`Exp`) | `ExpDbc`, `Subzones`, `ExpArea`, `NpcSpawnLocations`, `ExpHistory`, ... | id spaces and addon mappings differ per client |
| **neither** | `MPQ`, `Class`, `Path`, `PPather` | one directory, whatever you point it at |

`ClientEra`: vanilla/tbc/wrath and their `legacy_*` → `precata`; `cata`/`legacy_cata` →
`cata`; `mop`/`legacy_mop` → `mop`.

## Things that will bite you

**`MPQ` is not era- or client-aware.** It is `Join(Root, "MPQ")` full stop, so a root
can only hold one client's archives. Running `--exp=cata` against wrath archives loads
wrath geometry silently and only fails on a continent wrath lacks. To use another
client, build a mirror root: junction every `Json/*` subdir back to the repo except
`MPQ`, which points at that client's `Data`.

**`TileEra` is not `ClientEra`.** Minimap art is shared for `TileSharedContinents`
(Northrend, Outland) on the **cata era only**, because that art was measured against
precata and found effectively identical. Navmesh tiles are *never* shared — a stale mesh
routes the bot through solid geometry. Mists is deliberately excluded from the sharing
whitelist: its art was never compared. Measure before adding to it, and never turn it
into a blanket "fall back if missing" — that would serve precata *Azeroth* art to a cata
client, which is a different world.

**`LeafletEras` / `HasLeaflet` gate the UI.** Gate on these, never on a
`ClientVersion` list; the map stayed hidden on wrath/cata/mop for exactly that reason.

**Keep the JS in step.** `Frontend/wwwroot/script/leaflet-watch.js` mirrors `ClientEra`,
`TileEra`, `TileSharedContinents` and `LeafletEras`. Changing one without the other
renders one era's art against another's data.

**TFM is plain `net10.0`** so the pathing chain runs on macOS/Linux. Do not take a
Windows-only dependency here.
