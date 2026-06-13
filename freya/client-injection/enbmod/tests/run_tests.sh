#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# run_tests.sh -- headless test suite for the enbmod Lua mods.
#  1. Builds the vendored Lua 5.4 as a NATIVE Linux interpreter (once, cached).
#  2. Runs every spec under tests/spec/ against the mock enb host
#     (each spec in its own process so require-caches never leak).
#  3. Rasterizes the screenshot scenarios to PNG (build/tests/shots/) for
#     visual verification -- no client, no WINE, no D3D8 needed.
set -u

HERE="$(cd "$(dirname "$0")" && pwd)"
MOD="$(dirname "$HERE")"
LUA_SRC="$MOD/third_party/lua/src"
BUILD="$MOD/build/tests"
LUA="$BUILD/lua"
SHOTS="$BUILD/shots"

mkdir -p "$BUILD" "$SHOTS"

# --- 1. native Lua interpreter (rebuild only if sources are newer) -----------
if [ ! -x "$LUA" ] || [ -n "$(find "$LUA_SRC" -name '*.c' -newer "$LUA" 2>/dev/null | head -1)" ]; then
    echo ">>> building native Lua interpreter"
    # LUA_USE_POSIX adds OS glue only (io.popen, used by the mock's enb.list_dir
    # to mirror the DLL's directory listing) -- the LANGUAGE config is unchanged
    # from what the DLL ships. (Not full LUA_USE_LINUX: no dlopen/readline needed.)
    # shellcheck disable=SC2046
    cc -O2 -DLUA_USE_POSIX -o "$LUA" $(ls "$LUA_SRC"/*.c | grep -v '/luac\.c$') -lm || exit 1
fi

# --- 2. specs -----------------------------------------------------------------
# Resolve every place a require()'d module can live after the mod restructure:
# scripts/ (init), scripts/lib/ (shared libs), and each scripts/mods/<id>/ folder
# (mod entrypoints, required by short name in the per-mod specs). Globbed so new
# mod folders are picked up automatically.
LUA_PATH="$MOD/scripts/?.lua;$MOD/scripts/lib/?.lua;$HERE/?.lua"
for d in "$MOD"/scripts/mods/*/; do
    [ -d "$d" ] && LUA_PATH="$LUA_PATH;${d}?.lua"
done
export LUA_PATH="$LUA_PATH;;"
export SCRIPTS_DIR="$MOD/scripts"
export FIXTURES="$HERE/fixtures"
export SHOT_DIR="$SHOTS"

fail=0
for spec in "$HERE"/spec/*_spec.lua; do
    "$LUA" "$HERE/runner.lua" "$spec" || fail=1
done

# --- 3. rasterize the screenshot scenarios ------------------------------------
if [ "$fail" -eq 0 ]; then
    echo ">>> rendering screenshots to $SHOTS"
    for j in "$SHOTS"/*.json; do
        [ -e "$j" ] || continue
        python3 "$HERE/render_frame.py" "$j" "${j%.json}.png" || fail=1
    done
fi

if [ "$fail" -ne 0 ]; then
    echo "!!! mod test suite FAILED"
    exit 1
fi
echo ">>> mod test suite passed"
