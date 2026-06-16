// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0046 COMPONENT_POSITIONAL_UPDATE. Wire (struct ComponentPositionalUpdate,
/// 64 bytes, LE, no byte-swap): an embedded SimplePositionalUpdate (48 bytes --
/// same layout as 0x08) followed by a four-field tractor-beam tail:
///   SimplePositionalUpdate simple;   // GameID, TimeStamp, Pos[3], Orient[4], Vel[3]
///   float ImpartedDecay;             // @48
///   float TractorSpeed;              // @52
///   int32 TractorID;                 // @56  (the object pulling/pulled)
///   int32 TractorEffectID;           // @60
/// Player::SendComponentPositionalUpdate direct-assigns every field then
/// SendOpcode(..., &amp;update, sizeof(update)) memcpys the packed struct, so the
/// whole thing is host-order LE on the wire. NB: the PacketStructures.h tail
/// comments ("this[68]" ...) are stale/off-by-8 -- the ATTRIB_PACKED struct is
/// contiguous, so the tail starts at payload offset 48 (proven by the retail
/// frame's "Length = 68 bytes" == payload 64 + 4).
/// </summary>
public sealed class ComponentPositionalUpdateRecord : PacketRecord
{
    public ComponentPositionalUpdateRecord(ReadOnlySpan<byte> payload) : base(0x0046, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 64) { Flag(sb, $"COMPONENT_POS truncated -- {Payload.Length} bytes, expected 64"); return; }

        // Embedded SimplePositionalUpdate (offsets 0..47) -- identical to 0x08.
        int   gameId = ReadI32LE(Payload, 0);
        uint  ts     = ReadU32LE(Payload, 4);
        float px = ReadF32LE(Payload,  8), py = ReadF32LE(Payload, 12), pz = ReadF32LE(Payload, 16);
        float ow = ReadF32LE(Payload, 20), ox = ReadF32LE(Payload, 24), oy = ReadF32LE(Payload, 28), oz = ReadF32LE(Payload, 32);
        float vx = ReadF32LE(Payload, 36), vy = ReadF32LE(Payload, 40), vz = ReadF32LE(Payload, 44);
        FHex(sb,   0, "GameID",      gameId);
        FHex(sb,   4, "TimeStamp",   ts);
        FBytes(sb, 8,  12, "Position",    $"({px:0.0##}, {py:0.0##}, {pz:0.0##})");
        FBytes(sb, 20, 16, "Orientation", $"({ow:0.0##}, {ox:0.0##}, {oy:0.0##}, {oz:0.0##})");
        FBytes(sb, 36, 12, "Velocity",    $"({vx:0.0##}, {vy:0.0##}, {vz:0.0##})");

        // Tractor-beam tail (offsets 48..63).
        FFloat(sb, 48, "ImpartedDecay", ReadF32LE(Payload, 48));
        FFloat(sb, 52, "TractorSpeed",  ReadF32LE(Payload, 52));
        FHex(sb,   56, "TractorID",       ReadI32LE(Payload, 56), "(object the beam acts on)");
        FHex(sb,   60, "TractorEffectID", ReadI32LE(Payload, 60));
    }
}
