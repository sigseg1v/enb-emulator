#!/usr/bin/env bash
# SPDX-License-Identifier: CC-BY-NC-SA-3.0
# Part of the Earth & Beyond emulator preservation project.
# License: LICENSES/enb-emulator
#
# Seed a developer account with a full roster of five ready-to-fly characters,
# one per archetype, each at total level 75 (combat/explore/trade = 25 each),
# with every class-appropriate skill at max rank, 10,000,000 credits, an
# upgraded hull (hull-upgrade level 4), and a level-appropriate gear loadout
# in the four core slots -- shield, reactor, engine, weapon -- matched to the
# maxed skills (see gen_equip_sql.py).
#
# Characters are named <username><suffix>, e.g. for username "Griever":
#   Grieverte  Terran  Enforcer (TE)
#   Grieverje  Jenquai Explorer (JE)
#   Grieverjd  Jenquai Defender (JD)
#   Grievertt  Terran  Trader   (TT)
#   Grieverts  Terran  Scout    (TS)
#
# Valid class codes (race x profession): TE TT TS / JD JS JE / PW PP PS.
#
# Why this is not a pure-SQL seed: a character is only flyable once the SERVER
# has run ReInitializeSavedData() -- that is what lays down the starting gear
# (shield/reactor/engine/weapon), the home-station spawn, faction state and the
# avatar_level_info row. Inventing those in SQL would be garbage data. So this
# script drives the CLI to `create` + `enter` each character (letting the server
# initialise it correctly), then runs gen_bump_sql.py to bump the plain stored
# values the server trusts verbatim on a normal load: levels, credits, ship
# slots/hull (from StaticData.h), and skills (from Skills.xml). A final step
# (gen_equip_sql.py) swaps the tier-1 starting shield/reactor/engine/weapon
# for the best items each maxed class can actually equip.
#
# Usage:  tools/seed-dev-account/seed-dev-account.sh <username> <password>
# Env:    ENB_NOREBUILD=1  reuse existing cli/proxy images without building.
set -euo pipefail

USER="${1:-}"
PASS="${2:-}"
if [ -z "$USER" ] || [ -z "$PASS" ]; then
    echo "usage: seed-dev-account <username> <password>" >&2
    exit 2
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
cd "$REPO_ROOT"

# Lower-cased, compose-safe unit name for this seed's dedicated CLI proxy.
UNIT="seed-$(printf '%s' "$USER" | tr '[:upper:]' '[:lower:]' | tr -cd 'a-z0-9_-')"
export STACK_NETWORK="${COMPOSE_PROJECT_NAME:-enb-emulator}_default"

# suffix:CLASS_CODE  -- create order == character slot order.
# Codes must be valid (TE TT TS / JD JS JE / PW PP PS); the suffix mirrors the
# code so the character name and the class sent to the server agree.
ROSTER=( "te:TE" "je:JE" "jd:JD" "tt:TT" "ts:TS" )

PSQL_USER=(docker compose exec -T -e PGPASSWORD=net7 postgres psql -U net7 -d net7_user -v ON_ERROR_STOP=1)

cleanup_proxy() {
    docker compose -f docker-compose.cli.yml -p "$UNIT" down >/dev/null 2>&1 || true
}
trap cleanup_proxy EXIT

echo ">>> [1/7] ensuring shared stack is up (no recreate)"
docker compose up -d --no-recreate postgres server net7go proxy >/dev/null

echo ">>> [2/7] generating Argon2id PHC + (re)creating account '$USER'"
PHC=$(printf '%s' "$PASS" | python3 -c 'import nacl.pwhash,sys; sys.stdout.write(nacl.pwhash.argon2id.str(sys.stdin.buffer.read()).decode())')
if [ -z "$PHC" ]; then
    echo "seed-dev-account: failed to generate Argon2id PHC -- install python3-nacl (sudo apt install python3-nacl)" >&2
    exit 1
fi

# Idempotent re-seed: drop any existing avatars for this account across every
# table keyed by avatar_id (captured first so deleting avatar_info doesn't
# break the lookup), then replace the account row.
printf '%s\n' \
    "CREATE TEMP TABLE _del_av AS SELECT avatar_id FROM avatar_info WHERE account_id = (SELECT id FROM accounts WHERE username = :'username');" \
    "DO \$\$ DECLARE t text; BEGIN FOR t IN SELECT table_name FROM information_schema.columns WHERE column_name='avatar_id' AND table_schema='public' LOOP EXECUTE format('DELETE FROM %I WHERE avatar_id IN (SELECT avatar_id FROM _del_av)', t); END LOOP; END \$\$;" \
    "DROP TABLE _del_av;" \
    "-- Resync to the highest REAL account id, EXCLUDING the reserved bot band" \
    "-- (id >= 9000001). A plain MAX(id) would pull the sequence up to the AhBot" \
    "-- sentinel and the next signup would overflow the 32-bit GameID" \
    "-- (account*5+1). See db/postgres/freya_online_bots.sql." \
    "SELECT setval('accounts_id_seq', GREATEST((SELECT COALESCE(MAX(id),0) FROM accounts WHERE id < 9000001), 1));" \
    "DELETE FROM accounts WHERE username = :'username';" \
    "INSERT INTO accounts (username, password_phc, status, formname, email)" \
    "VALUES (:'username', :'phc', 100, :'username' || '_form', :'username' || '@local');" \
    | "${PSQL_USER[@]}" -v username="$USER" -v phc="$PHC" >/dev/null
echo "    account '$USER' ready (status=100)"

echo ">>> [3/7] bringing up dedicated CLI proxy for unit '$UNIT'"
if [ -z "${ENB_NOREBUILD:-}" ]; then
    docker compose -f docker-compose.cli.yml -p "$UNIT" build >/dev/null
