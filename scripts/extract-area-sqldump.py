#!/usr/bin/env python3
"""Build Json/area/<client>/<areaId>.json from a mysqldump .sql - no Wowhead scraping.

WHY NOT WOWHEAD: its node coordinates are percentages of whichever map its zone page
happens to use, and that map is not always the zone whose id names the file. Cataclysm
split Stranglethorn Vale (5339) into Northern Stranglethorn (33) and The Cape of
Stranglethorn (5287); wowhead's zone=5339 data plots against the Cape, but the frontend
scales it with 5339's bounds - which still span both halves - so every node lands ~1000
yards out to sea. Measured: node [26.2, 52.5] resolves to (-13206, 37) via 5287 and
(-12109, -463) via 5339.

An emulator dump has real world coordinates. This script assigns each spawn to a zone
with the baked area grid, then converts to map coordinates using *that same zone's*
bounds, so the value written and the value the frontend reads back are the same
rectangle by construction.

COORDINATE CONVENTION (leaflet-watch.js addNodeTypeSpawns / npcLoc):
    coords = [[a, b], ...]   ->   worldX = toWorldX(b),  worldY = toWorldY(a)
so this writes a = toMapY(worldY), b = toMapX(worldX).

REQUIRES  Json/area_grid/<era>/*.grid  (BakeTool --area-grid)
          Json/dbc/<client>/WorldMapArea.json

USAGE
  python scripts/extract-area-sqldump.py <dump.sql> --client legacy_mop
  python scripts/extract-area-sqldump.py <dump.sql> --client cata --dry-run
"""
import argparse
import importlib.util
import json
import os
import struct
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# Reuse the dump reader rather than duplicating the tokenizer.
_spec = importlib.util.spec_from_file_location(
    "sqldump", os.path.join(ROOT, "scripts", "extract-creatures-sqldump.py"))
_sql = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_sql)
rows = _sql.rows

# ChunkReader.TILESIZE / ZEROPOINT / CHUNKSIZE
TILESIZE = 533.33333
ZEROPOINT = 32.0 * TILESIZE
CHUNKSIZE = TILESIZE / 16.0

GO_TYPE_CHEST = 3          # herbs and mining nodes are both chests
AGRD_MAGIC = 0x44524741

# NpcFlags bits (SharedLib/Data/NpcFlags.cs)
NPC_VENDOR = (1 << 7) | (1 << 8) | (1 << 9) | (1 << 10) | (1 << 11)
NPC_TRAINER = (1 << 4) | (1 << 5) | (1 << 6)
NPC_REPAIR = 1 << 12
NPC_FLIGHTMASTER = 1 << 13
NPC_INNKEEPER = 1 << 16

# Seeds for classifying a lock id as herbalism or mining. Every type-3 gameobject
# carries a lock in data0; rather than hard-code the full lock table (or fetch Lock.db2)
# the lock ids are learned from objects whose purpose is unambiguous, then applied to
# everything sharing those locks - so new nodes are picked up without a name list.
HERB_SEEDS = {"Peacebloom", "Silverleaf", "Earthroot", "Mageroyal", "Briarthorn",
              "Bruiseweed", "Wild Steelbloom", "Kingsblood", "Liferoot", "Fadeleaf",
              "Goldthorn", "Khadgar's Whisker", "Dreamfoil", "Golden Sansam",
              "Mountain Silversage", "Cinderbloom", "Stormvine", "Azshara's Veil",
              "Fool's Cap", "Green Tea Leaf", "Silkweed", "Rain Poppy", "Snow Lily"}

MINE_SEEDS = {"Copper Vein", "Tin Vein", "Silver Vein", "Iron Deposit", "Gold Vein",
              "Mithril Deposit", "Truesilver Deposit", "Thorium Vein", "Fel Iron Deposit",
              "Adamantite Deposit", "Cobalt Deposit", "Saronite Deposit",
              "Obsidium Deposit", "Elementium Vein", "Pyrite Deposit",
              "Ghost Iron Deposit", "Kyparite Deposit", "Trillium Vein"}


