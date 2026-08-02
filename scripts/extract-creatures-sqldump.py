#!/usr/bin/env python3
"""Build the per-client emulator-sourced data straight from a mysqldump .sql file.

Produces, depending on --what:
  creatures    Json/dbc/<client>/creatures.json          creature id -> name/level/flags
  npcspawns    Json/npcspawnlocations/<client>/<map>.json  {"<id>":[{x,y,z},...]}
  mailboxes    Json/mailboxlocations/<client>/<map>.json   [{x,y,z},...]

The existing extractors (X:\\Programming\\Trinity_Extract_npc_coords) query a live
MySQL server. That is fine when you run the emulator, but pointless friction when all
you have is a dump: this reads the dump directly, so no server, no import, no
credentials. Pure stdlib.

Schema differences are handled by reading each CREATE TABLE for its column order rather
than assuming positions - SkyFire (5.4.8) splits `faction` into `faction_A`/`faction_H`
and renames `rank` to `npc_rank`, so a query written for 3.3.5 TrinityCore silently
selects the wrong things.

Reproduces the filtering of extract_trinity_creatures.ps1:
  * drop names containing [DND] (developer/debug NPCs)
  * drop entries whose every spawn is bound to a game event (seasonal, not normally up)
  * drop vendors selling mounts (item class 15, subclass 5)

USAGE
  python scripts/extract-creatures-sqldump.py <dump.sql> --client legacy_mop
  python scripts/extract-creatures-sqldump.py <dump.sql> --client legacy_mop --what all
  python scripts/extract-creatures-sqldump.py <dump.sql> --client legacy_mop --dry-run
"""
import argparse
import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# creature_template column -> output JSON key. Values are the names this dump uses;
# alternatives are tried in order so one script copes with several schema generations.
FIELDS = {
    "Entry": ["entry"],
    "Name": ["name"],
    "SubName": ["subname"],
    "Faction": ["faction_A", "faction", "faction_a"],
    "MinLevel": ["minlevel"],
    "MaxLevel": ["maxlevel"],
    "Rank": ["npc_rank", "rank"],
    "NpcFlag": ["npcflag"],
    "SkinLoot": ["skinloot"],
    "Family": ["family"],
    "Type": ["type"],
}

MOUNT_ITEM_CLASS = 15
MOUNT_ITEM_SUBCLASS = 5

# gameobject_template.type 19 = mailbox.
GO_TYPE_MAILBOX = 19

# World continents the bot paths over. Instances and scenarios are excluded on purpose -
# they are not navigable and would bloat the output. HawaiiMainLand (870, Pandaria)
# only exists on a MoP-era dump; maps absent from the dump are simply skipped.
WORLD_MAPS = [
    0,    # Azeroth
    1,    # Kalimdor
    530,  # Expansion01 / Outland
    571,  # Northrend
    646,  # Deephome / Deepholm
    648,  # LostIsles / Lost Isles + Kezan
    654,  # Gilneas2 / Gilneas
    730,  # MaelstromZone / The Maelstrom
    860,  # NewRaceStartZone / The Wandering Isle
    870,  # HawaiiMainLand / Pandaria
]


def columns_of(path, table):
    """Column names, in declaration order, from the table's CREATE TABLE block."""
    want = f"CREATE TABLE `{table}`"
    cols = []
    inside = False

    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        for line in fh:
            if not inside:
                if line.startswith(want):
                    inside = True
                continue
            if line.startswith(")"):
                break
            m = re.match(r"\s+`([^`]+)`\s", line)
            if m:
                cols.append(m.group(1))

    if not cols:
        sys.exit(f"table `{table}` not found in {path}")
    return cols


def split_tuples(payload):
    """Yield one list of raw values per (...) group.

    Hand-rolled because the values contain commas and parentheses inside quoted
    strings; a regex or naive split corrupts every NPC whose name has a comma.
    """
    val, row, in_str, esc = [], [], False, False
    depth = 0

    for ch in payload:
        if in_str:
            if esc:
                val.append(ch)
                esc = False
            elif ch == "\\":
                val.append(ch)
                esc = True
            elif ch == "'":
                in_str = False
                val.append(ch)
            else:
                val.append(ch)
            continue

        if ch == "'":
            in_str = True
            val.append(ch)
        elif ch == "(":
            depth += 1
            if depth == 1:
                row, val = [], []
            else:
                val.append(ch)
        elif ch == ")":
            depth -= 1
            if depth == 0:
                row.append("".join(val).strip())
                yield row
                row, val = [], []
            else:
                val.append(ch)
        elif ch == "," and depth == 1:
            row.append("".join(val).strip())
            val = []
        elif depth >= 1:
            val.append(ch)

    return


