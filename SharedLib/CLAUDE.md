# SharedLib — the common bottom layer

Depended on by `Core`, `Game`, `PPather`, `PathingAPI`, `WowheadDB` and most of
`Utilities/`. That breadth is the point and the constraint: **a change here reaches
everything**, including the tools that regenerate `Json/` data.

TFM is plain **`net10.0`** (overriding the repo-global `net10.0-windows`) so `PPather`,
`BakeTool` and a future headless server run on macOS/Linux. **Do not take a Windows-only
dependency here** — it would drag the whole pathing chain back onto Windows. Screen and
NPC-finding types in here are deliberately interfaces or pure geometry; the Windows
implementations live in `Core`/`WinAPI`.

## Layout

```
StartupConfig/       DI options bound from configuration
  NavmeshBakeOptions.cs      "Navmesh:Bake"    - feeds the tile cache hash
  NavmeshQueryOptions.cs     "Navmesh:Query"   - query-time only, never rebakes
  SplineFollowerOptions.cs   "SplineFollower"
  StartupConfigPathing.cs    "Pathing"         - Engine, RemoteV1/V3 host+port
  StartupClientVersion.cs    which client is running
Data/                ContinentDB, WorldMapArea(DB), SubZoneArea, Creature, Item, Spell, NpcFlags
AddonDataProviderType/ClientVersion.cs   the enum everything version-switches on
NpcFinder/           NPC nameplate detection (colour matching, line segments)
ImageProvider/, Screen/   capture interfaces only
Converters/          System.Numerics <-> JSON (Vector2/3/4)
Extensions/, Util/, Logging/
```

## Things that will bite you

**`ClientVersion` has twelve values and `Legacy_*` are real clients.** `None`, `Retail`,
`SoM`, `TBC`, `Wrath`, `Cata`, `Mop`, then `Legacy_Vanilla`/`_TBC`/`_Wrath`/`_Cata`/
`_Mop`. Any `switch` that lists only the modern ones silently sends every legacy client
down the default branch — that is how `WApi` pointed legacy clients at the retail
Wowhead site. Pair each modern value with its `Legacy_*` twin, or better, derive from
`DataConfig.ClientEra` so new eras do not need a new case.

**`NavmeshBakeOptions` values are part of the tile cache identity.** `AgentRadius`,
`AgentMaxClimb`, `WalkableSlope` and `MinWorldZ` feed `ComputeSettingsHash`, so changing
a *default* here silently orphans every baked tile and every published CDN bundle.
`NavmeshQueryOptions` is safe to change freely — it is query-time only, which is exactly
why the two are separate classes.

**`ContinentDB` is static mutable state, populated by `Init`.** `MPQTriangleSupplier`
and anything resolving a continent name needs it filled first; `PPatherService` does
that in its constructor, but a standalone harness (benchmarks, a utility) must call
`ContinentDB.Init(new WorldMapAreaDB(cfg).Values)` itself or every continent lookup
throws. `TryAdd` means the first client loaded wins for a given map id.

**`WorldMapAreaDB` reads `dbc/<client>/WorldMapArea.json` and throws if it is absent** —
that is the failure a new client hits before `ReadDBC_CSV -v <client>` has been run. The
file is per **client**, not per era, because id spaces differ.

**Casing matters off Windows.** `WorldMapArea.json` is requested by that exact name by
the frontend; a lowercase file on disk works locally and 404s on Linux/macOS.
