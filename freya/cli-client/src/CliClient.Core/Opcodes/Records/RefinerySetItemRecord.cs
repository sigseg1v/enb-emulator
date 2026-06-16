// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// REFINERY_SET_ITEM_ID (0x007C, client->server). Sent when the player picks the
/// item to refine in a refinery terminal. Wire (struct ManufactureData, 8 bytes):
/// int32 GameID; int32 Data. Data is the item template id being selected --
/// Player::HandleRefineSetItem (PlayerManufacturing.cpp:442) casts the buffer to
/// ManufactureData* and reads Data little-endian (long Item = Packet->Data), then
/// hedges: if that value exceeds 0xFFFF it re-reads it as ntohl(Packet->Data).
/// Real item template ids fit under 0xFFFF (the capture shows 1237/1239), so the
/// little-endian read is the one that stands and the byte-swap branch never fires.
/// The leading GameID is the manufacturing/refinery context the client populated;
/// HandleRefineSetItem ignores it entirely (it only consumes Data), so its order
/// is unverified by the parser -- decoded LE by the packed-struct convention,
/// which is the only reading that yields a sane id (BE would give ~471M).
/// </summary>
public sealed class RefinerySetItemRecord : PacketRecord
{
    public RefinerySetItemRecord(ReadOnlySpan<byte> payload) : base(0x007C, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 8) { Flag(sb, $"REFINERY_SET_ITEM_ID truncated -- {Payload.Length} bytes, expected 8"); return; }

        FHex(sb, 0, "GameID", ReadI32LE(Payload, 0), "(LE; refinery context, not consumed by the handler)");
        int data = ReadI32LE(Payload, 4);
        string note = data > 0xFFFF
            ? "(item template id; > 0xFFFF so the server re-reads it big-endian)"
            : "(LE; item template id to refine)";
        FDec(sb, 4, "Data", data, note);
    }
}
