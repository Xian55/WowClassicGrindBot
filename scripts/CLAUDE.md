# scripts — data generation and CDN distribution

Everything here produces or moves the data the bot needs at runtime. Nothing here is
built by the solution; Python scripts need `pip install`, PowerShell ones are
dependency-free by design (users double-click the `.bat` wrappers in `.bat/`).

| script | what it does |
|---|---|
| `download-navmesh.ps1` | pull pre-baked navmesh tiles from the CDN into `<Root>/PathInfo/navmesh/<era>` |
| `download-leaflet.ps1` | pull minimap tiles into `<Root>/leaflet/<era>` |
| `upload-navmesh-r2.py` | zip + upload navmesh bundles, maintain `navmesh/index.json` |
| `upload-leaflet-r2.py` | per-tile or `--zip` bundle upload, maintain `leaflet/index.json` |
| `extract-minimap.py` | generate leaflet tiles from a client's minimap BLPs (needs Pillow + StormLib) |
| `extract-creatures-sqldump.py` | `Json/dbc/<client>/creatures.json` straight from a mysqldump `.sql` - no MySQL server needed |
| `extract-area-sqldump.py` | `Json/area/<client>/<areaId>.json` from the same dump, replacing the Wowhead scrape |
| `extract-tele-sqldump.py` | merge a dump's `game_tele` (the `.tele` GM command) into `teleport_locations.txt` |
| `build-stormlib-unix.sh` | build `libstorm.dylib`/`.so` for macOS/Linux bakes |
| `migrate-json-era.{ps1,sh}` | move a pre-era `Json/` layout into `<era>/` subdirs |

## Credentials

Uploads read **env vars only** — `R2_ACCOUNT_ID`, `R2_BUCKET`, `R2_ACCESS_KEY_ID`
(or `R2_AC`), `R2_SECRET_ACCESS_KEY` (or `R2_SAK`). Never put a secret on the command
line. `--dry-run` needs no credentials.

## Things that will bite you

**An index describes the whole bucket, not your run.** Both uploaders GET and *merge*
`index.json` before writing it. Publishing `--era mop` with a wholesale rewrite would
strip the precata and cata entries and break every existing user's download. If you add
a third artifact type, copy that pattern.

**Cloudflare overrides the index cache header.** The scripts set `max-age=300` on
`index.json`, but a cache rule serves it as `max-age=14400`, so a plain URL can return a
stale index for hours after upload. Verify with a cache-buster (`?cb=<random>`);
`cf-cache-status: HIT` vs `MISS` is the tell. Request-side `no-cache` is ignored.

**Use a range GET, not HEAD, on R2 zips.** PowerShell's HEAD returns an empty status
against them; `-Headers @{Range="bytes=0-0"}` returns 206 plus `Content-Range` with the
true size. HEAD is fine for the small `.webp` tiles and for boto3's `head_object`.

**Cloudflare 1010s a default user agent.** Every script sends a browser UA; keep it.

**`extract-minimap.py`: index listfiles BEFORE chaining patches.** Attaching a PTCH
patch also patches `(listfile)`, which hides base entries — that mistake silently
dropped whole continents. Marlamin's `MPQReader.cs` orders it the same way.

**Emulator schemas drift, so read the CREATE TABLE rather than assuming columns.**
`extract-creatures-sqldump.py` resolves each field by trying candidate names, because
SkyFire (5.4.8) splits `faction` into `faction_A`/`faction_H` and renames `rank` to
`npc_rank`. A query written for 3.3.5 TrinityCore does not error on those - it selects
the wrong column or fails late, which is exactly how a bad dataset gets shipped.

**Check what a creature dump actually covers before trusting it.** `Json/dbc/cata`,
`legacy_cata` and `mop` all shared one file that was a *Wrath* dump - ids 1..43282, zero
MoP-era entries - so every Cata and Pandaria NPC was nameless. Bucket the ids
(`<20000` vanilla, `<30000` tbc, `<40000` wrath, `<54000` cata, above that MoP) before
assuming an expansion is represented.