def decode(raw):
    """MySQL literal -> Python value."""
    if raw == "NULL":
        return None
    if raw.startswith("'") and raw.endswith("'"):
        s = raw[1:-1]
        return (s.replace("\\'", "'").replace('\\"', '"')
                 .replace("\\n", "\n").replace("\\r", "\r").replace("\\\\", "\\"))
    try:
        return int(raw)
    except ValueError:
        try:
            return float(raw)
        except ValueError:
            return raw


def rows(path, table, wanted):
    """Yield dicts of the wanted columns for every INSERTed row of a table.

    Column names are matched case-insensitively: cores disagree on the casing of
    the same column, and only on the casing. TrinityCore 4.3.4 writes
    gameobject_template.Data0 where SkyFire 5.4.8 writes data0, which aborted the
    area extractor on a dump that had the column all along.
    """
    cols = columns_of(path, table)
    idx = {c: i for i, c in enumerate(cols)}
    fold = {c.lower(): i for i, c in enumerate(cols)}

    # Exact match wins, so a table carrying both spellings is unambiguous.
    picked = {c: idx.get(c, fold.get(c.lower())) for c in wanted}

    missing = [c for c, i in picked.items() if i is None]
    if missing:
        sys.exit(f"`{table}` has no column(s) {missing}; available: {', '.join(cols)}")

    prefix = f"INSERT INTO `{table}` VALUES "
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        for line in fh:
            if not line.startswith(prefix):
                continue
            for values in split_tuples(line[len(prefix):]):
                if len(values) != len(cols):
                    continue
                yield {c: decode(values[picked[c]]) for c in wanted}


def resolve(cols, candidates, table):
    for c in candidates:
        if c in cols:
            return c
    sys.exit(f"`{table}` has none of {candidates}")


def write_per_map(kind, client, by_map, dry_run):
    """One <map>.json per world map under Json/<kind>/<client>/."""
    dest_dir = os.path.join(ROOT, "Json", kind, client)

    for map_id in sorted(by_map):
        payload = by_map[map_id]
        count = len(payload)
        path = os.path.join(dest_dir, f"{map_id}.json")

        if dry_run:
            print(f"  (dry-run) map {map_id}: {count} -> {path}")
            continue

        os.makedirs(dest_dir, exist_ok=True)
        with open(path, "w", encoding="utf-8") as fh:
            json.dump(payload, fh, separators=(",", ":"))
        print(f"  map {map_id}: {count} -> {os.path.basename(path)}")

    if not by_map:
        print("  nothing found for the world maps")


def extract_npcspawns(dump, client, dry_run):
    """creature -> {"<creature id>": [{x,y,z}, ...]} per map."""
    print("reading creature (spawns) ...")
    wanted = set(WORLD_MAPS)
    by_map = {}

    for r in rows(dump, "creature", ["id", "map", "position_x", "position_y", "position_z"]):
        if r["map"] not in wanted:
            continue
        # Keyed by string: the consumer reads a JSON object, whose keys are strings.
        by_map.setdefault(r["map"], {}).setdefault(str(r["id"]), []).append(
            {"x": r["position_x"], "y": r["position_y"], "z": r["position_z"]})

    # Stable order, matching the SQL's ORDER BY position_x, position_y, position_z.
    for spawns in by_map.values():
        for points in spawns.values():
            points.sort(key=lambda p: (p["x"], p["y"], p["z"]))

    print(f"npc spawn locations across {len(by_map)} map(s)")
    write_per_map("npcspawnlocations", client, by_map, dry_run)


def extract_mailboxes(dump, client, dry_run):
    """gameobject joined to gameobject_template type 19 -> [{x,y,z}, ...] per map."""
    print("reading gameobject_template (mailboxes) ...")
    mailbox_ids = {r["entry"] for r in rows(dump, "gameobject_template", ["entry", "type"])
                   if r["type"] == GO_TYPE_MAILBOX}
    print(f"  {len(mailbox_ids)} mailbox gameobject template(s)")

    print("reading gameobject (spawns) ...")
    wanted = set(WORLD_MAPS)
    by_map = {}

    for r in rows(dump, "gameobject", ["id", "map", "position_x", "position_y", "position_z"]):
        if r["map"] not in wanted or r["id"] not in mailbox_ids:
            continue
        by_map.setdefault(r["map"], []).append({
            "x": round(r["position_x"], 2),
            "y": round(r["position_y"], 2),
            "z": round(r["position_z"], 2),
        })

    for coords in by_map.values():
        coords.sort(key=lambda p: (p["x"], p["y"]))

    print(f"mailboxes across {len(by_map)} map(s)")
    write_per_map("mailboxlocations", client, by_map, dry_run)


