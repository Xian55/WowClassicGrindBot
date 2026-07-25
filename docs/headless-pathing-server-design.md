# Headless Pathing Server — Design Doc

**Status:** Draft / proposal — compile probe running (§9 to be filled).
**Scope:** A minimal, standalone server whose only job is to host the DotRecast navmesh pathing engine and answer route requests over the existing RemoteV3 protocol. No MPQ / game files at runtime, no bot, no UI. Cross-platform (`net10.0`), shippable as a **multi-arch Docker image** (linux/amd64 + linux/arm64) published to GHCR, and runnable on Windows/macOS/Linux/arm64.

Related: this rides on the same navmesh the bot already uses; distribution reuses R2 (see `navmesh-bake-upload`).

---

## 1. Why now / why feasible

Two things changed that make this practical:

1. **MPQ is no longer a runtime requirement.** The navmesh is **pre-baked**; querying it loads serialized tiles and runs DotRecast Detour. No game files, no StormLib at query time.
2. **The navmeshes are small.** Distributable (R2 today), mountable into a container.

The pathing **query path is pure managed C#** (DotRecast Core/Recast/Detour + `PPatherService`). The only Windows-flavored code in the wider engine is **bake-time** (`StormDll` P/Invoke for MPQ, `MapTileFile` ADT parsing) — never touched by a route request. So a query-only server has no Windows dependency.

---

## 2. The one real blocker: the Windows TFM

The repo's global `TargetFramework` is **`net10.0-windows10.0.19041.0`** (in `Directory.Packages.props`) — required by the **bot side** for **WGC (Windows Graphics Capture)**. A `-windows` assembly cannot run in a Linux container.

