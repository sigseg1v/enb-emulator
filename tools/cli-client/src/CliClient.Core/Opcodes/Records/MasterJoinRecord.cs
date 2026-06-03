// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// MASTER_JOIN (0x0035, client->server) -- the first packet the client sends to
/// the master/global server, which causes the galaxy loading screen and is
/// answered with a ServerRedirect to the sector server. Wire (struct MasterJoin,
/// 64 bytes): 11 int32 + a 20-byte ticket. UNLIKE the sector-server raw-struct
/// packets (which are little-endian), the entire master-join handshake is in
/// NETWORK BYTE ORDER (big-endian). That is proven from three independent angles:
/// the consumer (Player::HandleLogin, which re-reads this same struct embedded in
/// the 0x02 LOGIN packet) does ntohl(ToSectorID) / ntohl(FromSectorID); the
/// producer (AccountManager builds the avatar id via avatar_id_lsb =
/// ntohl(avatar_id)); and the login-server reads every master-protocol field
/// (Major/Minor/character_slot) through ntohl. The capture corroborates it: the
/// unknown3 field increments monotonically across successive joins only when read
/// big-endian (0x40E5E7E8 < 0x40E5ECA2 < 0x40E5F57B), and the small flag fields
/// carry their value in the trailing byte (00 00 00 02 = 2). The unknown* and
/// PlayerLevel fields are decoded with the struct's own names and big-endian byte
/// order, but their semantics are not consumed server-side, so they are labelled
/// as unknown rather than guessed. Source: struct MasterJoin
/// (PacketStructures.h:309), Player::HandleLogin (PlayerConnection.cpp:684) +
/// AccountManager avatar_id build (AccountManager.cpp:590). Pinned to capture_3.rar.
/// </summary>
public sealed class MasterJoinRecord : PacketRecord
{
    public MasterJoinRecord(ReadOnlySpan<byte> payload) : base(0x0035, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 64) { Flag(sb, $"MASTER_JOIN truncated -- {Payload.Length} bytes, expected 64"); return; }

        FDec(sb, 0,  "unknown1",      ReadI32BE(Payload, 0),  "(BE; unknown -- flags/animation)");
        FDec(sb, 4,  "unknown2",      ReadI32BE(Payload, 4),  "(BE; unknown)");
        FHex(sb, 8,  "unknown3",      ReadI32BE(Payload, 8),  "(BE; unknown -- increments across joins this session)");
        FHex(sb, 12, "avatar_id_msb", ReadI32BE(Payload, 12), "(BE; high 32 bits of the 64-bit avatar id)");
        FHex(sb, 16, "avatar_id_lsb", ReadI32BE(Payload, 16), "(BE; low 32 bits of the avatar id)");
        FDec(sb, 20, "ToSectorID",    ReadI32BE(Payload, 20), "(BE; destination sector -- HandleLogin ntohl's this)");
        int from = ReadI32BE(Payload, 24);
        FDec(sb, 24, "FromSectorID",  from, from == 0 ? "(BE; origin sector, 0 = fresh login)" : "(BE; origin sector)");
        FDec(sb, 28, "PlayerLevel",   ReadI32BE(Payload, 28), "(BE; PlayerLevel per the struct; not consumed server-side)");
        FDec(sb, 32, "unknown8",      ReadI32BE(Payload, 32), "(BE; unknown)");
        FDec(sb, 36, "unknown9",      ReadI32BE(Payload, 36), "(BE; unknown)");
        FHex(sb, 40, "unknown10",     ReadI32BE(Payload, 40), "(BE; unknown)");

        var ticket = new StringBuilder();
        for (int i = 0; i < 20; i++) { if (i > 0) ticket.Append(' '); ticket.Append(Payload[44 + i].ToString("X2")); }
        FBytes(sb, 44, 20, "Ticket", ticket.ToString(), "(20-byte session ticket)");
    }
}
