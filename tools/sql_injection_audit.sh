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
#
# Cross-line patterns
# -------------------
# The check now uses a small look-ahead window: when an unsafe-build
# keyword is seen on a line, the next CROSS_LINE_WINDOW lines are also
# inspected for a SQL keyword. This catches the common idiom:
#     sprintf_s(buf, sizeof(buf),
#         "SELECT ... WHERE x = '%d'", x);
# where the function-call open and the SQL string literal sit on
# different physical lines and so escaped the strict single-line check.
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
# Look-ahead window (lines after an unsafe-build site) within which a
# SQL keyword still counts as part of the same site. 8 covers every
# multi-line sprintf we encountered in this codebase.
CROSS_LINE_WINDOW=8

# Walk one file and emit "path:lineno:line" for any unsafe-build line
# that isn't inside a Win32-walled block. Pure bash + sed; no heavy deps.
scan_file() {
    local path="$1"
    # LC_ALL=C so awk treats input as bytes — the source tree has
    # Latin-1 © glyphs in license headers that trip the locale-aware
    # regex engine and spam "Invalid multibyte data" warnings.
    LC_ALL=C awk -v path="$path" -v sqlre="$SQL_KW" -v buildre="$UNSAFE_BUILD" -v window="$CROSS_LINE_WINDOW" '
        BEGIN {
            walled = 0; depth_at_wall = -1; depth = 0
            pending_line = 0       # line number of the unsafe-build site we are following, 0 if none
            pending_text = ""      # the original line text (for reporting)
        }
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

            if (walled) { pending_line = 0; next }

            is_line_comment = match(line, /^[[:space:]]*\/\//)
            is_block_cont   = match(line, /^[[:space:]]*\*/)

            # Strip trailing // comment so a SQL keyword inside a comment
            # tail does not trigger.
            effective = line
            cmt = index(effective, "//")
            if (cmt > 0) effective = substr(effective, 1, cmt - 1)

            has_build = match(effective, buildre)
            has_sql   = match(effective, sqlre)

            if (!is_line_comment && !is_block_cont) {
                # Same-line classic case.
                if (has_build && has_sql) {
                    printf("%s:%d:%s\n", path, NR, line)
                    pending_line = 0
                    next
                }
                # Cross-line: an unsafe-build site opens a multi-line call
                # whose closing args (the SQL string literal) sit a few
                # lines below. Only open the window if this line genuinely
                # looks like an UNCLOSED format-string call -- a sprintf/
                # snprintf with at least one trailing comma and unbalanced
                # parens. Other unsafe-build flavours (strcat / stpcpy)
                # never span lines this way.
                if (has_build && match(effective, /[sf]?n?printf/)) {
                    # Trim trailing whitespace before counting commas/parens.
                    sub(/[[:space:]]+$/, "", effective)
                    opens  = gsub(/\(/, "(", effective)
                    closes = gsub(/\)/, ")", effective)
                    if (opens > closes && substr(effective, length(effective), 1) == ",") {
                        pending_line = NR
                        pending_text = line
                        next
                    }
                }
                # The continuation line itself must contain a SQL keyword
                # inside a string literal -- bare identifiers like
                # XML_TAG_ID_FOO_UPDATE in switch cases would otherwise
                # false-positive.
                if (pending_line > 0 && (NR - pending_line) <= window) {
                    if (match(effective, /"[^"]*(SELECT|INSERT|UPDATE|DELETE|REPLACE|CALL)[^"]*"/)) {
                        printf("%s:%d:%s\n", path, pending_line, pending_text)
                        pending_line = 0
                        next
                    }
                }
            }

            # Close the window once we pass it without finding a SQL keyword.
            if (pending_line > 0 && (NR - pending_line) > window) pending_line = 0
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
