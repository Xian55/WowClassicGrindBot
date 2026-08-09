# Generated Grind Routes — Design Doc

**Status:** Phase 1 and Phase 2 implemented and verified offline — 2026-08-04.
See §15 for the defects implementation turned up.
**Scope:** Let a class profile describe a grind route as a *query* over the client's own NPC
spawn data instead of pointing at a hand-recorded waypoint file. Two generation modes
(`Wander`, `Loop`), both producing world-space waypoints that `FollowRouteGoal` walks
unchanged. Recorded routes keep working exactly as they do today; this is additive.

---

## 1. Motivation

Grind routes today are hand-recorded waypoint files. `ClassConfiguration.Paths[]` holds
`PathSettings` entries, each a `PathFilename` plus `Requirements[]`;
`GoalFactory.GetPathSettings` reads the file into `PathSettings.Path`, and one
`FollowRouteGoal` per entry walks it, GOAP picking the cheapest whose `CanRun()` passes.

Two costs:

1. **One file per race × level band × zone.** `Json/class/_/Warrior_1-10--------------.json`
   is Human-only because `1_Human.json` is a recorded Elwynn loop. Covering the other nine
   starting zones means nine more recordings.
2. **Cataclysm and Mists moved the world.** Routes recorded on som/tbc/wrath do not survive
   the zone revamps, so every profile needs re-recording per client. This is the dominant
   cost and the reason the feature exists — generation is the only fix that scales, because
   the spawn data is already partitioned per client.

Everything needed already ships:

| data | path / API | shape |
|---|---|---|
| mob spawns | `Json/npcspawnlocations/<Exp>/<mapId>.json` | `entry -> Vector3[]`, **world** coords with real z |
| creature metadata | `Json/dbc/<Exp>/creatures.json` → `SharedLib/Data/Creature.cs` | `Entry, Name, SubName, MinLevel, MaxLevel, Rank, Faction, NpcFlag, Type` |
| hostility | `Core/Database/FactionTemplateDB.cs` | faction → friend-group mask |
| zone/subzone names + world bounds | `SharedLib/Data/WorldMapAreaDB.cs` | `TryFindByAreaName`, `TryFindBySubzoneName`, `GetByAreaId` |
| world (x,y) → subzone id | `PPather/Navmesh/AreaGrid.cs:61` `GetAreaId` | baked `Json/area_grid/<era>/<continent>.grid` |
| walkable z without game files | `PPatherService.GetAreaIdAndZ` (`PPather/Search/PPatherService.cs:1072`) | `(areaId, z)` |

`Utilities/DangerZoneGenerator/Program.cs:142-172` is working prior art for the
spawns × creatures × subzone join.

---

## 2. Selector design

The selector is expressed as a **single requirement expression** rather than a bag of typed
fields. Grind-area configuration is conventionally a fixed set of knobs — a faction list, a
mob-id list, min/max target level, an elites flag, a blacklist — every one of which is
implicitly ANDed together.

That shape has two problems here. It cannot express `||` or `!`, so "wolves or kobolds, but
not the diseased ones" needs new syntax each time. And it duplicates a language this codebase
already has: `Core/RPN/ExpressionParser.cs` parses `&&`, `||`, `!`, parens, arithmetic and
comparisons for the profile's own `Requirements`.

So all of it collapses into `Mobs`:

| conventional knob | here |
|---|---|
| faction list | `"Mobs": "Faction:7"` |
| mob id list | `"Mobs": "NpcId == 299 \|\| NpcId == 6"` |
| min/max target level | `"Mobs": "Level >= 3 && Level <= 6"` |
| "no elites" flag | `"Mobs": "!Elite"` |
| blacklist | `"Mobs": "... && NpcId != 1234"` |
| creature type | `"Mobs": "Type:Humanoid"` |

Two consequences worth stating:

* **Faction and creature type are the primary selectors, not mob names.** A faction id means
  "everything hostile around here" — language-independent, immune to renames, and needing no
  per-zone authoring. `Creature.Faction` and `Creature.Type` are both already in the DBC rows
  we load.
* **Loot radius, targeting distance, mount use and level tolerances are out of scope.** They
  are already global class-config concerns (`classConfig.Loot`, `NPCMaxLevels_*`, the mount
  handler), and duplicating them per path would create two places to set the same thing.

---

## 3. Config surface

```json
"Paths": [
  {
    "Generate": {
      "Subzone": "Northshire Valley",
      "Mobs": "Name:Wolf || Name:Kobold",
      "Mode": "Wander"
    },
    "Requirements": [ "Level < 5", "Race:Human" ]
  },
  {
    "Generate": {
      "Zone": "Elwynn Forest",
      "Mobs": "Type:Humanoid && Level <= PlayerLevel + 2 && !Elite",
      "Mode": "Loop",
      "Stops": 10
    },
    "PathThereAndBack": false,
    "Requirements": [ "Level < 10" ]
  }
]
```

