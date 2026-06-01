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
            0x0004 => new CreateRecord(payload),
            0x0005 => new StartRecord(payload),
            0x0007 => new RemoveRecord(payload),
            0x0008 => new SimplePosRecord(payload),
            0x0009 => new ObjectEffectRecord(payload),
            0x0010 => new DecalRecord(payload),
            0x0011 => new ColorizationRecord(payload),
            0x001B => new AuxDataRecord(payload),
            0x001D => new MessageStringRecord(payload),
            0x0025 => new ItemBaseRecord(payload),
            0x0030 => new ActivateRenderStateRecord(payload, 0x0030),
            0x0031 => new ActivateRenderStateRecord(payload, 0x0031),
            0x003C => new ClientTypeRecord(payload),
            0x003F => new PlanetPositionalUpdateRecord(payload),
            0x0034 => new ClientSetTimeRecord(payload),
            0x0036 => new ServerRedirectRecord(payload),
            0x0037 => new ClientAvatarRecord(payload),
            0x003E => new AdvancedPositionalUpdateRecord(payload),
            0x0040 => new ConstantPosRecord(payload),
            0x0042 => new ServerParametersRecord(payload),
            0x0047 => new ClientShipRecord(payload),
            0x004F => new StarbaseSetRecord(payload),
            0x0052 => new LoungeNpcRecord(payload),
            0x0061 => new AvatarDescriptionRecord(payload),
            0x006F => new GlobalTicketRecord(payload),
            0x007F => new ManufactureSetManufactureIdRecord(payload),
            0x0089 => new RelationshipRecord(payload),
            0x0097 => new GalaxyMapRecord(payload),
            0x0099 => new NavigationRecord(payload),
            0x00B2 => new NameDecalRecord(payload),
            0x00B4 => new SubpartsRecord(payload),
            0x00D0 => new GuildMessageSectorRecord(payload),
            _      => new GenericRecord(opcode, payload),
        };
    }
}
