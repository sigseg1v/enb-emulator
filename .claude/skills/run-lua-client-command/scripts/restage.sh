#!/usr/bin/env bash
# Re-stage the enbmod Lua scripts from the repo into the location the running
# client reads them from, so a `reload()` over the command channel picks up your
# edits. This is the file-copy half of the hot-reload loop; reload() (the Lua
# half) only re-reads files that are already next to the client.
#
# What it does: copies the repo's scripts/ tree (init.lua + lib/ + mods/) over
# <clientDir>/scripts/, OVERWRITING changed files but never deleting anything at
# the destination -- so the runtime-written, install-specific calib_data.lua and
# any user-only mod survive. It does NOT rebuild the DLL (Lua edits don't need a
# build); rebuild with `just build-enbmod` + relaunch only when you change C++.
#
# Usage: restage.sh            # stage every mod + lib + init
#        restage.sh freya-hud  # restrict the mods/ copy to one mod id (lib/init
#                              # still copied); handy for a tight single-mod loop
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../../../.." && pwd)"

SRC="$REPO_ROOT/freya/client-injection/enbmod/scripts"
SETTINGS="$REPO_ROOT/tools/LaunchFreya/bin/Debug/net10.0/FreyaLauncher.settings.json"

[ -d "$SRC" ] || { echo "ERROR: repo scripts dir not found: $SRC" >&2; exit 1; }
[ -f "$SETTINGS" ] || { echo "ERROR: launcher settings not found: $SETTINGS (launch once first)" >&2; exit 1; }

CLIENT_DIR="$(python3 -c "import json,os;print(os.path.dirname(json.load(open('$SETTINGS'))['ClientPath']))")"
DST="$CLIENT_DIR/scripts"
[ -d "$CLIENT_DIR" ] || { echo "ERROR: client dir does not exist: $CLIENT_DIR" >&2; exit 1; }
mkdir -p "$DST/lib" "$DST/mods"

ONLY_MOD="${1:-}"

# Top-level bootstrap files (init.lua, ...). Copy over; never wipe the dir, so a
# runtime-written calib_data.lua at the destination root is preserved.
cp -f "$SRC"/*.lua "$DST"/ 2>/dev/null || true

# Shared libraries.
cp -f "$SRC"/lib/*.lua "$DST"/lib/ 2>/dev/null || true

# Mods. Copy each mod folder (or just the one named) over the staged copy.
for moddir in "$SRC"/mods/*/; do
    [ -d "$moddir" ] || continue
    id="$(basename "$moddir")"
    if [ -n "$ONLY_MOD" ] && [ "$id" != "$ONLY_MOD" ]; then continue; fi
    mkdir -p "$DST/mods/$id"
    cp -rf "$moddir"* "$DST/mods/$id"/
done

echo "restaged scripts -> $DST${ONLY_MOD:+  (mod: $ONLY_MOD)}"
