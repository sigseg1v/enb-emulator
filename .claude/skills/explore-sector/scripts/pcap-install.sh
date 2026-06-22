#!/usr/bin/env bash
# SPDX-License-Identifier: MIT
# Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
# License: LICENSES/Freya
#
# pcap-install.sh -- ONE-TIME setup so per-sector packet capture runs unattended.
# Run once WITH sudo:
#     sudo bash .claude/skills/explore-sector/scripts/pcap-install.sh
#
# It installs the privileged capture worker ROOT-OWNED to /usr/local/sbin (so the
# NOPASSWD grant points at a non-user-writable target -- a user-writable sudo
# target would be a privilege-escalation footgun) and drops a SCOPED sudoers rule
# allowing ONLY that one worker to run without a password. It does NOT grant a
# blanket nsenter/tcpdump sudo right. Re-run it after changing the worker script.
#
# Uninstall: sudo rm -f /usr/local/sbin/freya-pcap-capture /etc/sudoers.d/freya-pcap
set -euo pipefail

[ "$(id -u)" = 0 ] || { echo "run with sudo: sudo bash $0" >&2; exit 1; }

SKILL_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SRC="$SKILL_DIR/pcap-capture-root.sh"
DST="/usr/local/sbin/freya-pcap-capture"
RUNAS="${SUDO_USER:-$(logname 2>/dev/null || echo root)}"

[ -f "$SRC" ] || { echo "missing worker source: $SRC" >&2; exit 1; }
command -v tcpdump >/dev/null || { echo "tcpdump not installed on host -- apt install tcpdump" >&2; exit 1; }
command -v nsenter  >/dev/null || { echo "nsenter not installed on host -- apt install util-linux" >&2; exit 1; }

install -m 0755 -o root -g root "$SRC" "$DST"
echo "installed worker -> $DST"

SUDOERS="/etc/sudoers.d/freya-pcap"
tmp="$(mktemp)"
printf '# explore-sector per-sector packet capture (scoped NOPASSWD -- one worker only)\n%s ALL=(root) NOPASSWD: %s\n' "$RUNAS" "$DST" > "$tmp"
if visudo -cf "$tmp" >/dev/null 2>&1; then
    install -m 0440 -o root -g root "$tmp" "$SUDOERS"
    rm -f "$tmp"
    echo "installed sudoers rule -> $SUDOERS  (user: $RUNAS)"
else
    rm -f "$tmp"
    echo "sudoers rule failed validation -- NOT installed" >&2; exit 1
fi

# verify the user can now invoke the worker passwordless
if sudo -u "$RUNAS" sudo -n "$DST" status /tmp >/dev/null 2>&1; then
    echo "OK -- '$RUNAS' can run the capture worker without a password."
    echo "Capture is now enabled. The survey will create one .pcap per sector under"
    echo "  $SKILL_DIR/../state/captures/"
else
    echo "WARN -- passwordless check did not pass; inspect $SUDOERS" >&2
fi
