// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x00BA LOGOFF_CONFIRMATION. Server acknowledges the client's 0x00B9 logoff
/// request; the client may now tear down the sector connection. No payload.
/// </summary>
public sealed class LogoffConfirmationRecord : PacketRecord
{
    public LogoffConfirmationRecord(ReadOnlySpan<byte> payload) : base(0x00BA, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length != 0) Flag(sb, $"LOGOFF_CONFIRMATION expected 0 bytes, got {Payload.Length}");
    }
}
