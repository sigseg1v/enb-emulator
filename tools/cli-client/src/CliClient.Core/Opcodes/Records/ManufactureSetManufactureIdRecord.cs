// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x007F MANUFACTURE_SET_MANUFACTURE_ID. Wire: int32 mfg_id (4 bytes, LE).
/// Emitter sends sizeof(int32_t) explicitly to avoid LP64 sizeof(long)=8.
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
