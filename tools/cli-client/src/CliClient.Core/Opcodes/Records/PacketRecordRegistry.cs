// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// Opcode -> record-factory lookup. Adding decode coverage for a new
/// opcode is one new record class plus one line here.
/// </summary>
/// <remarks>
/// Records are constructed lazily per call (cheap; just span copies);
/// nothing thread-local. Unknown opcodes fall through to
/// <see cref="GenericRecord"/>.
/// </remarks>
public static class PacketRecordRegistry
{
    /// <summary>
    /// Construct the right record for an opcode. Never returns null.
    /// </summary>
    public static IPacketRecord Resolve(ushort opcode, ReadOnlySpan<byte> payload)
    {
        return opcode switch
        {
            0x0002 => new LoginRecord(payload),
            0x0004 => new CreateRecord(payload),
            0x0005 => new StartRecord(payload),
            0x0006 => new StartAckRecord(payload),
            0x0007 => new RemoveRecord(payload),
            0x0008 => new SimplePosRecord(payload),
            0x0009 => new ObjectEffectRecord(payload),
            0x000A => new PointEffectRecord(payload),
            0x000B => new ObjectToObjectEffectRecord(payload),
            0x000E => new ObjectToObjectLinkedEffectRecord(payload),
            0x000F => new RemoveEffectRecord(payload),
            0x0010 => new DecalRecord(payload),
            0x0011 => new ColorizationRecord(payload),
            0x0012 => new TurnTiltRecord(payload, 0x0012),
            0x0013 => new TurnTiltRecord(payload, 0x0013),
            0x0014 => new MoveRecord(payload),
            0x0017 => new RequestTargetRecord(payload),
            0x0019 => new SetTargetRecord(payload),
            0x001A => new DebugRecord(payload),
            0x001B => new AuxDataRecord(payload),
            0x001D => new MessageStringRecord(payload),
            0x001F => new TradeActionRecord(payload),
            0x0020 => new PriorityMessageRecord(payload),
            0x0021 => new PushMessageRecord(payload, 0x0021),
            0x0022 => new PushMessageRecord(payload, 0x0022),
            0x0025 => new ItemBaseRecord(payload),
            0x0026 => new ChangeBaseAssetRecord(payload),
            0x0027 => new InventoryMoveRecord(payload),
            0x0028 => new InventorySortRecord(payload),
            0x002A => new SetZBandRecord(payload),
            0x002B => new SetBBoxRecord(payload),
            0x002C => new ActionRecord(payload),
            0x002F => new InitRenderStateRecord(payload),
            0x0030 => new ActivateRenderStateRecord(payload, 0x0030),
            0x0031 => new ActivateRenderStateRecord(payload, 0x0031),
            0x0032 => new DeactivateRenderStateRecord(payload),
            0x003A => new ServerHandoffRecord(payload),
            0x003C => new ClientTypeRecord(payload),
            0x003F => new PlanetPositionalUpdateRecord(payload),
            0x0034 => new ClientSetTimeRecord(payload),
            0x0035 => new MasterJoinRecord(payload),
            0x0036 => new ServerRedirectRecord(payload),
            0x0037 => new ClientAvatarRecord(payload),
            0x003E => new AdvancedPositionalUpdateRecord(payload),
            0x0040 => new ConstantPosRecord(payload),
            0x0041 => new FormationPositionalUpdateRecord(payload),
            0x0042 => new ServerParametersRecord(payload),
            0x0044 => new RequestTimeRecord(payload),
            0x0046 => new ComponentPositionalUpdateRecord(payload),
            0x0047 => new ClientShipRecord(payload),
            0x004A => new CreateAttachmentRecord(payload),
            0x004E => new StarbaseRequestRecord(payload),
            0x004F => new StarbaseSetRecord(payload),
            0x0052 => new LoungeNpcRecord(payload),
            0x0053 => new FindMemberRecord(payload),
            0x0054 => new TalkTreeRecord(payload),
            0x0055 => new SelectTalkTreeRecord(payload),
            0x0056 => new TalkTreeActionRecord(payload),
            0x0058 => new SkillAbilityRecord(payload),
            0x005A => new VerbRequestRecord(payload),
            0x005C => new VerbUpdateRecord(payload),
            0x005D => new EquipUseRecord(payload),
            0x005F => new AvatarEmoteResponseRecord(payload),
            0x0061 => new AvatarDescriptionRecord(payload),
            0x0064 => new ClientDamageRecord(payload),
            0x0066 => new OpenInterfaceRecord(payload),
            0x006A => new ClientSoundRecord(payload),
            0x006F => new GlobalTicketRecord(payload),
            0x0079 => new ManufactureItemCategoryRecord(payload),
            0x007C => new RefinerySetItemRecord(payload),
            0x007E => new ManufactureActionRecord(payload),
            0x007F => new ManufactureSetManufactureIdRecord(payload),
            0x0083 => new RecustomizeAvatarStartRecord(payload),
            0x0087 => new MissionDismissalRecord(payload),
            0x0089 => new RelationshipRecord(payload),
            0x008B => new AttackerUpdatesRecord(payload),
            0x008C => new LootHulkPermissionRecord(payload),
            0x0092 => new CameraControlRecord(payload),
            0x0096 => new JobAcceptReplyRecord(payload),
            0x0097 => new GalaxyMapRecord(payload),
            0x0099 => new NavigationRecord(payload),
            0x009B => new WarpRecord(payload),
            0x009C => new WarpIndexRecord(payload),
            0x009D => new StarbaseAvatarChangeRecord(payload),
            0x009E => new StarbaseAvatarChangeS2CRecord(payload),
            0x009F => new StarbaseRoomChangeRecord(payload, 0x009F),
            0x00A0 => new StarbaseRoomChangeRecord(payload, 0x00A0),
            0x00A2 => new NotifyEmoteRecord(payload),
            0x00A3 => new ClientChatRequestRecord(payload),
            0x00A4 => new ClientChatListRecord(payload),
            0x00A5 => new ClientChatEventRecord(payload),
            0x00B2 => new NameDecalRecord(payload),
            0x00B4 => new SubpartsRecord(payload),
            0x00BA => new LogoffConfirmationRecord(payload),
            0x00BC => new CtaRequestRecord(payload),
            0x00BD => new CtaResponseRecord(payload),
            0x00BE => new ConfirmedActionOfferRecord(payload),
            0x00D0 => new GuildMessageSectorRecord(payload),
            0x00D2 => new GuildPlayerPermissionsRecord(payload),
            0x2011 => new GalaxyMapCacheRecord(payload),
            0x2012 => new StartProspectRecord(payload),
            0x2013 => new TractorOreRecord(payload),
            0x2014 => new LootItemRecord(payload),
            0x2018 => new StaticObjectCreateRecord(payload),
            0x2019 => new ResourceObjectCreateRecord(payload),
            0x2020 => new LoginStageRecord(payload),
            0x2025 => new Opcode2025Record(payload),
            _      => new GenericRecord(opcode, payload),
        };
    }
}
