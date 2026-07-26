# Technical Challenges

This project serves as a technical sandbox for exploring advanced .NET features and solving real-world engineering problems. The game automation domain provides a rich problem space that spans multiple software engineering disciplines. Below is an overview of the key technical challenges addressed.

<details>
<summary><strong>Data Pipeline</strong> — Heterogeneous source ingestion and transformation</summary>

Multiple data formats are parsed, transformed, and unified into runtime-optimized structures:

- **MPQ archives** — Binary game archives read through native StormLib via P/Invoke (`PPather/StormDll/`), extracting map geometry (triangles, vertices) for pathfinding
- **DBC (Database Client) files** — Game database extraction via high-performance CSV parsing (`Utilities/ReadDBC_CSV/`) for items, spells, talents, factions, consumables, and world map areas
- **ClassConfig / FrameConfig JSON** — Hierarchical behavior profiles and pixel-coordinate mappings between the Lua addon and the C# screen reader, with version-tracked integrity validation
- **Runtime databases** — `FrozenDictionary<int, T>` and `FrozenSet<T>` for immutable, zero-allocation lookups after initialization
</details>

<details>
<summary><strong>Multi-Version Support</strong> — 11 client versions through a single codebase</summary>

A `ClientVersion` enum drives version-specific behavior across the entire stack: data loading paths, Leaflet map tile configurations, addon compatibility, and pathfinder backend selection. Expansion-specific data (DBC, area polygons, NPC spawns, mailbox locations) is segregated by version while sharing core logic.
</details>

<details>
<summary><strong>Multi-Threaded Architecture</strong> — Dedicated threads with priority management</summary>

