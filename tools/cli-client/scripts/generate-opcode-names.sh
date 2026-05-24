#!/usr/bin/env bash
# SPDX-License-Identifier: CC-BY-NC-SA-3.0
# Part of the Earth & Beyond emulator preservation project.
# New code; project default license (LICENSES/enb-emulator).
#
# Regenerates tools/cli-client/src/CliClient.Core/Opcodes/OpcodeNames.Generated.cs
# from common/include/net7/Opcodes.h.
#
# Rerun whenever Opcodes.h changes. Output is committed so production
# builds need no codegen step.
#
# Usage: ./tools/cli-client/scripts/generate-opcode-names.sh

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/../../.." && pwd)"
SRC="$REPO_ROOT/common/include/net7/Opcodes.h"
DST="$REPO_ROOT/tools/cli-client/src/CliClient.Core/Opcodes/OpcodeNames.Generated.cs"

if [[ ! -f "$SRC" ]]; then
    echo "FATAL: source not found: $SRC" >&2
    exit 1
fi

# Extract `(hex_uppercase, NAME)` pairs from `#define ENB_OPCODE_<hex>_<NAME>  0x<hex>`.
# Output is sorted by opcode value. When the same opcode hex has two names
# (DATA_FILE/SET_GLOBAL_LOGIN_LINK at 0x2010, GALAXY_MAP_CACHE/SET_PROXY_SECTOR_LINK
# at 0x2011 — see Opcodes.h:222-225) we collapse them to "NAME_A_OR_NAME_B".
PAIRS=$(awk 'match($0, /#define[[:space:]]+ENB_OPCODE_([0-9A-Fa-f]+)_([A-Z_0-9]+)[[:space:]]+0x([0-9A-Fa-f]+)/, m) {
    hex = toupper(m[3])
    name = m[2]
    if (hex in seen) {
        seen[hex] = seen[hex] "_OR_" name
    } else {
        seen[hex] = name
        order[++n] = hex
    }
}
END {
    for (i = 1; i <= n; i++) {
        hex = order[i]
        print hex, seen[hex]
    }
}' "$SRC" | sort)

COUNT=$(echo "$PAIRS" | wc -l)

cat > "$DST" <<EOF
// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator
//
// === AUTO-GENERATED — DO NOT EDIT BY HAND ===
// Source: common/include/net7/Opcodes.h
// Regenerate via: tools/cli-client/scripts/generate-opcode-names.sh

using System.Collections.Frozen;

namespace N7.CliClient.Opcodes;

/// <summary>
/// Human-readable names for every opcode declared in
/// <c>common/include/net7/Opcodes.h</c>. Generated from the C header
/// so the C# side stays in lockstep without manual transcription.
///
/// Used by the packet log to print "0x0033 CLIENT_CHAT" instead of
/// "0x0033 UNKNOWN", and by <c>OpcodeRegistry.RegisterAllOpaque</c>
/// to seed the registry with name-tagged opaque codecs for every
/// known opcode (Phase S Item 15).
/// </summary>
public static class OpcodeNames
{
    /// <summary>How many distinct opcodes the EnB protocol declares.</summary>
    public const int Count = ${COUNT};

    /// <summary>Maps opcode value → upstream symbolic name (e.g. CLIENT_CHAT).</summary>
    public static readonly FrozenDictionary<ushort, string> All =
        new Dictionary<ushort, string>
        {
EOF

echo "$PAIRS" | while read -r hex name; do
    printf '            { 0x%s, "%s" },\n' "$hex" "$name" >> "$DST"
done

cat >> "$DST" <<'EOF'
        }.ToFrozenDictionary();

    /// <summary>
    /// Look up an opcode's symbolic name. Returns "0xNNNN UNKNOWN" for
    /// opcodes outside Opcodes.h. Never throws.
    /// </summary>
    public static string Get(OpcodeId opcode) =>
        All.TryGetValue(opcode.Value, out var name) ? name : "UNKNOWN";

    /// <summary>
    /// Render in the form "0xNNNN NAME" suitable for log lines.
    /// </summary>
    public static string Format(OpcodeId opcode) =>
        $"{opcode} {Get(opcode)}";
}
EOF

echo "wrote $DST ($COUNT opcodes)"
