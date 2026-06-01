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
            0x0036 => new ServerRedirectRecord(payload),
            0x0040 => new ConstantPosRecord(payload),
            0x004F => new StarbaseSetRecord(payload),
            0x0061 => new AvatarDescriptionRecord(payload),
            0x006F => new GlobalTicketRecord(payload),
            0x00B2 => new NameDecalRecord(payload),
            _      => new GenericRecord(opcode, payload),
        };
    }
}
