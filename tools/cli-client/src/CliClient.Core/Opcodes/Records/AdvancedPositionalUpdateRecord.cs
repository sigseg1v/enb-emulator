// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x003E ADVANCED_POSITIONAL_UPDATE. Wire (variable): int16 Bitmask; then 4-byte slots at offset 2:
///   [0] int32 GameID; [1] uint32 TimeStamp; [2-4] float Pos[3]; [5-8] float Orient[4]; [9] uint32 MovementID.
/// Conditional per bitmask bit: 0x0001 CurrentSpeed; 0x0002 SetSpeed; 0x0004 Acceleration;
///   0x0008 RotY; 0x0010 DesiredY; 0x0020 RotZ; 0x0040 DesiredZ;
///   0x0080 ImpartedVelocity[3]+Spin+Roll+Pitch (6 floats); 0x0100 UpdatePeriod.
/// Min 42 bytes (10 mandatory slots).
/// </summary>
public sealed class AdvancedPositionalUpdateRecord : PacketRecord
{
    public AdvancedPositionalUpdateRecord(ReadOnlySpan<byte> payload) : base(0x003E, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 42) { Flag(sb, $"ADVANCED_POSITIONAL_UPDATE truncated -- {'{'}Payload.Length{'}'} bytes, expected >= 42"); return; }
        short bitmask = ReadI16LE(Payload, 0);
        FHex(sb, 0, "Bitmask", (ushort)bitmask);
        int off = 2;
        int gameId     = ReadI32LE(Payload, off); FHex(sb, off, "GameID",      gameId);  off += 4;
        uint timestamp = ReadU32LE(Payload, off); FHex(sb, off, "TimeStamp",   timestamp); off += 4;
        float px = ReadF32LE(Payload, off), py = ReadF32LE(Payload, off+4), pz = ReadF32LE(Payload, off+8);
        FBytes(sb, off, 12, "Position", $"{px:0.###}, {py:0.###}, {pz:0.###}"); off += 12;
        float ox = ReadF32LE(Payload, off), oy = ReadF32LE(Payload, off+4), oz = ReadF32LE(Payload, off+8), ow = ReadF32LE(Payload, off+12);
        FBytes(sb, off, 16, "Orientation", $"{ox:0.###}, {oy:0.###}, {oz:0.###}, {ow:0.###}"); off += 16;
        uint movId = ReadU32LE(Payload, off); FHex(sb, off, "MovementID", movId); off += 4;
        if ((bitmask & 0x0001) != 0 && off + 4 <= Payload.Length) { FFloat(sb, off, "CurrentSpeed", ReadF32LE(Payload, off)); off += 4; }
        if ((bitmask & 0x0002) != 0 && off + 4 <= Payload.Length) { FFloat(sb, off, "SetSpeed",     ReadF32LE(Payload, off)); off += 4; }
        if ((bitmask & 0x0004) != 0 && off + 4 <= Payload.Length) { FFloat(sb, off, "Acceleration", ReadF32LE(Payload, off)); off += 4; }
        if ((bitmask & 0x0008) != 0 && off + 4 <= Payload.Length) { FFloat(sb, off, "RotY",         ReadF32LE(Payload, off)); off += 4; }
        if ((bitmask & 0x0010) != 0 && off + 4 <= Payload.Length) { FFloat(sb, off, "DesiredY",     ReadF32LE(Payload, off)); off += 4; }
        if ((bitmask & 0x0020) != 0 && off + 4 <= Payload.Length) { FFloat(sb, off, "RotZ",         ReadF32LE(Payload, off)); off += 4; }
        if ((bitmask & 0x0040) != 0 && off + 4 <= Payload.Length) { FFloat(sb, off, "DesiredZ",     ReadF32LE(Payload, off)); off += 4; }
        if ((bitmask & 0x0080) != 0 && off + 24 <= Payload.Length)
        {
            float ivx = ReadF32LE(Payload, off), ivy = ReadF32LE(Payload, off+4), ivz = ReadF32LE(Payload, off+8);
            float spin = ReadF32LE(Payload, off+12), roll = ReadF32LE(Payload, off+16), pitch = ReadF32LE(Payload, off+20);
            FBytes(sb, off, 12, "ImpartedVelocity", $"{ivx:0.###}, {ivy:0.###}, {ivz:0.###}"); off += 12;
            FFloat(sb, off, "ImpartedSpin",  spin); off += 4;
            FFloat(sb, off, "ImpartedRoll",  roll); off += 4;
            FFloat(sb, off, "ImpartedPitch", pitch); off += 4;
        }
        if ((bitmask & 0x0100) != 0 && off + 4 <= Payload.Length)
            FHex(sb, off, "UpdatePeriod", ReadU32LE(Payload, off));
    }
}
