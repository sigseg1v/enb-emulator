# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# lib.sh -- shared config + helpers for the import-captures skill. Sourced by
# every script. Loads a gitignored .env (next to SKILL.md) if present so the
# per-machine paths (capture dir, work dir) live in ONE place and never get
# baked into committed files.
set -uo pipefail

SKILL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
REPO_ROOT="$(cd "$SKILL_DIR/../../.." && pwd)"

# Load .env (KEY=VALUE lines) if present. Never commit this file.
if [ -f "$SKILL_DIR/.env" ]; then
    set -a; . "$SKILL_DIR/.env"; set +a
fi

# Where the raw packet captures live. NEVER committed; an external, gitignored
# directory. Override in .env (ENB_CAPS_DIR). No default points anywhere real --
# fail loud if unset so we never silently read the wrong tree.
CAPS_DIR="${ENB_CAPS_DIR:-}"

# Scratch/work dir for decoded JSON + aggregated per-sector JSON. Defaults to a
# session scratch dir; override with ENB_IMPORT_WORK.
WORK="${ENB_IMPORT_WORK:-/tmp/enb-import-captures}"

# Postgres container + the built decoder DLL.
PG_CONTAINER="${ENB_PG_CONTAINER:-freya-postgres-1}"
DECODER="${ENB_DECODER:-$REPO_ROOT/tools/pcap-inventory/bin/Release/net10.0/pcap-inventory.dll}"

# Generated SQL lands here (committed; this is the import artifact).
SQL_OUT_DIR="$REPO_ROOT/db/postgres/capture_import"
SQL_WRAPPER="$REPO_ROOT/db/postgres/seed_captures.sql"

die() { echo "ERROR: $*" >&2; exit 1; }

# Run a parameterless SQL query against net7 content DB; rows on stdout, fields
# separated by US (0x1f). Callers MUST pass a constant query (no value interp).
pg() { docker exec "$PG_CONTAINER" psql -U net7 -d net7 -tA -F $'\x1f' -c "$1"; }
