// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

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
/// Source: Player::SendServerHandoff (PlayerConnection.cpp:10159),
///         struct ServerHandoff / MasterJoin (PacketStructures.h:950 / 309).
/// </summary>
public sealed class ServerHandoffRecord : PacketRecord
{
    public ServerHandoffRecord(ReadOnlySpan<byte> payload) : base(0x003A, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 64) { Flag(sb, $"SERVER_HANDOFF truncated -- {Payload.Length} bytes, expected >= 64"); return; }

        FHex(sb, 0,  "Unknown1",     ReadI32LE(Payload, 0));
        FHex(sb, 4,  "Unknown2",     ReadI32LE(Payload, 4));
        FHex(sb, 8,  "Unknown3",     ReadI32LE(Payload, 8));
        FHex(sb, 12, "AvatarIdMsb",  ReadI32LE(Payload, 12));
        FHex(sb, 16, "AvatarIdLsb",  ReadI32LE(Payload, 16));
        FDec(sb, 20, "ToSectorID",   ReadI32BE(Payload, 20), "(BE -- ntohl at emit)");
        FDec(sb, 24, "FromSectorID", ReadI32BE(Payload, 24), "(BE -- ntohl at emit)");
        FDec(sb, 28, "PlayerLevel",  ReadI32LE(Payload, 28));
        FHex(sb, 32, "Unknown8",     ReadI32LE(Payload, 32));
        FHex(sb, 36, "Unknown9",     ReadI32LE(Payload, 36));
        FHex(sb, 40, "Unknown10",    ReadI32LE(Payload, 40));
        FStr(sb, 44, 20, "Ticket",   ReadNulString(Payload.AsSpan(44, 20)));

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
}
