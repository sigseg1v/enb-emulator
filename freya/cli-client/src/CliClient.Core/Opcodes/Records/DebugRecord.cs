// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// DEBUG (0x001A, client->server). 12-byte body: an object GameID followed by two
/// 4-byte words. The server's Player::HandleDebug (PlayerConnection.cpp:10773) is a
/// no-op -- it only LogDebug's "Received Debug packet" and never parses the body --
/// and there is no Debug struct in PacketStructures.h, so the field layout cannot be
/// proven from a parser. Only the leading GameID is ground-truthed, and strongly:
/// in the capture, the 0x1A frame from session :3029 (packet #543)
/// carries GameID bytes EE CC AA 00, the identical value the SAME session's
/// StarbaseRoomChange 0x9F frames (#553/#611/#1225/#1242) carry as their AvatarID --
/// and 0x9F's byte order is proven little-endian (Player::HandleStarbaseRoomChange
/// reads it with no ntohl). The same player id appearing in a proven-LE packet pins
/// the Debug GameID to little-endian (0x00AACCEE). The two trailing words are
/// constant across every captured frame (0x00000021 and 0x00000000); they are shown
/// little-endian by convention but flagged unverified, since the server discards
/// them. Source: Player::HandleDebug (PlayerConnection.cpp:10773); GameID byte order
/// cross-proven against StarbaseRoomChange 0x9F. Pinned to capture_3.rar.
/// </summary>
public sealed class DebugRecord : PacketRecord
{
    public DebugRecord(ReadOnlySpan<byte> payload) : base(0x001A, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 12) { Flag(sb, $"DEBUG truncated -- {Payload.Length} bytes, expected 12"); return; }

        FHex(sb, 0, "GameID", ReadI32LE(Payload, 0), "(LE; object id -- cross-proven LE via this session's StarbaseRoomChange)");
        FDec(sb, 4, "Unknown4", ReadI32LE(Payload, 4), "(LE by convention; HandleDebug discards the body -- unverified, constant 0x21 in capture)");
        FDec(sb, 8, "Unknown8", ReadI32LE(Payload, 8), "(LE by convention; unverified -- 0 in every captured frame)");
    }
}
