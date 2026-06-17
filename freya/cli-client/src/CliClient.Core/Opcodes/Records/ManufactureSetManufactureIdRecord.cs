// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x007F MANUFACTURE_SET_MANUFACTURE_ID. Wire: int32 mfg_id (4 bytes).
/// Unlike every other GameID field on the wire (which is host little-endian),
/// this opcode's field is emitted BYTE-REVERSED -- the network-order encoding
/// of the host GameID. capture_1 carries the manufacture-lab anchor as the same
/// GameID two ways in one stream: the lab object CREATE and its AuxData carry it
/// host-LE (`F7 13 EE 06`, line 4604/4626), while the 0x007F payload carries it
/// reversed (`06 EE 13 F7`, line 3769). The two are byte-for-byte mirrors, so
/// reading 0x007F big-endian recovers the identical host GameID the CREATE and
/// AuxData carry. A SetManufactureID(0) reset clears it.
///
/// <para>
/// The emitter (Player::SetManufactureID, PlayerConnection.cpp) byte-swaps the
/// host GameID before sending and writes exactly sizeof(int32_t) bytes (4) to
/// avoid the LP64 sizeof(long)=8 drift. Reading the field LE here would yield
/// the byte-reversed value and fail to match the lab object's CREATE GameID;
/// the client resolves the manufacture session by this id, so a mismatched id
/// leaves the analyze terminal unresolved.
/// </para>
/// </summary>
public sealed class ManufactureSetManufactureIdRecord : PacketRecord
{
    public ManufactureSetManufactureIdRecord(ReadOnlySpan<byte> payload) : base(0x007F, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 4) { Flag(sb, $"MANUFACTURE_SET_MANUFACTURE_ID truncated -- {Payload.Length} bytes, expected 4"); return; }
        // Field is network-order (byte-reversed vs host-LE GameIDs); read BE to
        // recover the host GameID the manufacture-lab CREATE/AuxData carry.
        int mfgId = ReadI32BE(Payload, 0);
        FHex(sb, 0, "ManufactureID", mfgId);
    }
}
