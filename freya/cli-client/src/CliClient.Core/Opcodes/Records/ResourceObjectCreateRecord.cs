// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x2019 RESOURCE_OBJECT_CREATE. Prospectable resources (asteroids, gas,
/// etc.). Wire (variable, PACKED) matching the server emitter
/// Resource::FormStaticPacket (server/src/ResourceClass.cpp:694):
///   int32  GameID          @0
///   int16  BaseAsset       @4
///   float  Scale           @6
///   float  HSV0            @10
///   float  HSV1            @14   (2nd HSV channel; 0 for resources)
///   float  PosX,PosY,PosZ  @18,@22,@26
///   float  Orientation[4]  @30,@34,@38,@42
///   int16  NameLen         @46
///   char   Name[NameLen]   @48   (NOT null-terminated)
/// The retail capture carries TWO HSV channels (HSV0 then HSV1) before the
/// position -- the @14 float is 0.0 in every sampled frame, so it cannot be a
/// position coordinate. The sibling 0x2018 StaticMap::FormStaticPacket
/// (server/src/NavTypeClass.cpp:287) emits all three HSV channels; resources
/// emit only the first two (HSV2 is absent -- the frame is exactly 56 bytes,
/// leaving no room for a third). This matches the server emitter after the
/// HSV1 line was un-commented to restore retail parity.
/// </summary>
public sealed class ResourceObjectCreateRecord : PacketRecord
{
    public ResourceObjectCreateRecord(ReadOnlySpan<byte> payload) : base(0x2019, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 48) { Flag(sb, $"RESOURCE_OBJECT_CREATE truncated -- {Payload.Length} bytes, expected >= 48"); return; }

        int   gid       = ReadI32LE(Payload, 0);
        short baseAsset = ReadI16LE(Payload, 4);
        float scale     = ReadF32LE(Payload, 6);
        float hsv0      = ReadF32LE(Payload, 10);
        float hsv1      = ReadF32LE(Payload, 14);
        float px        = ReadF32LE(Payload, 18);
        float py        = ReadF32LE(Payload, 22);
        float pz        = ReadF32LE(Payload, 26);
        float o0        = ReadF32LE(Payload, 30);
        float o1        = ReadF32LE(Payload, 34);
        float o2        = ReadF32LE(Payload, 38);
        float o3        = ReadF32LE(Payload, 42);
        short nameLen   = ReadI16LE(Payload, 46);

        FHex(sb,   0, "GameID",      gid);
        FDec(sb,   4, "BaseAsset",   baseAsset);
        FFloat(sb, 6, "Scale",       scale);
        FFloat(sb, 10, "HSV0",       hsv0);
        FFloat(sb, 14, "HSV1",       hsv1);
        FBytes(sb, 18, 12, "Position", $"({px:0.#}, {py:0.#}, {pz:0.#})");
        FBytes(sb, 30, 16, "Orientation", $"({o0:0.###}, {o1:0.###}, {o2:0.###}, {o3:0.###})");
        FDec(sb,  46, "NameLen",     nameLen);

        if (nameLen < 0 || 48 + nameLen > Payload.Length)
        {
            Flag(sb, $"NameLen {nameLen} overruns payload ({Payload.Length} bytes)");
            return;
        }
        string name = Encoding.Latin1.GetString(Payload, 48, nameLen);
        FStr(sb, 48, nameLen, "Name", name, required: false);
    }
}
