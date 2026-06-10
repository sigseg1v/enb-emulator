// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0030 ACTIVATE_RENDER_STATE / 0x0031 ACTIVATE_NEXT_RENDER_STATE. Wire (8 bytes):
///   int32 GameID; uint32 RenderStateID.
/// </summary>
public sealed class ActivateRenderStateRecord : PacketRecord
{
    private readonly ushort _opcode;

    public ActivateRenderStateRecord(ReadOnlySpan<byte> payload, ushort opcode)
        : base(opcode, payload)
    {
        _opcode = opcode;
    }

    protected override void WriteFields(StringBuilder sb)
    {
        string label = _opcode == 0x0031 ? "ACTIVATE_NEXT_RENDER_STATE" : "ACTIVATE_RENDER_STATE";
        if (Payload.Length < 8) { Flag(sb, $"{label} truncated -- {Payload.Length} bytes, expected 8"); return; }
        int    gameId        = ReadI32LE(Payload, 0);
        uint   renderStateId = ReadU32LE(Payload, 4);
        FHex(sb, 0, "GameID",        gameId);
        FHex(sb, 4, "RenderStateID", (int)renderStateId);
    }
}
