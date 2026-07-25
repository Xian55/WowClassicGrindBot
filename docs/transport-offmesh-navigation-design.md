# Transport & Off-Mesh Navigation — Design Doc

**Status:** Draft / proposal — **blocked on empirical in-game data** (see §7). Design captured; execution feasibility for moving transports depends on what the client actually reports.
**Scope:** Let on-foot routing cross seemingly disconnected navmesh regions using **off-mesh connections**, including dynamic transports — elevators, boats, zeppelins. Related: [`flying-navigation-design.md`](flying-navigation-design.md) (shares the "no live Z" constraint).

---

## 1. Motivation

The DotRecast navmesh is baked from **static** ADT/WMO geometry. It has gaps that are genuinely traversable in-game but not on the mesh:

- **Static gaps** — a jump across a ledge, a drop, a doorway the bake split.
- **Dynamic transports** — the boat deck / zeppelin / elevator platform is a **moving GameObject**, not part of the static bake, so it is never walkable-in-place on the mesh. Yet it connects two mainland regions (e.g. Menethil Harbor ↔ Auberdine).

Both are the same graph problem — *connect point A to point B that aren't mesh-adjacent* — solved with **off-mesh connections**. They differ entirely at **execution** time.

---

## 2. Two separable parts

| Part | What it is | Difficulty |
| --- | --- | --- |
| **A. Off-mesh graph** | author + bake + route across A→B links | tractable, greenfield |
| **B. Dynamic execution** | actually board/ride/disembark a moving transport | hard — needs runtime detection (§6–7) |

Part A is worth building on its own (static jumps/drops). Part B rides on top of it for transports.

---

## 3. Current state (verified in code)

- **No off-mesh support** in the bake or query (`PPather/Navmesh/*`). DotRecast *does* provide `DtOffMeshConnection`; the pipeline simply never creates or consumes any.
- **No transport handling** anywhere in `Core` / `PPather`.
- Only movement signal related is `AddonBits.OnTaxi()` (bit 10, `UnitOnTaxi`) — that's the **flight-master taxi** network, a different mechanic (auto-flown, no control). Not boats/zeppelins/elevators.
- Live player state available: `PlayerReader.WorldPos` (X,Y trusted), `WorldPosZ` (suspect — see §6), `MapX/MapY`, `UIMapId`, `Direction`; bits `Mounted()`(18) `Flying()`(21) `Falling()`(23) `OnTaxi()`(10).

---

## 4. Part A — Off-mesh connections

### 4.1 Authoring format

A hand-authored connection list, per continent (JSON under `Json/…`), each entry:

```jsonc
{
  "id": 1,
  "start": [x, y, z],          // world coords, mesh-side boarding point
  "end":   [x, y, z],          // world coords, mesh-side landing point
  "radius": 2.5,               // snap radius to the endpoints
  "bidir": true,               // usable both directions?
  "type": "boat",              // jump | drop | elevator | boat | zeppelin
  "area": "...", "flags": "...", // Recast poly area/flags for cost/filtering
  "wait": { "board": "dock123", "arrive": "dock456" } // optional metadata for transports
}
```

`type` drives execution (§5). `jump`/`drop` need nothing at runtime; transports carry the extra metadata.

### 4.2 Bake vs pather-graph — two options

- **(A) Bake into `dtNavMesh`** — add `DtOffMeshConnection`s to tiles in `NavmeshTileBuilder`; Detour's `FindPath` then routes through them natively and returns them as path points. Cleanest for routing; the connection becomes a first-class navmesh edge.
- **(B) Pather-graph edges** — keep the mesh untouched; add the links as edges in the higher-level pather graph and stitch two mesh paths (to `start`, then from `end`). More code in the pather, but decouples authoring from the (expensive) bake and allows live edits.

**Lean (B) first** — off-mesh data changes shouldn't force a re-bake, and transport links are few and hand-tuned. Revisit (A) if routing quality needs it.

### 4.3 Follower awareness

The pather must emit the link as a **typed waypoint pair** (enter `start`, exit `end`, `type`). The follower switches from normal ground-follow into a **link handler** for that segment, then resumes ground-follow at `end`.

---

## 5. Transport taxonomy (drives execution)

| Class | Examples | Motion | Runtime detection needed? |
| --- | --- | --- | --- |
| **Static gap** | jump, drop, doorway | none | **No** — walk/jump `start`→`end` |
| **Horizontal** | boat, zeppelin | large X/Y | Yes — board/ride/disembark |
| **Vertical** | elevator, lift | Z only (unreadable) | Yes — and **fall-risk** |

---

## 6. Part B — Dynamic detection (the crux)

**There is no `ON_TRANSPORT` movement flag exposed to classic addon Lua** and no reliable GameObject-position API. So we cannot directly ask "is a boat at the dock" or "am I on the platform." We must infer from what the client leaks.

### Candidate signals (to confirm with §7 data)

