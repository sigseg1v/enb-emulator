// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

namespace N7.CliClient;

/// <summary>
/// Static identifiers for the CLI client. Used by --version output and by
/// the unit-test trinity smoke check (lib reachable from console + tests).
/// </summary>
public static class ClientInfo
{
    public const string Name = "enb-cli-client";

    /// <summary>
    /// Semantic version of the CLI client. Bump on protocol-impacting
    /// changes (handshake, opcode coverage). Cosmetic-only changes keep
    /// the same version.
    /// </summary>
    public const string Version = "0.1.0-dev";

    /// <summary>
    /// Phase pointer for cross-referencing with plans/19-phase-s-cli-client.md.
    /// </summary>
    public const string Phase = "S";
}
