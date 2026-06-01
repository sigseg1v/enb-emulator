// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>0x004F STARBASE_SET. Wire (struct StarbaseSet): int32 StarbaseID; char Action; char ExitMode. = 6 bytes.</summary>
public sealed class StarbaseSetRecord : PacketRecord
{
    public StarbaseSetRecord(ReadOnlySpan<byte> payload) : base(0x004F, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 6) { Flag(sb, $"STARBASE_SET truncated -- {'{'}Payload.Length{'}'} bytes, expected 6"); return; }
        int  starbaseId = ReadI32LE(Payload, 0);
        byte action     = Payload[4];
        byte exitMode   = Payload[5];
        FHex(sb, 0, "StarbaseID", starbaseId);
        FlagSuspicious(sb, "StarbaseID", starbaseId);
        FDec(sb, 4, "Action",   action);
        FDec(sb, 5, "ExitMode", exitMode);
    }
}
