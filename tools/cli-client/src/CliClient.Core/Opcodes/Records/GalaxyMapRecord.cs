// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0097 GALAXY_MAP. Wire (variable):
///   int32 Type (always 4); int32 Size (variable data size, excl. 8-byte Type+Size header);
///   int32 PlayerID; char[up to 64] Variable -- 3 NUL-terminated strings: system, sector, station;
///   int32 unknown (always 375, appended after strings). Total sent = Size + 8.
/// Source: PlayerConnection.cpp SendGalaxyMap().
/// </summary>
public sealed class GalaxyMapRecord : PacketRecord
{
    public GalaxyMapRecord(ReadOnlySpan<byte> payload) : base(0x0097, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 12)
        {
            Flag(sb, $"GALAXY_MAP truncated -- {Payload.Length} bytes, expected >= 12");
            return;
        }
        int type     = ReadI32LE(Payload, 0);
        int size     = ReadI32LE(Payload, 4);
        int playerId = ReadI32LE(Payload, 8);

        FDec(sb, 0, "Type",     type, type == 4 ? "(map update)" : null);
        FDec(sb, 4, "Size",     size, "(variable data size, excl. 8-byte Type+Size header)");
        FHex(sb, 8, "PlayerID", playerId);
        if (type != 4)
            Flag(sb, $"unexpected Type {type} (expected 4)");

        int off = 12;
        string[] labels = { "System", "Sector", "Station" };
        foreach (string label in labels)
        {
            if (off >= Payload.Length) break;
            int nul = Array.IndexOf(Payload, (byte)0, off);
            if (nul < 0) nul = Payload.Length;
            string s = System.Text.Encoding.ASCII.GetString(Payload, off, nul - off);
            FStr(sb, off, nul - off + 1, label, s); // +1 includes the NUL
            off = nul + 1;
        }

        if (off + 4 <= Payload.Length)
        {
            int unknown = ReadI32LE(Payload, off);
            FDec(sb, off, "Unknown", unknown, unknown == 375 ? "(expected 375)" : null);
            if (unknown != 375)
                Flag(sb, $"Unknown field is {unknown}, expected 375");
        }
    }
}