**WGC is bot-side only** — `Core/WoWScreen/{WowScreenWGC,GraphicsCaptureInterop}.cs` + `WinAPI/`. The pathing chain does **not** touch it (SharedLib's only `WGC` reference is an enum *name*).

**Fix = retarget only the pathing chain to plain `net10.0`:**

| Projects | TFM | Reason |
| --- | --- | --- |
| Core, Game, WinAPI, BlazorServer, HeadlessServer, PathingAPI | `net10.0-windows` (unchanged) | **WGC** |
| DataConfig, SharedLib, PPather, DotRecast | `net10.0` | cross-platform pathing chain |
| **new** `PathingServer` | `net10.0` | Linux/arm64 Docker host |

A `net10.0-windows` project may reference a `net10.0` library, so **the bot keeps WGC** and simply consumes the retargeted pathing libs. No multi-targeting needed *if* the chain is compile-clean cross-platform (that's what §9 proves).

---

## 3. Server shape

```
new project  PathingServer   (SDK.Web, net10.0)
  ProjectReferences: PPather, DataConfig, SharedLib   (+ DotRecast transitively) — all net10.0
  hosts:  PPatherController  (RemoteV3 REST: route + height)   over  PPatherService
  config: DataConfig (navmesh path), Pathing:Engine = Navmesh, listen port (default 47111)
  NOT referenced: Core, Frontend, Blazor, MatBlazor, MPQ/StormLib, WGC
```

### Keep vs drop from `PathingAPI`
`PathingAPI` today is `SDK.Web` and references **Core + Frontend (Blazor/leaflet) + PPather** — far more than a pather needs (Core drags in the whole bot; Frontend drags in Blazor/leaflet).

- **Keep:** `PPatherService` (the engine, lives in `PPather`), and `PPatherController` (`api/PPather/...` REST) — the routes the bot's RemoteV3 client actually calls (route + height; confirm exact subset).
- **Drop:** Core, Frontend, Blazor host, `WatchHub` (SignalR leaflet viz), MatBlazor, Swagger optional.

`PathingServer` is a fresh minimal project rather than a trim of `PathingAPI`, so the heavy references never enter the graph.

---

## 4. Baking stays separate

The bake pipeline (MPQ read via `StormDll`/StormLib, ADT parse via `MapTileFile`/`System.Drawing`) is **not** in the query server's runtime path. It stays a Windows-side tool (it needs the game client + StormLib anyway). The query container:
- ships the baked tiles (mounted/fetched),
- never invokes `StormDll` or GDI+,
- so it runs clean on Linux/arm64.

*(Future nicety: split `PPather` into `PPather.Query` + `PPather.Bake` so the container image doesn't even carry the bake code. Not required for v1 — dead code that's never called is harmless.)*

---

## 5. Navmesh provisioning

The container needs the baked tiles at `DataConfig`'s navmesh path. Two options:
- **Volume mount** the baked tile dir into the container (simplest; user controls the data).
- **Entrypoint fetch from R2** on startup (reuse `download-navmesh` logic) — self-provisioning image, no local data needed.

Config points `DataConfig.Root` at the mounted/fetched location; `Pathing:Engine = Navmesh`.

---

## 6. Docker + distribution

- **Multi-stage Dockerfile:** `sdk` image builds `dotnet publish -c Release`, `runtime`/`runtime-deps` (or `aspnet`) image runs it. Framework-dependent (small) since base image carries the runtime.
- **Multi-arch:** `docker buildx build --platform linux/amd64,linux/arm64` — .NET 10 supports linux-arm64. Use `-a $TARGETARCH` in publish for correct RID.
- **Publish to GHCR** via a GitHub Action (`docker/build-push-action`), tagged on release. Public image → users `docker run`.
- **Cross-platform reach:** the linux multi-arch image runs on Linux hosts, arm64 (Raspberry Pi / ARM servers / Apple-silicon Docker), and Windows/macOS via Docker Desktop (linux containers). For non-Docker native use, `dotnet publish -r {win-x64|osx-arm64|linux-arm64} --self-contained` produces standalone binaries.

Sketch:
```dockerfile
# build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY . .
RUN dotnet publish PathingServer/PathingServer.csproj -c Release -a $TARGETARCH -o /app
# run
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
VOLUME /data                # baked navmesh tiles
ENV Pathing__Engine=Navmesh
EXPOSE 47111
ENTRYPOINT ["dotnet", "PathingServer.dll"]
```
(Submodule note: DotRecast is a git submodule — CI must `submodule update --init` before build. Adjust the `COPY` for a submodule-aware context.)

---

## 7. Bot integration

No bot change needed — point the bot at the container:
```
--Pathing:Mode=RemoteV3 --Pathing:hostv3=<container-host> --Pathing:portv3=47111
```
The bot's existing RemoteV3 client speaks the same `PPatherController` protocol. One pather container can serve multiple bot instances.

---

## 8. Cross-platform reach summary

| Target | How |
| --- | --- |
| Linux x64 | Docker `linux/amd64` |
| Linux arm64 (Pi, ARM servers) | Docker `linux/arm64` |
| Windows / macOS | Docker Desktop (linux container) |
| native (no Docker) | `dotnet publish -r <rid> --self-contained` per OS/arch |

---

## 9. Compile probe results  ✅ (2026-07-25, isolated worktree)

**Verdict: the pathing chain compiles clean as `net10.0` and cross-platform-builds for `linux-arm64` with ZERO code changes — only the three `<TargetFramework>net10.0</TargetFramework>` overrides.**

**Build (`net10.0`)** — `dotnet build PPather/PPather.csproj -c Release` → **succeeded, 0 errors**. Outputs to `net10.0` paths for `DataConfig`, `SharedLib`, `PPather`. Only benign pre-existing warnings (CS8632/CA2265 in the DotRecast submodule; one CA1873 in `PPatherService.cs:246`).

**linux-arm64** — `dotnet build … -r linux-arm64` → **succeeded, 0 errors**. Cross-platform restore + RID build works (`bin/Release/net10.0/linux-arm64/`).

**DotRecast submodule TFMs** — all three multi-target `netstandard2.1;net8.0;net9.0;net10.0` (no `-windows`). `external/Directory.Packages.props` is a deliberate CPM firewall (`ManagePackageVersionsCentrally=false`) so the submodule keeps its own multi-targeting; it selects the `net10.0` slice cleanly. **Not a blocker.**

**`System.Drawing`** — **no GDI+ anywhere** in the chain. All uses are cross-platform `System.Drawing.Primitives` structs:
- `SharedLib/Extensions/PointExt.cs` (`Point`), `RectangleExt.cs` (`Point`,`Rectangle`) — safe.
- `PPather/Triangles/Game/MapTileFile.cs` — `using System.Drawing;` is an **unused import** (deletable).
- `Image<Bgra32>` in `SharedLib/ImageProvider/*` is **SixLabors.ImageSharp** (managed, cross-plat), not `System.Drawing.Bitmap`.

**`StormDll` (the one hard Windows dep)** — StormLib P/Invoke (`PPather/StormDll/StormDll.cs`, `[LibraryImport]` + `NativeLibrary.SetDllImportResolver` loading Windows PE `StormLib_{x64,x86,arm64}.dll`). Reachable **only via the bake path**, never the query path:
- Bake: `MPQTriangleSupplier` → `ChunkedTriangleCollection` → `TileGeometryExtractor.Extract` → `NavmeshTileBuilder.Bake`.
- Query: `NavmeshTileCache.LoadOrBake(tx,tz)` — if the pre-baked `.tile` exists on disk it's read via `DtMeshDataReader` (pure managed); **only a cache miss** falls into `TileGeometryExtractor.Extract` → StormDll.
- The route query itself (`NavmeshPathfinder` → `DtNavMeshQuery`) is pure DotRecast.

**Operational consequence:** a Linux query container **must ship pre-baked tiles on disk** so `LoadOrBake` never misses into the MPQ extractor. Baking stays Windows-only (StormLib is a Windows PE — even the arm64 one; running the bake on Linux would need a native StormLib `.so` + a resolver branch). Query containers that consume pre-baked tiles never touch StormDll.

---

## 10. Risks / to verify

1. ~~`System.Drawing.Common` at runtime~~ — **resolved (§9):** no GDI+ in the chain; all `System.Drawing` uses are cross-platform Primitives structs. (Optional cleanup: drop the unused `using System.Drawing;` in `MapTileFile.cs`.)
2. ~~DotRecast submodule TFM~~ — **resolved (§9):** multi-targets incl. `net10.0`.
3. ~~`StormDll` on a route request~~ — **resolved (§9):** bake-only. **But** the container must **ship pre-baked tiles** so `NavmeshTileCache.LoadOrBake` never cache-misses into the MPQ extractor. Optionally add a hard guard: on a Linux/query build, a cache miss should error ("tile not baked") rather than attempt StormDll.
4. **RemoteV3 route subset** *(open)* — confirm exactly which `PPatherController` endpoints the bot's RemoteV3 client calls, so `PathingServer` exposes the right minimal set.
5. **Submodule in CI/Docker** *(open)* — build context must `git submodule update --init external/DotRecast` (linked worktrees / clean CI checkouts don't auto-populate it).
6. **Baking stays Windows-only** *(by design)* — StormLib is a Windows PE. Baking on Linux would need a native StormLib `.so` + a resolver branch; out of scope for the query server.

---

## 11. Phasing

- **P1 — Retarget + carve.** Pathing chain → `net10.0` (per §9); new minimal `PathingServer` hosting `PPatherController` + `PPatherService`; build + smoke-test a route locally against baked tiles.
- **P2 — Docker + CI.** Multi-stage, multi-arch Dockerfile; GHCR publish Action (submodule-aware).
- **P3 — Provisioning + polish.** R2-fetch entrypoint option; optional native self-contained RID builds; optional `PPather.Query`/`PPather.Bake` split.

---

## Appendix — relevant code

- `Directory.Packages.props` — global `TargetFramework net10.0-windows…` (override in the pathing chain).
- `PPather/Search/PPatherService.cs` — the engine (query API).
- `PathingAPI/Controllers/PPatherController.cs` — RemoteV3 REST routes to keep.
- `PathingAPI/Startup.cs` — reference for host wiring (drop Blazor/WatchHub/Frontend).
- `PPather/StormDll/*`, `PPather/Triangles/Game/MapTileFile.cs` — bake-only, Windows-flavored; keep out of the query runtime.
- `Core/WoWScreen/*`, `WinAPI/*` — WGC; stays `net10.0-windows` (bot side).
- `SharedLib/`, `DataConfig/` — retarget to `net10.0`.
- `external/DotRecast/src/*` — submodule; verify cross-platform TFM.
