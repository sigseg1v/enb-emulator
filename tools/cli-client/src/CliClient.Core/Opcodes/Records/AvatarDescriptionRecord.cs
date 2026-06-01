// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0061 AVATAR_DESCRIPTION. Wire layout (struct AvatarDescription):
///   uint32 AvatarID; AvatarData (241 bytes); int32 unknown1; u8 unknown2[3];
///   float unknown3; float unknown4; ~261 bytes total.
/// First/last names are NUL-padded ASCII at offset +4 / +24 inside AvatarData.
/// </summary>
public sealed class AvatarDescriptionRecord : PacketRecord
{
    public AvatarDescriptionRecord(ReadOnlySpan<byte> payload) : base(0x0061, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 4 + 60)
        {
            Flag(sb, $"AVATAR_DESCRIPTION truncated -- {Payload.Length} bytes");
            return;
        }
        uint avatarId = ReadU32LE(Payload, 0);

        // AvatarData layout (241 bytes), inlined at offset 4:
        //   +0..19   first_name[20]
        //   +20..39  last_name[20]
        //   +40..43  avatar_type int32
        //   +44      filler
        //   +45      avatar_version byte
        //   +46..49  race int32
        //   +50..53  profession int32
        //   +54..57  gender int32
        //   +58..61  mood_type int32
        const int AvatarDataOffset = 4;
        string firstName = ReadNulString(Payload.AsSpan(AvatarDataOffset, 20));
        string lastName  = ReadNulString(Payload.AsSpan(AvatarDataOffset + 20, 20));
        int avatarType   = ReadI32LE(Payload, AvatarDataOffset + 40);
        byte version     = Payload[AvatarDataOffset + 45];
        int race         = ReadI32LE(Payload, AvatarDataOffset + 46);
        int profession   = ReadI32LE(Payload, AvatarDataOffset + 50);
        int gender       = ReadI32LE(Payload, AvatarDataOffset + 54);
        int mood         = ReadI32LE(Payload, AvatarDataOffset + 58);

        FieldHex(sb, "AvatarID", avatarId);
        FieldString(sb, "FirstName", firstName, requiredNonEmpty: true);
        FieldString(sb, "LastName", lastName);
        FieldDec(sb, "AvatarType", avatarType);
        FieldDec(sb, "Version", version);
        FieldDec(sb, "Race", race);
        FieldDec(sb, "Profession", profession);
        FieldDec(sb, "Gender", gender);
        FieldDec(sb, "MoodType", mood);
    }
}
