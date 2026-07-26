#!/usr/bin/env python3
"""Merge a world DB's `game_tele` table into Frontend/wwwroot/teleport_locations.txt.

`game_tele` is what backs the `.tele <name>` GM command on TrinityCore and its forks
(SkyFire included), so it is a ready-made POI list: one named, reachable spot per
landmark, already in world coordinates.

FILE FORMAT  `id x y z orientation mapId name`, space separated, one per line.
Both readers - leaflet-watch.js addPoi/addTeleportPoints and PathingAPI's
SearchParameters.razor - index fields positionally and use only x, y, z, mapId and
name. `id` and `orientation` are never read, but must stay present or the positions
shift.

MERGE RULES
  * existing lines are kept verbatim, in order - the committed file is hand-augmented
    (it has extra entries like DurotarZeppelinBellow that no dump contains), and its
    coordinates come from a WotLK DB, which is what pre-Cata users need;
  * a dump row is appended only if its name is new, so nothing pre-Cata gets silently
    replaced by post-Shattering coordinates;
  * new ids continue past the highest existing id, keeping the diff to appended lines.

USAGE
  python scripts/extract-tele-sqldump.py <dump.sql> --dry-run
  python scripts/extract-tele-sqldump.py <dump.sql>
"""
import argparse
import importlib.util
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

_spec = importlib.util.spec_from_file_location(
    "sqldump", os.path.join(ROOT, "scripts", "extract-creatures-sqldump.py"))
_sql = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_sql)
rows = _sql.rows

# Two tracked copies, kept byte-identical. Frontend's is served to the Leaflet page;
# PathingAPI reads its own from disk at wwwroot/teleport_locations.txt. Everything else
# a glob turns up is a bin/ build artefact.
DEFAULT_OUTS = [
    os.path.join(ROOT, "Frontend", "wwwroot", "teleport_locations.txt"),
    os.path.join(ROOT, "PathingAPI", "wwwroot", "teleport_locations.txt"),
]


def fmt(v):
    """Match the existing file: plain floats, no exponent, no trailing zeros."""
    return f"{v:.6g}"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("dump")
    ap.add_argument("--out", action="append", default=None,
                    help="repeatable; defaults to both tracked copies")
    ap.add_argument("--dry-run", action="store_true")
    args = ap.parse_args()

    if not os.path.isfile(args.dump):
        sys.exit(f"no such file: {args.dump}")

    outs = args.out or DEFAULT_OUTS

    # The committed files are CRLF. Reading in text mode normalises that away, so
    # rewriting with "\n" rewrites all 1002 existing lines and buries the 532 added
    # ones in a whole-file diff. Read bytes and put back whatever was there.
    raw = open(outs[0], "rb").read()
    eol = "\r\n" if raw.count(b"\r\n") > raw.count(b"\n") // 2 else "\n"
    existing = raw.decode("utf-8").replace("\r\n", "\n").split("\n")
    print(f"  line ending: {'CRLF' if eol == chr(13) + chr(10) else 'LF'}")

    seen = set()
    max_id = 0
    kept = 0
    for line in existing:
        parts = line.split()
        if len(parts) < 7:
            continue
        kept += 1
        # Normalise existing names exactly like new ones, or the committed
        # "Lakkari TarPits" would index as just "Lakkari" and let the dump's row
        # through as a near-duplicate of the "LakkariTarPits" already on the next line.
        seen.add("".join(parts[6:]).lower())
        try:
            max_id = max(max_id, int(parts[0]))
        except ValueError:
            pass

    print(f"  existing : {kept} entries, {len(seen)} names, max id {max_id}")

    tele = list(rows(args.dump, "game_tele",
                     ["id", "position_x", "position_y", "position_z",
                      "orientation", "map", "name"]))
    print(f"  game_tele: {len(tele)} rows in the dump")

    added, by_map = [], {}
    next_id = max_id
    for r in tele:
        # Both readers treat the name as "everything from field 7 on", so an embedded
        # space parses fine - but every existing entry is space-free CamelCase, and the
        # dump has exactly one that is not ("Lakkari TarPits") plus five with trailing
        # blanks. Normalise rather than special-case the parsers.
        name = "".join((r["name"] or "").split())
        if not name or name.lower() in seen:
            continue

        seen.add(name.lower())
        next_id += 1
        added.append(f"{next_id} {fmt(r['position_x'])} {fmt(r['position_y'])} "
                     f"{fmt(r['position_z'])} {fmt(r['orientation'])} {r['map']} {name}")
        by_map[r["map"]] = by_map.get(r["map"], 0) + 1

    print(f"  new      : {len(added)} entries -> ids {max_id + 1}..{next_id}")
    for m in sorted(by_map):
        print(f"      map {m:5} +{by_map[m]}")

    if args.dry_run:
        print("  (dry-run) nothing written")
        for line in added[:10]:
            print("      " + line)
        return

    if not added:
        print("  nothing to add")
        return

    # The committed file ends without a trailing newline; keep every existing line
    # untouched and append, so the diff is purely additive.
    body = "\n".join(existing).rstrip("\n")
    blob = (body + "\n" + "\n".join(added)).replace("\n", eol).encode("utf-8")

    for path in outs:
        with open(path, "wb") as fh:
            fh.write(blob)
        print(f"  wrote {path} ({kept + len(added)} entries)")


if __name__ == "__main__":
    main()
