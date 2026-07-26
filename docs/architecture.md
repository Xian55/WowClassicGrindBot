# Architecture

How the solution is put together: project layout, dependency direction, technology
choices and the design decisions behind them.

Further detail about the early architecture can be found in [Blog post](http://www.codesin.net/post/wowbot/). The project has evolved significantly since then.

## Solution Structure (13 projects)

```
MasterOfPuppets.sln
│
├── Hosts ─────────────────────────────────────────────────────────
│   ├── BlazorServer/       ASP.NET Core Blazor Server — primary web UI
│   ├── HeadlessServer/     Console app — CLI automation, no UI
│   └── PathingAPI/         ASP.NET Core Web API — standalone pathfinding microservice
│
├── Libraries ─────────────────────────────────────────────────────
│   ├── Core/               Business logic, bot control, addon readers, databases,
│   │                       DI orchestration, screen analysis, GPU/CPU NPC detection
│   ├── Frontend/           Razor component library shared across hosts
│   │                       (Leaflet maps, keyboard overlay, path recorder, mail UI)
│   ├── Game/               Windows-specific game interaction
│   │                       (process management, DXGI & WGC screen capture, input)
│   ├── SharedLib/          Cross-project utilities, NPC finder algorithms,
│   │                       image processing, logging, configuration models
│   ├── PPather/            In-process pathfinding engine (DotRecast navmesh over
│   │                       pre-baked tiles; legacy triangle-mesh spot A*)
│   ├── DataConfig/         Expansion-aware data path resolution
│   ├── WowheadDB/          External game database integration
│   └── WinAPI/             P/Invoke bindings (user32, gdi32, dwmapi, kernel32)
│
├── Tools ─────────────────────────────────────────────────────────
│   ├── Benchmarks/         BenchmarkDotNet performance suite
│   ├── CoreTests/          Integration tests (NPC finder, cursor, minimap, input)
│   └── Utilities/          DBC extraction, navmesh baking, data processing helpers
│
└── Addons ────────────────────────────────────────────────────────
    └── Addons/DataToColor/ Lua addon — game state → pixel color encoding
```

Note that `Utilities/` projects are deliberately **not** part of `MasterOfPuppets.sln`,
so a solution build does not build them.

## Dependency Flow

```
┌─────────────┐  ┌───────────────┐  ┌────────────┐
│ BlazorServer│  │ HeadlessServer│  │ PathingAPI │
└──────┬───┬──┘  └───────┬───────┘  └─────┬──────┘
       │   │             │                 │
       │   └──────┬──────┘                 │
       │          ▼                        │
       │     ┌──────────┐                  │
       └────►│ Frontend │                  │
             └────┬─────┘                  │
                  ▼                        │
             ┌──────────┐                  │
             │   Core   │◄─────────────────┘
             └┬───┬───┬─┘
              │   │   │
     ┌────────┘   │   └────────┐
     ▼            ▼            ▼
┌────────┐  ┌──────────┐  ┌─────────┐
│  Game  │  │  PPather │  │WowheadDB│
└───┬────┘  └────┬─────┘  └─────┬───┘
    │            │              │
    ▼            ▼              ▼
┌────────┐  ┌──────────┐  ┌──────────┐
│ WinAPI │  │DataConfig│  │ SharedLib│
└────────┘  └──────────┘  └──────────┘
```

`Game/` and `WinAPI/` are the only Windows-only projects. The pathing chain
(`PPather` → `DataConfig` / `SharedLib`) targets plain `net10.0` rather than the
solution-wide `net10.0-windows`, which is what allows navmesh tiles to be baked and
queried on macOS and Linux.

## Technology Stack

| Layer | Technology |
|-------|-----------|
| **Runtime** | .NET 10 (C# 14 preview) with nullable reference types |
| **Web** | ASP.NET Core Blazor Server, SignalR (MessagePack + LZ4) |
| **UI** | Blazor Bootstrap, MatBlazor, Leaflet.js, Pixi.js (WebGL) |
| **Graphics** | DirectX 11 via Vortice (DXGI Desktop Duplication, Windows Graphics Capture, Compute Shaders) |
| **Pathfinding** | DotRecast navmesh (in-process, default) with pre-baked tiles; legacy spot-grid A* and out-of-process/AmeisenNavigation backends |
| **Native Interop** | `[LibraryImport]` P/Invoke with `DisableRuntimeMarshalling` (user32, gdi32, dwmapi, StormLib) |
| **Serialization** | Newtonsoft.Json, MessagePack, MemoryPack |
| **Logging** | Serilog (structured, multi-sink: console, file, debug, in-memory circular buffer) |
| **Image Processing** | SixLabors.ImageSharp |
| **Benchmarking** | BenchmarkDotNet |
| **Networking** | mDNS (Makaretu.Dns.Multicast) for zero-config `wowbot.local` discovery |
| **Build** | Central package management (`Directory.Packages.props`), `global.json` SDK pinning |

## Key Design Decisions

- **No memory tampering** — Pure screen analysis via pixel reading and simulated input (PostMessage). The Lua addon encodes game state into pixel colors; C# decodes them
- **Graceful fallback chains** — Screen capture (WGC &rarr; DXGI), pathfinding (RemoteV3 &rarr; RemoteV1 &rarr; Local, when a remote mode is opted into; `Local` is the shipped default), NPC detection (GPU &rarr; CPU)
- **Dependency injection layering** — `Core/DependencyInjection.cs` provides 5 registration modes (LoadOnly, Base, Normal, Configuration, Frontend) so each host composes only what it needs
- **Expansion-agnostic core** — Version-specific behavior is driven by `DataConfig` path resolution and `ClientVersion` enum, keeping the core logic shared across all supported client versions
- **Minimal state in the decision loop** — goals and components cache almost nothing, because the app can be started at any moment (mid-combat, mid-flight, dead) and must adapt to whatever the game currently reports

## Deployment Configurations

The host and pathfinder are independently composable — pick one from each column:

| Host | Pathfinder | Notes |
|------|-----------|-------|
| **BlazorServer** | Navmesh (in-process) | **Recommended** - DotRecast navmesh, no external services, every client |
| **BlazorServer** | PathingAPI (out-of-process) | Offloads pathfinding to a dedicated service |
| **BlazorServer** | AmeisenNavigation (external) | Legacy; superseded by the in-process navmesh |
| **HeadlessServer** | Navmesh (in-process) | **Recommended** - fully headless, single process |
| **HeadlessServer** | PathingAPI (out-of-process) | Headless with remote pathfinding |
| **HeadlessServer** | AmeisenNavigation (external) | Legacy; superseded by the in-process navmesh |

**Multi-instance support** — Using Windows Graphics Capture (WGC), each instance captures a specific window rather than the entire desktop. This allows running multiple bot instances on a single machine, each attached to a different game client, with no additional configuration needed.

## Related documents

| document | covers |
|---|---|
| [`headless-pathing-server-design.md`](headless-pathing-server-design.md) | the OS-neutral pathing chain and headless bake host |
| [`mpq-casc-storage-abstraction.md`](mpq-casc-storage-abstraction.md) | per-client game-file formats and the storage seam |
| [`dotrecast-fork.md`](dotrecast-fork.md) | the DotRecast fork, bake performance measurements |
| [`config-migration.md`](config-migration.md) | configuration key renames |
