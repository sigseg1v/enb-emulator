// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// MANUFACTURE_ACTION (0x007E, client->server) -- the button the player presses on
/// a manufacture/refine terminal (leave, retry, refine, refine-stack). Wire (struct
/// ManufactureData, 8 bytes): int32 GameID, int32 Data. UNIFORM BIG-ENDIAN. The
/// consumer, Player::HandleManufactureAction (PlayerManufacturing.cpp:499), reads
/// the Action as ntohl(Packet-&gt;Data) -- network byte order -- and switches it on
/// the Manufacture_Action enum (PlayerManufacturing.h:21): 0 LEAVE_TERMINAL,
/// 1 RETRY, 2 REFINE, 3 REFINE_STACK. The capture proves the byte order
/// decisively: across the session's eight 0x7E frames the Action reads {0, 2, 3}
/// big-endian -- every one a valid enum member -- whereas little-endian would give
/// {0, 0x02000000, 0x03000000}, i.e. six of eight frames fall to the handler's
/// "Unknown Action" default. GameID is the manufacture-terminal id; the handler
/// never reads it, but it is the SAME terminal (10012) the same session's 0x7C
/// REFINERY_SET_ITEM carries -- and 0x7C, which reads its Data little-endian,
/// stores that 10012 byte-reversed (1C 27 00 00) where 0x7E stores it big-endian
/// (00 00 27 1C). That shared logical value across opposite encodings pins both
/// packets' byte order. Source: Player::HandleManufactureAction
/// (PlayerManufacturing.cpp:499), Manufacture_Action enum (PlayerManufacturing.h:21).
/// Pinned to capture_3.rar.
/// </summary>
public sealed class ManufactureActionRecord : PacketRecord
{
    public ManufactureActionRecord(ReadOnlySpan<byte> payload) : base(0x007E, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 8) { Flag(sb, $"MANUFACTURE_ACTION truncated -- {Payload.Length} bytes, expected 8"); return; }

        FHex(sb, 0, "GameID", ReadI32BE(Payload, 0),
            "(BE; manufacture-terminal id -- not consumed by HandleManufactureAction; same terminal as the session's 0x7C, byte-reversed there)");

        int action = ReadI32BE(Payload, 4);
        string name = action switch
        {
            0 => "ACTION_LEAVE_TERMINAL",
            1 => "ACTION_RETRY",
            2 => "ACTION_REFINE",
            3 => "ACTION_REFINE_STACK",
            _ => "unknown action -- HandleManufactureAction logs \"Unknown Action\"",
        };
        FDec(sb, 4, "Action", action, $"(BE; {name} -- HandleManufactureAction ntohl's this)");

        if (Payload.Length > 8) Flag(sb, $"MANUFACTURE_ACTION has {Payload.Length - 8} trailing bytes");
    }
}
