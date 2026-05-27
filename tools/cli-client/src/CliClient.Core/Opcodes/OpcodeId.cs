// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

namespace N7.CliClient.Opcodes;

/// <summary>
/// Wrapper around the 16-bit EnB opcode identifier. Mirrors the
/// <c>ENB_OPCODE_xxxx</c> #defines in <c>common/include/net7/Opcodes.h</c>.
/// Wraps a <see cref="ushort"/> for type safety so an opcode can't be
/// accidentally passed where an arbitrary int is expected.
/// </summary>
public readonly record struct OpcodeId(ushort Value)
{
    public static implicit operator ushort(OpcodeId id) => id.Value;
    public static explicit operator OpcodeId(ushort value) => new(value);

    public override string ToString() => $"0x{Value:X4}";

    /// <summary>
    /// Subset of the opcodes consumed in Phase K integration tests
    /// (and therefore the first opcodes Phase S round-trips). The
    /// remaining opcodes (~200+) come online with
    /// <see cref="OpcodeRegistry"/> + per-opcode stubs in Inbound/
    /// and Outbound/ per plans/19-phase-s-cli-client.md Item 15.
    /// </summary>
    public static class Known
    {
        public static readonly OpcodeId VersionRequest               = new(0x0000);
        public static readonly OpcodeId VersionResponse              = new(0x0001);
        public static readonly OpcodeId Login                        = new(0x0002);
        public static readonly OpcodeId Logoff                       = new(0x0003);
        public static readonly OpcodeId Create                       = new(0x0004);
        public static readonly OpcodeId Start                        = new(0x0005);
        public static readonly OpcodeId StartAck                     = new(0x0006);
        public static readonly OpcodeId Decal                        = new(0x0010);
        public static readonly OpcodeId Colorization                 = new(0x0011);
        public static readonly OpcodeId Turn                         = new(0x0012);
        public static readonly OpcodeId Tilt                         = new(0x0013);
        public static readonly OpcodeId Move                         = new(0x0014);
        public static readonly OpcodeId RequestTarget                = new(0x0017);
        public static readonly OpcodeId RequestTargetsTarget         = new(0x0018);
        public static readonly OpcodeId SetTarget                    = new(0x0019);
        public static readonly OpcodeId Debug                        = new(0x001A);
        public static readonly OpcodeId AuxData                      = new(0x001B);
        public static readonly OpcodeId MessageString                = new(0x001D);
        public static readonly OpcodeId PriorityMessage              = new(0x0020);
        public static readonly OpcodeId ItemBase                     = new(0x0025);
        public static readonly OpcodeId InventoryMove                = new(0x0027);
        public static readonly OpcodeId ItemState                    = new(0x0029);
        public static readonly OpcodeId Action                       = new(0x002C);
        public static readonly OpcodeId Action2                      = new(0x002D);
        public static readonly OpcodeId Option                       = new(0x002E);
        public static readonly OpcodeId ClientChat                   = new(0x0033);
        public static readonly OpcodeId ClientSetTime                = new(0x0034);
        public static readonly OpcodeId MasterJoin                   = new(0x0035);
        public static readonly OpcodeId ServerRedirect               = new(0x0036);
        public static readonly OpcodeId ClientAvatar                 = new(0x0037);
        public static readonly OpcodeId ServerHandoff                = new(0x003A);
        public static readonly OpcodeId ClientType                   = new(0x003C);
        public static readonly OpcodeId AdvancedPositionalUpdate     = new(0x003E);
        public static readonly OpcodeId ConstantPositionalUpdate     = new(0x0040);
        public static readonly OpcodeId RequestTime                  = new(0x0044);
        public static readonly OpcodeId ClientShip                   = new(0x0047);
        public static readonly OpcodeId StarbaseRequest              = new(0x004E);
        public static readonly OpcodeId StarbaseSet                  = new(0x004F);
        public static readonly OpcodeId LoungeNpc                    = new(0x0052);
        public static readonly OpcodeId SkillStringRq                = new(0x0051);
        public static readonly OpcodeId SelectTalkTree               = new(0x0055);
        public static readonly OpcodeId TalkTreeAction               = new(0x0056);
        public static readonly OpcodeId SkillUp                      = new(0x0057);
        public static readonly OpcodeId SkillAbility                 = new(0x0058);
        public static readonly OpcodeId VerbRequest                  = new(0x005A);
        public static readonly OpcodeId EquipUse                     = new(0x005D);
        public static readonly OpcodeId AvatarEmote                  = new(0x005E);
        public static readonly OpcodeId AvatarEmoteResponse          = new(0x005F);
        public static readonly OpcodeId AvatarDescription            = new(0x0061);
        public static readonly OpcodeId ManufactureSetManufactureId  = new(0x007F);
        public static readonly OpcodeId ManufactureTerminal          = new(0x0079);
        public static readonly OpcodeId ManufactureCategorySelect    = new(0x007A);
        public static readonly OpcodeId ManufactureSetItem           = new(0x007B);
        public static readonly OpcodeId RefinerySetItem              = new(0x007C);
        public static readonly OpcodeId ManufactureAction            = new(0x007E);
        public static readonly OpcodeId ManufactureTechLevelFilter   = new(0x0080);
        public static readonly OpcodeId MissionForfeit               = new(0x0086);
        public static readonly OpcodeId MissionDismissal             = new(0x0087);
        public static readonly OpcodeId PetitionStuck                = new(0x0088);
        public static readonly OpcodeId Relationship                 = new(0x0089);
        public static readonly OpcodeId TriggerEmote                 = new(0x00A1);
        public static readonly OpcodeId NotifyEmote                  = new(0x00A2);
        public static readonly OpcodeId ClientChatRequest            = new(0x00A3);
        public static readonly OpcodeId ClientChatEvent              = new(0x00A5);
        public static readonly OpcodeId NameDecal                    = new(0x00B2);
        public static readonly OpcodeId Subparts                     = new(0x00B4);
        public static readonly OpcodeId LogoffRequest                = new(0x00B9);
        public static readonly OpcodeId LogoffConfirmation           = new(0x00BA);
        public static readonly OpcodeId ConfirmedActionResponse      = new(0x00C0);
        public static readonly OpcodeId GuildSimpleClientSector      = new(0x00CD);
        public static readonly OpcodeId GuildRankNamesRequestClient  = new(0x00D4);
        public static readonly OpcodeId GlobalConnect          = new(0x006D);
        public static readonly OpcodeId GlobalTicketRequest    = new(0x006E);
        public static readonly OpcodeId GlobalTicket           = new(0x006F);
        public static readonly OpcodeId GlobalAvatarList       = new(0x0070);
        public static readonly OpcodeId GlobalDeleteCharacter  = new(0x0071);
        public static readonly OpcodeId GlobalCreateCharacter  = new(0x0072);
        public static readonly OpcodeId GlobalError            = new(0x0075);
        public static readonly OpcodeId StarbaseAvatarChange   = new(0x009D);
        public static readonly OpcodeId StarbaseRoomChange     = new(0x009F);
        public static readonly OpcodeId LoginStage             = new(0x2020);
        public static readonly OpcodeId LoginStageAck          = new(0x2021);
        public static readonly OpcodeId StarbaseLoginComplete  = new(0x3008);
    }
}
