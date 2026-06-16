// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x003A SERVER_HANDOFF. Wire (struct ServerHandoff): a 64-byte MasterJoin
/// followed by variable_data holding four AddString fields
/// (FromSector, FromSystem, ToSector, ToSystem).
///
/// MasterJoin (64 bytes, all int32 LE then ticket[20]):
///   unknown1..3 (3x i32), avatar_id_msb i32, avatar_id_lsb i32,
///   ToSectorID i32, FromSectorID i32, PlayerLevel i32, unknown8..10 (3x i32),
///   char ticket[20].
/// ToSectorID/FromSectorID are emitted via ntohl() so they are BIG-ENDIAN on
/// the wire (Player::SendServerHandoff). Every other MasterJoin field is host
/// (little-endian).
/// AddString = u16 length (LE) + raw chars, no NUL.
/// </summary>
public sealed class ServerHandoffRecord : PacketRecord
{
    public ServerHandoffRecord(ReadOnlySpan<byte> payload) : base(0x003A, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 64) { Flag(sb, $"SERVER_HANDOFF truncated -- {Payload.Length} bytes, expected >= 64"); return; }

        FHex(sb, 0, "Unknown1", ReadI32LE(Payload, 0));
        FHex(sb, 4, "Unknown2", ReadI32LE(Payload, 4));
        FHex(sb, 8, "Unknown3", ReadI32LE(Payload, 8));
        FHex(sb, 12, "AvatarIdMsb", ReadI32LE(Payload, 12));
        FHex(sb, 16, "AvatarIdLsb", ReadI32LE(Payload, 16));
        FDec(sb, 20, "ToSectorID", ReadI32BE(Payload, 20), "(BE -- ntohl at emit)");
        FDec(sb, 24, "FromSectorID", ReadI32BE(Payload, 24), "(BE -- ntohl at emit)");
        FDec(sb, 28, "PlayerLevel", ReadI32LE(Payload, 28));
        FHex(sb, 32, "Unknown8", ReadI32LE(Payload, 32));
        FHex(sb, 36, "Unknown9", ReadI32LE(Payload, 36));
        FHex(sb, 40, "Unknown10", ReadI32LE(Payload, 40));
        FBytes(sb, 44, 20, "Ticket", TicketDisplay(Payload.AsSpan(44, 20)));

        int off = 64;
        string[] labels = { "FromSector", "FromSystem", "ToSector", "ToSystem" };
        foreach (var label in labels)
        {
            if (off + 2 > Payload.Length) return;
            ushort len = ReadU16LE(Payload, off);
            if (off + 2 + len > Payload.Length)
            {
                FDec(sb, off, $"{label}.Len", (short)len);
                Flag(sb, $"{label} length {len} overruns payload");
                return;
            }
            string s = Encoding.ASCII.GetString(Payload, off + 2, len);
            F(sb, off, 2 + len, label, Quote(s));
            off += 2 + len;
        }
    }

    /// <summary>
    /// The 20-byte session ticket is opaque binary on the wire. The retail
    /// server filled it with random bytes; our server fills it with printable
    /// "username-rand" ASCII (AccountManager.cpp StationAuthEx). Render hex so
    /// neither case is lossy, and append an ASCII gloss only when every byte is
    /// printable-or-NUL (i.e. our own server's tickets stay human-readable).
    /// </summary>
    private static string TicketDisplay(ReadOnlySpan<byte> ticket)
    {
        var sb = new StringBuilder(ticket.Length * 3);
        bool allText = true;
        for (int i = 0; i < ticket.Length; i++)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(ticket[i].ToString("x2"));
            byte b = ticket[i];
            if (b != 0 && (b < 0x20 || b >= 0x7F)) allText = false;
        }
        if (allText)
        {
            var ascii = new StringBuilder(ticket.Length);
            foreach (byte b in ticket)
                if (b != 0) ascii.Append((char)b);
            sb.Append("  \"").Append(ascii).Append('"');
        }
        return sb.ToString();
    }
}
