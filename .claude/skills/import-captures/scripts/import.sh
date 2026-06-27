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
    apply_one() {
        echo "apply: $(basename "$1")"
        docker exec -i "$PG_CONTAINER" psql -U net7 -d net7 -v ON_ERROR_STOP=1 \
            < "$1" >/dev/null || die "apply failed on $(basename "$1")"
    }
    # 1) global purge of any prior synthetic rows FIRST -- a full re-apply over a
    #    different prior id mapping is only a clean replace if the whole synth range
    #    is cleared up front (per-sector self-deletes are sector-scoped; see _purge.sql).
    # 2) then mob_base clone templates, which the per-sector spawns reference.
    n=0
    [ -f "$SQL_OUT_DIR/_purge.sql" ] && { apply_one "$SQL_OUT_DIR/_purge.sql"; n=$((n+1)); }
    [ -f "$SQL_OUT_DIR/_mob_templates.sql" ] && { apply_one "$SQL_OUT_DIR/_mob_templates.sql"; n=$((n+1)); }
    for f in "$SQL_OUT_DIR"/[0-9]*.sql; do
        apply_one "$f"; n=$((n+1))
    done
    [ "$n" -gt 0 ] || die "no generated SQL to apply in $SQL_OUT_DIR"
    echo "applied $n file(s)."
fi
echo
echo "done. Generated SQL in $SQL_OUT_DIR (+ $(basename "$SQL_WRAPPER"))."
