// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0097 GALAXY_MAP. Wire shape (variable):
///   int32 Type;     (always 4)
///   int32 Size;     (total variable data size, NOT including the 8-byte Type+Size header)
///   int32 PlayerID;
///   char[up to 64] Variable -- three consecutive NUL-terminated strings: system, sector, station
///   int32 unknown;  (always 375, appended after the strings inside Variable)
/// Total sent = Size + 8 (emitter: SendOpcode(..., size + 8)).
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

        FieldDec(sb, "Type",     type, type == 4 ? "(map update)" : null);
        FieldDec(sb, "Size",     size, "(variable data size, excludes 8-byte Type+Size header)");
        FieldHex(sb, "PlayerID", playerId);

        if (type != 4)
            Flag(sb, $"unexpected Type {type} (expected 4)");

        // Variable section starts at offset 12
        int off = 12;
        string[] labels = { "System", "Sector", "Station" };
        foreach (string label in labels)
        {
            if (off >= Payload.Length) break;
            int nul = Array.IndexOf(Payload, (byte)0, off);
            if (nul < 0) nul = Payload.Length;
            string s = System.Text.Encoding.ASCII.GetString(Payload, off, nul - off);
            FieldString(sb, label, s);
            off = nul + 1;
        }

        // Trailing int32 unknown = 375 (appended by emitter after the strings)
        if (off + 4 <= Payload.Length)
        {
            int unknown = ReadI32LE(Payload, off);
            FieldDec(sb, "Unknown", unknown, unknown == 375 ? "(expected 375)" : null);
            if (unknown != 375)
                Flag(sb, $"Unknown field is {unknown}, expected 375");
        }
    }
}