```csharp
public sealed class RouteGenSettings
{
    public string? Zone { get; set; }          // "Elwynn Forest"
    public string? Subzone { get; set; }       // "Northshire Valley" — narrower than Zone
    public int UIMapId { get; set; }           // explicit override, wins over names
    public string? Name { get; set; }          // label for logs / goal name / UI badge

    public string Mobs { get; set; } = string.Empty;   // the whole selector, creature scope

    public RouteGenMode Mode { get; set; } = RouteGenMode.Wander;
    public int Stops { get; set; } = 8;                  // control points; route is densified
    public float PaddingYards { get; set; } = 25f;       // polygon dilation
    public float MaxVerticalDelta { get; set; } = 5f;    // sample vs anchor z, roof gate
    public int? Seed { get; set; }                       // per-path override of global seed
}

public enum RouteGenMode { Wander, Loop }
```

`Race:Human` needs no work — `Race:` already exists (`RequirementFactory.cs:127`, handler
`:1092-1110`) and `PathSettings.Requirements` goes through the same parser
(`RequirementFactory.Init(PathSettings)` at `:420-431`). The new surface is `Generate` only.

### Reuse what `PathSettings.cs` already carries

Do not invent parallel machinery:

* **`RaceStartingZones`** (`PathSettings.cs:33-45`) — race name → starting `AreaId`.
  `Generate` with no `Zone`/`Subzone` resolves through it.
* **`TryFindRaceZone` / `TryParseMinLevel`** (`:119-157`) — reuse verbatim.
* **The 4-step UIMapId fallback** in `ConvertToWorldCoords` (`:76-110`) — `Generate` resolves
  its target the same way and in the same order: explicit `UIMapId` → `Zone`/`Subzone` name →
  race starting zone → player's current `UIMapId`.
* **`WorldCoords` + `OriginalMapPath`** (`:21-22`) — the world/map split already exists;
  generation produces world coords and back-fills `OriginalMapPath` so the Blazor/Leaflet
  route view (`FollowRouteGoal.MapRoute()`, `:65-67`) keeps working unchanged.
* **`CanRun()` / `CanRunSideActivity()`** (`:159-191`) tick memoization and the `Finished`
  hook — untouched.

---

## 4. The creature-scope expression

**Build the selector on the expression language, not on typed fields.** The profile DSL
already parses `&&`, `||`, `!`, parens, arithmetic and comparisons
(`Core/RPN/ExpressionParser.cs`, binding powers at `:324-336`). Duplicating a slice of it as
`Mobs[] + NpcIds[] + Factions[] + Types[] + MinLevel + MaxLevel + MinLevelOffset +
MaxLevelOffset + IncludeElite + Blacklist[]` gives users a second, worse language that can
only ever express AND. One expression subsumes all ten:

```json
"Mobs": "Type:Humanoid && Level <= PlayerLevel + 2 && !Elite"
"Mobs": "(Name:Wolf || Name:Kobold) && Level >= PlayerLevel - 3"
"Mobs": "Faction:7 && !Name:Defias"
"Mobs": "NpcId == 299 || NpcId == 6"
```

### It is a second scope, not the same variable table

This is the feature's sharpest edge, because the syntax is identical and the subject is not:

| | subject | decides | example |
|---|---|---|---|
| `PathSettings.Requirements` | the **player** | *whether* this path runs | `"Level < 5"`, `"Race:Human"` |
| `Generate.Mobs` | a **candidate creature row** | *which* mobs shape the polygon | `"Type:Humanoid"`, `"Level < 5"` |

`"Level < 5"` is legal in both and means different things. In creature scope `Level` shadows
to the creature's level and the player's is `PlayerLevel`, which makes the common case read
the way you would say it out loud: `Level <= PlayerLevel + 2`. `Health%` and friends are
absent from the creature table and produce a parse error, which is the desired failure.

### Implementation — a second `ExpressionParser` instance

`ExpressionParser`'s constructor already takes its maps (`RequirementFactory.cs:307-308`), so
a creature-scoped parser is a matter of supplying different ones. Keeping it a separate
instance also sidesteps a live defect: `requirementMap` keys are bare prefixes and
`ParseParameterized` re-dispatches by `text.Contains(key)` over an *unordered*
`FrozenDictionary` (`ExpressionParser.cs:307-322`), so adding short keys to the global map
risks `Trigger:0:Form change` dispatching to `CreateForm`. A separate map cannot collide with
player-scope keys at all. New keys are colon-terminated regardless.

| kind | keys |
|---|---|
| int | `Level` (avg of `MinLevel`/`MaxLevel`), `MinLevel`, `MaxLevel`, `Rank`, `NpcId`, `SpawnCount`, `PlayerLevel` |
| bool | `Elite` (`Rank > 0`), `Hostile`, `Skinnable` |
| parameterized | `Type:` (`CreatureType`, `SharedLib/Data/Creature.cs:23-39`), `Name:` (substring), `Faction:`, `Family:` |

