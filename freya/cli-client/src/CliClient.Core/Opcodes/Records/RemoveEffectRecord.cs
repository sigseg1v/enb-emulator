// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x000F REMOVE_EFFECT. Wire: a single int32 LE EffectID (the server-assigned
/// effect id to remove). Source: Player::SendRemoveEffect (PlayerConnection.cpp).
/// </summary>
public sealed class RemoveEffectRecord : PacketRecord
{
    public RemoveEffectRecord(ReadOnlySpan<byte> payload) : base(0x000F, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 4) { Flag(sb, $"REMOVE_EFFECT truncated -- {Payload.Length} bytes, expected 4"); return; }
        FHex(sb, 0, "EffectID", ReadI32LE(Payload, 0));
    }
}
