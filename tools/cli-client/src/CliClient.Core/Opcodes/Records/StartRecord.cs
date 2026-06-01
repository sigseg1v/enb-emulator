// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>0x0005 START. Wire: int32 StartID (4 bytes) = player GameID; client treats this as its own ID.</summary>
public sealed class StartRecord : PacketRecord
{
    public StartRecord(ReadOnlySpan<byte> payload) : base(0x0005, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 4) { Flag(sb, $"START truncated -- {'{'}Payload.Length{'}'} bytes, expected 4"); return; }
        int startId = ReadI32LE(Payload, 0);
        FHex(sb, 0, "StartID", startId, "(= player GameID; client uses as self-id)");
        FlagSuspicious(sb, "StartID", startId);
        if (Payload.Length > 4) Flag(sb, $"START has {'{'}Payload.Length - 4{'}'} trailing bytes");
    }
}
