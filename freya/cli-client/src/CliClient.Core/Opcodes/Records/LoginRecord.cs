// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// LOGIN (0x0002, client->server) -- the sector-server login packet. Wire
/// (struct Login, 137 bytes): a 64-byte MasterJoin embedded verbatim at offset 0,
/// then int32 TimeSent, a 65-byte LoginData (40 unknown + an 18-byte local-time
/// string + 7 unknown), then int32 TimeReceived. This packet is MIXED-ENDIAN, and
/// the split is proven by the consumer, Player::HandleLogin
/// (PlayerConnection.cpp:674): it copies m_MasterJoin = login-&gt;join_data and then
/// reads that sub-struct in NETWORK byte order (sector_id = ntohl(ToSectorID),
/// m_FromSectorID = ntohl(FromSectorID)), but reads the LOGIN-appended TimeSent
/// directly (m_JoinTime = login-&gt;TimeSent) with NO ntohl -- i.e. little-endian.
/// So bytes 0..63 are big-endian (delegated to MasterJoinRecord, which documents
/// that handshake's byte order three ways) and the appended fields are
/// little-endian. The capture corroborates the split: the embedded MasterJoin is
/// byte-identical to the same session's 0x35 MASTER_JOIN (ToSectorID 10521,
/// avatar id 0x3E221201:0xF7645CC0), and TimeSent reads as a small positive tick
/// (77768) only little-endian. login_data.timestamp is the client's local-time
/// string ("07/02/04 22:54:30"). The unknown40/unknown7 blocks and TimeReceived
/// are part of LoginData/Login but HandleLogin never reads them, so they are shown
/// as raw bytes / labelled little-endian-by-convention rather than guessed.
/// </summary>
public sealed class LoginRecord : PacketRecord
{
    public LoginRecord(ReadOnlySpan<byte> payload) : base(0x0002, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 137) { Flag(sb, $"LOGIN truncated -- {Payload.Length} bytes, expected 137"); return; }

        // bytes 0..63: embedded MasterJoin handshake, NETWORK byte order. Reuse the
        // 0x35 decoder rather than duplicating the big-endian layout; its printed
        // offsets are LOGIN-absolute because the struct sits at LOGIN offset 0. The
        // delegate marks its own coverage map, so Mark the region here ourselves.
        new MasterJoinRecord(Payload.AsSpan(0, 64)).AppendFieldsTo(sb);
        Mark(0, 64);

        // LOGIN-appended fields are little-endian (HandleLogin reads TimeSent with no ntohl).
        int timeSent = ReadI32LE(Payload, 64);
        FDec(sb, 64, "TimeSent", timeSent, "(LE; client tick -- HandleLogin stores this as m_JoinTime, no ntohl)");

        var u40 = new StringBuilder();
        for (int i = 0; i < 40; i++) { if (i > 0) u40.Append(' '); u40.Append(Payload[68 + i].ToString("X2")); }
        FBytes(sb, 68, 40, "LoginData.unknown40", u40.ToString(), "(40 bytes; not read by HandleLogin)");

        string timestamp = ReadNulString(Payload.AsSpan(108, 18));
        FStr(sb, 108, 18, "LoginData.timestamp", timestamp);

        var u7 = new StringBuilder();
        for (int i = 0; i < 7; i++) { if (i > 0) u7.Append(' '); u7.Append(Payload[126 + i].ToString("X2")); }
        FBytes(sb, 126, 7, "LoginData.unknown7", u7.ToString(), "(7 bytes; not read by HandleLogin)");

        int timeReceived = ReadI32LE(Payload, 133);
        FDec(sb, 133, "TimeReceived", timeReceived, "(LE; Login.TimeReceived -- not read by HandleLogin)");

        if (Payload.Length > 137) Flag(sb, $"LOGIN has {Payload.Length - 137} trailing bytes");
    }
}
