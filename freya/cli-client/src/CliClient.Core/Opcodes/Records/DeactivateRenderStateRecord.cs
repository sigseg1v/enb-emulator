// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0032 DEACTIVATE_RENDER_STATE. Wire: bare int32 GameID. = 4 bytes, LE.
/// Emitter PlayerConnection.cpp:1500 (sends &game_id, sizeof(game_id)).
/// </summary>
public sealed class DeactivateRenderStateRecord : PacketRecord
{
    public DeactivateRenderStateRecord(ReadOnlySpan<byte> payload) : base(0x0032, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 4) { Flag(sb, $"DEACTIVATE_RENDER_STATE truncated -- {Payload.Length} bytes, expected 4"); return; }
        FHex(sb, 0, "GameID", ReadI32LE(Payload, 0));
    }
}