1. **`GetPlayerMapPosition` → (0,0)/invalid while on a transport.** Well-known classic quirk: transport-relative position can't map, so map coords read as `(0,0)`. If true, this is the strongest **"I'm on a transport"** indicator — DataToColor already reads map position.
2. **World X/Y drifting with zero movement input.** On a *horizontal* transport you move without pressing keys → detectable coupling → confirms boat/zeppelin carrying you.
3. **`Falling()` stays false while carried** — distinguishes "riding" from "fell off."
4. **`WorldPosZ`** — does it update at all mid-transport? (Also the open question in the flying doc.)

### Implications by class

- **Boat / zeppelin (horizontal): plausibly detectable** via signals 1+2. Flow: walk to boarding point → wait → board (attempt + confirm) → ride *blind* (position may be invalid) → detect arrival when position becomes valid near the destination endpoint → step off.
- **Elevator (vertical): likely NOT detectable** with current signals — X/Y barely change, Z is unreadable, and a mistimed step = fall into the shaft (`Falling()` → damage/death). **Defer**; if ever attempted, gate hard on `Falling()`-abort + conservative timing, and treat as best-effort.

If detection proves impossible, transports fall back to: **route around on the ground**, or **hand off to the flight-master taxi** (`OnTaxi`) where one exists.

---

## 7. Empirical data-gathering plan  ⟵ *you run this*

Goal: determine which §6 signals are real, so we know if boat/zeppelin (and maybe elevator) execution is feasible **before** building it.

### What to read
Per tick the bot already exposes: `MapX`, `MapY`, `UIMapId`, `WorldPos.X`, `WorldPos.Y`, `WorldPosZ`, `Direction`, and bits `Mounted/Flying/Falling/OnTaxi`. Easiest capture:
- Watch the **Leaflet player marker + coordinate readout** while boarding, and/or
- Temporarily enable **verbose `PlayerReader`/`AddonReader` logging** (or add a one-off debug line dumping the fields above), and read the BlazorServer console/`out.log`.

### Scenarios & moments to capture
Run each transport type; **record the fields at each moment**:

**Boat (e.g. Menethil ↔ Auberdine) and Zeppelin (e.g. Orgrimmar ↔ Undercity):**
1. Standing on the **dock**, boat absent.
2. Boat **arrives / docked**, you standing on dock.
3. **Instant of stepping onto** the deck.
4. **Mid-voyage** (transport moving, you standing still, no keys).
5. **Arrival** at the far dock.
6. **Stepping off** onto the far dock.

**Elevator (e.g. Thunder Bluff lift, Undercity):**
1. Standing at the boarding edge, platform elsewhere.
2. Platform **arrives at your level**.
3. **Stepping onto** the platform.
4. **Mid-travel** (platform moving vertically, you still).
5. **Arrival** at the far level; step off.

### Fill-in table (one row per moment)

| Scenario | Moment | MapX | MapY | UIMapId | WorldX | WorldY | WorldPosZ | Dir | Mounted | Flying | Falling | OnTaxi | Notes (pos moving w/o input? coords go 0,0?) |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Boat | 1 dock, absent | | | | | | | | | | | | |
| Boat | 2 docked | | | | | | | | | | | | |
| Boat | 3 boarding | | | | | | | | | | | | |
| Boat | 4 mid-voyage | | | | | | | | | | | | |
| Boat | 5 arrival | | | | | | | | | | | | |
| Boat | 6 stepping off | | | | | | | | | | | | |
| Zeppelin | … | | | | | | | | | | | | |
| Elevator | … | | | | | | | | | | | | |

### Hypotheses this test resolves
- **H1** MapPos → (0,0)/invalid while on a transport? (signal 1)
- **H2** WorldPos X/Y changes with no movement input while riding? (signal 2)
- **H3** `Falling()` stays false while carried?
- **H4** Elevator: does *anything* readable change besides (unreadable) Z?
- **H5** Does `WorldPosZ` update live while on a transport / airborne? (also unblocks the flying doc)

The answers decide whether §8 boat/zeppelin execution is buildable and whether elevators are hopeless.

### Findings — 2026-07-25, cmangos `.gps` on the Theramore↔Menethil boat (Dustwallow)

> ⚠️ **Server-side data.** `.gps` is a GM command reporting *server*-authoritative state. It proves transport **mechanics**, not what our **client addon** can read. The client-side test above (H1) is still the gate.

Six samples while riding (standing still after boarding):

| # | Global X | Global Y | Global Z | Transport coords (x,y) | Area |
|---|---|---|---|---|---|
| 1 | -4283.57 | -5272.62 | 6.0986 | -4.23, 7.44 (walking) | 2318 Great Sea |
| 2 | -4277.19 | -5179.63 | 6.0822 | -1.64, 11.54 | 2318 |
| 3 | -4275.74 | -5130.61 | 6.0822 | -1.64, 11.54 (constant) | 2318 |
| 4 | -4274.89 | -5105.40 | 6.0822 | -1.64, 11.54 | 2318 |
| 5 | -4274.00 | -5081.80 | 6.0822 | -1.64, 11.54 | 2318 |
| 6 | -4271.93 | -5033.68 | 6.0822 | -1.64, 11.54 | 2318 |

