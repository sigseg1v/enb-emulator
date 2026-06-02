// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x2019 RESOURCE_OBJECT_CREATE. Prospectable resources (asteroids, gas,
/// etc.). Wire (variable, PACKED) as observed in the retail capture:
///   int32  GameID          @0
///   int16  BaseAsset       @4
///   float  Scale           @6
///   float  HSV0            @10
///   float  PosX,PosY,PosZ  @14,@18,@22
///   float  Orientation[4]  @26,@30,@34,@38
///   float  Extra           @42   (retail emits this; see note below)
///   int16  NameLen         @46
///   char   Name[NameLen]   @48   (NOT null-terminated)
/// NOTE: the retail server sends an 8th trailing float at @42 (value ~0.2 in
/// the sample) before the name. Our Resource::FormStaticPacket emits only 7
/// floats (3 position + 4 orientation), so our 0x2019 is 4 bytes shorter than
/// retail. That divergence is a server bug tracked separately; this decoder
/// pins the retail-correct layout so the gap reporter flags ours.
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
        float px        = ReadF32LE(Payload, 14);
        float py        = ReadF32LE(Payload, 18);
        float pz        = ReadF32LE(Payload, 22);
        float o0        = ReadF32LE(Payload, 26);
        float o1        = ReadF32LE(Payload, 30);
        float o2        = ReadF32LE(Payload, 34);
        float o3        = ReadF32LE(Payload, 38);
        float extra     = ReadF32LE(Payload, 42);
        short nameLen   = ReadI16LE(Payload, 46);

        FHex(sb,   0, "GameID",      gid);
        FDec(sb,   4, "BaseAsset",   baseAsset);
        FFloat(sb, 6, "Scale",       scale);
        FFloat(sb, 10, "HSV0",       hsv0);
        FBytes(sb, 14, 12, "Position", $"({px:0.#}, {py:0.#}, {pz:0.#})");
        FBytes(sb, 26, 16, "Orientation", $"({o0:0.###}, {o1:0.###}, {o2:0.###}, {o3:0.###})");
        FFloat(sb, 42, "Extra",      extra);
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
