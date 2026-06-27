#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# import.sh -- end-to-end driver: decode every capture, aggregate per sector,
# generate the per-sector SQL files (+ wrapper). By default it ONLY generates
# the committed SQL artifacts. Pass --apply to also apply the wrapper to the
# running dev DB now (otherwise the SQL lands on a fresh boot via schema-init).
#
#   import.sh            decode + aggregate + gen_sql (no DB writes)
#   import.sh --apply    ... then apply seed_captures.sql to the running net7 DB
SKILL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"; . "$SKILL_DIR/lib.sh"

APPLY=0
[ "${1:-}" = "--apply" ] && APPLY=1

echo "== decode =="
bash "$SKILL_DIR/scripts/decode.sh"
echo
echo "== aggregate =="
python3 "$SKILL_DIR/scripts/aggregate.py"
echo
echo "== generate SQL =="
python3 "$SKILL_DIR/scripts/gen_sql.py"

if [ "$APPLY" = 1 ]; then
    echo
    echo "== apply to running net7 DB =="
    shopt -s nullglob
    files=("$SQL_OUT_DIR"/*.sql)
    [ ${#files[@]} -gt 0 ] || die "no generated SQL to apply in $SQL_OUT_DIR"
    for f in "${files[@]}"; do
        echo "apply: $(basename "$f")"
        docker exec -i "$PG_CONTAINER" psql -U net7 -d net7 -v ON_ERROR_STOP=1 \
            < "$f" >/dev/null || die "apply failed on $(basename "$f")"
    done
    echo "applied ${#files[@]} sector file(s)."
fi
echo
echo "done. Generated SQL in $SQL_OUT_DIR (+ $(basename "$SQL_WRAPPER"))."
