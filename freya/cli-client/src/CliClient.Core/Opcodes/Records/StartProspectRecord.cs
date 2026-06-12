// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x2012 START_PROSPECT. Compact control packet the server emits to every
/// range-list member when a player begins prospecting/mining an asteroid. The
/// client never sees this opcode -- the proxy EXPANDS it into a 0x000B
/// OBJECT_TO_OBJECT_EFFECT beam (see proxy UDPClient::StartProspecting and
/// plans/27 §3a). Decoding it here lets the Phase-T mining test byte-pin both
/// the compact source and the fabricated 0x000B against each other.
///
/// Fixed 20-byte layout, from the emitter Player::MineResource
/// (server/src/PlayerSkills.cpp:691-697 -- all five fields AddData'd as 4 wire
/// bytes; AddData&lt;long&gt; is the int32-LE specialization):
///   int32  PlayerGID    @0   GameID()        -- beam source
///   int32  AsteroidGID  @4   obj-&gt;GameID()    -- beam target
///   int32  EffectUID    @8   effect_UID
///   uint32 ProspectTick @12  prospect_tick   (GetNet7TickCount)
///   uint32 DrainMs      @16  drain_effect_time (ms the beam runs)
/// </summary>
public sealed class StartProspectRecord : PacketRecord
{
    public StartProspectRecord(ReadOnlySpan<byte> payload) : base(0x2012, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 20) { Flag(sb, $"START_PROSPECT truncated -- {Payload.Length} bytes, expected 20"); return; }

        FHex(sb,  0, "PlayerGID",    ReadI32LE(Payload, 0));
        FHex(sb,  4, "AsteroidGID",  ReadI32LE(Payload, 4));
        FHex(sb,  8, "EffectUID",    ReadI32LE(Payload, 8));
        FHex(sb, 12, "ProspectTick", ReadU32LE(Payload, 12));
        FDec(sb, 16, "DrainMs",      (int)ReadU32LE(Payload, 16));
    }
}