fi
docker compose -f docker-compose.cli.yml -p "$UNIT" up -d proxy >/dev/null

echo ">>> [4/7] creating + entering ${#ROSTER[@]} characters (server-side init)"
for entry in "${ROSTER[@]}"; do
    suffix="${entry%%:*}"
    cls="${entry##*:}"
    name="${USER}${suffix}"
    echo "    - $name ($cls)"
    # One REPL session per character: create then enter (the enter handshake is
    # what triggers ReInitializeSavedData server-side). Drive non-interactively.
    # The server force-kicks a duplicate login per account, so the PREVIOUS
    # character's global session must be fully torn down before this login --
    # otherwise it is rejected with GlobalError code=13 ("log in twice"). That
    # teardown lags the CLI's clean logout by a few seconds, so retry with
    # backoff. create is harmless on retry (the name already exists, but the
    # following enter still drives the sector handshake we need).
    ok=
    for attempt in 1 2 3 4 5 6; do
        # Capture the full session (don't pipe to grep -q -- it would SIGPIPE the
        # docker run and pipefail would mis-report a success as a failure).
        session=$(printf 'connect cliproxy\nlogin %s %s\ncreate %s %s\nenter %s\nquit\n' \
                "$USER" "$PASS" "$cls" "$name" "$name" \
            | timeout 120 docker compose -f docker-compose.cli.yml -p "$UNIT" run --rm -T cli 2>&1) || true
        if printf '%s' "$session" | grep -qE "entering sector"; then
            ok=1
            break
        fi
        echo "      (attempt $attempt: session not ready yet, waiting for prior logout)" >&2
        sleep 5
    done
    if [ -z "$ok" ]; then
        echo "seed-dev-account: failed to create/enter $name ($cls)" >&2
        printf '%s\n' "$session" | tail -20 >&2
        exit 1
    fi
    # Give the server a moment to retire THIS session before the next login.
    sleep 3
done

echo ">>> [5/7] bumping levels / credits / slots / skills"
python3 "$SCRIPT_DIR/gen_bump_sql.py" "$REPO_ROOT/server/data/Skills.xml" \
    | "${PSQL_USER[@]}" -v acctname="$USER" >/dev/null

echo ">>> [6/7] equipping level-appropriate gear (shield/reactor/engine/weapon)"
# Resolve the best EQUIPPABLE item per class/slot from the net7 CONTENT db
# (gen_equip_sql.py mirrors exactly the gates Equipable::CanEquip enforces:
# slot/sub_category, skill-rank >= item tech level, race + profession masks).
# The two DBs are isolated, so we cannot join content tables from the
# net7_user connection -- resolve on net7, then apply on net7_user keyed by
# each avatar's class_index. The resulting item ids are integers straight
# from this query, never user input.
PSQL_NET7=(docker compose exec -T -e PGPASSWORD=net7 postgres psql -U net7 -d net7 -v ON_ERROR_STOP=1)
EQUIP_QUERY="$(python3 "$SCRIPT_DIR/gen_equip_sql.py" "$REPO_ROOT/server/data/Skills.xml")"
# rows: class_index|slot|item_id
RESOLVED="$("${PSQL_NET7[@]}" -At -F'|' -c "$EQUIP_QUERY")"
EQUIP_VALUES="$(printf '%s\n' "$RESOLVED" \
    | awk -F'|' 'NF==3 && $3!="" { printf "%s(%d,%d,%d)", sep, $1, $2, $3; sep="," }')"
if [ -z "$EQUIP_VALUES" ]; then
    echo "seed-dev-account: no equippable gear resolved -- leaving starting gear" >&2
else
    # Overwrite slots 0-3 (which the server already created in
    # ReInitializeSavedData) with the resolved upgrades. UPDATE, not INSERT:
    # the slot rows already exist, so no ON CONFLICT / unique-constraint
    # assumption. quality/structure pinned to a pristine 1.0.
    printf '%s\n' \
        "BEGIN;" \
        "WITH av AS (" \
        "  SELECT ai.avatar_id, (ad.race*3 + ad.prof) AS ci" \
        "    FROM avatar_info ai JOIN avatar_data ad ON ad.avatar_id = ai.avatar_id" \
        "   WHERE ai.account_id = (SELECT id FROM accounts WHERE username = :'acctname'))," \
        "m(ci, slot, item_id) AS (VALUES ${EQUIP_VALUES})" \
        "UPDATE avatar_equipment e" \
        "   SET item_id = m.item_id, quality = 1.0, structure = 1.0, builder_name = ''" \
        "  FROM av, m" \
        " WHERE e.avatar_id = av.avatar_id AND av.ci = m.ci AND e.equipment_slot = m.slot;" \
        "COMMIT;" \
        | "${PSQL_USER[@]}" -v acctname="$USER" >/dev/null
fi

echo ">>> [7/7] result"
"${PSQL_USER[@]}" -At -v username="$USER" <<'SQL'
SELECT '    ' || ad.first_name
       || '  L' || (ai.combat + ai.explore + ai.trade)
       || ' (' || ai.combat || '/' || ai.explore || '/' || ai.trade || ')'
       || '  ' || li.credits || ' cr'
       || '  ' || (SELECT count(*) FROM avatar_skill_levels s
                   WHERE s.avatar_id = ai.avatar_id AND s.skill_level > 0) || ' skills'
  FROM avatar_info ai
  JOIN avatar_data ad ON ad.avatar_id = ai.avatar_id
  JOIN avatar_level_info li ON li.avatar_id = ai.avatar_id
 WHERE ai.account_id = (SELECT id FROM accounts WHERE username = :'username')
 ORDER BY ai.slot;
SQL

echo ">>> done. login as '$USER' / '$PASS' (5 characters, total level 75 each)"
