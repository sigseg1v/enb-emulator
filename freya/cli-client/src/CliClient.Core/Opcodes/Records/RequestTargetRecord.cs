// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0017 REQUEST_TARGET. Wire (struct RequestTarget, 8 bytes, LE, no byte-swap):
///   int32 GameID; int32 TargetID.
/// Client->server "select this object as my target"; identical layout to 0x0019
/// SET_TARGET. The server reads TargetID raw host-order (GetObjectFromID(request->
/// TargetID) in Player::HandleRequestTarget, no ntohl), so the wire is LE.
/// </summary>
public sealed class RequestTargetRecord : PacketRecord
{
    public RequestTargetRecord(ReadOnlySpan<byte> payload) : base(0x0017, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 8) { Flag(sb, $"REQUEST_TARGET truncated -- {Payload.Length} bytes, expected 8"); return; }
        FHex(sb, 0, "GameID", ReadI32LE(Payload, 0));
        int target = ReadI32LE(Payload, 4);
        FHex(sb, 4, "TargetID", target, target == -1 ? "(no target)" : null);
    }
}
