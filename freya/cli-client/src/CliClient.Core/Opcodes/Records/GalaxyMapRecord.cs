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
/// int32 (375).
///
/// Types 5/6/7/8/9 are the static galaxy-map detail records shipped in
/// server/data/GalaxyMap.dat (a framed stream of [len:u16][0x0097:u16][record])
/// and also emitted on the live wire. Their byte layouts were derived from and
/// pinned against that data file:
///   Type 5 (star system): int32 Id; int32 Id2; cstr Name; float ColorR,G,B
///     (faction colour); float PosX,PosY,PosZ (map position); float One (1.0);
///     byte Kind (0/1/3 glyph). The .dat form ends right after Kind; the live
///     wire carries extra trailing bytes captured as ExtendedTail.
///   Type 6/7/8 (sector / planet-sector / gas-giant-sector, identical layout,
///     Type selects the glyph): int32 NodeId; int32 ParentSystemId; int32 Kind
///     (11/17/19, type-dependent); cstr Name; int32 TypeEcho (== Type);
///     float PosX,PosY,PosZ.
///   Type 9 (gate/link): int32 LinkId; int32 FromSector; int32 ToSector;
///     cstr Name. The compact .dat form ends after Name; the live wire carries
///     a richer trailing float block captured as ExtendedTail. The decoder
///     branches on whether bytes remain after Name.
///
/// Type 3 is an opaque blob (int32 count + a fixed table) and Type 2 is a
/// remove/refresh-ids control message; neither is renderable, so they keep the
/// minimal header + name-scan treatment.
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

        switch (type)
        {
            case 4: WriteType4(sb, size); return;
            case 5: WriteType5(sb, size); return;
            case 6:
            case 7:
            case 8: WriteType678(sb, type, size); return;
            case 9: WriteType9(sb, size); return;
        }

        // Remaining nav/map-detail subtypes (Type 2/3 and any future band) are
        // not renderable. Decode the header and the embedded name; leave the
        // surrounding numeric fields unmodeled.
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

    // Type 5 -- star system. int32 Id; int32 Id2; cstr Name; float ColorR,G,B;
    // float PosX,PosY,PosZ; float One (1.0); byte Kind. The .dat form ends right
    // after Kind; the live wire carries extra trailing bytes (ExtendedTail).
    private void WriteType5(StringBuilder sb, int size)
    {
        FDec(sb, 0, "Type", 5, "(star system)");
        FDec(sb, 4, "Size", size, "(body size, excl. 8-byte Type+Size header)");
        if (Payload.Length < 16) { Flag(sb, "GALAXY_MAP type 5 truncated -- need >= 16 bytes for the two IDs"); return; }

        FDec(sb,  8, "Id",  ReadI32LE(Payload, 8));
        FDec(sb, 12, "Id2", ReadI32LE(Payload, 12));

        int off = 16;
        if (!TryReadCString(sb, ref off, "Name")) return;

        if (off + 29 > Payload.Length)
        {
            Flag(sb, $"GALAXY_MAP type 5 truncated -- need 29 bytes after Name, only {Payload.Length - off} remain");
            return;
        }
        FFloat(sb, off,      "ColorR", ReadF32LE(Payload, off));
        FFloat(sb, off + 4,  "ColorG", ReadF32LE(Payload, off + 4));
        FFloat(sb, off + 8,  "ColorB", ReadF32LE(Payload, off + 8), "(faction colour)");
        FFloat(sb, off + 12, "PosX",   ReadF32LE(Payload, off + 12));
        FFloat(sb, off + 16, "PosY",   ReadF32LE(Payload, off + 16));
        FFloat(sb, off + 20, "PosZ",   ReadF32LE(Payload, off + 20), "(map position)");
        FFloat(sb, off + 24, "One",    ReadF32LE(Payload, off + 24), "(expected 1.0)");
        FDec(sb,   off + 28, "Kind",   Payload[off + 28], "(glyph: 0/1/3)");
        off += 29;

        WriteExtendedTail(sb, off);
    }

    // Type 6/7/8 -- sector / planet-sector / gas-giant-sector. Identical layout;
    // the Type byte picks the glyph. int32 NodeId; int32 ParentSystemId;
    // int32 Kind (11/17/19, type-dependent); cstr Name; int32 TypeEcho (== Type);
    // float PosX,PosY,PosZ.
    private void WriteType678(StringBuilder sb, int type, int size)
    {
        string label = type switch
        {
            6 => "(sector)",
            7 => "(planet-sector)",
            8 => "(gas-giant-sector)",
            _ => "(sector-like)",
        };
        FDec(sb, 0, "Type", type, label);
        FDec(sb, 4, "Size", size, "(body size, excl. 8-byte Type+Size header)");
        if (Payload.Length < 20) { Flag(sb, "GALAXY_MAP type 6/7/8 truncated -- need >= 20 bytes for the three IDs"); return; }

        FDec(sb,  8, "NodeId",         ReadI32LE(Payload, 8));
        FDec(sb, 12, "ParentSystemId", ReadI32LE(Payload, 12));
        FDec(sb, 16, "Kind",           ReadI32LE(Payload, 16), "(11/17/19, type-dependent)");

        int off = 20;
        if (!TryReadCString(sb, ref off, "Name")) return;

        if (off + 16 > Payload.Length)
        {
            Flag(sb, $"GALAXY_MAP type {type} truncated -- need 16 bytes after Name, only {Payload.Length - off} remain");
            return;
        }
        int echo = ReadI32LE(Payload, off);
        FDec(sb, off, "TypeEcho", echo, echo == type ? $"(== Type {type})" : null);
        if (echo != type) Flag(sb, $"TypeEcho is {echo}, expected {type}");
        FFloat(sb, off + 4,  "PosX", ReadF32LE(Payload, off + 4));
        FFloat(sb, off + 8,  "PosY", ReadF32LE(Payload, off + 8));
        FFloat(sb, off + 12, "PosZ", ReadF32LE(Payload, off + 12), "(map position)");
        off += 16;

        WriteExtendedTail(sb, off);
    }

    // Type 9 -- gate/link. int32 LinkId; int32 FromSector; int32 ToSector;
    // cstr Name. The compact .dat form ends after Name; the live wire carries a
    // trailing float block (ExtendedTail). Branch on whether bytes remain.
    private void WriteType9(StringBuilder sb, int size)
    {
        FDec(sb, 0, "Type", 9, "(gate/link)");
        FDec(sb, 4, "Size", size, "(body size, excl. 8-byte Type+Size header)");
        if (Payload.Length < 20) { Flag(sb, "GALAXY_MAP type 9 truncated -- need >= 20 bytes for the three IDs"); return; }

        FDec(sb,  8, "LinkId",     ReadI32LE(Payload, 8));
        FDec(sb, 12, "FromSector", ReadI32LE(Payload, 12));
        FDec(sb, 16, "ToSector",   ReadI32LE(Payload, 16));

        int off = 20;
        if (!TryReadCString(sb, ref off, "Name")) return;

        // Compact form: nothing after Name. Live form: a trailing float block.
        WriteExtendedTail(sb, off);
    }

    /// <summary>
    /// Capture any bytes remaining at <paramref name="off"/> as the raw
    /// ExtendedTail (the richer live-wire trailing block; empty for the compact
    /// .dat form). Bytes are marked decoded so they don't show as an undecoded
    /// gap, and rendered as lossless hex.
    /// </summary>
    private void WriteExtendedTail(StringBuilder sb, int off)
    {
        if (off >= Payload.Length) return;
        int len = Payload.Length - off;
        string hex = string.Join(" ", Payload.Skip(off).Take(len).Select(b => b.ToString("X2")));
        FBytes(sb, off, len, "ExtendedTail", hex, $"({len} live-wire bytes)");
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