What this establishes (server-side):
- **On-transport is a first-class state** with two coordinate frames: **global** (moves along route, ~240 yd of Y over the samples) and **transport-relative** (deck offset, **constant while standing** → the "being carried" tell).
- **Deck Z is constant (6.08)**, far above `GroundZ/FloorZ ~1.78` and seafloor `-15…-48` — the deck is a fixed-height platform moving in X/Y over void/water.
- **Area flips to `2318 "The Great Sea"`** while aboard — a candidate client-readable hint.

What it does **not** answer (still needs the client test):
- **H1** — does the *client* `GetPlayerMapPosition` go (0,0)? If yes, the addon is **blind to the gliding global pos** (so H2 is real server-side but *unusable* client-side) → confirms "ride blind" model.
- Whether the client exposes **any** on-transport flag / transport GUID / the `2318` area to Lua.

**Add to the client-side capture** (same boat, bot attached): does `MapX/MapY` read (0,0)? does the reported **AreaID/zone flip to 2318**? does `WorldPos` glide or freeze? These map server truth → addon reality.

---

## 8. Execution state machines (provisional — pending §7)

### Boat / zeppelin
```
 …ground path→ WALK_TO_DOCK ─ arrived ─► WAIT_BOARD ─ transport present ─► BOARD
                                              │ timeout: keep waiting        │ confirm on-transport signal
                                              ▼                              ▼
                                          (retry)                          RIDE ── arrival signal near end ─► DISEMBARK ─► ground path
                                                                            │ Falling()/fell off → ABORT (re-plan)
```
- **WALK_TO_DOCK**: normal ground follow to link `start`.
- **WAIT_BOARD**: at the boarding point; detect the transport (signal 1/2). No schedule modeling in v1 — just wait + detect.
- **BOARD**: step onto deck; confirm via on-transport signal within a timeout, else retry.
- **RIDE**: hold position; position may read invalid — ride blind.
- **DISEMBARK**: when position becomes valid near link `end`, step off onto the mesh; resume ground follow.

### Elevator (deferred / best-effort)
Same skeleton, but boarding must only happen when the platform is confirmed present at level — **which §7 may show is undetectable**. Hard-gate on `Falling()` abort. Likely **unsupported in v1**.

---

## 9. Failure modes & safety

| Failure | Detection | Response |
| --- | --- | --- |
| Missed the boat (left without us) | position didn't couple within timeout | WAIT_BOARD retry |
| Stepped into empty elevator shaft | `Falling()` true | abort; take the death/rez or ground-route |
| Fell off mid-ride | `Falling()` / position valid unexpectedly | ABORT, re-plan from new pos |
| Wrong stop / overshoot | arrival signal vs link `end` distance | ride to next valid-position window; disembark at nearest |
| Blind during ride (pos invalid) | expected | wait for valid position near `end`; timeout guard |

---

## 10. What we explicitly do NOT do (v1)

- No transport **schedule/timetable** modeling — wait-and-detect only.
- No GameObject scraping we can't reliably get in classic.
- No elevator support until §7 proves detection is possible.
- No re-bake required for off-mesh edits (favor pather-graph option §4.2-B).

---

## 11. Phasing

- **P0 — Empirical data (§7).** *(you)* Gather client-reported state on boat/zeppelin/elevator. **Gate for everything dynamic.**
- **P1 — Off-mesh infra (Part A).** Authoring format + loader + pather-graph edges + typed link waypoints + follower link-handler. Validate on a **static gap** (jump/drop) — no dynamic detection.
- **P2 — Horizontal transports.** Add on-transport detection (per §7 results) + boat/zeppelin state machine on the link.
- **P3 — Elevators.** Only if §7 shows a usable signal; fall-safety mandatory. May remain unsupported.

---

## Appendix — relevant existing code

- `PPather/Navmesh/NavmeshTileBuilder.cs` — bake; where `DtOffMeshConnection`s would be added (option A).
- `PPather/Navmesh/NavmeshPathfinder.cs` — query; `TryGetHeight`; where link routing/stitching lives (option B).
- `PPather/Search/*` — higher-level path graph (pather-graph edges, option B).
- `Core/Addon/PlayerReader.cs` — `WorldPos`, `WorldPosZ`, `MapX/MapY`, `UIMapId`, `Direction`.
- `Core/AddonComponent/AddonBits.cs` — `OnTaxi()`(10), `Mounted()`(18), `Flying()`(21), `Falling()`(23); add an on-transport bit here if §7 yields a signal.
- `Addons/DataToColor/BitCache.lua`, `Query.lua` — where movement/state bits are encoded (`UnitOnTaxi` pattern); add transport-detection query here.
- `Core/GoalsComponent/Navigation/*` — follower; the link-handler state machine sits beside the ground/spline follow.
