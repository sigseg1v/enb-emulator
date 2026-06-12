// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x007F MANUFACTURE_SET_MANUFACTURE_ID. Wire: int32 mfg_id (4 bytes, LE).
/// Emitter (Player::SetManufactureID, PlayerConnection.cpp:10137) memcpy's the
/// raw 4 bytes; sends sizeof(int32_t) explicitly to avoid LP64 sizeof(long)=8.
/// For the manufacturing-lab anchor the value is GameID | MANU_TAG | PLAYER_TAG,
/// so the LE int32 has bit 31 (MANU_TAG) and bit 30 (PLAYER_TAG) set -- a large
/// negative int32 / top byte >= 0xC0. capture_1 frame #351 payload `06 EE 13 F7`
/// LE-decodes 0xF713EE06 (top byte 0xF7 = both tag bits set); a BE read would
/// give 0x06EE13F7 with no tag bits, an invalid manu-id -- this is the byte-order
/// pin. A SetManufactureID(0) reset (frame #640) clears it. Source: the
/// SectorManager StationLogin ntohl-trap fix (SectorManager.cpp:490).
/// </summary>
public sealed class ManufactureSetManufactureIdRecord : PacketRecord
{
    public ManufactureSetManufactureIdRecord(ReadOnlySpan<byte> payload) : base(0x007F, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 4) { Flag(sb, $"MANUFACTURE_SET_MANUFACTURE_ID truncated -- {Payload.Length} bytes, expected 4"); return; }
        int mfgId = ReadI32LE(Payload, 0);
        FHex(sb, 0, "ManufactureID", mfgId);
    }
}
