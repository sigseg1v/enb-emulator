// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0097 GALAXY_MAP -- a multiplexed nav/map opcode keyed by a leading int32
/// Type. Wire header is always: int32 Type; int32 Size (body size, excl. the
/// 8-byte Type+Size header; total sent = Size + 8).
///
/// Type 4 is the "you are here" update our server emits
/// (PlayerConnection.cpp SendGalaxyMap): after the header, int32 PlayerID then
/// three NUL-terminated strings (system, sector, station) and a trailing
/// int32 (375). The retail captures (capture_1/2/3.rar) also carry Types
/// 3/5/6/7/8/9 -- nav/map-detail entries our server never sends: a few int32
/// IDs, a NUL-terminated name (Type 5 = star systems "Aragoth"/"Alpha
/// Centauri"; 6/9 = sectors "Io"/"Earth"; 7/8 = planets/areas "Jagerstadt
/// (Planet Zweihander)"), then a float coordinate/orientation block. We decode
/// the header + the verifiable Name for those; the surrounding numeric fields
/// are not modeled (no server source to pin them against) and stay in the hex
/// tail rather than being mis-read as the Type-4 string layout.
/// </summary>
public sealed class GalaxyMapRecord : PacketRecord
{
    public GalaxyMapRecord(ReadOnlySpan<byte> payload) : base(0x0097, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 8)
        {
            Flag(sb, $"GALAXY_MAP truncated -- {Payload.Length} bytes, expected >= 8");
            return;
        }
        int type = ReadI32LE(Payload, 0);
        int size = ReadI32LE(Payload, 4);

        if (type == 4) { WriteType4(sb, size); return; }

        // Retail nav/map-detail subtype our server does not emit. Decode the
        // header and the embedded name; leave the numeric fields unmodeled.
        FDec(sb, 0, "Type", type, "(nav/map-detail subtype; numeric fields unmodeled)");
        FDec(sb, 4, "Size", size, "(body size, excl. 8-byte Type+Size header)");
        if (TryFindName(8, out int nameOff, out string name))
            FStr(sb, nameOff, name.Length + 1, "Name", name);   // +1 includes the NUL
    }

    private void WriteType4(StringBuilder sb, int size)
    {
        if (Payload.Length < 12)
        {
            Flag(sb, $"GALAXY_MAP type 4 truncated -- {Payload.Length} bytes, expected >= 12");
            return;
        }
        int playerId = ReadI32LE(Payload, 8);
        FDec(sb, 0, "Type",     4, "(map update)");
        FDec(sb, 4, "Size",     size, "(variable data size, excl. 8-byte Type+Size header)");
        FHex(sb, 8, "PlayerID", playerId);

        int off = 12;
        string[] labels = { "System", "Sector", "Station" };
        foreach (string label in labels)
        {
            if (off >= Payload.Length) break;
            int nul = Array.IndexOf(Payload, (byte)0, off);
            if (nul < 0) nul = Payload.Length;
            string s = Encoding.ASCII.GetString(Payload, off, nul - off);
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

    /// <summary>
    /// Find the first NUL-terminated printable-ASCII run (len &gt;= 2) at or after
    /// <paramref name="start"/>. The leading int32 IDs hold small values whose
    /// high bytes are 0x00, so they don't form spurious 2-char strings ahead of
    /// the real name.
    /// </summary>
    private bool TryFindName(int start, out int nameOff, out string name)
    {
        for (int o = start; o < Payload.Length; o++)
        {
            if (!IsPrintable(Payload[o])) continue;
            int j = o;
            while (j < Payload.Length && IsPrintable(Payload[j])) j++;
            if (j < Payload.Length && Payload[j] == 0 && j - o >= 2)
            {
                nameOff = o;
                name = Encoding.ASCII.GetString(Payload, o, j - o);
                return true;
            }
            o = j;  // skip the run we just rejected
        }
        nameOff = -1;
        name = "";
        return false;
    }

    private static bool IsPrintable(byte b) => b >= 0x20 && b < 0x7F;
}
