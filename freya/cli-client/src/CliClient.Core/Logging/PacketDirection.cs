// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

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
