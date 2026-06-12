// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// MANUFACTURE_ITEM_CATAGORY (0x0079, client-&gt;server) -- the player selects a
/// terminal mode at a manufacture/refine station. Despite the upstream opcode name,
/// the dispatch routes 0x79 to Player::HandleManufactureTerminal
/// (PlayerConnection.cpp:519 -&gt; PlayerManufacturing.cpp:25), which treats the packet
/// as a terminal-mode selector, NOT a category id. Wire (struct ManufactureData,
/// 8 bytes): int32 GameID, int32 Data. UNIFORM BIG-ENDIAN. The handler reads the
/// mode as ntohl(Packet-&gt;Data) -- network byte order -- and switches it: 0 exit /
/// reset-on-leave, 1 MODE_MANUFACTURE, 2 MODE_ANALIZE, 4 MODE_REFINE, anything else
/// logs "Unknown Terminal". The capture proves the byte order: the two 0x79 frames
/// read Data {4, 0} big-endian (REFINE, exit) -- both valid -- whereas little-endian
/// would give {0x04000000, 0} and the first frame would fall to "Unknown Terminal".
/// GameID is the manufacture-terminal id; the handler never reads it, but it is the
/// SAME terminal (10012, bytes 00 00 27 1C big-endian) the same session's 0x7E
/// MANUFACTURE_ACTION carries, which shares this exact struct and byte order. Source:
/// struct ManufactureData (PacketStructures.h:1062), Player::HandleManufactureTerminal
/// (PlayerManufacturing.cpp:25). Pinned to capture_3.rar.
/// </summary>
public sealed class ManufactureItemCategoryRecord : PacketRecord
{
    public ManufactureItemCategoryRecord(ReadOnlySpan<byte> payload) : base(0x0079, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 8) { Flag(sb, $"MANUFACTURE_ITEM_CATAGORY truncated -- {Payload.Length} bytes, expected 8"); return; }

        FHex(sb, 0, "GameID", ReadI32BE(Payload, 0),
            "(BE; manufacture-terminal id -- not consumed by HandleManufactureTerminal; same terminal as the session's 0x7E)");

        int mode = ReadI32BE(Payload, 4);
        string name = mode switch
        {
            0 => "exit / reset-on-leave",
            1 => "MODE_MANUFACTURE",
            2 => "MODE_ANALIZE",
            4 => "MODE_REFINE",
            _ => "unknown -- HandleManufactureTerminal logs \"Unknown Terminal\"",
        };
        FDec(sb, 4, "Terminal", mode, $"(BE; {name} -- HandleManufactureTerminal ntohl's this)");

        if (Payload.Length > 8) Flag(sb, $"MANUFACTURE_ITEM_CATAGORY has {Payload.Length - 8} trailing bytes");
    }
}