**Regenerate a client and its `legacy_` twin together.** `cata`/`legacy_cata` and
`mop`/`legacy_mop` describe the same world and their `creatures.json` must stay
byte-identical; all four are now the same 56661-entry SkyFire build. Fixing only one of
a pair is how the Wrath file survived in three of the four folders long after it was
documented as wrong. The symptom is indirect: `area/<id>.json` loads fine (HTTP 200) but
the zone renders nothing, because `addNpcSpawns` looked up a skinnable id that
`creatures.json` did not have. **These files are read once at host startup** - a
regenerated `creatures.json` does nothing until the server is restarted.

**Wowhead node coordinates are percentages of the map its *zone page* uses, which is
not always the zone whose id names the file.** Cataclysm split Stranglethorn Vale (5339)
into Northern Stranglethorn (33) and the Cape (5287); wowhead's zone=5339 data is
plotted against the Cape, but the frontend rescales it with 5339's bounds, which span
both halves - so every node landed a thousand yards out to sea. There is no correction
factor for this, because a single file mixes both maps.
`extract-area-sqldump.py` sidesteps it: assign each spawn a zone from the area grid,
then convert with *that same zone's* bounds, so what is written and what the frontend
reads back are the same rectangle by construction. Validate a regenerated file by
picking an NPC that also exists pre-Cata and diffing against the old data - Innkeeper
Farley in Elwynn reads `[43.8, 65.8]` in som/tbc/wrath and `[43.77, 65.8]` now.

**The area grid is a top-down 2D projection, so it cannot see interiors.** Ironforge and
Undercity have no cells of their own; 93 of Ironforge's 94 vendors come back as "Dun
Morogh" and file under the enclosing outdoor zone. The data is present and correctly
placed, just under the parent zone id. Do not try to fix this by preferring the smallest
`WorldMapArea` rect that contains the point - Northern Stranglethorn's rect and the
Cape's overlap heavily, so that recreates the cross-zone misplacement above. The grid
wins wherever it has an answer; rects only fill bake holes.

**Zone rectangles overlap, so a bounding-box query is not a zone query.** All 636 dump
spawns inside Un'Goro Crater's rect are Zul'Farrak / Noxious Lair / Sandsorrow content.
Un'Goro, Silithus and Mount Hyjal really are near-empty in SkyFire 5.4.8 (2-5 spawns
each) - confirmed by resolving through the grid, not the rect. Check that way before
concluding a zone is missing.

**`teleport_locations.txt` exists twice and both are tracked.** `Frontend/wwwroot` is
served to the Leaflet page, `PathingAPI/wwwroot` is read from disk by
`SearchParameters.razor`; they are kept byte-identical and a glob turns up ~25 more
copies that are all `bin/` artefacts. The format is positional -
`id x y z orientation mapId name` - and both readers use only x, y, z, mapId and name,
but `id` and `orientation` must stay present or the fields shift. **The files are CRLF**:
reading them in Python text mode and writing back with `\n` rewrites all 1002 existing
lines and buries the additions in a whole-file diff.

**A world DB's `game_tele` is a free POI list.** It backs the `.tele` command, so every
row is a named, reachable landmark already in world coordinates. The committed file came
from a WotLK DB truncated at `LIMIT 1000` (Northrend had 2 entries); SkyFire 5.4.8 adds
531 more, including the first entries for Pandaria (870), Deepholm (646), Lost Isles
(648) and the Maelstrom (730). Merge by name and keep the existing row on a collision -
the old coordinates are pre-Shattering and that is what pre-Cata users need.

**Downloads delete the target dir before extracting.** `.NET`'s `ExtractToDirectory`
cannot merge, so `-Force` discards locally generated tiles for that era. The
already-present skip is what keeps that from happening unasked.
