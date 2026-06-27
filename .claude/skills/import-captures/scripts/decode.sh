#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# decode.sh -- run the pcap-inventory decoder (--json) over every capture in
# CAPS_DIR, writing one <name>.json per capture into WORK/objjson/. Each JSON
# carries the full object list (with per-object frame + create-type + position)
# and the in-stream sector-marker list, which aggregate.py uses to assign each
# object to the correct sector. Skips a capture whose JSON is already newer than
# the .pcap (so re-runs are cheap).
SKILL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"; . "$SKILL_DIR/lib.sh"

[ -n "$CAPS_DIR" ] || die "ENB_CAPS_DIR is unset -- copy .env.example to .env and set it."
[ -d "$CAPS_DIR" ] || die "capture dir not found: $CAPS_DIR"
[ -f "$DECODER" ] || die "decoder DLL not built: $DECODER (build tools/pcap-inventory)"

CAP_GLOB="$CAPS_DIR"
[ -d "$CAPS_DIR/captures" ] && CAP_GLOB="$CAPS_DIR/captures"

mkdir -p "$WORK/objjson"
shopt -s nullglob
n=0; skipped=0
for cap in "$CAP_GLOB"/*.pcap "$CAP_GLOB"/*.pcapng; do
    base="$(basename "$cap")"
    out="$WORK/objjson/${base%.*}.json"
    if [ -f "$out" ] && [ "$out" -nt "$cap" ]; then
        skipped=$((skipped+1)); continue
    fi
    echo "decode: $base"
    if ! dotnet "$DECODER" "$cap" --json "$out" >/dev/null 2>"$WORK/objjson/${base%.*}.err"; then
        echo "  WARN: decoder failed for $base (see ${base%.*}.err)" >&2
        continue
    fi
    n=$((n+1))
done
echo "decode: wrote $n, skipped $skipped (up to date) -> $WORK/objjson"
