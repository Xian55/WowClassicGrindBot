# Headless Pathing Server — Design Doc

**Status:** Draft / proposal — compile probe done (§9). macOS bake host scoped (§12).
**Scope:** A minimal, standalone server whose only job is to host the DotRecast navmesh pathing engine and answer route requests over the existing RemoteV3 protocol. No MPQ / game files at runtime, no bot, no UI. Cross-platform (`net10.0`), shippable as a **multi-arch Docker image** (linux/amd64 + linux/arm64) published to GHCR, and runnable on Windows/macOS/Linux/arm64.

**Also covered:** §12 extends this to Apple silicon, including running the **bake** natively on
macOS — the interesting case being a spare Mac as a dedicated bake host.

Related: this rides on the same navmesh the bot already uses; distribution reuses R2 (see `navmesh-bake-upload`).

---

## 1. Why now / why feasible

Two things changed that make this practical:

1. **MPQ is no longer a runtime requirement.** The navmesh is **pre-baked**; querying it loads serialized tiles and runs DotRecast Detour. No game files, no StormLib at query time.
2. **The navmeshes are small.** Distributable (R2 today), mountable into a container.

> **2026-07-26 — premise 1 is now actually true in `PPatherService`, not just in the
> engine.** `NavmeshPathfinder`/`NavmeshTileCache` always accepted a null triangle
> world ("disk-only": answer from baked tiles, never bake), but `PPatherService.Initialise`
> unconditionally constructed `Search` → `MPQTriangleSupplier` → `ArchiveSet` + WDT, so a
> query still died without archives. It now falls back to disk-only per continent and logs
> `disk-only, no game files`; only baking, area-grid extraction and `SpotAStar` still demand
> geometry, and they now refuse with a stated reason (`503` / bake status message) instead of
> throwing. Verified by pathing Pandaria with a **wrath** archive set installed — which
> contains no Pandaria at all — where the same query previously returned `HTTP 500`
> `FileNotFoundException: World\Maps\HawaiiMainLand\HawaiiMainLand.wdt`.
>
> Practical driver: pre-Cata clients are ~9 GB, but 4.3.4 Cataclysm and 5.4.8 Mists are
> ~19 GB each. Requiring a client just to *query* would mean keeping tens of GB installed
> to walk over tiles that are already baked and only ~1 GB on the wire.

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

> **Baking is no longer inherently Windows-only.** §12 scopes running the bake on macOS
> (Apple silicon) — the blocker is a native StormLib build plus two P/Invoke signatures, not
> anything structural. The query server design here is unaffected either way.

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
6. ~~**Baking stays Windows-only**~~ — **scoped in §12.** Still out of scope for the *query server*, but no longer treated as impossible: it needs a native StormLib build, a resolver branch **and** platform-conditional marshalling on two P/Invokes (the `TCHAR` path arguments). Verified against StormLib source, not assumed.

---

## 11. Phasing

- **P1 — Retarget + carve.** Pathing chain → `net10.0` (per §9); new minimal `PathingServer` hosting `PPatherController` + `PPatherService`; build + smoke-test a route locally against baked tiles.
- **P2 — Docker + CI.** Multi-stage, multi-arch Dockerfile; GHCR publish Action (submodule-aware).
- **P3 — Provisioning + polish.** R2-fetch entrypoint option; optional native self-contained RID builds; optional `PPather.Query`/`PPather.Bake` split.
- **P4 — macOS bake host (§12).** Independent of P1–P3: turns an Apple-silicon box into a bake
  machine. Only coupling is the `net10.0` retarget, which P1 already does.

---

## 12. macOS / Apple silicon — query *and* bake

**Motivation.** An idle Apple-silicon machine (e.g. Mac mini M4) is attractive as a dedicated
bake host: baking is the expensive, occasional, offline step, and its output is portable data
that any platform can then consume.

### 12.1 What already works today, with zero code changes