`Type:` copies `CreateTargetType` (`RequirementFactory.cs:1112-1134`) verbatim — same
`Enum.Parse<CreatureType>(..., true)`, same enum — so `Target:Humanoid` in a combat
requirement and `Type:Humanoid` in a mob filter stay consistent. Each `Requirement` still
supplies `HasRequirement` + `LogMessage` (`Core/Requirement/Requirement.cs`); the delegates
close over a mutable cursor holding the candidate `Creature`, and the generator sets the
cursor and evaluates per row. ~3k creatures per map, once — free.

### Base filter and default

A base filter applies underneath *every* expression and is not user-overridable, mirroring
`DangerZoneGenerator/Program.cs:157-172`: `NpcFlag == None`, `MinLevel > 0`, `Type` not
`Critter`/`Totem`/`NonCombatPet`, plus a hostility check against the player faction (the
inverse of `AreaDB.FriendlyToPlayer`, `Core/Database/AreaDB.cs:234-256`).

With `Mobs` empty the default is every hostile, non-elite creature whose
`[MinLevel, MaxLevel]` overlaps `[PlayerLevel - 3, PlayerLevel + 2]`, matching the spirit of
`ClassConfiguration.NPCMaxLevels_Below/Above`.

---

## 5. Spawn polygon

Not a convex hull — a **cell occupancy mask** at `ChunkReader.CHUNKSIZE` (33.33 yd), the same
resolution `AreaGrid` already uses:

1. Take every spawn point of every selected creature that lies inside the target zone
   (`AreaGrid.GetAreaId(x,y) == areaId` when the grid is baked; otherwise
   `SubZoneArea.Contains` from `Json/subzones/<Exp>/<mapId>.json`, which is populated for all
   seven clients — `area_grid/precata` only has Azeroth + Expansion01).
2. Mark its cell; dilate the mask by `ceil(PaddingYards / CHUNKSIZE)` cells.
3. Drop connected components below a spawn-count floor, which kills lone stragglers across
   the map.

That gives a naturally concave region, O(1) containment, and no hull geometry to debug. It
also handles "bounding box" better than a box would: Northshire Valley's spawns form an L, and
a box routes the player through the abbey.

---

## 6. Mode `Wander`

Per lap, produce `Stops` waypoints. **Every point must be on the navmesh by construction, not
by after-the-fact testing** — let Detour pick it.

Add `NavmeshPathfinder.TrySnapWalkable(Vector3 wowPointHint, out Vector3 wowPoint, out long polyRef)`
— a small addition over the already-present private `query` + `filter`
(`PPather/Navmesh/NavmeshPathfinder.cs:99`), reachable via the existing public
`PPatherService.NavmeshPathfinder` property (`:87`). It runs `FindNearestPoly` with a
deliberately short vertical extent, then `ClosestPointOnPoly`.

The caller draws the offset from its own seeded RNG and asks for that point to be snapped.
**This is not the original design.** The first implementation used
`FindRandomPointWithinCircle`, letting Detour choose — which is the obvious approach and is
what `ApplyJitter` (`:865-888`) does. It had to be replaced; see §15.

**On-mesh is not enough — a rooftop, a ledge and a tree canopy are all walkable polys.** They
are simply not *connected* to the floor the route lives on. The codebase already states the
rule at `NavmeshEndpointResolver.cs:69-73`: "a position with no trustworthy height can land on
a roof or ledge above the floor the caller meant, **and only connectivity can tell those
apart**." `PPatherService.cs:580-586` carries the same warning for raw `z=0` callers.

So each candidate passes three gates, cheapest first:

* **Anchor.** Pick a random occupied cell from the mask; anchor on a real spawn point inside
  it. Its z comes from the emulator dump, so unlike a raw `z=0` query it is a *trustworthy*
  height — which is what lets the two gates below work at all.
* **Gate 1 — on-poly.** Draw a uniform offset within one cell radius of the anchor
  (`sqrt(u)` on the radius so the draw is uniform over the disc, not clustered at the
  centre), then `TrySnapWalkable` that point. The result is on the mesh by construction,
  and unlike letting Detour pick, the seed genuinely determines it.
* **Gate 2 — vertical band (free).** Reject when `|sample.Z - anchor.Z| > MaxVerticalDelta`.
  A roof, canopy or ledge poly sits meters above the ground spawn that anchored it, so this
  costs zero queries and kills the common cases before spending anything. Default from the
  existing `NavmeshEndpointResolver.PreferZToleranceYd = 5f` (`:55`), which encodes exactly
  this "same floor as the surface I trust" notion. Also re-check the mask and
  `AreaGrid.GetAreaId` here.
