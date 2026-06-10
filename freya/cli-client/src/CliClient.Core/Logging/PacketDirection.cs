// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

namespace N7.CliClient.Logging;

/// <summary>
/// Whether a logged packet was sent BY the client (outbound) or
/// received FROM the server (inbound). The log line records the
/// lowercase serialised form.
/// </summary>
public enum PacketDirection
{
    /// <summary>Server → client (received).</summary>
    Inbound,

    /// <summary>Client → server (sent).</summary>
    Outbound,
}
