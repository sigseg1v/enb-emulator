// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x002F INIT_RENDER_STATE. Wire (struct InitRenderState, PacketStructures.h:872):
///   int32 GameID; uint32 RenderStateID. = 8 bytes, LE. Emitter PlayerConnection.cpp:1482.
/// </summary>
public sealed class InitRenderStateRecord : PacketRecord
{
    public InitRenderStateRecord(ReadOnlySpan<byte> payload) : base(0x002F, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 8) { Flag(sb, $"INIT_RENDER_STATE truncated -- {Payload.Length} bytes, expected 8"); return; }
        FHex(sb, 0, "GameID", ReadI32LE(Payload, 0));
        FHex(sb, 4, "RenderStateID", ReadU32LE(Payload, 4));
    }
}
