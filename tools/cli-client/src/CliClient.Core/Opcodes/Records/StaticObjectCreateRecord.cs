// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x2018 STATIC_OBJECT_CREATE. The sector's static map objects: navs,
/// stations, gates, fields, decos. Wire (variable, >= 60 bytes, PACKED):
///   int32  GameID            @0
///   uint8  CreateType        @4   (3 planet, 11 gate, 12 station, 37 deco/nav ...)
///   int16  BaseAsset         @5
///   float  Scale             @7
///   float  H                 @11
///   float  S                 @15
///   float  V                 @19
///   uint8  Relationship      @23  (friendly/shun/attack)
///   uint8  PosType           @24
///   float  PosX,PosY,PosZ    @25,@29,@33
///   float  Orientation[4]    @37,@41,@45,@49
///   float  Signature         @53
///   uint8  SigFlags          @57  (bit0x80 HAS_VISITED, 0x40 IS_HUGE,
///                                  0x20 IS_NAV, 0x10 HAS_NAV, low nibble NavType)
///   int16  NameLen           @58
///   char   Name[NameLen]     @60  (NOT null-terminated)
/// The map node renders only when HAS_NAV (0x10) is set, which the server
/// sets when HasNavInfo() && NavType()>0 && AppearsInRadar().
/// </summary>
public sealed class StaticObjectCreateRecord : PacketRecord
{
    public StaticObjectCreateRecord(ReadOnlySpan<byte> payload) : base(0x2018, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 60) { Flag(sb, $"STATIC_OBJECT_CREATE truncated -- {Payload.Length} bytes, expected >= 60"); return; }

        int   gid       = ReadI32LE(Payload, 0);
        byte  createTy  = Payload[4];
        short baseAsset = ReadI16LE(Payload, 5);
        float scale     = ReadF32LE(Payload, 7);
        float h         = ReadF32LE(Payload, 11);
        float s         = ReadF32LE(Payload, 15);
        float v         = ReadF32LE(Payload, 19);
        byte  rel       = Payload[23];
        byte  posType   = Payload[24];
        float px        = ReadF32LE(Payload, 25);
        float py        = ReadF32LE(Payload, 29);
        float pz        = ReadF32LE(Payload, 33);
        float o0        = ReadF32LE(Payload, 37);
        float o1        = ReadF32LE(Payload, 41);
        float o2        = ReadF32LE(Payload, 45);
        float o3        = ReadF32LE(Payload, 49);
        float signature = ReadF32LE(Payload, 53);
        byte  sigFlags  = Payload[57];
        short nameLen   = ReadI16LE(Payload, 58);

        FHex(sb,   0, "GameID",       gid);
        FDec(sb,   4, "CreateType",   createTy);
        FDec(sb,   5, "BaseAsset",    baseAsset);
        FFloat(sb, 7, "Scale",        scale);
        FBytes(sb, 11, 12, "HSV",     $"H={h:0.###} S={s:0.###} V={v:0.###}");
        FDec(sb,  23, "Relationship", rel);
        FDec(sb,  24, "PosType",      posType);
        FBytes(sb, 25, 12, "Position", $"({px:0.#}, {py:0.#}, {pz:0.#})");
        FBytes(sb, 37, 16, "Orientation", $"({o0:0.###}, {o1:0.###}, {o2:0.###}, {o3:0.###})");
        FFloat(sb, 53, "Signature",   signature);

        string flags = DecodeSigFlags(sigFlags);
        FHex(sb,  57, "SigFlags",     sigFlags, flags);
        FDec(sb,  58, "NameLen",      nameLen);

        if (nameLen < 0 || 60 + nameLen > Payload.Length)
        {
            Flag(sb, $"NameLen {nameLen} overruns payload ({Payload.Length} bytes)");
            return;
        }
        string name = Encoding.Latin1.GetString(Payload, 60, nameLen);
        FStr(sb, 60, nameLen, "Name", name, required: false);
    }

    private static string DecodeSigFlags(byte f)
    {
        var parts = new List<string>();
        if ((f & 0x80) != 0) parts.Add("HAS_VISITED");
        if ((f & 0x40) != 0) parts.Add("IS_HUGE");
        if ((f & 0x20) != 0) parts.Add("IS_NAV");
        if ((f & 0x10) != 0) parts.Add("HAS_NAV");
        int navType = f & 0x0F;
        string flagStr = parts.Count > 0 ? string.Join("|", parts) : "-";
        return $"{flagStr} NavType={navType}";
    }
}
