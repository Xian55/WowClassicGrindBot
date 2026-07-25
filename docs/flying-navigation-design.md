# Flying Navigation — Design Doc

**Status:** Draft / proposal — **implementation postponed (2026-07-25)**. Design captured for later; not scheduled.
**Scope:** Add flight-based traversal for clients where the player can fly (TBC+ Outland, WotLK Northrend). Ground pathing (DotRecast navmesh) stays the default; flight is an alternate traversal mode chosen when it is available and worthwhile.

---

## 1. Motivation

Outland (map 530) and Northrend (571) have terrain the walkable navmesh cannot cross cheaply — cliffs, waterfalls, floating islands. When the player can fly, the correct move is to go *over* the obstacle instead of routing around it on the ground.

The navmesh **cannot** represent this. Recast/DotRecast produces a **2.5-D walkable ground surface**; it has no concept of free airspace. Building a true 3-D volumetric pathfinder (voxel/octree) would be a large new subsystem, and it is unnecessary: once airborne above the tallest terrain on the route, obstacles are irrelevant and the path is essentially a straight line in X/Y. So flight is modeled as a **straight-line cruise at a safe altitude**, not as navmesh routing.

---

## 2. The hard constraint: no live altitude

**We cannot read the player's current Z (altitude) reliably at runtime.** This is the single fact that shapes the whole design.

What *is* available each tick (`PlayerReader` / `AddonBits`):

| Signal | Source | Use |
| --- | --- | --- |
| `WorldPos.X`, `WorldPos.Y` | `PlayerReader.WorldPos` | Horizontal position — **live, trusted** |
| `Direction` (facing) | `PlayerReader.Direction` | Yaw control (existing follower) |
| `Mounted()` | `AddonBits` bit 18 | Confirm mount-up |
| `Flying()` | `AddonBits` bit 21 (`IsFlying()`) | Airborne? / landed? |
| `Falling()` | `AddonBits` bit 23 | Free-fall (danger: dismounted in air) |
| terrain height at any X,Y | `NavmeshPathfinder.TryGetHeight(x,y,out z)` | Plan clearance altitude offline |

> **Note / verify first:** `PlayerReader.WorldPosZ` exists (commented *"MapZ not exists. Alias for WorldLoc.Z"*). If it turns out to update **live while airborne**, altitude control becomes **closed-loop** and most of this doc's open-loop machinery collapses into a simple "hold jump until Z ≥ cruiseZ". **Confirm this before building the open-loop path** — it is the cheapest possible win.

### Consequence: split the control axes

- **Horizontal (X/Y): closed-loop.** We can measure X,Y, so cruising to a target and correcting drift reuses the existing yaw+forward follower.
- **Vertical (Z): open-loop (dead reckoning).** We cannot measure altitude, so we ascend by **timing**: hold the ascend key for a duration computed from a *calibrated ascend rate*. Over-ascending is safe outdoors (no ceiling), so we bias the timing upward.
- **Landing: closed-loop on a boolean.** We cannot measure altitude, but `Flying()` flips to `false` the moment we touch ground. That is our "we have arrived vertically" signal.

---

## 3. Flight profile (the user's sketch, formalized)

```
            cruiseZ
          ┌───────────────────────┐   ← 3. CRUISE  (forward toward Dest X,Y,
          │                       │              closed-loop on X/Y; tap jump
   ascend │                       │ dive         to hold/gain altitude)
  (timed) │                       ▼
          │                      Dest ( -3357.24, 3682.67, z≈284 )
   Start  ┘
( -2800.95, 3883, z≈-5 )

   waterfall / cliff somewhere between  →  cleared because cruiseZ is above it
```

1. **Mount up** a flying mount.
2. **Ascend** (open-loop, timed) to `cruiseZ`.
3. **Cruise** straight to the destination X,Y (closed-loop), holding altitude.
4. **Dive / descend** over the destination X,Y until `Flying()` goes false (landed).

