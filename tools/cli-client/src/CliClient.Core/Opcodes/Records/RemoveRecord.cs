// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>0x0007 REMOVE. Wire: int32 GameID (4 bytes).</summary>
public sealed class RemoveRecord : PacketRecord
{
    public RemoveRecord(ReadOnlySpan<byte> payload) : base(0x0007, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 4) { Flag(sb, $"REMOVE truncated -- {'{'}Payload.Length{'}'} bytes, expected 4"); return; }
        int gameId = ReadI32LE(Payload, 0);
        FHex(sb, 0, "GameID", gameId);
        FlagSuspicious(sb, "GameID", gameId);
    }
}
