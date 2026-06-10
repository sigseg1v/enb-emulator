// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;
using System.Globalization;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0061 AVATAR_DESCRIPTION. Wire (struct AvatarDescription, 260 bytes, LE):
///   u32 AvatarID; AvatarData (241 bytes); i32 unknown1; u8 unknown2[3];
///   f32 unknown3 (=1.0); f32 unknown4 (=1.0).
/// AvatarData (struct-relative offsets; absolute = +4):
///   first_name[20]@0, last_name[20]@20, avatar_type i32@40, filler@44,
///   version@45, race i32@46, profession i32@50, gender i32@54, mood i32@58,
///   14 appearance bytes@62, then nine f32[3] colour/offset triples@76..172,
///   four i32 metal fields@184, filler@200, height_weight_1 f32[5]@201,
///   height_weight_2 f32[5]@221.
/// Source: struct AvatarDescription / AvatarData (PacketStructures.h), emitter
/// Player avatar send (PlayerClass.cpp); offsets confirmed via gcc offsetof.
/// </summary>
public sealed class AvatarDescriptionRecord : PacketRecord
{
    private static readonly string[] AppearanceNames =
    {
        "personality", "nlp", "body_type", "pants_type", "head_type", "hair_num",
        "ear_num", "goggle_num", "beard_num", "weapon_hip_num", "weapon_unique_num",
        "weapon_back_num", "head_texture_num", "tattoo_texture_num",
    };
    private static readonly (int off, string name)[] Triples =
    {
        (80, "tattoo_offset"), (92, "hair_color"), (104, "beard_color"), (116, "eye_color"),
        (128, "skin_color"), (140, "shirt_primary_color"), (152, "shirt_secondary_color"),
        (164, "pants_primary_color"), (176, "pants_secondary_color"),
    };

    public AvatarDescriptionRecord(ReadOnlySpan<byte> payload) : base(0x0061, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 245) { Flag(sb, $"AVATAR_DESCRIPTION truncated -- {Payload.Length} bytes, expected >= 245"); return; }

        FHex(sb, 0, "AvatarID", ReadU32LE(Payload, 0));
        FStr(sb, 4,  20, "FirstName", ReadNulString(Payload.AsSpan(4, 20)), required: true);
        FStr(sb, 24, 20, "LastName",  ReadNulString(Payload.AsSpan(24, 20)));
        FHex(sb, 44, "AvatarType",  ReadI32LE(Payload, 44));
        FDec(sb, 48, "Filler1",     Payload[48]);
        FDec(sb, 49, "Version",     Payload[49]);
        FDec(sb, 50, "Race",        ReadI32LE(Payload, 50));
        FDec(sb, 54, "Profession",  ReadI32LE(Payload, 54));
        FDec(sb, 58, "Gender",      ReadI32LE(Payload, 58));
        FDec(sb, 62, "MoodType",    ReadI32LE(Payload, 62));

        for (int i = 0; i < AppearanceNames.Length; i++)
            FDec(sb, 66 + i, AppearanceNames[i], Payload[66 + i]);

        foreach (var (off, name) in Triples)
            F(sb, off, 12, name, FloatTriple(off));

        FDec(sb, 188, "shirt_primary_metal",   ReadI32LE(Payload, 188));
        FDec(sb, 192, "shirt_secondary_metal", ReadI32LE(Payload, 192));
        FDec(sb, 196, "pants_primary_metal",   ReadI32LE(Payload, 196));
        FDec(sb, 200, "pants_secondary_metal", ReadI32LE(Payload, 200));
        FDec(sb, 204, "Filler2", Payload[204]);
        F(sb, 205, 20, "height_weight_1", FloatN(205, 5));
        F(sb, 225, 20, "height_weight_2", FloatN(225, 5));

        if (Payload.Length >= 260)
        {
            FHex(sb, 245, "Unknown1", ReadI32LE(Payload, 245));
            F(sb, 249, 3, "Unknown2", $"[{Payload[249]}, {Payload[250]}, {Payload[251]}]");
            FFloat(sb, 252, "Unknown3", ReadF32LE(Payload, 252));
            FFloat(sb, 256, "Unknown4", ReadF32LE(Payload, 256));
        }
    }

    private string FloatTriple(int off) => "(" + FloatN(off, 3) + ")";

    private string FloatN(int off, int n)
    {
        var parts = new string[n];
        for (int k = 0; k < n; k++)
            parts[k] = ReadF32LE(Payload, off + 4 * k).ToString("0.0###", CultureInfo.InvariantCulture);
        return string.Join(", ", parts);
    }
}
