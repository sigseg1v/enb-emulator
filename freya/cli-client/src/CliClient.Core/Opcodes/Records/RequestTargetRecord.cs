// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0017 REQUEST_TARGET. Wire (struct RequestTarget, 8 bytes, LE, no byte-swap):
///   int32 GameID; int32 TargetID.
/// Client->server "select this object as my target"; identical layout to 0x0019
/// SET_TARGET. The server reads TargetID raw host-order (GetObjectFromID(request->
/// TargetID) in Player::HandleRequestTarget, no ntohl), so the wire is LE.
/// Source: struct RequestTarget (PacketStructures.h), Player::HandleRequestTarget
/// (PlayerConnection.cpp). Pinned to capture_3.rar Packet 1475 (Client->Server).
/// </summary>
public sealed class RequestTargetRecord : PacketRecord
{
    public RequestTargetRecord(ReadOnlySpan<byte> payload) : base(0x0017, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 8) { Flag(sb, $"REQUEST_TARGET truncated -- {Payload.Length} bytes, expected 8"); return; }
        FHex(sb, 0, "GameID",   ReadI32LE(Payload, 0));
        int target = ReadI32LE(Payload, 4);
        FHex(sb, 4, "TargetID", target, target == -1 ? "(no target)" : null);
    }
}
