#!/usr/bin/env bash
# Migrate a local Json data folder to the era-partitioned layout (precata/cata/...).
# Idempotent + safe to re-run. Twin of migrate-json-era.ps1 for Linux/macOS
# (e.g. the headless pathing server). Leaflet tiles were already era-partitioned.
#
#   road/<Continent>             -> road/precata/<Continent>
#   PathInfo/navmesh/<Continent> -> PathInfo/navmesh/precata/<Continent>
#   PathInfo/<Continent>         -> PathInfo/precata/<Continent>   (legacy V1 cache)
#
# Usage:  scripts/migrate-json-era.sh [JSON_ROOT] [--dry-run]
set -euo pipefail

repo_root="$(cd "$(dirname "$0")/.." && pwd)"
json_root=""
dry=0
for a in "$@"; do
  case "$a" in
    --dry-run) dry=1 ;;
    *) json_root="$a" ;;
  esac
done
[ -z "$json_root" ] && json_root="$repo_root/Json"

eras=" precata cata mop retail som tbc bcc wrath wotlk vanilla classic "

move_into_era() {   # $1=parent  $2=era  $3=extra-keep (optional)
  local parent="$1" era="${2:-precata}" keep="${3:-}"
  [ -d "$parent" ] || return 0
  echo "$parent:"
  for d in "$parent"/*/; do
    [ -d "$d" ] || continue
    local name; name="$(basename "$d")"
    case "$name" in
      "$era"|legacy_*) continue ;;
    esac
    case "$eras" in *" $name "*) continue ;; esac
    [ -n "$keep" ] && [ "$name" = "$keep" ] && continue
    local target="$parent/$era/$name"
    if [ -e "$target" ]; then echo "  skip (target exists): $name"; continue; fi
    echo "  move: $name -> $era/$name"
    if [ "$dry" -eq 0 ]; then mkdir -p "$parent/$era"; mv "$d" "$target"; fi
  done
}

echo "Json root: $json_root$([ "$dry" -eq 1 ] && echo '   (dry-run)')"
move_into_era "$json_root/road"
move_into_era "$json_root/PathInfo/navmesh"
move_into_era "$json_root/PathInfo" precata navmesh
echo "done."
