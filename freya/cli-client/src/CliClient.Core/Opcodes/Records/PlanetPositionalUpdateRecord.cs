// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x003F PLANET_POSITIONAL_UPDATE. Wire (48 bytes, PACKED):
///   int32 GameID; uint32 TimeStamp;
///   float X; float Y; float Z;
///   int32 OrbitID; float OrbitDist; float OrbitAngle; float OrbitRate;
///   float RotateAngle; float RotateRate; float TiltAngle.
/// </summary>
public sealed class PlanetPositionalUpdateRecord : PacketRecord
{
    public PlanetPositionalUpdateRecord(ReadOnlySpan<byte> payload) : base(0x003F, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 48) { Flag(sb, $"PLANET_POSITIONAL_UPDATE truncated -- {Payload.Length} bytes, expected 48"); return; }
        int    gameId      = ReadI32LE(Payload, 0);
        uint   timeStamp   = ReadU32LE(Payload, 4);
        float  x           = ReadF32LE(Payload, 8);
        float  y           = ReadF32LE(Payload, 12);
        float  z           = ReadF32LE(Payload, 16);
        int    orbitId     = ReadI32LE(Payload, 20);
        float  orbitDist   = ReadF32LE(Payload, 24);
        float  orbitAngle  = ReadF32LE(Payload, 28);
        float  orbitRate   = ReadF32LE(Payload, 32);
        float  rotateAngle = ReadF32LE(Payload, 36);
        float  rotateRate  = ReadF32LE(Payload, 40);
        float  tiltAngle   = ReadF32LE(Payload, 44);

        FHex(sb,   0, "GameID",      gameId);
        FHex(sb,   4, "TimeStamp",   timeStamp);
        FBytes(sb, 8, 12, "Position", $"X={x:0.0##}  Y={y:0.0##}  Z={z:0.0##}");
        FHex(sb,  20, "OrbitID",     orbitId);
        FFloat(sb, 24, "OrbitDist",  orbitDist);
        FFloat(sb, 28, "OrbitAngle", orbitAngle);
        FFloat(sb, 32, "OrbitRate",  orbitRate);
        FFloat(sb, 36, "RotateAngle", rotateAngle);
        FFloat(sb, 40, "RotateRate", rotateRate);
        FFloat(sb, 44, "TiltAngle",  tiltAngle);
    }
}