def load_area_grid(path):
    """-> (minCellX, minCellY, width, height, cells) from a baked .grid."""
    with open(path, "rb") as fh:
        blob = fh.read()

    magic, version, min_x, min_y, w, h = struct.unpack_from("<Iiiiii", blob, 0)
    if magic != AGRD_MAGIC:
        sys.exit(f"{path}: not an AGRD file")

    cells = struct.unpack_from(f"<{w * h}H", blob, 24)
    return min_x, min_y, w, h, cells


def area_id_at(grid, world_x, world_y):
    """Mirrors AreaGrid.WorldToCell + GetAreaId. 0 = outside the baked bounds."""
    min_x, min_y, w, h, cells = grid

    cell_x = int((ZEROPOINT - world_y) // CHUNKSIZE)
    cell_y = int((ZEROPOINT - world_x) // CHUNKSIZE)

    lx = cell_x - min_x
    ly = cell_y - min_y
    if lx < 0 or ly < 0 or lx >= w or ly >= h:
        return 0

    return cells[(ly * w) + lx]


def to_map(zone, world_x, world_y):
    """World -> the [a, b] pair the frontend expects, using this zone's bounds."""
    top, bottom = zone["LocTop"], zone["LocBottom"]
    left, right = zone["LocLeft"], zone["LocRight"]

    if bottom == top or right == left:
        return None

    b = 100.0 * (world_x - top) / (bottom - top)      # toMapX
    a = 100.0 * (world_y - left) / (right - left)     # toMapY
    return [round(a, 2), round(b, 2)]


def classify_locks(dump):
    """-> (herb lock ids, mining lock ids, entry -> (name, lock)) for type-3 objects."""
    chests, herb_locks, mine_locks = {}, set(), set()

    for r in rows(dump, "gameobject_template", ["entry", "type", "name", "data0"]):
        if r["type"] != GO_TYPE_CHEST:
            continue

        name = (r["name"] or "").strip()
        lock = r["data0"] or 0
        chests[r["entry"]] = (name, lock)

        if not lock:
            continue
        if name in HERB_SEEDS:
            herb_locks.add(lock)
        elif name in MINE_SEEDS:
            mine_locks.add(lock)

    # A lock claimed by both seeds is ambiguous; drop it rather than guess.
    both = herb_locks & mine_locks
    if both:
        print(f"  note: {len(both)} lock id(s) matched both seed sets, ignoring them")
        herb_locks -= both
        mine_locks -= both

    return herb_locks, mine_locks, chests


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("dump")
    ap.add_argument("--client", required=True, help="e.g. legacy_mop, cata")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    if not os.path.isfile(args.dump):
        sys.exit(f"no such file: {args.dump}")

    # Era for the grids, client for everything else (ids differ per client).
    era = {"cata": "cata", "legacy_cata": "cata",
           "mop": "mop", "legacy_mop": "mop"}.get(args.client, args.client)

    wma_path = os.path.join(ROOT, "Json", "dbc", args.client, "WorldMapArea.json")
    if not os.path.isfile(wma_path):
        sys.exit(f"missing {wma_path} - run ReadDBC_CSV -v {args.client} worldmap")

    wma = json.load(open(wma_path, encoding="utf-8-sig"))

    # Every AreaID, so the parent chain can be walked; and the subset that owns a map.
    # The area grid stores the *innermost* area - "Cleft of Shadow", not Orgrimmar - but
    # AreaDB only ever reads area/<id>.json for the id the addon reports, and the
    # existing files are all top-level zones (82 of cata's 88). A subzone's WorldMapArea
    # entry carries its own tiny bounds, so writing zone-wide content against one would
    # push most of it outside 0..100. Roll spawns up to the root zone instead.
    all_areas = {}
    for r in wma:
        all_areas.setdefault(r["AreaID"], r)
    zones = {aid: r for aid, r in all_areas.items()
             if not r.get("ParentAreaId") and r.get("UIMapId")}

    # Bounds always come from this client's own WorldMapArea, because that is what
    # AreaDB and the Leaflet page convert with at runtime.
    parents = {aid: r.get("ParentAreaId") or 0 for aid, r in all_areas.items()}

    # The child->parent links are a different matter. legacy_cata's WorldMapArea is
    # built from 8.1.0.27826 and carries 327 areas with 43 parents - the top-level zones
    # only, none of the subzones the area grid actually stores - so resolving a spawn's
    # zone from it alone dropped 19732 of ~20000 spawns. Its era sibling covers the same
    # world with the same area ids and is subzone-extended, so borrow the chain from
    # there. Sibling entries only ever fill gaps; they never override this client's own.
    sibling = {"cata": "legacy_cata", "legacy_cata": "cata",
               "mop": "legacy_mop", "legacy_mop": "mop"}.get(args.client)

    sib_path = os.path.join(ROOT, "Json", "dbc", sibling or "", "WorldMapArea.json")
    if sibling and os.path.isfile(sib_path):
        added = 0
        for r in json.load(open(sib_path, encoding="utf-8-sig")):
            if r["AreaID"] not in parents:
                parents[r["AreaID"]] = r.get("ParentAreaId") or 0
                added += 1
        if added:
            print(f"  parent chain: +{added} areas from {sibling}/WorldMapArea.json")

    print(f"  areas {len(parents)}, top-level zones with a map {len(zones)}")

    def root_of(area_id):
        """Innermost area -> the zone that owns the map it is drawn on."""
        seen_ids = set()
        while area_id and area_id not in seen_ids:
            if area_id in zones:
                return area_id
            seen_ids.add(area_id)
            area_id = parents.get(area_id, 0)
        return 0

    resolved = {aid: root_of(aid) for aid in parents}

    grid_dir = os.path.join(ROOT, "Json", "area_grid", era)
    grids = {}
    for r in wma:
        c = r.get("Continent")
        if not c or c in grids:
            continue
        p = os.path.join(grid_dir, f"{c}.grid")
        if os.path.isfile(p):
            grids[c] = (r["MapID"], load_area_grid(p))
    by_map = {mid: g for mid, g in grids.values()}
    print(f"  area grids loaded : {len(by_map)} map(s) from {grid_dir}")

    if not by_map:
        sys.exit(f"no .grid files in {grid_dir} - run BakeTool --exp {args.client} --area-grid")

    herb_locks, mine_locks, chests = classify_locks(args.dump)
    print(f"  herb locks {len(herb_locks)}, mining locks {len(mine_locks)}, "
          f"{len(chests)} chest templates")

    # areaId -> Area shape
    areas = {}

    def area_of(area_id):
        return areas.setdefault(area_id, {
            "flightmaster": [], "innkeeper": [], "repair": [], "vendor": [], "trainer": [],
            "herb": {}, "vein": {}, "skinnable": [], "gatherable": [], "minable": [],
            "salvegable": [],
        })

    # Zone rectangles per map, smallest first. The grid alone is not enough:
    #   - it is a top-down 2D projection of outdoor geometry, so a city interior has no
    #     cells of its own and reports the zone above it (93 of Ironforge's 94 vendors
    #     came back as "Dun Morogh"),
    #   - and where the bake left a hole it reports nothing at all (Mount Hyjal).
    # A zone's own WorldMapArea rect resolves both, and picking the smallest rect that
    # contains the point keeps a city from being swallowed by the zone around it.
    rects = {}
    for aid, r in zones.items():
        x0, x1 = sorted((r["LocTop"], r["LocBottom"]))
        y0, y1 = sorted((r["LocLeft"], r["LocRight"]))
        if x1 <= x0 or y1 <= y0:
            continue
        rects.setdefault(r["MapID"], []).append((
            (x1 - x0) * (y1 - y0), aid, x0, x1, y0, y1))
    for v in rects.values():
        v.sort()

    drops = {"no grid": 0, "no zone": 0}
    stats = {"grid": 0, "rect only": 0}

    def smallest_rect(map_id, wx, wy):
        for _, aid, x0, x1, y0, y1 in rects.get(map_id, ()):
            if x0 <= wx <= x1 and y0 <= wy <= y1:
                return aid
        return 0

    def place(map_id, wx, wy):
        """-> (zone id, zone) for a world position, or (0, None)."""
        g = by_map.get(map_id)
        if g is None:
            drops["no grid"] += 1
            return 0, None

        aid = area_id_at(g, wx, wy)
        root = resolved.get(aid, 0) if aid else 0

        if root:
            # The grid wins wherever it has an answer: it follows true zone borders and
            # a rectangle cannot. Northern Stranglethorn's rect and the Cape's overlap
            # heavily, so preferring the smaller containing rect would recreate exactly
            # the cross-zone misplacement this script exists to fix.
            stats["grid"] += 1
        else:
            root = smallest_rect(map_id, wx, wy)
            if root:
                stats["rect only"] += 1

        if not root:
            drops["no zone"] += 1
            return 0, None

        return root, zones[root]

    print("\n  reading gameobject (nodes) ...")
    node_hits = {"herb": 0, "vein": 0}
    for r in rows(args.dump, "gameobject", ["id", "map", "position_x", "position_y"]):
        info = chests.get(r["id"])
        if info is None:
            continue

        name, lock = info
        kind = "herb" if lock in herb_locks else ("vein" if lock in mine_locks else None)
        if kind is None:
            continue

        aid, zone = place(r["map"], r["position_x"], r["position_y"])
        if not aid or zone is None:
            continue

        mp = to_map(zone, r["position_x"], r["position_y"])
        if mp is None:
            continue

        bucket = area_of(aid)[kind].setdefault(name, [{"coords": []}])
        bucket[0]["coords"].append(mp)
        node_hits[kind] += 1

    print(f"    herb {node_hits['herb']}, vein {node_hits['vein']} placed")

    print("  reading creature_template (flags) ...")
    npc_meta = {}
    for r in rows(args.dump, "creature_template",
                  ["entry", "name", "subname", "npcflag", "minlevel", "maxlevel", "skinloot"]):
        npc_meta[r["entry"]] = r

    print("  reading creature (spawns) ...")
    seen = set()
    skinnable = {}
    for r in rows(args.dump, "creature", ["id", "map", "position_x", "position_y"]):
        m = npc_meta.get(r["id"])
        if m is None:
            continue

        flags = m["npcflag"] or 0
        useful = (flags & (NPC_VENDOR | NPC_TRAINER | NPC_REPAIR |
                           NPC_FLIGHTMASTER | NPC_INNKEEPER)) != 0
        if not useful and not m["skinloot"]:
            continue

        aid, zone = place(r["map"], r["position_x"], r["position_y"])
        if not aid or zone is None:
            continue

        if m["skinloot"]:
            skinnable.setdefault(aid, set()).add(r["id"])

        if not useful or (aid, r["id"]) in seen:
            continue
        seen.add((aid, r["id"]))

        mp = to_map(zone, r["position_x"], r["position_y"])
        if mp is None:
            continue

        npc = {
            "coords": [mp],
            "level": m["maxlevel"] or 0,
            "name": m["name"] or "",
            "type": 0,
            "id": r["id"],
            "reacthorde": 0,
            "reactalliance": 0,
            "description": m["subname"] or "",
        }

        a = area_of(aid)
        if flags & NPC_FLIGHTMASTER:
            a["flightmaster"].append(npc)
        if flags & NPC_INNKEEPER:
            a["innkeeper"].append(npc)
        if flags & NPC_REPAIR:
            a["repair"].append(npc)
        if flags & NPC_VENDOR:
            a["vendor"].append(npc)
        if flags & NPC_TRAINER:
            a["trainer"].append(npc)

    for aid, ids in skinnable.items():
        area_of(aid)["skinnable"] = sorted(ids)

    dest = os.path.join(ROOT, "Json", "area", args.client)
    print(f"\n  zones with data: {len(areas)} -> {dest}")

    tally = {k: 0 for k in ("flightmaster", "innkeeper", "repair", "vendor", "trainer",
                            "skinnable")}
    for a in areas.values():
        for k in tally:
            tally[k] += len(a[k])
    print("  " + "  ".join(f"{k}={v}" for k, v in tally.items()))
    print("  dropped: " + "  ".join(f"{k}={v}" for k, v in drops.items()))

    if args.dry_run:
        print("  (dry-run) nothing written")
        return

    os.makedirs(dest, exist_ok=True)
    for aid, a in sorted(areas.items()):
        with open(os.path.join(dest, f"{aid}.json"), "w", encoding="utf-8") as fh:
            json.dump(a, fh, separators=(",", ":"))
    print(f"  wrote {len(areas)} files")


if __name__ == "__main__":
    main()
