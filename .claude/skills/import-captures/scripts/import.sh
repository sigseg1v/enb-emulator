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
echo
echo "== generate baseline turrets =="
# Reads gate/starbase positions from the running net7 DB and writes the
# committed db/postgres/seed_turrets.sql (independent of the capture import).
python3 "$SKILL_DIR/scripts/gen_turrets.py"
echo
echo "== generate capture-mob loot =="
# Reads the synth mob templates (mob_id >= 900000) + base loot + item_base from
# the running net7 DB and writes committed db/postgres/seed_capture_loot.sql:
# loot for the synthesized capture mobs (reconstruct-by-name, else same-model
# sibling loot). Set ENB_RECONSTRUCT_DIR (in .env) to enable the by-name tier;
# without it every synth mob falls back to sibling loot. The synth templates
# must be present in the DB -- run against the running dev stack (captures
# applied), or after `import.sh --apply` has applied the templates.
python3 "$SKILL_DIR/scripts/gen_loot.py"

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
    # 3) baseline guardian turrets LAST -- _purge.sql above deletes the whole synth
    #    range (sector_object_id >= 1000000), which includes the turret range
    #    (>= 2000000), so the turrets must be re-applied after the capture seed to
    #    mirror schema-init's boot order (base -> seed_captures -> seed_turrets).
    #    Omitting this leaves the dev DB turret-less, a state a real boot never has.
    TURRETS="$REPO_ROOT/db/postgres/seed_turrets.sql"
    [ -f "$TURRETS" ] && { apply_one "$TURRETS"; n=$((n+1)); }
    # 4) loot for the synth capture-mob templates LAST -- it references the
    #    mob_base clones applied in step 2, mirroring schema-init's boot order
    #    (seed_captures -> seed_capture_loot). Self-purges its own synth-range
    #    mob_items (mob_id >= 900000), so a re-apply is a clean replace.
    LOOT="$REPO_ROOT/db/postgres/seed_capture_loot.sql"
    [ -f "$LOOT" ] && { apply_one "$LOOT"; n=$((n+1)); }
    [ "$n" -gt 0 ] || die "no generated SQL to apply in $SQL_OUT_DIR"
    echo "applied $n file(s)."
fi
echo
echo "done. Generated SQL in $SQL_OUT_DIR (+ $(basename "$SQL_WRAPPER"))."