**Windows-on-ARM in a VM.** Issue
[#803](https://github.com/Xian55/WowClassicGrindBot/issues/803) is a user running the bot under
Windows 11 ARM64 in a VM on an Apple-silicon host. It works: the repo already ships
`StormLib_arm64.dll` and `StormDll.Resolve` already selects it from
`RuntimeInformation.ProcessArchitecture`. Nothing to build, nothing to port.

That issue is also where the shipped ARM64 DLL got its build flags settled, and the failure it
produced is the single most important thing to know before attempting a native port — see
§12.3.

So: if the goal is just "use the Mac as a bake box", a Windows ARM VM is the zero-risk answer.
Everything below is for running natively.

### 12.2 The query path is already proven

§9's compile probe stands unchanged: the pathing chain builds clean as plain `net10.0` and
cross-builds for `linux-arm64` with **zero code changes** beyond three `<TargetFramework>`
overrides. `osx-arm64` is the same shape — a RID swap, since nothing in the chain is
platform-specific:

```
dotnet publish PathingServer/PathingServer.csproj -c Release -r osx-arm64 --self-contained
```

DotRecast multi-targets `net10.0`; every `System.Drawing` use is cross-platform Primitives
structs. A query-only macOS host needs nothing further.

### 12.3 The bake delta: StormLib, and the `TCHAR` trap

The bake's only native dependency is StormLib. Two things must change, and the second is the
one that will silently waste an afternoon if missed.

**(a) Build StormLib for `osx-arm64`.** The three shipped binaries are Windows PEs —
*including* `StormLib_arm64.dll`, which is Windows-on-ARM, not macOS. StormLib builds on macOS
via CMake:

```
cmake -S StormLib -B build -DBUILD_SHARED_LIBS=ON -DCMAKE_OSX_ARCHITECTURES=arm64
cmake --build build --config Release      # -> libstorm.dylib
```

Note there is deliberately **no `-DSTORM_UNICODE=ON`** here, unlike the Windows ARM64 build
recipe in #803. That flag is Windows-only, which leads directly to:

**(b) `TCHAR` is `char` off Windows — so two P/Invokes need platform-conditional marshalling.**
Verified against `StormLib/src/StormPort.h`, non-Windows branch:

```c
typedef char TCHAR;      // not wchar_t
#define _T(x)  x
```

And the affected exports (`StormLib/src/StormLib.h`):

```c
bool SFileOpenArchive     (const TCHAR * szMpqName, ...);                       // TCHAR
bool SFileOpenPatchArchive(HANDLE, const TCHAR * szPatchMpqName,
                           const char * szPatchPathPrefix, ...);                // TCHAR + char*
bool SFileOpenFileEx      (HANDLE, const char  * szFileName, ...);              // always char*
```

Our P/Invokes currently declare both `TCHAR` paths as `LPWStr`, which is correct **only**
against a `STORM_UNICODE=ON` Windows build:

```csharp
public static partial bool SFileOpenArchive(
    [MarshalAs(UnmanagedType.LPWStr)] string szMpqName, ...);
public static partial bool SFileOpenPatchArchive(
    nint hMpq, [MarshalAs(UnmanagedType.LPWStr)] string szPatchMpqName, nint prefix, uint flags);
```

On macOS those must marshal as UTF-8 `LPStr`. Everything else is already portable —
`SFileOpenFileEx` is `const char*` on every platform, which is why `Archive` already converts
file names to a UTF-8 span and needs no change. The patch-path *prefix* is likewise always
`char*`; we pass `nint.Zero` (NULL) and that stays correct.

**Why this matters more than it looks.** #803's symptom was a `NullReferenceException` deep in
`ArchiveSet.GetStream`, because an ANSI StormLib silently failed to open archives against a
wide P/Invoke — wrong-encoding path in, open fails, `null` archive kept, crash much later. A
macOS `.dylib` reached through the current `LPWStr` declaration reproduces exactly that class
of failure, in mirror image. **It does not throw at the boundary; it returns "archive not
found".** `ArchiveSet` now skips-and-logs a failed open instead of NRE'ing (hardened in #803,
extended since for Cataclysm's empty-listfile stub archive), so the symptom will be a clear
"No MPQ archive could be opened" error rather than a mystery — but only if you read the log.

### 12.4 Implementation — status

Implemented and verified on a real Mac mini M4 (macOS 26.5, arm64, 10 cores, 16 GB),
2026-07-26. Everything except the native library is done.

| Change | Where | Status |
| --- | --- | --- |
| `net10.0` retarget | DataConfig, SharedLib, PPather | **done** — `<TargetFramework>` override in each; Windows solution still builds and the 6-tile corpus stays byte-identical |
| Resolver: OS **and** arch | `StormDll.Resolve` | **done** — `libstorm.dylib` / `libstorm.so` / `StormLib_*.dll` |
| Platform-conditional marshalling | `SFileOpenArchive`, `SFileOpenPatchArchive` | **done** — `…W`/`…A` partials behind `OperatingSystem.IsWindows()`, sharing one `EntryPoint` |
| Cross-platform bake entry point | `Utilities/BakeTool` | **done** — plain `net10.0` console; every other bake path (Benchmarks, PathingAPI, CoreTests) is `net10.0-windows` |
| Ship the unix library | `PPather.csproj` | **done** — `None Include` conditioned on `Exists`, so Windows and fresh checkouts are unaffected |
| Build helper | `scripts/build-stormlib-unix.sh` | **done** |
| `libstorm.dylib` itself | `PPather/MPQ/` | **done** — built on the Mac, Mach-O arm64 |
| MPQ-internal path separator | `WDTFile` | **done** — see §12.4.1 |

The dylib is deliberately **gitignored**: it is a host-specific native binary, unlike the
committed Windows DLLs.

**Verified on the Mac, without the dylib present:**

- .NET SDK 10.0.110 installed user-local (`~/.dotnet`) to match `global.json`'s `10.0.100`
  feature band — a 10.0.3xx SDK is rejected by `latestPatch` roll-forward. Running the apphost
  from a user-local install needs `DOTNET_ROOT=$HOME/.dotnet`.
- `dotnet build Utilities/BakeTool` → **0 errors**, only the two pre-existing DotRecast CS8632
  warnings Windows also emits. §9's "compile-clean as `net10.0`" now confirmed on macOS itself,
  not just as a cross-build.
- `DataConfig` resolves `legacy_mop` → era `mop`; the MPQ path (a symlink to the client's
  `Data` folder) enumerates all 38 archives; `dbc/legacy_mop` loads.
- The bake then fails exactly as intended — a `DllNotFoundException` naming
  `…/bin/Release/net10.0/MPQ/libstorm.dylib`. That is the resolver arm working: an actionable
  message, not a mystery.

**Full recipe on a fresh Mac:**

```
# toolchain (all user-local, no admin except CLT)
xcode-select --install                               # clang + git; GUI/admin
curl -fsSL https://dot.net/v1/dotnet-install.sh -o ~/dotnet-install.sh
~/dotnet-install.sh --version 10.0.110 --install-dir ~/.dotnet
# cmake is NOT in CLT - grab the official universal tarball
#   https://github.com/Kitware/CMake/releases -> cmake-<ver>-macos-universal.tar.gz
#   binary lives at CMake.app/Contents/bin/cmake

export PATH="$HOME/.dotnet:$HOME/tools/bin:$PATH"
export DOTNET_ROOT="$HOME/.dotnet"

scripts/build-stormlib-unix.sh                       # -> PPather/MPQ/libstorm.dylib
dotnet build Utilities/BakeTool -c Release
./Utilities/BakeTool/bin/Release/net10.0/BakeTool \
    --exp legacy_mop --root ~/wcgb-data --continent HawaiiMainLand
```

The data root needs `MPQ/` (a symlink to the client's `Data` folder is fine) and
`dbc/<exp>/` from `ReadDBC_CSV -v <exp>`.

Toolchain gotchas, all of which cost time here:

- **`global.json` pins `10.0.100`.** Roll-forward is `latestPatch`, which does not cross
  feature bands, so a `10.0.3xx` SDK is *rejected*. Install a `10.0.1xx` - matching the Windows
  box exactly is also one fewer variable for byte-identity.
- **`DOTNET_ROOT` must be set** for a user-local `~/.dotnet`, or the apphost reports
  "You must install .NET" despite `dotnet --version` working.
- **`xcode-select --install` may say "already installed" while `xcode-select -p` errors** -
  the CLT files exist but no active developer directory is set, so `/usr/bin/{git,clang}` stay
  stubs. Running the install (or `sudo xcode-select --switch /Library/Developer/CommandLineTools`)
  fixes it.
- **cmake ships with neither macOS nor CLT.** Its binary is nested at
  `CMake.app/Contents/bin/cmake` inside the tarball.

### 12.4.1 The one code bug the port exposed

`WDTFile` built its archive path with `Path.Join`:

```csharp
Path.Join("World", "Maps", path, $"{path}.wdt")   // "World/Maps/..." on macOS
```

MPQ entries are **always** backslash-separated, so on macOS this asked for
`World/Maps/HawaiiMainLand/HawaiiMainLand.wdt` and matched nothing - the continent failed to
load with `FileNotFoundException` even though every archive had opened. Every sibling lookup
(`LoadMapTile`, `BuildAreaData`, `ReadObjectFile`) already used interpolated `\\` strings; this
was the only `Path.Join` on an in-archive path, and on Windows it was invisible because the
platform separator happened to be right.

### 12.4.2 Verified: macOS output is byte-identical

The gate from §12.5(4), run for real:

| check | result |
|---|---|
| settings hash | `675b9b8a` on both platforms - so no platform component is needed |
| single ADT (16 tiles) | **16/16 byte-identical** to the Windows bake |
| full Pandaria overlap | **2320/2320 byte-identical**, 0 different |

macOS-baked tiles can therefore be mixed with Windows-baked ones in the same cache directory
and published to the same CDN bundle.

`Resolve` today branches only on architecture and hardcodes `.dll`:

```csharp
string fileName = RuntimeInformation.ProcessArchitecture switch {
    Architecture.X64   => "StormLib_x64.dll",
    Architecture.Arm64 => "StormLib_arm64.dll",
    ...
};
return NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "MPQ", fileName));
```

It needs an OS arm first (`libstorm.dylib` on macOS, `libstorm.so` on Linux), keeping the
existing Windows behaviour untouched.

For the marshalling, `LibraryImport` cannot vary an attribute at runtime, so it wants two
partial declarations selected by an `OperatingSystem.IsWindows()` check at the call site (or a
`#if`-free wrapper method that picks one). Keep the wide path for Windows so the shipped DLLs
and #803's ARM64 build keep working byte-for-byte.

### 12.5 Verification — do not trust "it compiled"

The failure mode here is quiet, so the gate must be "did it actually read game data", not
"did it run". Minimum checks, in order:

1. **Archive opens.** Log line count of successfully opened archives matches the `*.MPQ` files
   present. A zero here is the `TCHAR` bug.
2. **A known ADT reads.** Pull `World\Maps\Azeroth\Azeroth_32_48.adt` and compare its SHA-256
   against the same read on Windows. Byte-identical, or the marshalling/patch chain is wrong.
3. **Geometry matches.** Extract triangles for a handful of ADTs and compare counts *and* a
   vertex hash against Windows. Counts alone are too weak — two clients can agree on counts
   and differ in vertices.
4. **Tiles match.** Bake a few tiles and compare the serialized `DtMeshData` SHA-256 against
   the Windows output. This is the real gate: a macOS bake must be byte-identical, otherwise
   the era/settings hash silently means two different things on two machines and mixed tiles
   end up in one cache directory.

(4) is the one that matters for distribution. If macOS output is *not* byte-identical, the
settings hash must gain a platform component — otherwise `precata/Azeroth/<hash>/` could hold a
mix of Windows- and macOS-baked tiles.

### 12.6 Will it actually be faster?

Unknown, and worth measuring rather than assuming. The honest arguments both ways:

- **For:** `docs/dotrecast-fork.md` records that the worst C#-vs-C++ bake stages are the
  **memory-bandwidth-bound** sweeps (`MEDIAN_AREA` 2.26x, `FILTER_BORDER` 2.18x,
  `BUILD_DISTANCEFIELD` 1.88x) — they lose precisely because our storage is wider than C++'s.
  Apple silicon's unified memory bandwidth is well above typical desktop DDR, so those stages
  are where a real win would come from.
- **Against:** the baker runs only 2–4 workers (`NavmeshTileCache` Min/MaxBakeWorkers), so most
  of an M4's cores sit idle regardless. Observed peak RSS for a full-continent bake is ~3.4 GB,
  so memory capacity is not a factor.

Measuring is one command — `Benchmarks --bake-profile <label>` already emits the per-stage
`RC_TIMER_*` breakdown plus a per-tile SHA-256, so a macOS run is directly comparable to the
numbers already committed under `benchmark_results/`. That single run answers both §12.5(4)
and §12.6 at once.

### 12.7 Reach after P4

| Target | Query | Bake |
| --- | --- | --- |
| Windows x64 / ARM64 | yes | yes (today) |
| Linux x64 / arm64 (Docker) | yes | needs `libstorm.so` (same delta as §12.3) |
| macOS arm64 native | yes (§12.2) | after P4 |
| macOS via Windows ARM VM | yes | yes (today, #803) |

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
