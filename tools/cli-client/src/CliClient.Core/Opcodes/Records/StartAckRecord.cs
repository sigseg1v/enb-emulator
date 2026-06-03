// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// START_ACK (0x0006, client->server). The client's reply to the server's 0x05
/// START packet: it acknowledges that it has loaded its in-sector avatar and is
/// ready, which flips the player Active (Player::HandleStartAck calls
/// SetActive(true) and sends the login camera). Wire: a single int32 StartID,
/// 4 bytes, little-endian -- the same sector-assigned avatar id the server put in
/// the 0x05 START packet, echoed back verbatim. Proven by round-trip in three
/// independent capture sessions: .44:3029 server START 1E 14 00 00 (#384) ->
/// client START_ACK 1E 14 00 00 (#543); .38:3034 3589 (#1419/#1444 -> #1461);
/// .38:3434 3093 (#12945/#12979 -> #12988). The byte order matches StartRecord
/// (0x05), which already documents StartID as a little-endian sector-assigned id.
/// HandleStartAck itself discards the payload (it only toggles Active), so the
/// field meaning rests on the 0x05<->0x06 round-trip, not on a server-side read.
/// Source: Player::HandleStartAck (PlayerConnection.cpp:1613), StartRecord (0x05).
/// Pinned to capture_3.rar.
/// </summary>
public sealed class StartAckRecord : PacketRecord
{
    public StartAckRecord(ReadOnlySpan<byte> payload) : base(0x0006, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 4) { Flag(sb, $"START_ACK truncated -- {Payload.Length} bytes, expected 4"); return; }
        int startId = ReadI32LE(Payload, 0);
        FHex(sb, 0, "StartID", startId, "(LE; echoes the 0x05 START id -- the client's sector-assigned avatar)");
        FlagSuspicious(sb, "StartID", startId);
        if (Payload.Length > 4) Flag(sb, $"START_ACK has {Payload.Length - 4} trailing bytes");
    }
}
