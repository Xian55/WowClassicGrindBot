# Core — bot logic

The decision loop. Reads game state produced by the `DataToColor` addon (pixels →
`AddonDataProvider` → `PlayerReader`), picks a goal via GOAP, and drives input.

## Layout

```
GOAP/                GoapAgent, goal selection, world state
Goals/               28 goals (Combat, Pull, Loot, Skin, AdhocNPC, FollowRoute, WalkToCorpse, ...)
GoalsFactory/        wires goals from the class profile
GoalsComponent/      shared machinery: CastingHandler, Navigation, MountHandler, CombatTracker
Requirement/         the class-profile expression language (see below)
RPN/                 expression parsing for Requirement
ClassConfig/         class profile model (Json/class/*.json)
AddonComponent/      PlayerReader and the typed views over addon data
AddonDataProvider/   pixel capture -> ints
Input/               keyboard/mouse simulation
PPather/             pathing clients: LocalPathingApi, RemotePathingAPI (V1), RemotePathingAPIV3
Path/                route model, RouteInfo
WowheadAPI/          per-version wowhead/zamimg URLs
```

## Rules that matter

**Store almost no state in goals or components.** The app can be started at any moment —
mid-combat, mid-flight, dead — and must adapt to whatever the game reports. Anything
cached is a chance to be wrong about the live world. Prefer re-reading `PlayerReader`
over remembering.

**User-facing API changes must update `README.md`.** Any add/remove/rename in
`Core/Requirement/RequirementFactory.cs` changes the class-profile language that users
write, so the README's requirement tables are part of the change, not follow-up.

**Input is latched — release what you press.** Runaway forward movement and endless
jumping have both been caused by an early-return skipping a `KeyUp`, or a reset that
cleared state without releasing the key. When touching `Input/` or goal exits, make the
release path unconditional.

**Navigation re-requests constantly.** `Navigation` asks the pather again every few
seconds while walking, so a single failed request is not fatal — but an engine that
returns a *wrong* path (rather than none) is, and one that returns empty on a rate limit
is indistinguishable from "no route exists".

**`PathsAreSmoothed` gates route thinning.** The navmesh returns a dense funnel path;
`SimplifyRouteToWaypoint`/`ReduceByDistance` were tuned for sparse SpotAStar output and
will gut it. Check the capability, not the engine type.

## Pathing backends

`GetPather` picks one at startup by **probing, not by config alone**. `Pathing:Mode`
ships as `Local` on both hosts (`BlazorServer/appsettings.json`, and `RunOptions`'
`Default` for HeadlessServer) = in-process `PPatherService` with
`Pathing:Engine=Navmesh`, which needs no game archives — see `PPather/CLAUDE.md`.

The remote modes are opt-in and degrade rather than fail: `RemoteV3` (AmeisenNavigation
over AnTCP) falls through to `RemoteV1` (PathingAPI over HTTP) and then to `Local` when
`PingServer` fails. That fallback is silent apart from a log line, so a wrong `hostv3`/
`portv3` presents as "the remote server is being ignored" rather than as an error —
check the `Using {Type}` line before assuming the remote is broken.

`appsettings.json` shipped `RemoteV3` until 2026-07-27; anything asserting that is
stale.

**Do not gate behaviour on a hardcoded `ClientVersion` list.** `WApi.cs` and the Leaflet
UI both went stale that way. Pair each modern Classic version with its `Legacy_*` twin,
or derive from `DataConfig.ClientEra`.
