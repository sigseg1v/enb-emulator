// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// MISSION_DISMISSAL / MissionDismissRequest (0x0087, client-&gt;server) -- the player
/// abandons / dismisses an active mission. Wire (struct MissionDismissal, 8 bytes):
/// int32 PlayerID, int32 MissionID. UNIFORM BIG-ENDIAN. The consumer, Player::
/// HandleMissionDismissal (PlayerConnection.cpp:11013), reads BOTH fields through
/// ntohl -- ntohl(dismiss-&gt;PlayerID) and ntohl(dismiss-&gt;MissionID) -- so both
/// int32s are network byte order. The capture proves it: the frame reads PlayerID
/// 0x0000273D = 10045 and MissionID 2 big-endian -- both small sane ids -- whereas
/// little-endian would give 0x3D270000 / 0x02000000, multi-hundred-million values.
/// The struct comment notes "the 1st 2 bytes are always 0" of MissionID, consistent
/// with a small mission slot index in the low bytes of a big-endian int32. Only
/// MissionID is acted on (MissionDismiss(MissionID, false)); PlayerID is read but
/// unused. Source: struct MissionDismissal (PacketStructures.h:1001), Player::
/// HandleMissionDismissal (PlayerConnection.cpp:11013). Pinned to capture_3.rar.
/// </summary>
public sealed class MissionDismissalRecord : PacketRecord
{
    public MissionDismissalRecord(ReadOnlySpan<byte> payload) : base(0x0087, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 8) { Flag(sb, $"MISSION_DISMISSAL truncated -- {Payload.Length} bytes, expected 8"); return; }

        FHex(sb, 0, "PlayerID", ReadI32BE(Payload, 0),
            "(BE; HandleMissionDismissal ntohl's this but does not use it)");
        FDec(sb, 4, "MissionID", ReadI32BE(Payload, 4),
            "(BE; HandleMissionDismissal ntohl's this -> MissionDismiss(MissionID, false))");

        if (Payload.Length > 8) Flag(sb, $"MISSION_DISMISSAL has {Payload.Length - 8} trailing bytes");
    }
}
