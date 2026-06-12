// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x002C ACTION. Wire (struct ActionPacket, 16 bytes, LE, no byte-swap):
///   int32 GameID; int32 Action; int32 Target; int32 OptionalVar.
/// Client->server "perform Action against Target". The server reads every field
/// raw host-order in Player::HandleAction (switch(myAction->Action),
/// GetObjectFromID(myAction->Target) -- no ntohl), so the wire is LE. Action codes
/// observed in HandleAction: 1=tractor, plus dock/loot/etc. handled downstream.
/// (The string-carrying ActionPacket2 form is variable-length and arrives via a
/// different path; the fixed 16-byte body here is the plain ACTION.)
/// Source: struct ActionPacket (PacketStructures.h), Player::HandleAction
/// (PlayerConnection.cpp). Pinned to capture_3.rar Packet 1663 (Client->Server).
/// </summary>
public sealed class ActionRecord : PacketRecord
{
    public ActionRecord(ReadOnlySpan<byte> payload) : base(0x002C, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 16) { Flag(sb, $"ACTION truncated -- {Payload.Length} bytes, expected 16"); return; }
        FHex(sb, 0,  "GameID",      ReadI32LE(Payload, 0));
        FDec(sb, 4,  "Action",      ReadI32LE(Payload, 4));
        int target = ReadI32LE(Payload, 8);
        FHex(sb, 8,  "Target",      target, target == -1 ? "(no target)" : null);
        FDec(sb, 12, "OptionalVar", ReadI32LE(Payload, 12));
    }
}
