#!/usr/bin/env bash
# check_no_mojibake.sh
#
# Fail if any tracked source file under server/, login-server/, proxy/,
# launcher/, or client/ contains the U+FFFD REPLACEMENT CHARACTER
# (UTF-8 bytes EF BF BD).
#
# Background
# ----------
# Net-7 source files arrived from the original SVN drop with CRLF line
# endings and ISO-8859 / Windows-1252 encoding. The license headers
# contain a Latin-1 © (0xA9). When an editor pipeline (Read → Edit) writes
# such a file back as UTF-8, the lone 0xA9 byte is interpreted as the
# start of a malformed UTF-8 sequence and replaced with U+FFFD on save.
# This silently corrupts the license header (and any other Latin-1 text in
# the file).
#
# That actually happened in this repo across 21 files (see commit b820058,
# "Restore mangled © in Net-7 license headers"). This script exists so it
# doesn't happen quietly again. Run in CI on every PR.
#
# Exit codes
#   0 — no mojibake found
#   1 — at least one file contains U+FFFD; offending paths printed
set -euo pipefail

ROOT="$(git rev-parse --show-toplevel)"
cd "$ROOT"

# Roots we care about. Skip vendored / archived / generated trees.
ROOTS=(server login-server proxy launcher client tools)
EXCLUDE_RE='(server/third_party/|server/src/openssl/|server/src/mysql/|server/src/LUA/|login-server/Net7Mysql/mysql/|archive/|tools/.*/bin/|tools/.*/obj/)'

found=0
while IFS= read -r path; do
    # Skip excluded paths
    if echo "$path" | grep -Eq "$EXCLUDE_RE"; then continue; fi
    # Only text-ish files (skip binaries — they may legitimately contain EF BF BD)
    if ! file --mime "$path" 2>/dev/null | grep -q 'text\|json\|xml\|script'; then continue; fi
    if grep -lP $'\xef\xbf\xbd' "$path" >/dev/null 2>&1; then
        echo "MOJIBAKE: $path" >&2
        found=1
    fi
done < <(git ls-files -- "${ROOTS[@]}" 2>/dev/null || true)

if [ "$found" -ne 0 ]; then
    cat >&2 <<'EOF'

One or more source files contain the U+FFFD replacement character.
This typically means a Read → Edit cycle re-wrote a Latin-1/CRLF file
as UTF-8 and corrupted a non-ASCII byte (most often the © in the
Net-7 license header).

Recover by checking out the file from a known-good revision:
    git checkout <good-rev> -- <path>
or by mass-substituting the corruption back to the intended character:
    sed -i $'s/\xef\xbf\xbd/©/g' <path>
EOF
    exit 1
fi

exit 0
