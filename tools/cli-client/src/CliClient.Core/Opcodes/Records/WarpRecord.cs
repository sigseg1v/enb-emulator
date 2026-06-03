// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x009B WARP (client->server). Wire (struct WarpPacket, VARIABLE length,
/// LITTLE-ENDIAN): int32 GameID; short Navs; int32 TargetID[Navs].
/// The player asks to warp through a route of nav points. <c>Navs</c> is the
/// count of valid entries in the route; only that many <c>TargetID</c> int32s
/// follow, so the on-wire payload is 6 + 4*Navs bytes (the struct's TargetID[20]
/// is just the maximum the server will read). Every field is little-endian:
/// Player::HandleWarp casts the buffer straight to WarpPacket* and reads
/// warp->GameID / warp->Navs / warp->TargetID without any ntohl, and
/// SetupWarpNavs copies exactly Navs entries -- so the client sends raw host
/// order. The server masks GameID with 0x00FFFFFF when logging, i.e. the low 24
/// bits are the object id and the top byte is a marker; the hex view below
/// shows all four bytes.
/// Source: struct WarpPacket (PacketStructures.h), Player::HandleWarp +
/// Player::SetupWarpNavs (PlayerConnection.cpp / PlayerClass.cpp). Pinned to
/// capture_3.rar (Client->Server).
/// </summary>
public sealed class WarpRecord : PacketRecord
{
    private const int MaxNavs = 20;

    public WarpRecord(ReadOnlySpan<byte> payload) : base(0x009B, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 6) { Flag(sb, $"WARP truncated -- {Payload.Length} bytes, expected at least 6 (GameID + Navs)"); return; }

        FHex(sb, 0, "GameID", ReadI32LE(Payload, 0), "(LE; low 24 bits = object id)");
        short navs = ReadI16LE(Payload, 4);
        FDec(sb, 4, "Navs", navs, "(LE; count of TargetID route entries)");

        if (navs < 0 || navs > MaxNavs)
        {
            Flag(sb, $"Navs = {navs} outside [0,{MaxNavs}] -- server reads at most {MaxNavs} entries");
            return;
        }

        int expected = 6 + 4 * navs;
        int have = Math.Min(navs, (Payload.Length - 6) / 4);
        if (Payload.Length < expected)
            Flag(sb, $"WARP truncated -- {Payload.Length} bytes, expected {expected} for {navs} navs; decoding {have}");

        for (int i = 0; i < have; i++)
        {
            int off = 6 + 4 * i;
            FHex(sb, off, $"TargetID[{i}]", ReadI32LE(Payload, off), "(LE; nav route waypoint)");
        }
    }
}
