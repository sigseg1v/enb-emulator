// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x003E ADVANCED_POSITIONAL_UPDATE. Wire shape (variable):
///   int16 Bitmask; then 4-byte slots starting at offset 2:
///     [0] int32  GameID
///     [1] uint32 TimeStamp
///     [2..4] float Position[3]
///     [5..8] float Orientation[4]
///     [9]  uint32 MovementID
///   Conditional (per bitmask bit):
///     bit 0x0001: float  CurrentSpeed
///     bit 0x0002: float  SetSpeed
///     bit 0x0004: float  Acceleration
///     bit 0x0008: float  RotY
///     bit 0x0010: float  DesiredY
///     bit 0x0020: float  RotZ
///     bit 0x0040: float  DesiredZ
///     bit 0x0080: float  ImpartedVelocity[3], ImpartedSpin, ImpartedRoll, ImpartedPitch (6 floats)
///     bit 0x0100: uint32 UpdatePeriod
/// Total length = 2 + 4 * index (emitter formula).
/// </summary>
public sealed class AdvancedPositionalUpdateRecord : PacketRecord
{
    public AdvancedPositionalUpdateRecord(ReadOnlySpan<byte> payload) : base(0x003E, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        // Fixed header: 2 (bitmask) + 10 * 4 (10 mandatory 4B slots) = 42 bytes minimum
        if (Payload.Length < 42)
        {
            Flag(sb, $"ADVANCED_POSITIONAL_UPDATE truncated -- {Payload.Length} bytes, expected >= 42");
            return;
        }
        short bitmask = ReadI16LE(Payload, 0);

        int off = 2;
        int gameId     = ReadI32LE(Payload, off);     off += 4;
        uint timestamp = ReadU32LE(Payload, off);      off += 4;
        float px       = ReadF32LE(Payload, off);      off += 4;
        float py       = ReadF32LE(Payload, off);      off += 4;
        float pz       = ReadF32LE(Payload, off);      off += 4;
        float ox       = ReadF32LE(Payload, off);      off += 4;
        float oy       = ReadF32LE(Payload, off);      off += 4;
        float oz       = ReadF32LE(Payload, off);      off += 4;
        float ow       = ReadF32LE(Payload, off);      off += 4;
        uint movId     = ReadU32LE(Payload, off);      off += 4;

        FieldHex(sb,   "Bitmask",     (ushort)bitmask);
        FieldHex(sb,   "GameID",      gameId);
        FieldHex(sb,   "TimeStamp",   timestamp);
        Field(sb,      "Position",    $"{px:0.###}, {py:0.###}, {pz:0.###}");
        Field(sb,      "Orientation", $"{ox:0.###}, {oy:0.###}, {oz:0.###}, {ow:0.###}");
        FieldHex(sb,   "MovementID",  movId);

        if ((bitmask & 0x0001) != 0 && off + 4 <= Payload.Length)
            { FieldFloat(sb, "CurrentSpeed", ReadF32LE(Payload, off)); off += 4; }
        if ((bitmask & 0x0002) != 0 && off + 4 <= Payload.Length)
            { FieldFloat(sb, "SetSpeed", ReadF32LE(Payload, off));     off += 4; }
        if ((bitmask & 0x0004) != 0 && off + 4 <= Payload.Length)
            { FieldFloat(sb, "Acceleration", ReadF32LE(Payload, off)); off += 4; }
        if ((bitmask & 0x0008) != 0 && off + 4 <= Payload.Length)
            { FieldFloat(sb, "RotY",     ReadF32LE(Payload, off));     off += 4; }
        if ((bitmask & 0x0010) != 0 && off + 4 <= Payload.Length)
            { FieldFloat(sb, "DesiredY", ReadF32LE(Payload, off));     off += 4; }
        if ((bitmask & 0x0020) != 0 && off + 4 <= Payload.Length)
            { FieldFloat(sb, "RotZ",     ReadF32LE(Payload, off));     off += 4; }
        if ((bitmask & 0x0040) != 0 && off + 4 <= Payload.Length)
            { FieldFloat(sb, "DesiredZ", ReadF32LE(Payload, off));     off += 4; }
        if ((bitmask & 0x0080) != 0 && off + 24 <= Payload.Length)
        {
            float ivx = ReadF32LE(Payload, off);      off += 4;
            float ivy = ReadF32LE(Payload, off);      off += 4;
            float ivz = ReadF32LE(Payload, off);      off += 4;
            float spin  = ReadF32LE(Payload, off);    off += 4;
            float roll  = ReadF32LE(Payload, off);    off += 4;
            float pitch = ReadF32LE(Payload, off);    off += 4;
            Field(sb, "ImpartedVelocity", $"{ivx:0.###}, {ivy:0.###}, {ivz:0.###}");
            FieldFloat(sb, "ImpartedSpin",  spin);
            FieldFloat(sb, "ImpartedRoll",  roll);
            FieldFloat(sb, "ImpartedPitch", pitch);
        }
        if ((bitmask & 0x0100) != 0 && off + 4 <= Payload.Length)
            FieldHex(sb, "UpdatePeriod", ReadU32LE(Payload, off));
    }
}