---

## 4. Capability detection (Phase 1)

We currently expose `Flying()` = *is currently flying*, which is not the same as *can fly here*.

**Add an addon query** (`Query.lua` / `BitCache.lua`, new bit) combining:

- `IsFlyableArea()` — WoW API, true in zones where flight is allowed; and
- player knows a flying riding skill (Expert/Artisan Riding) **or** owns a flying mount.

Encode as a new bit, read in C# as e.g. `AddonBits.CanFlyHere()`. This mirrors the existing bit/keybinding extraction pattern (see the `TOGGLERUN` walk-toggle work). Small, independently testable, no pathing changes.

Flight is only *considered* when `CanFlyHere()` is true.

---

## 5. Flight plan generation (Phase 2)

Input: start `S`, destination `D` (both world X,Y,Z from the route / mesh).

1. **Decide whether to fly.** Fly if `CanFlyHere()` **and** the trip clears a threshold (e.g. straight-line horizontal distance ≥ `MinFlyDistance`, or the ground route's vertical gain / length ratio exceeds a bound). Otherwise fall back to the ground navmesh path.
2. **Compute `cruiseZ`.** Sample terrain along the S→D line in X/Y with `TryGetHeight` at a fixed spacing (e.g. every 10 yd):
   ```
   cruiseZ = max( S.z, D.z, max_i TryGetHeight(sample_i) ) + Clearance
   ```
   `Clearance` (e.g. 25–40 yd) absorbs sampling gaps and waterfall lips.
3. **Emit a flight plan** (not a navmesh path):
   ```
   FlightPlan {
     ascendYards   = cruiseZ - S.z        // vertical distance to gain
     cruiseTarget  = (D.x, D.y)           // horizontal goal
     descendTarget = (D.x, D.y)           // dive column
   }
   ```
   Note the plan is 3 phases, not a dense polyline. There is no in-air obstacle avoidance — correctness comes from `cruiseZ` being above the tallest sampled terrain.

**Risk:** a terrain spike *between* samples, or a destination under an overhang (e.g. behind the waterfall), breaks the "straight down is clear" assumption. Mitigate with fine sampling + generous `Clearance`; flag overhung destinations as unsupported for v1 (see §8).

---

## 6. Locomotion state machine (Phase 3 — the bulk of the work)

The existing follower is **ground-only**: yaw + forward (+ jump). Flight adds a vertical axis and a mount step. Proposed a **new `FlightFollowerCore`** beside `SplineFollowerCore`, driven by the same tick loop.

```
        ┌─────────┐  CanFlyHere && plan ready
 Idle ──┤  MOUNT  │───────────────► ASCEND ───► CRUISE ───► DESCEND ───► DONE
        └─────────┘                    │           │           │
             │ mount failed            │ !Flying   │ !Flying   │ Falling (danger)
             ▼                         ▼           ▼           ▼
        (fallback: ground path)      ABORT ◄───────┴───────────┘
```

| State | Control output | Exit condition |
| --- | --- | --- |
| **MOUNT** | invoke `IMountHandler` with a **flying** mount; wait | `Mounted()` true → ASCEND; timeout → ABORT (ground fallback) |
| **ASCEND** | hold **Jump** (ascend); start a timer | elapsed ≥ `T_ascend` (see §7) **and** `Flying()` true → CRUISE |
| **CRUISE** | yaw toward `cruiseTarget` (existing heading control) + hold Forward; **tap Jump periodically** to not lose altitude (over-altitude is safe) | horizontal distance to `cruiseTarget` ≤ `ArriveXY` → DESCEND |
| **DESCEND** | keep X/Y over `descendTarget` (small yaw/forward corrections) + **hold X** (descend, §8) | `Flying()` → false (touched ground) → DONE |
| **ABORT** | release keys; re-plan as ground path | — |

**Safety monitor (all airborne states):** if `Flying()` drops to false unexpectedly mid-CRUISE, or `Falling()` becomes true (dismounted / knocked off), abort and re-plan from the new ground position. Never dismount deliberately while airborne.

**Why timed ascent works:** flying mounts ascend at a roughly constant vertical rate while Jump is held. `distance = rate × time`. We can't verify Z, but we *can* over-fly safely, so we hold Jump a bit longer than the nominal time.

---

## 7. Calibration constants

All configurable (e.g. a `FlightFollowerOptions`), tuned empirically like the ground spline:

| Constant | Meaning | Notes |
| --- | --- | --- |
| `AscendRate` (yd/s) | vertical speed while holding Jump on the flying mount | **Measure empirically.** Mount-type-dependent (60% vs 280% flight). Use the slowest expected, or per-mount table. |
| `AscendSafety` | multiplier / added yards on `T_ascend` | e.g. ×1.25 or +30 yd — bias to over-ascend |
| `Clearance` (yd) | altitude added above max terrain | 25–40 |
| `SampleSpacing` (yd) | terrain sampling interval for `cruiseZ` | ~10 |
| `MinFlyDistance` (yd) | don't fly for short hops | e.g. 60–100 |
| `ArriveXY` (yd) | horizontal tolerance to switch CRUISE→DESCEND | ~5–10 |
| `CruiseHoldTapMs` | jump-tap cadence during cruise | keep altitude ≥ cruiseZ |

`T_ascend = (ascendYards / AscendRate) × AscendSafety`.

**Calibration procedure (one-off, per mount speed):** from a known flat spot, hold Jump for a fixed T (e.g. 10 s), fly to a landmark of known height, back-solve `AscendRate = Δz / T`. Record; add margin.

---

## 8. Descent mechanism

Descent is **symmetric with ascent**: the default keybind **`X`** ("Sit / Move Down", action `SITORSTAND` / `MOVE_DOWN`) descends while flying, exactly as **Space** (Jump) ascends. So the vertical axis is simply *hold Jump = up, hold X = down* — no mouse-pitch or dive maneuver needed.

- **Ascend:** hold Jump — open-loop timed (no altitude feedback).
- **Descend:** hold X — but descent has a **closed-loop terminator**: hold X over the target X/Y until `Flying()` → false (touched ground). **No timing needed for descent** — the ground-contact bit ends it. Correct X/Y drift with small yaw/forward taps while descending.

**Extract the binding like `TOGGLERUN`.** So the bot sends the exact key the game maps to `SITORSTAND` / `MOVE_DOWN` (not a hardcoded `X`, which the user may have rebound): add the binding to the DataToColor `BindingIndex` + `KeyReader.IndexToBindingID`, read it back in C#, and press that code. Same pipeline as the walk-toggle work. Falls back to a configurable key if the binding isn't exported.

- **Deliberate dismount over target:** ❌ still rejected — you fall and take fall damage / die.

**Overhung destinations:** if `descendTarget` X/Y is under geometry (cave behind the waterfall), a straight descent lands on the roof. v1: detect (destination Z far below sampled terrain at its X/Y) and **fall back to ground path for the final approach**, or land at the nearest open X/Y and walk in.

---

## 9. Worked example (the requested route, map 530)

```
Start S = (-2800.95, 3883.00, -5)
Dest  D = (-3357.24, 3682.67, 284)     waterfall/cliff between S and D
```

Horizontal:
```
dx = D.x - S.x = -556.29
dy = D.y - S.y = -200.33
horizDist = sqrt(556.29² + 200.33²) ≈ 591.3 yd
```

Vertical & clearance (numbers **illustrative** — real `cruiseZ` comes from `TryGetHeight` sampling):
```
Suppose max sampled terrain along S→D ≈ 300  (the waterfall lip)
cruiseZ    = max(-5, 284, 300) + Clearance(30) = 330
ascendYards = cruiseZ - S.z = 330 - (-5) = 335
```

Timed ascent (with a **placeholder** `AscendRate = 8 yd/s`, `AscendSafety = ×1.25`):
```
T_ascend = (335 / 8) × 1.25 ≈ 52 s   ← hold Jump ~52 s
```

Sequence:
1. **MOUNT** flying mount → wait for `Mounted()`.
2. **ASCEND** — hold Jump ~52 s (over-fly the 335 yd rise; safe, no ceiling). `Flying()` confirms airborne.
3. **CRUISE** — face bearing to (-3357.24, 3682.67), hold Forward ~591 yd; tap Jump every `CruiseHoldTapMs` to stay ≥ cruiseZ. Stop when horizontal distance ≤ `ArriveXY`.
4. **DESCEND** — dive over D.x/D.y, correcting X/Y drift, until `Flying()` → false. Landed ≈ at D.

The waterfall/cliff is never routed around — cruiseZ is above it.

---

## 10. Failure modes & safety

| Failure | Detection | Response |
| --- | --- | --- |
| Mount-up fails / no flying mount | `Mounted()` stays false past timeout | ground fallback |
| Knocked off / dismounted mid-air | `Flying()` false + `Falling()` true unexpectedly | abort, re-plan from new ground pos |
| `AscendRate` mis-calibrated (under) | can't verify directly | `AscendSafety` over-fly bias; cruise jump-taps |
| Terrain spike between samples | — | fine `SampleSpacing` + `Clearance` |
| Destination under overhang | dest Z ≪ terrain(dest X,Y) | ground fallback for final approach (§8) |
| Combat / dazed during flight | existing combat detection | land / abort per bot policy |

---

## 11. What we explicitly do NOT do

- No 3-D volumetric navmesh / voxel pathfinder — unjustified complexity.
- No in-air obstacle avoidance beyond the single `cruiseZ` clearance.
- No flight in non-flyable areas (`CanFlyHere()` gate).
- No deliberate air-dismount.

---

## 12. Phasing

- **P1 — Capability detection.** Addon `IsFlyableArea()` + flying skill/mount → new bit → `AddonBits.CanFlyHere()`. *(small, isolated)*
- **P2 — Flight plan generation.** `cruiseZ` via `TryGetHeight` sampling; `FlightPlan` struct + "fly vs ground" decision. *(pure/offline, unit-testable)*
- **P3 — Flight locomotion.** `FlightFollowerCore` state machine + mount-up + ascend/cruise/descend/land; descent mechanism (§8); calibration + in-game tuning. *(the bulk; iterative like the ground spline)*

**First action:** verify whether `PlayerReader.WorldPosZ` updates live while airborne. If yes → altitude closed-loop, and §6/§7 simplify dramatically.

---

## Appendix — relevant existing code

- `Core/Addon/PlayerReader.cs` — `WorldPos` (X,Y live), `WorldPosZ` (verify live-while-flying), `Direction`.
- `Core/AddonComponent/AddonBits.cs` — `Mounted()` (18), `Flying()` (21), `Falling()` (23), `Grounded()`.
- `Addons/DataToColor/Query.lua`, `BitCache.lua` — where `IsFlying()` is encoded; add `IsFlyableArea()` + flying-skill here.
- `PPather/Navmesh/NavmeshPathfinder.cs` — `TryGetHeight(x,y,out z)` for terrain clearance sampling.
- `Core/Input/ConfigurableInput.cs` — `Jump` (held-state supported) = ascend; add descend = hold the `SITORSTAND`/`MOVE_DOWN` key (default `X`), extracted like `TOGGLERUN` (§8).
- `Core/GoalsComponent/Navigation/SplineFollowerCore.cs` — ground follower; `FlightFollowerCore` sits beside it.
- `Core/Goals/*MountHandler*` / `IMountHandler` — mount-up; extend to select a flying mount.
