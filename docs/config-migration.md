# Config migration: `PPATHER_*` env vars → DI Options

The scattered `PPATHER_*` environment variables that the navmesh engine and the
spline follower read at start-up have been replaced by the standard .NET
configuration/Options pipeline. Each knob is now a bound option, settable three
ways (highest precedence last):

| source | example |
|---|---|
| `appsettings.json` (or `headless_appsettings.json`) | `"Navmesh": { "Bake": { "AgentRadius": 0.5 } }` |
| environment variable (`:` becomes `__`) | `Navmesh__Bake__AgentRadius=0.5` |
| command line | `--Navmesh:Bake:AgentRadius=0.5` |

**Breaking:** the old flat `PPATHER_*` names are no longer read. Rename per the
tables below. Defaults are unchanged, so leaving everything unset behaves exactly
as before (and the navmesh tile cache directory hash is unchanged).

## Navmesh bake — section `Navmesh:Bake` (`NavmeshBakeOptions`)
These feed the tile-cache settings hash; a change gives a distinct cache dir.

| old env var | new config key | default |
|---|---|---|
| `PPATHER_AGENT_RADIUS` | `Navmesh:Bake:AgentRadius` | 0.5 |
| `PPATHER_AGENT_MAX_CLIMB` | `Navmesh:Bake:AgentMaxClimb` | 1.6 |
| `PPATHER_WALKABLE_SLOPE` | `Navmesh:Bake:WalkableSlope` | 48 |
| `PPATHER_BAKE_WORKERS` | `Navmesh:Bake:BakeWorkers` | (unset = auto) |
| (new) | `Navmesh:Bake:MinWorldZ:<Continent>` | (unset = keep all) |

`MinWorldZ` is a **per-continent** world-Z floor: geometry whose highest vertex
is below it is dropped, for that continent only. Use it for floating continents:
Outland's MPQ still carries a low "base" landmass (~Z -1190) that would otherwise
bake as a walkable death-fall floor. Configure a cutoff between the base and the
lowest real ground:

```json
"Navmesh": { "Bake": { "MinWorldZ": { "Expansion01": -700 } } }
```

The resolved value feeds the settings hash, so a filtered continent bakes into
its own cache directory while unlisted continents (Azeroth, Kalimdor) keep their
existing hash and bake untouched.

**Set it in config, not only on the bake CLI.** The hash must match between the
bake and every later run (runtime also bakes tiles on demand and must filter
them the same way). Putting it in `appsettings.json` guarantees bake and run
resolve the same cache directory; a value passed only to the bake but not the
run would look in a different (unfiltered) directory. Equivalent CLI form, if you
pass it identically to both bake and run:

```powershell
--Navmesh:Bake:MinWorldZ:Expansion01=-700
```

## Navmesh query — section `Navmesh:Query` (`NavmeshQueryOptions`)
Applied per request; no rebake.

| old env var | new config key | default |
|---|---|---|
| `PPATHER_PATH_EDGE_MARGIN` | `Navmesh:Query:EdgeMargin` | 1.5 |
| `PPATHER_PATH_SPACING` | `Navmesh:Query:PathSpacing` | 2.5 |
| `PPATHER_CORRIDOR_TILES` | `Navmesh:Query:CorridorTiles` | 2 |
| `PPATHER_CORRIDOR_MAX_TILES` | `Navmesh:Query:CorridorMaxTiles` | 4 |
| `PPATHER_CORRIDOR_WIDEN` (`0` disabled) | `Navmesh:Query:CorridorWiden` (`true`/`false`) | true |
| `PPATHER_CORRIDOR_WIDEN_SHORT` (`1` = widen anyway) | `Navmesh:Query:SkipWidenShortRoutes` (`true`/`false`) | true |
| `PPATHER_ROAD_CORE` | `Navmesh:Query:RoadCore` | 8 |

**Note the inversion:** the old `PPATHER_CORRIDOR_WIDEN_SHORT=1` meant "widen even on
short routes". The new key states the default behaviour directly:
`SkipWidenShortRoutes=true` (skip widening on short routes). To widen regardless,
set `SkipWidenShortRoutes=false`.