def extract_creatures(dump, client, dry_run):
    ct_cols = columns_of(dump, "creature_template")
    picked = {key: resolve(ct_cols, names, "creature_template")
              for key, names in FIELDS.items()}
    print("column mapping:")
    for key, col in picked.items():
        note = "  <- differs from the 3.3.5 name" if col not in (key.lower(),) and col in ("faction_A", "npc_rank") else ""
        print(f"  {key:<9} <- {col}{note}")

    # Spawns bound to a game event: excluded only when an entry has no ordinary spawn,
    # which is what the LEFT JOIN + "gec.guid IS NULL" + DISTINCT in the SQL amounts to.
    print("\nreading game_event_creature ...")
    event_guids = {r["guid"] for r in rows(dump, "game_event_creature", ["guid"])}

    print("reading creature (spawns) ...")
    spawned, event_only = set(), {}
    for r in rows(dump, "creature", ["guid", "id"]):
        cid = r["id"]
        spawned.add(cid)
        if r["guid"] in event_guids:
            event_only[cid] = event_only.get(cid, True)
        else:
            event_only[cid] = False

    print("reading item_template + npc_vendor (mount vendors) ...")
    mount_items = {r["entry"] for r in rows(dump, "item_template", ["entry", "class", "subclass"])
                   if r["class"] == MOUNT_ITEM_CLASS and r["subclass"] == MOUNT_ITEM_SUBCLASS}
    mount_vendors = {r["entry"] for r in rows(dump, "npc_vendor", ["entry", "item"])
                     if r["item"] in mount_items}

    print("reading creature_template ...")
    out, dropped = [], {"dnd": 0, "event": 0, "mount": 0}
    for r in rows(dump, "creature_template", list(picked.values())):
        name = r[picked["Name"]] or ""
        entry = r[picked["Entry"]]

        if "[DND]" in name:
            dropped["dnd"] += 1
            continue
        if entry in spawned and event_only.get(entry):
            dropped["event"] += 1
            continue
        if entry in mount_vendors:
            dropped["mount"] += 1
            continue

        out.append({
            "Entry": entry,
            "Name": name,
            "SubName": r[picked["SubName"]] or "",
            "Faction": r[picked["Faction"]] or 0,
            "MinLevel": r[picked["MinLevel"]] or 0,
            "MaxLevel": r[picked["MaxLevel"]] or 0,
            "Rank": r[picked["Rank"]] or 0,
            "NpcFlag": r[picked["NpcFlag"]] or 0,
            "SkinLoot": r[picked["SkinLoot"]] or 0,
            "Family": r[picked["Family"]] or 0,
            "Type": r[picked["Type"]] or 0,
        })

    out.sort(key=lambda c: c["Entry"])
    print(f"\ncreatures: {len(out)}  "
          f"(dropped {dropped['dnd']} [DND], {dropped['event']} event-only, "
          f"{dropped['mount']} mount vendors)")
    if out:
        print(f"  id range {out[0]['Entry']}..{out[-1]['Entry']}")

    dest = os.path.join(ROOT, "Json", "dbc", client, "creatures.json")
    if dry_run:
        print(f"(dry-run) would write {dest}")
        return

    os.makedirs(os.path.dirname(dest), exist_ok=True)
    with open(dest, "w", encoding="utf-8") as fh:
        json.dump(out, fh, indent=2)
    print(f"wrote {dest} ({os.path.getsize(dest) / 1048576:.1f} MB)")


EXTRACTORS = {
    "creatures": extract_creatures,
    "npcspawns": extract_npcspawns,
    "mailboxes": extract_mailboxes,
}


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("dump", help="path to the mysqldump .sql")
    ap.add_argument("--client", required=True,
                    help="target client folder, e.g. legacy_mop (these outputs are "
                         "per client, not per era - ids differ between clients)")
    ap.add_argument("--what", default="creatures",
                    choices=[*EXTRACTORS, "all"],
                    help="which dataset to build (default: creatures)")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    if not os.path.isfile(args.dump):
        sys.exit(f"no such file: {args.dump}")

    chosen = list(EXTRACTORS) if args.what == "all" else [args.what]

    for i, name in enumerate(chosen):
        if i:
            print()
        print(f"=== {name} -> {args.client} ===")
        EXTRACTORS[name](args.dump, args.client, args.dry_run)


if __name__ == "__main__":
    main()
