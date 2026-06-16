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
/// GetObjectFromID(myAction->Target) -- no ntohl), so the wire is LE. This is the
/// group/formation lifecycle and object-action carrier; the Action selector is the
/// GroupAction code (see <see cref="DescribeAction"/>).
/// (The string-carrying ActionPacket2 form is variable-length and arrives via a
/// different path; the fixed 16-byte body here is the plain ACTION.)
/// </summary>
public sealed class ActionRecord : PacketRecord
{
    public ActionRecord(ReadOnlySpan<byte> payload) : base(0x002C, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 16) { Flag(sb, $"ACTION truncated -- {Payload.Length} bytes, expected 16"); return; }
        FHex(sb, 0, "GameID", ReadI32LE(Payload, 0));
        int action = ReadI32LE(Payload, 4);
        FDec(sb, 4, "Action", action, $"({DescribeAction(action)})");
        int target = ReadI32LE(Payload, 8);
        FHex(sb, 8, "Target", target, target == -1 ? "(no target)" : null);
        FDec(sb, 12, "OptionalVar", ReadI32LE(Payload, 12));
    }

    /// <summary>
    /// 0x002C Action selector codes (group/formation lifecycle + object actions),
    /// observed in Player::HandleAction.
    /// </summary>
    internal static string DescribeAction(int action) => action switch
    {
        1 => "tractor",
        7 => "docking-complete",
        8 => "land",
        10 => "invite",
        11 => "accept",
        12 => "decline",
        13 => "disband",
        14 => "leave",
        15 => "kick",
        16 => "LFG-list",
        17 => "mine",
        18 => "gate",
        19 => "finish-gate",
        28 => "dock",
        29 => "planet-land",
        30 => "scan",
        _ => "action",
    };
}