## Spline follower — section `SplineFollower` (`SplineFollowerOptions`)

| old env var | new config key | default |
|---|---|---|
| `PPATHER_SPLINE_FOLLOWER` (`1`) | `SplineFollower:Enabled` (`true`) | false |
| `PPATHER_FOLLOW_MAP_HEADING` (`1`) | `SplineFollower:UseMapHeading` (`true`) | false |
| `PPATHER_FOLLOW_DEADBAND_ON` | `SplineFollower:DeadbandOn` | 0.14 |
| `PPATHER_FOLLOW_DEADBAND_OFF` | `SplineFollower:DeadbandOff` | 0.052 |
| `PPATHER_FOLLOW_TAU_OUT` | `SplineFollower:TauSteerOutdoor` | 0.6 |
| `PPATHER_FOLLOW_TAU_IN` | `SplineFollower:TauSteerIndoor` | 0.35 |
| `PPATHER_FOLLOW_LMIN_OUT` | `SplineFollower:LookAheadMinOutdoor` | 3 |
| `PPATHER_FOLLOW_LMAX_OUT` | `SplineFollower:LookAheadMaxOutdoor` | 15 |
| `PPATHER_FOLLOW_LMIN_IN` | `SplineFollower:LookAheadMinIndoor` | 1.2 |
| `PPATHER_FOLLOW_LMAX_IN` | `SplineFollower:LookAheadMaxIndoor` | 4 |
| `PPATHER_FOLLOW_BRAKE_T` | `SplineFollower:BrakeWindowSeconds` | 0.8 |
| `PPATHER_FOLLOW_BRAKE_SAFETY` | `SplineFollower:BrakeSafety` | 0.85 |
| `PPATHER_FOLLOW_BRAKE_RELEASE` | `SplineFollower:BrakeRelease` | 0.70 |
| `PPATHER_FOLLOW_FWD_HOLD_MS` | `SplineFollower:ForwardMinHoldMs` | 150 |
| `PPATHER_FOLLOW_OFFPATH_OUT` | `SplineFollower:OffPathOutdoor` | 4 |
| `PPATHER_FOLLOW_OFFPATH_IN` | `SplineFollower:OffPathIndoor` | 3 |
| `PPATHER_FOLLOW_INPUT_LAT_MS` | `SplineFollower:InputLatencyMs` | 30 |

## Logging

| old env var | new config key | default |
|---|---|---|
| `PPATHER_LOG_DEBUG` (`1`) — PathingAPI | `Logging:PatherDebug` (`true`) | false |

## PathingAPI startup bake

`--bake=<continent>` kicks a background navmesh bake as PathingAPI starts, using
the `--exp` expansion. The API serves immediately; poll
`GET api/PPather/Bake/Status`. Tiles already on disk are skipped.

| flag | effect |
|---|---|
| `--bake=Azeroth` | bake the named continent |
| `--bake=all` (or `--bake=*`) | bake every continent |
| (omitted) | no startup bake |

```powershell
dotnet run --project PathingAPI -c Release -- --exp=tbc --bake=Azeroth
```

The expansion is always the service's `--exp` (a PathingAPI instance loads one
expansion's data); there is no separate per-bake expansion.

## Example: reproduce the old in-game bake/tuning session

Before (PowerShell):
```powershell
$env:PPATHER_AGENT_RADIUS = "0.5"
$env:PPATHER_WALKABLE_SLOPE = "48"
$env:PPATHER_PATH_EDGE_MARGIN = "1.5"
$env:PPATHER_SPLINE_FOLLOWER = "1"
dotnet run --project BlazorServer -c Release
```

After — either edit `appsettings.json`, or as env vars:
```powershell
$env:Navmesh__Bake__AgentRadius = "0.5"
$env:Navmesh__Bake__WalkableSlope = "48"
$env:Navmesh__Query__EdgeMargin = "1.5"
$env:SplineFollower__Enabled = "true"
dotnet run --project BlazorServer -c Release
```

…or entirely on the command line:
```powershell
dotnet run --project BlazorServer -c Release -- `
  --Navmesh:Bake:AgentRadius=0.5 --Navmesh:Query:EdgeMargin=1.5 --SplineFollower:Enabled=true
```