- Dedicated threads for addon reading (AboveNormal priority), screen capture (200ms tick), and remote pathing (500ms tick) in `Core/BotController.cs`
- `ManualResetEventSlim` for data-ready signaling across AddonReader, Navigation, CastingHandler, and ScreenCapture
- Modern `Lock` type (C# 13) for fine-grained synchronization
- `[ThreadStatic]` storage for per-thread element buffers to avoid allocation contention
</details>

<details>
<summary><strong>Parallel Processing</strong> — Lock-free spatial algorithms</summary>

`Parallel.For` with `ThreadLocal<T>` storage for spatial binning in `PPather/Triangles/TriangleMatrix.cs`:
- Thread-local dictionaries merged post-parallel to avoid lock contention
- Batched processing with `stackalloc` scratch buffers
- A* pathfinding over triangle meshes with model avoidance heuristics
</details>

<details>
<summary><strong>External Service Communication</strong> — Three interop paradigms</summary>

- **REST API** — `PathingAPI` runs as a standalone ASP.NET Core service with Swagger documentation and rate-limited route calculation endpoints
- **Native DLL Interop** — Modern `[LibraryImport]` with `[assembly: DisableRuntimeMarshalling]` for Windows API calls (input simulation, DWM-aware window management, keyboard layout translation) and StormLib MPQ archive access with architecture-aware (x86/x64/arm64) `NativeLibrary.SetDllImportResolver` selection
- **SignalR + MessagePack** — Binary protocol with LZ4 compression for real-time pathfinding visualization, reducing bandwidth ~70% vs JSON
</details>

<details>
<summary><strong>GPU/CPU Dual Implementation</strong> — DirectX 11 Compute Shaders with managed fallback</summary>

The NPC detection system has both GPU and CPU implementations behind a common interface:
- **GPU path** — HLSL compute shader (`Core/WoWScreen/Shaders/NpcColorMatch.hlsl`) with 256 threads per group, atomic counters, and `groupshared` memory. Resource management via constant buffers and staging readback buffers with `[StructLayout]` matching the HLSL layout exactly
- **CPU path** — Identical algorithm in managed C# with `Span<T>` optimization (`SharedLib/NpcFinder/CpuLineSegmentProvider.cs`)
- **Graceful degradation** — Permanent fallback on GPU init failure, 100-frame cooldown on dispatch failure, device removal detection
</details>

<details>
<summary><strong>Dual Hosting Models</strong> — Same core across three deployment targets</summary>

| Host | UI | Use Case |
|------|------|----------|
| **BlazorServer** | Full Blazor Server web UI | Primary interactive application |
| **HeadlessServer** | Console with CommandLineParser | Automation |
| **PathingAPI** | Swagger UI | Standalone pathfinding microservice |

Shared `Core` and `Frontend` libraries via dependency injection layering with 5 distinct registration modes (LoadOnly, Base, Normal, Configuration, Frontend).
</details>

<details>
<summary><strong>Cross-Language Data Bridge</strong> — Lua to C# via pixel color encoding</summary>

With no direct memory access, the Lua addon and C# communicate through pixel color encoding:
- 32-bit integers packed into RGB components: `B | (G << 8) | (R << 16)`
- Fixed-point math for sub-meter precision coordinates
- UTF-8 text encoded as color values (3 characters per pixel)
- Sentinel pixels for frame integrity validation
- Event-driven Lua caching reduces API calls from 56/frame to 5/frame
</details>

<details>
<summary><strong>Memory Optimization</strong> — Systematic GC pressure reduction</summary>

- `stackalloc` + `Span<T>` for zero-allocation hot paths (pathfinding, image hashing, color encoding)
- `ArrayPool<T>.Shared` for variable-length buffers (MPQ reading, GPU readback, path simplification)
- `[SkipLocalsInit]` on hot methods where locals are immediately overwritten
- `FrozenDictionary` / `FrozenSet` for immutable runtime lookups
- Struct-based records with `[StructLayout(Pack = 1)]` for binary-compatible game data structures
- Circular buffer logging with bitwise AND indexing
</details>

<details>
<summary><strong>Configuration-Driven Architecture</strong> — Runtime-flexible behavior</summary>

- **ClassConfig modes**: Grind, CorpseRun, AttendedGather, AttendedGrind, AssistFocus, AutoGather — each with distinct behavior trees
- **Pathfinder selection**: Automatic discovery with fallback chain (RemoteV3 &rarr; RemoteV1 &rarr; Local PPather)
- **Screen capture strategy**: Windows Graphics Capture vs DXGI fallback based on OS version
- **Per-path overrides**: `PathSettings[]` array allows different behavior per route segment
</details>

<details>
<summary><strong>Domain-Specific Language</strong> — Expression parser and evaluator for combat behavior</summary>

The ClassConfig system defines combat behavior through a custom DSL with a full **Pratt parser (precedence climbing)** implementation (`Core/RPN/ExpressionParser.cs`). `RequirementFactory` (~14,500 lines) maps 50+ variables to runtime evaluators, each producing both a `Func<bool>` check and a `Func<string>` log message for observability.

**Variable types:**
- **Boolean** — `InCombat`, `HasTarget`, `TargetsMe`, `Mounted`, `Swimming`, `BagFull`, `Casting`, `AutoAttacking`, ...
- **Integer** — `Health%`, `Mana%`, `TargetHealth%`, `PetHealth%`, `ComboPoint`, `BagCount`, `MobCount`, `Level`, ...
- **Parameterized** — `Spell:Name`, `Talent:Name:Rank`, `BagItem:Variable`, `Form:Name`, `Race:Name`, `npcID:Id`, `Trigger:Bit:Text`, ...

**Operators** (with precedence levels):
- Logical: `&&`, `||`, `!`
- Comparison: `==`, `!=`, `<`, `>`, `<=`, `>=`
- Arithmetic: `+`, `-`, `*`, `/`, `%`
- Grouping: `(` `)`

**Example expressions from class profiles:**
```
Health% < 45
!Immolate && TargetHealth% > 20
(TargetHealth% < 95 && TargetsMe) || TargetCastingSpell
Talent:Improved Corruption:5 && Mana% > 30
!BagItem:Item_Soul_Shard:3
!InMeleeRange && LastAutoShotMs < 500
CD_Heroic_Strike > 1500 || Rage > 60
```
</details>

<details>
<summary><strong>Observability & Visualization</strong> — Real-time state inspection across all system layers</summary>

The BlazorServer frontend provides deep runtime observability beyond basic UI controls:

- **GOAP planner view** — Visualizes the goal queue, available goals with cost values, precondition state badges (GoapKey satisfied/unsatisfied), and per-action requirement satisfaction with color-coded indicators
- **Player & target state** — Real-time display of health, mana, class-specific resources (rage, energy, runes, combo points, holy power), buffs/debuffs, casting status, equipment durability, and target information linked to Wowhead
- **Action bar inspection** — Keyboard layout component (1,600+ lines) rendering spell icons, cooldown timers, usability status, and keybinding validation with mismatch warnings
- **Session statistics** — Kill/death counters, session duration, XP rate, time-to-level estimates, bot update latency, and NPC latency
- **Structured log viewer** — Circular buffer (256 entries) with severity color-coding, powered by a custom Serilog sink with `OnLogChanged` event for real-time Blazor updates
- **Raw debug view** — Direct inspection of all addon data readers: AddonBits, PlayerReader, SpellInRange, CombatLog, ActionBarCost, and individual buff/debuff tracking
- **Leaflet map** — Multi-expansion, multi-continent tile system with NPC spawn markers, mailbox locations, zone polygons, real-time player position tracking, and route polyline rendering via Pixi.js WebGL overlay
- **PathingAPI visualization** — Debug overlays for pathfinding (DrawLines, DrawSphere) streamed via SignalR
</details>

---

Back to the [README](../README.md) &middot; see also [`architecture.md`](architecture.md) for how the solution is laid out.
