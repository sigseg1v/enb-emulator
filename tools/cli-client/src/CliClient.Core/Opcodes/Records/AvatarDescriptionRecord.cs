// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0061 AVATAR_DESCRIPTION. Wire (struct AvatarDescription):
///   uint32 AvatarID; AvatarData (241 bytes); int32 unknown1; u8 unknown2[3]; float unknown3; float unknown4.
/// AvatarData layout: first_name[20] at +0; last_name[20] at +20; avatar_type int32 at +40;
///   filler u8 at +44; avatar_version u8 at +45; race int32 at +46; profession int32 at +50;
///   gender int32 at +54; mood_type int32 at +58.
/// </summary>
public sealed class AvatarDescriptionRecord : PacketRecord
{
    public AvatarDescriptionRecord(ReadOnlySpan<byte> payload) : base(0x0061, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 4 + 60) { Flag(sb, $"AVATAR_DESCRIPTION truncated -- {'{'}Payload.Length{'}'} bytes"); return; }
        uint avatarId  = ReadU32LE(Payload, 0);
        const int D    = 4; // AvatarData starts at offset 4
        string firstName = ReadNulString(Payload.AsSpan(D, 20));
        string lastName  = ReadNulString(Payload.AsSpan(D + 20, 20));
        int    avatarType = ReadI32LE(Payload, D + 40);
        byte   filler     = Payload[D + 44];
        byte   version    = Payload[D + 45];
        int    race       = ReadI32LE(Payload, D + 46);
        int    profession = ReadI32LE(Payload, D + 50);
        int    gender     = ReadI32LE(Payload, D + 54);
        int    mood       = ReadI32LE(Payload, D + 58);
        FHex(sb, 0,      "AvatarID",    avatarId);
        FStr(sb, D,      20, "FirstName",  firstName, required: true);
        FStr(sb, D+20,   20, "LastName",   lastName);
        FHex(sb, D+40,   "AvatarType",  avatarType);
        FDec(sb, D+44,   "Filler",      filler);
        FDec(sb, D+45,   "Version",     version);
        FDec(sb, D+46,   "Race",        race);
        FDec(sb, D+50,   "Profession",  profession);
        FDec(sb, D+54,   "Gender",      gender);
        FDec(sb, D+58,   "MoodType",    mood);
        // Remaining AvatarData bytes (D+62 through D+240) are not yet decoded
    }
}