* **Gate 3 — connectivity, as an O(1) lookup.** No pathfinding in the sampler loop:

  ```csharp
  accept = connectivity.ComponentOf(candidateRef) == routeComponent;   // array read
  ```

  `TrySampleWalkable` already has the `polyRef` in hand
  (`FindRandomPointWithinCircle`'s `out long randomRef`). Same component means a path provably
  exists — that is what "connected component" means — so this is not an approximation of the
  reachability test, it *is* the reachability test, and it is exactly what separates a
  disconnected porch roof or bridge deck from the ground the route lives on.

* Reject candidates closer than a min-spacing to an already-chosen stop, so the route spreads.
* Bounded resample attempts. If the budget runs out, emit the shorter route rather than a
  broken one, and log the shortfall.

**No unbaked fallback.** If the continent has no baked navmesh, `TrySampleWalkable` cannot
guarantee anything — generation fails, logs the missing cache dir (`GetQueryNavmesh` already
emits "No baked navmesh for {Continent}", `PPatherService.cs:1116-1120`), and sets
`Generated = false` so the path drops out of GOAP. Snapping to raw spawn coordinates was
considered and rejected: an emulator spawn is standable in-game but carries no guarantee it is
on *our* baked mesh, which is precisely the guarantee being asked for.

`FollowRouteGoal.RefillWaypoints` (`Core/Goals/FollowRouteGoal.cs:375`) asks the generator for
a fresh route each lap instead of reversing the old one, so the bot never retraces a line.
`PathThereAndBack` is ignored in this mode.

---

## 7. Mode `Loop`

Deterministic and a drop-in replacement for a recorded file. Unlike `Wander` it takes no
seed at all: which cells are densest, how far apart stops must be, and the walking distance
between them are all fixed by the zone and the mob filter, so there is nothing left to
randomise.

* `SpawnPolygon.DensityPeaks` returns the densest occupied cells, at least
  `MinSpacingYards` apart, densest first - the mean spawn position per cell, greedily
  selected. Deterministic by construction; ties break on position so dictionary enumeration
  order cannot leak in.
* Each peak passes **the same gates 1-3** as `Wander`. A centroid is an average, so it can
  land off-mesh, on a roof, or across a wall even when every spawn that formed it is fine.
* `BuildCostMatrix` measures the **walking** distance between every surviving pair through
  `IPPather.FindWorldRoute` - the same call `Densify` will make later, so the tour is
  optimised against the route that actually gets walked. A pair the pather cannot answer
  falls back to euclidean rather than being treated as unreachable; gate 3 already
  established they share a component, so a failure there is the pather giving up, not a wall.
* `TourSolver` orders them: nearest-neighbour, then 2-opt until no improving segment
  reversal remains. Measured: Northshire 918 → 800 yd, Elwynn over 12 stops 6367 → 4613 yd.
  A self-crossing is always longer than the uncrossed alternative, so removing them is
  exactly what 2-opt does.
* The tour is closed explicitly - the first stop is appended again - so the leg home is
  pathed and walked like any other. `PathThereAndBack: false` is the correct pairing.

**Deviation from the original plan.** This does not reuse
`Utilities/WowheadDB_Extractor/TSP/GeneticTSPSolver.cs`. That solver derives its distances
internally from `Vector2` positions via Euclidean and draws from a `static readonly Random`.
Route generation needs the opposite of both - cost is a navigable path length, and a tour
must reproduce exactly - so reusing it would have meant replacing its distance function, its
randomness and its point type, which is most of it. At the handful of stops a grind route
carries, 2-opt is effectively optimal and needs no tuning.

## 8. Connectivity

### 8a. `NavmeshConnectivity` — the gate-3 primitive

**Build.** One BFS/union-find over polygon adjacency, walking `DtLink`s through
`NavmeshTileCache.NavMesh` (public at `:113`, with its `ReaderWriterLockSlim` at `:114` —
build under the read lock). Every poly gets a component id.

**Key on `(tileX, tileZ, polyIndex)`, never on the raw `polyRef`.** A ref encodes a tile
*slot* index and a salt, both of which change when the LRU evicts a tile and later reloads
it — which happens routinely mid-sampling. Keying on the ref made lookups start missing
partway through a run, which reads as "unreachable" and silently shortens or reshapes the
route. `DtDetour.DecodePolyIdPoly` plus the tile header's `x`/`y` give the stable identity;
links are translated the same way, since a link can cross into another tile.

**Scope it to the route, not the continent.** The spawn polygon's world AABB converts to a
tile range via `NavmeshCoords.GetTileIndex` (`:40`); flood only those tiles plus a one-tile
skirt. `NavmeshSettings.TileWorldSize = 133.33f`, so a ~1000 yd zone is ~60 tiles — a few tens
of thousands of polys, single-digit milliseconds, once.

**Cache and invalidate.** Key by continent + tile range. Tiles bake on demand and a newly
baked tile can join two components, so stamp the cache with
`NavmeshTileCache.TilesBakedThisSession` (`:131`) and rebuild when it moves.

```csharp
public int ComponentOf(long polyRef);              // O(1)
public bool SameComponent(long a, long b);
```

**Why not `FindWorldRoute` per candidate:** it runs full A* plus `FindStraightPath`,
smoothing, jitter and edge-margin passes (`NavmeshPathfinder.FindPath`, `:267`) — hundreds of
times the work, to answer a yes/no the component map answers with an array read. Reserve the
real pathfinder for `Loop`'s distance matrix, where the *length* is what is wanted.

**`Wander` regeneration therefore does no pathfinding at all.** It runs from
`RefillWaypoints` on the bot thread, so its whole budget is the cached component map plus
`Stops` array reads — no A*, no contention with `Navigation`'s requests on `PathFinderThread`.
The component build is paid once and survives across laps.

### 8b. `RouteValidator` — `Loop`'s distance matrix

`Loop` runs at session build time with the pather to itself, so it can afford real navigable
lengths where `Wander` cannot. `GetPathSettings` resolves lazily inside `CreateSession`
(`Core/BotController.cs:463-500`), before the bot loop starts — and `PPatherService` is a
non-reentrant singleton with a two-call `SetLocations`/`DoSearch` protocol that `Reset()`s on
continent change (`PPather/Search/PPatherService.cs:305-307`), which is exactly why this must
not be spread across threads.

* Filter every candidate stop through gates 1-3 first, so no unreachable stop reaches the
  matrix.
* `pather.FindWorldRoute(uiMapId, startIndoors: false, from, to)`
  (`Core/PPather/IPPather.cs:20`) for the surviving pairs; leg length feeds the TSP.
* Validate the chosen tour including **the closing leg, last → first** — the one a euclidean
  tour most often gets wrong and the one `PathThereAndBack: false` walks every lap. An empty
  result here should be impossible after gate 3; if it happens the component map and the live
  pather disagree, which is a bug to chase, not a case to silently drop.
* **Log any stop that was dropped**, with coordinates. A silently 40%-culled route reads as
  "the generator works" when it does not. If drops exceed `Stops / 3`, set `Generated = false`.
* The dense leg points are discarded — `Navigation` re-requests during the walk anyway, and
  `PathSettings.Path` stays a waypoint list exactly like a recorded file.

**Gate on the local pather.** `RemotePathingAPI` (V1) turns a 429 rate-limit into an *empty
route* (`PathingAPI/CLAUDE.md:34-38`), indistinguishable from "unreachable" — a batch
validation loop through it would cull a perfectly good route. Validate only when `IPPather` is
`LocalPathingApi`; otherwise skip and log that it was skipped.

---

## 9. Seed

* `ClassConfiguration.Seed` (`int?`, default `null` → `Random.Shared.Next()` drawn once at
  session start and **logged**, so a run can be reproduced after the fact).
* `RouteGenSettings.Seed` overrides per path.
* Derive each generation's `Random` from `RouteSeed.For(globalSeed, path.Id, lap)` so laps
  differ but the whole run replays identically. **That mix must not be `HashCode.Combine`** —
  see §15.
* The same seed fills `PPatherService.PathJitterSeed` (`:97`), which is currently declared but
  never set from config — closing that makes jittered legs reproducible too.

---

## 10. `PathEnd_{Id}` / `PathDist_{Id}`

`RequirementFactory` binds both families per path **id**, in its constructor
(`ClassConfiguration.Initialise:220`), which runs *before* `GoalFactory.GetPathSettings` ever
loads a route:

```csharp
private void BindPathSettingsIntVariables(PathSettings[] paths)
{
    ...
    intVariables.TryAdd($"{prefixKey}_{suffix}", settings.GetDistanceXYFromPath);
    InitPerKeyAction(prefixKey, suffix);
    Init(settings);
}
```
(`RequirementFactory.cs:720-735`; the bool twin `PathEnd_{Id}` / `PathEnd_Any` at `:633-662`)

Both bind **delegates over the instance**, not values — `GetDistanceXYFromPath`
(`PathSettings.cs:193`) reads `Path` at call time, and `PathFinished` calls the `Finished` func
that `FollowRouteGoal` overwrites at `:124`. A `Path` assigned later, or replaced every lap by
the wander generator, is therefore picked up with no rebinding. **Nothing here needs
reordering, and it should not be "fixed" to bind eagerly.**

Two consequences that do need handling:

* **`Id` must stay stable.** `settings.Init(globalTime, playerReader, i)` assigns
  `Id = Id == default ? i : Id` (`PathSettings.cs:69`) and `ClassConfiguration.cs:215` enforces
  uniqueness. Generated entries go through the same loop, so `PathEnd_2` / `PathDist_2` mean
  the same thing whether path 2 is a file or a query. A profile can therefore already use
  `"PathEnd_0"` to react to a generated wander lap completing — the same signal
  `Navigation_OnDestinationReached` uses to trigger regeneration.
* **Generation failure must not leave a live goal with an empty route.** With
  `Path.Length == 0`, `GetDistanceXYFromPath` returns `int.MaxValue` (`PathSettings.cs:195`)
  and `PathFinished` is permanently true — a silently dead path that still passes `CanRun()`.
  A `Generated` flag on `PathSettings`, ANDed into `CanRun()` (`:159`), makes a path whose
  query yielded nothing drop out so GOAP falls through to the next entry. That reuses the
  existing priority ladder instead of adding a new requirement keyword.

---

## 11. Failure modes & known gaps

* **Two scopes, one syntax.** `"Level < 5"` is valid in both `Requirements` (player) and
  `Generate.Mobs` (creature) and means different things. Mitigations: the creature table has no
  player-only names except the explicit `PlayerLevel`, so a misplaced `Health%` fails at parse
  time rather than silently; and the log line for a generated path prints the resolved filter
  and the matched creature count, so "0 mobs matched" is visible immediately.
* **Same component ≠ nearby.** Connectivity proves a path exists, not that it is short — a
  candidate across a river with a bridge 600 yd away passes gate 3. The z-band and min-spacing
  cover the common cases; `Loop` additionally reorders by real walking distance. If wander
  routes still show long detours in practice, add a max-spacing reject before adding anything
  cleverer.
* **Navmesh coverage is a hard prerequisite.** Both the on-mesh guarantee and leg validation
  need baked tiles for the target continent; without them generation fails loudly instead of
  degrading. This is a behaviour change for users on an unbaked install — the log line must
  point at `--bake=<Continent>`.
* **`area_grid/precata` has only Azeroth + Expansion01.** Kalimdor/Northrend on
  vanilla/tbc/wrath fall back to `SubZoneArea` AABBs, which overlap heavily — expect a looser
  zone boundary there. Cata/MoP, the clients that motivate this, are fully baked.
* **Spawn dumps are emulator data.** Coverage varies per client dir; `npcspawnlocations/som`
  has only maps 0 and 1. Generation must degrade to a clear log line, not an exception, when
  the file is missing (`AreaDB.ReadJsonOrNull`, `:87-100`, is the precedent).
* **Fileless paths break two assumptions**: the `File.Exists` guard at
  `ClassConfiguration.cs:199`, and `Path.GetFileNameWithoutExtension(pathSettings.FileName)`
  used for the goal name and the synthetic `KeyAction` label (`FollowRouteGoal.cs:93, 115-121`).
  `Generate.Name` covers the latter.

---

## 12. What we explicitly do NOT do

* **No new requirement keywords in the global player-scope map.** The creature scope is a
  separate `ExpressionParser` instance with its own tables.
* **No convex/concave hull geometry.** A cell mask is concave for free and O(1) to test.
* **No offline generator utility in v1.** Runtime generation is needed anyway for per-lap
  wander; a CLI that dumps the same generator's output is a later convenience, not a
  prerequisite.
* **No changes to recorded-route behaviour.** `PathFilename` entries take exactly the path they
  take today.
* **No dry-spot timer in v1** - no per-stop time cap before moving on.

---

## 13. Phasing

1. `CreatureRequirementFactory` (the creature scope) + `NavmeshConnectivity` +
   `NavmeshPathfinder.TrySampleWalkable` + `NpcSpawnDB` + `SpawnPolygon` + `Wander` + `Seed`,
   with the config plumbing and the `File.Exists` fix. Connectivity is in phase 1, not
   deferred — gate 3 is what makes `Wander` correct, so shipping the sampler without it ships
   rooftop routes.
2. `Loop`: `RouteValidator`, the `GeneticTSPSolver` move, clustering, the navigable-length
   distance matrix, tour validation including the closing leg, and the drop/repair policy.
3. **Done for README**; `CoreTests routegen` renders every generated path in a profile to an
   SVG over the minimap tiles (`--profile`, `--output`), which is how a route is judged while
   iterating. It reports rather than asserts - it prints
   spacing, vertical spread, area-id histogram and route hashes, and is driven by hand
   (`CoreTests routegen --mode Loop ...`). Turning those reports into assertions is the
   remaining gap.

---

## 14. Verification

1. `dotnet build MasterOfPuppets.sln`.
2. **Expression scope.** Parse `"Type:Humanoid && Level <= PlayerLevel + 2 && !Elite"` and
   assert the matched creature set against a known zone. Then assert `"Health% > 50"` in
   `Generate.Mobs` throws at parse time (`ExpressionParser.cs:176-178`), and that
   `"Type:Humanoid"` in `PathSettings.Requirements` does the same — the two tables must not
   leak into each other.
3. Add a `Generate` entry to a scratch copy of `Json/class/_/Warrior_1-10--------------.json`
   and run `dotnet run --project BlazorServer`. The Blazor route view renders `MapRoute()` →
   `OriginalMapPath`, so the generated loop is visible on the Leaflet map with no UI change.
   Confirm it sits on the Northshire wolf/kobold spawns and not inside the abbey.
4. Fix `Seed`, run twice, diff the logged waypoint arrays — byte-identical in `Loop` mode,
   identical per-lap in `Wander`.
5. Repeat against a `mop` install pointed at Elwynn: same profile entry, different client,
   valid route. This is the acceptance test for the re-recording problem.
6. **Rooftop test.** Generate in a zone with stacked geometry — Goldshire (inn roofs, bridge)
   or Northshire (abbey) — and assert no waypoint lands on a roof. Force the failure first by
   disabling gate 2, confirm roof points appear, then re-enable and confirm they do not. A gate
   never observed rejecting anything is a gate that may not be wired up.
7. **Time the regeneration.** Log elapsed ms for a `Wander` lap regeneration; it should be
   dominated by nothing (array reads), with the one-off `NavmeshConnectivity` build reported
   separately. If a lap boundary shows an A*-shaped cost, gate 3 is falling through to the
   pathfinder.
8. `PathEnd_0` / `PathDist_0` against a generated path: distance falls as the player walks it,
   `PathEnd` flips at lap end. Then break the query on purpose (`"Mobs": "Name:NoSuchMob"`) and
   confirm the path drops out of GOAP instead of running empty.
9. Drive it live for several laps. `Navigation` failing to path between two generated waypoints
   should now be impossible — if it happens, the component map and the live pather disagree.
10. `dotnet test`.

---

## 15. Defects found while implementing phase 1

Both were caught by the offline harness (`CoreTests routegen`) comparing a route hash across
separate processes, and both had produced plausible-looking output that was quietly wrong.

### 15.1 `HashCode.Combine` is randomized per process

`RouteSeed.For` originally used `HashCode.Combine(sessionSeed, pathId, lap)`. `System.HashCode`
seeds itself randomly at process start as a hash-flooding defence, so the same
`ClassConfiguration.Seed` produced a different route in every session. Within one process it
looked perfect — the "sample twice, compare" check passed — which is exactly why the harness
has to compare across processes.

Replaced with an explicit splitmix64 finalizer. Any future "combine some ints into a seed"
must avoid `HashCode.Combine` for the same reason.

### 15.2 Poly refs are not stable identities

Gate 3 looked the candidate's `polyRef` up in the component map. Refs embed a tile slot and
salt, so once the tile cache evicted and reloaded a tile, every lookup for it missed and
returned "unreachable". The route then depended on which tiles happened to be resident.
Fixed by keying the map on `(tileX, tileZ, polyIndex)`.

### 15.3 Sparse waypoints defeat Navigation's trivial-hop heuristic

`Navigation.RefillRouteToNextWaypoint` pathfinds only when
`distance > MaxDistance || distance > AvgDistance * 2`, and `AvgDistance` is the route's own
mean point spacing (`SetWayPoints`). The intent is "a hop no longer than the gaps in this
route is not worth a pathfinder call" — sound for a recorded route of hundreds of points a
few yards apart, where the threshold lands around 15 yd.

A generated route is 8 anchors across a subzone, so `AvgDistance` is ~60 yd and the threshold
~120 yd, under `MaxDistance`. Every leg therefore counted as trivial: the bot turned toward
the next anchor and walked blind. Observed after a class-trainer visit — it set off straight
through the abbey and ended in `StuckDetector`, with no `Pathfinder`/`Spline loaded` line in
the log at all.

Fixed with `Navigation.SparseWaypoints`, set by `FollowRouteGoal` when `Generate != null`,
which pins `AvgDistance` to `OutDoorMinDistance` so every leg is pathfound.

Deliberately **not** changed: the heuristic itself. Any short hand-authored route has the same
exposure, and an absolute ceiling on the trivial-hop test would be the general fix — but that
alters pathing for every existing profile, so it is a separate decision.

### 15.4 Sparse anchors starve target acquisition

The first implementation emitted the stops themselves as the route - a handful of anchors
across the zone. That is how hotspot-style grind engines work, because they hunt *around* an
anchor. This bot does not: it acquires targets by cycling Tab **while walking the path**, so a
300 yd leg between two anchors is dead travel past every mob beside it.

Observed on Ammen Vale with legs of 291, 191, 170, 511 and 277 yd. `Stops` now means control
points, and `Densify` paths each consecutive pair and emits those points, so a route is
hundreds of waypoints a few yards apart - the shape the rest of the bot is tuned for. In
Northshire, 8 stops becomes 365 waypoints.

That required serialising the pather: `SetLocations`/`DoSearch` is a stateful two-call
protocol on a non-reentrant singleton, and until now the only caller was Navigation's
`PathFinderThread`, so it was serialized by accident. Generation runs on the bot thread.
`LocalPathingApi` now locks around both calls - including `SearchFrom`, which
`FindMapRoute` read after the search and which belongs to whichever search ran last.

### 15.5 What the vertical check actually proves

The harness reports `|z - nearest spawn z|`, which is a *proxy* for gate 2, not gate 2
itself: gate 2 compares against the spawn that anchored the sample, which the reporter no
longer knows. A large dz alone means nothing on a hillside. The signal is a large dz at a
**small** dxy — ground that close should be the same height. Elwynn produced one such stop
(21.9 yd up, 3.0 yd away) before 15.1/15.2 were fixed; none after.

## 16. Verified so far

`dotnet build MasterOfPuppets.sln` clean. The solution contains no test projects, so
`dotnet test` is a no-op; `CoreTests` is the harness.

```
CoreTests routegen --exp som --subzone "Northshire Valley" --mobs "Name:Wolf || Name:Kobold" --level 4
CoreTests routegen --exp <som|tbc|wrath|cata|mop> --zone "Elwynn Forest" --mobs "Type:Humanoid && !Elite" --level 8
```

* **Expression scope** — 7 creature expressions parse; `Health% > 50`, `Race:Human` and
  `BagFull` are all rejected. The two tables do not leak into each other.
* **Northshire Valley** (som) — 5 creature types, 100 spawns, 120 cells, 10368 polys in 176
  components. The 8 generated stops land within ~15 yd of the positions a human picked when
  hand-recording the same area, which is independent confirmation that the spawn data lands
  on the same ground.
* **Determinism** — identical route hash across separate processes, per zone and per client.
  Consecutive laps differ.
* **Every client** — the same profile entry generates a valid route on som, tbc, wrath, cata
  and mop, over each client's own spawn set and geometry. This is the acceptance test for the
  re-recording problem.
* **Vertical** — 0 suspect stops in Elwynn, Northshire and Goldshire.

Still unverified: anything requiring the live bot — the GOAP fallthrough when `Generated` is
false, per-lap regeneration through `FollowRouteGoal.RefillWaypoints`, and the Blazor preview
page rendering on the Leaflet map.

## Appendix — relevant existing code

| concern | file:line |
|---|---|
| Path config model | `Core/ClassConfig/PathSettings.cs:12-30, 33-45, 47-63, 72-117, 159-191` |
| Class config model / init | `Core/ClassConfig/ClassConfiguration.cs:55-62, 136-317` (`File.Exists` guard at `:199`) |
| Path file → `Vector3[]` | `Core/GoalsFactory/GoalFactory.cs:401-452` |
| Goal registration / cost ladder | `Core/GoalsFactory/GoalFactory.cs:368-391` |
| Route goal | `Core/Goals/FollowRouteGoal.cs:50-56, 115-124, 375-485` |
| Waypoint consumption | `Core/GoalsComponent/Navigation.cs:177-313, 566-642` |
| Requirement compilation | `Core/Requirement/RequirementFactory.cs:371-443, 633-662, 720-735` |
| Requirement keyword map | `Core/Requirement/RequirementFactory.cs:120-137`; `Race` handler `:1092-1110`; `Target:` handler `:1112-1134` |
| Expression parser / tokenizer | `Core/RPN/ExpressionParser.cs:31-78, 183-336`; `Core/RPN/ExpressionTokenizer.cs:66-196` |
| Goal selection / switching | `Core/GOAP/GoapPlanner.cs:36-77`, `Core/GOAP/GoapAgent.cs:189-198, 236-293` |
| Creature metadata | `SharedLib/Data/Creature.cs:5-39`; `Core/Database/CreatureDB.cs:17-19` |
| Spawn data + hostility | `Core/Database/AreaDB.cs:44-150, 165-233, 234-256` |
| Coordinate conversion | `SharedLib/Data/WorldMapAreaDB.cs:79-124, 162-201`; `PPather/Navmesh/NavmeshCoords.cs:21-63` |
| Subzone AABB | `SharedLib/Data/SubZoneArea.cs:6-24` |
| Area grid | `PPather/Navmesh/AreaGrid.cs:28, 54-119` |
| Navmesh query surface | `PPather/Navmesh/NavmeshPathfinder.cs:99, 234, 267, 855-888`; `NavmeshTileCache.cs:113-131` |
| Endpoint resolver / roof rule | `PPather/Navmesh/NavmeshEndpointResolver.cs:55, 69-113` |
| Pather facade | `PPather/Search/PPatherService.cs:87, 94-97, 305-307, 556, 996, 1072, 1130-1150` |
| Bot-side pather interface | `Core/PPather/IPPather.cs:9-23` |
| TSP prior art | `Utilities/WowheadDB_Extractor/TSP/GeneticTSPSolver.cs:17, 45, 60` |
| Spawn/creature/subzone join prior art | `Utilities/DangerZoneGenerator/Program.cs:142-172` |
| Data paths | `DataConfig/DataConfig.cs:25, 33, 35, 47, 145, 148` |
| Docs mandate | `CLAUDE.md:29`; `Core/CLAUDE.md:31-33` |
