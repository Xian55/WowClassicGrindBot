# Known gap: six zones have no area data on the Mists clients

`legacy_mop` and `mop` have no `Json/area/<client>/<id>.json` for:

| id | zone |
|---|---|
| 51 | Searing Gorge |
| 357 | Feralas |
| 361 | Felwood |
| 490 | Un'Goro Crater |
| 616 | Mount Hyjal |
| 1377 | Silithus |

A missing file is not an error. `AreaDB` logs `no ..\json\area\<client>\<id>.json` at
Debug and carries on with no vendor, repair, trainer or flightmaster for that zone, so
`AdhocNPCGoal` simply never fires there. Gathering and danger-zone data are absent too.

The Cataclysm clients do **not** have this gap — see the fix below for why.

## Cause: SkyFire's old-world coverage is thin

Both Mists clients are built from the SkyFire 5.4.8 dump, which is the correct era for
them and the only one of the two dumps carrying Pandaria. Its old-world Kalimdor content
is sparse enough that these zones produce nothing at all. Measured inside Feralas'
`WorldMapArea` rect:

| dump | spawns | of those, flagged NPCs |
|---|---|---|
| SkyFire 5.4.8 | 1575 | 13 |
| TDB 4.3.4 | 4721 | 74 |

`cata` and `legacy_cata` were rebuilt from TDB 4.3.4 (PR #821) and have all six zones.
The MoP clients cannot simply switch dumps: 4.3.4 has no Pandaria.

## Attempted fix, and where it broke

A `--fill-from <dump>` option: run the primary dump, then a second dump, and adopt from
the second **only zones the primary produced nothing for**. Whole-zone granularity is
deliberate — merging two eras inside one zone would mix a 4.3.4 vendor list with a 5.4.8
one and leave no way to tell which is which.

The mechanics worked. Running SkyFire as primary with 4.3.4 as fill reported exactly the
expected six zones adopted:

```
fill: +6 zone(s) the primary had nothing for: 51, 357, 361, 490, 616, 1377
```

**But the adopted zones came out with `herb` and `vein` nodes only — no NPCs.**
`legacy_mop/357.json` contained `{'herb': 4, 'vein': 5}` and nothing else, and Innkeeper
Greul appeared in no file on that client.

This is not an era-resolution problem. Running the 4.3.4 dump **alone** against the
`legacy_mop` client places everything correctly:

```
zones with data: 97
flightmaster=286  innkeeper=205  repair=791  vendor=2572  trainer=1139  skinnable=976
```

So the same dump, the same client zone data and the same `place()` produce full NPC lists
standalone, but node-only zones through the fill path. The defect is in the merge, and it
was not isolated before the option was pulled from PR #821.

Ruled out while investigating:

- **Grids** — `Json/area_grid/mop/` has `Kalimdor.grid`, so map 1 spawns are not
  `no grid` drops. (`no grid=4446` on a fill run is simply both dumps' drops summed;
  `drops` is shared across the two `collect()` calls.)
- **Nested zones** — nothing nests inside Feralas' rect in either era's zone set, so
  `descend()` is not stealing them.
- **Per-dump state** — `seen`, `skinnable`, `chests`, `herb_locks` and `npc_meta` are all
  local to `collect()`.

Unverified hypothesis worth testing first: the fill's NPCs resolve into a zone the primary
already covered, so whole-zone granularity discards them while their nodes land in a zone
the primary lacked. If that is what happens, the fix is to merge per **category** within a
zone, not per zone — or to record provenance per entry.

## Reproducing

```powershell
# the gap
python scripts/extract-area-sqldump.py <skyfire.sql> --client legacy_mop --dry-run

# full data from the wrong-era dump, proving resolution is fine
python scripts/extract-area-sqldump.py <tdb434.sql> --client legacy_mop --dry-run
```

Dumps used: `SFDB_full_548_25.001_2026_007_19_Release.sql` and
`TDB_full_world_434.22011_2022_01_09.sql`.

## Related

`scripts/CLAUDE.md` records the three rules this work established: which dump each era
uses, that a `legacy_` client takes zone-hood from its non-legacy sibling rather than its
own `WorldMapArea`, and that emulator column names differ by case between cores.
