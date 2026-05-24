#!/usr/bin/env bash
# sql_injection_audit.sh
#
# Fail if any tracked source file in server-native code (server/,
# login-server/, proxy/) builds a SQL query string by concatenating
# user-controlled or dynamic data via sprintf/snprintf/strcat/strncat
# instead of using parameterised statements.
#
# Background
# ----------
# Phase N migrated server/src/ to libpqxx with an opaque-handle wrapper,
# but the parameterised execute() API arrived in Wave 2 (Phase N+). Before
# that, every dynamic-SQL site used sprintf-style query construction —
# the classic SQL-injection pattern. Login-server's LinuxAuth.cpp gates
# its inputs behind SafeUsername/SafePassword whitelists as a band-aid;
# the real fix is parameter binding (mysql_stmt_bind_param or
# pqxx::params).
#
# This script keeps the SQL-injection surface at zero across the
# server-native tree. Anyone adding a new query is forced through the
# parameterised path.
#
# What it flags
# -------------
# Lines that combine ONE of these query-shaping keywords:
#     SELECT|INSERT|UPDATE|DELETE|REPLACE|CALL
# with ONE of these unsafe build patterns:
#     sprintf|snprintf|strcat|strncat|stpcpy
#
# What it exempts
# ---------------
#   * Win32-walled blocks (`#ifdef WIN32` ... `#endif`) — dead on Linux.
#     The audit walks the tree once to mark line ranges inside such
#     blocks and skips them. AccountManager.cpp's 34 dynamic-SQL sites
#     are all walled; they fall into this category until Phase J
#     unwalls them.
#   * Vendored / archived trees (third_party/, archive/, kyp-snapshot/,
#     server/src/openssl/, server/src/mysql/, server/src/LUA/,
#     login-server/Net7Mysql/mysql/).
#   * Comments — lines whose flagged occurrence sits inside a //... or
#     /* ... */ comment context (best-effort, line-based).
#
# What it does NOT catch
# ----------------------
#   * Manual string concatenation via `+` / `<<` (the codebase does not
#     use this pattern in query construction).
#   * Macros that expand to sprintf at preprocessing time.
#   * Dynamic SQL built across multiple lines (would need AST parsing).
#
# Exit codes
#   0 — clean
#   1 — at least one site flagged; offending paths/lines printed

set -euo pipefail

ROOT="$(git rev-parse --show-toplevel)"
cd "$ROOT"

ROOTS=(server/src login-server proxy)
EXCLUDE_RE='(server/third_party/|server/src/openssl/|server/src/mysql/|server/src/LUA/|login-server/Net7Mysql/mysql/|archive/|/bin/|/obj/)'

SQL_KW='SELECT|INSERT|UPDATE|DELETE|REPLACE|CALL'
UNSAFE_BUILD='sprintf|snprintf|strcat|strncat|stpcpy'

# Walk one file and emit "path:lineno:line" for any unsafe-build line
# that isn't inside a Win32-walled block. Pure bash + sed; no heavy deps.
scan_file() {
    local path="$1"
    # LC_ALL=C so awk treats input as bytes — the source tree has
    # Latin-1 © glyphs in license headers that trip the locale-aware
    # regex engine and spam "Invalid multibyte data" warnings.
    LC_ALL=C awk -v path="$path" -v sqlre="$SQL_KW" -v buildre="$UNSAFE_BUILD" '
        BEGIN { walled = 0; depth_at_wall = -1; depth = 0 }
        {
            line = $0
            # Track nesting of #if/#ifdef so we know when a WIN32 wall closes.
            if (match(line, /^[[:space:]]*#[[:space:]]*(if|ifdef|ifndef)[[:space:]]/)) {
                depth++
                if (match(line, /WIN32/) && !walled) {
                    walled = 1
                    depth_at_wall = depth
                }
            } else if (match(line, /^[[:space:]]*#[[:space:]]*endif/)) {
                if (walled && depth == depth_at_wall) {
                    walled = 0
                    depth_at_wall = -1
                }
                depth--
                if (depth < 0) depth = 0
            } else if (match(line, /^[[:space:]]*#[[:space:]]*else/) && walled && depth == depth_at_wall) {
                # The else branch of a WIN32 wall is the Linux side — stop walling.
                walled = 0
                depth_at_wall = -1
            }

            if (walled) next
            # Skip pure comment lines
            if (match(line, /^[[:space:]]*\/\//)) next
            if (match(line, /^[[:space:]]*\*/)) next

            if (match(line, buildre) && match(line, sqlre)) {
                # Skip if the SQL keyword is only inside a // comment
                cmt = index(line, "//")
                if (cmt > 0) {
                    head = substr(line, 1, cmt - 1)
                    if (!match(head, sqlre) || !match(head, buildre)) next
                }
                printf("%s:%d:%s\n", path, NR, line)
            }
        }
    ' "$path"
}

found=0
report=""
while IFS= read -r path; do
    if [ -z "$path" ]; then continue; fi
    if echo "$path" | grep -Eq "$EXCLUDE_RE"; then continue; fi
    case "$path" in
        *.cpp|*.c|*.cc|*.cxx|*.h|*.hpp) ;;
        *) continue ;;
    esac
    hits="$(scan_file "$path" || true)"
    if [ -n "$hits" ]; then
        report="${report}${hits}"$'\n'
        found=1
    fi
done < <(git ls-files -- "${ROOTS[@]}" 2>/dev/null || true)

if [ "$found" -ne 0 ]; then
    printf '%s' "$report" >&2
    cat >&2 <<'EOF'

One or more sites build SQL queries by string-concat with dynamic data.
This is the SQL-injection antipattern. Use parameterised statements:

    - libpqxx (server/src/, via mysqlplus.cpp):
        q.execute_params("SELECT id FROM t WHERE col=$1", {value});

    - libmysqlclient (login-server/, via mysql_stmt_*):
        MYSQL_STMT *s = mysql_stmt_init(conn);
        mysql_stmt_prepare(s, "SELECT id FROM t WHERE col=?", -1);
        MYSQL_BIND b{}; b.buffer_type=MYSQL_TYPE_STRING;
        b.buffer=(void*)value; b.buffer_length=strlen(value);
        mysql_stmt_bind_param(s, &b);
        mysql_stmt_execute(s);

If the flagged line is genuinely safe (e.g. all inputs are integer
constants from the binary itself, never user data), either rewrite it
to parameterised form anyway (one consistent path is cheaper than
documenting exceptions) or wrap it in `#ifdef WIN32` if it's truly
dead-on-Linux code awaiting a Phase J rewrite.
EOF
    exit 1
fi

exit 0
