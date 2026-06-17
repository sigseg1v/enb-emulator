// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using N7.CliClient.Logging;
using N7.CliClient.Opcodes.Records;
using Xunit;

namespace N7.CliClient.UnitTests.Opcodes;

/// <summary>
/// Byte-order pin for 0x007F MANUFACTURE_SET_MANUFACTURE_ID.
///
/// <para>
/// The 0x007F mfg_id field is the one GameID on the wire that is emitted
/// BYTE-REVERSED relative to every other GameID (those go host little-endian).
/// In capture_1 the manufacture-lab anchor appears as the same GameID two ways
/// in one stream: the lab CREATE and its AuxData carry it host-LE
/// (`F7 13 EE 06`), while the 0x007F payload carries it reversed (`06 EE 13 F7`).
/// The records decoder must read 0x007F big-endian so the recovered id equals
/// the host GameID the CREATE/AuxData carry -- if it does not, the client cannot
/// resolve the manufacture session and the analyze terminal opens against a
/// NULL session.
/// </para>
///
/// <para>
/// This pins the parse so the matching server emitter (Player::SetManufactureID,
/// which byte-swaps the host GameID before sending) can be changed with the
/// format locked first, per the understanding-before-change rule.
/// </para>
/// </summary>
public sealed class ManufactureSetManufactureIdRecordTests
{
    public ManufactureSetManufactureIdRecordTests() => AnsiPalette.Enabled = false;

    [Fact]
    public void Decode_ReadsFieldBigEndian_RecoveringHostGameId()
    {
        // capture_1 0x007F payload (the byte-reversed encoding of the host
        // GameID 0x06EE13F7 that the manufacture-lab CREATE/AuxData carry LE).
        byte[] payload = { 0x06, 0xEE, 0x13, 0xF7 };

        var dump = PacketRecord.Resolve(0x007F, payload).DumpToString();

        // BE read recovers the host GameID byte-for-byte; an LE read would have
        // produced 0xF713EE06 (the CREATE/AuxData wire bytes), proving the swap.
        Assert.Contains("ManufactureID", dump);
        Assert.Contains("0x06EE13F7", dump);
        Assert.DoesNotContain("0xF713EE06", dump);
    }

    [Fact]
    public void Decode_ZeroReset_DecodesToZero()
    {
        // SetManufactureID(0) reset -- byte order is a no-op for zero.
        byte[] payload = { 0x00, 0x00, 0x00, 0x00 };

        var dump = PacketRecord.Resolve(0x007F, payload).DumpToString();

        Assert.Contains("ManufactureID", dump);
        Assert.Contains("0x00000000", dump);
    }

    [Fact]
    public void Decode_Truncated_Flags()
    {
        byte[] payload = { 0x06, 0xEE };

        var dump = PacketRecord.Resolve(0x007F, payload).DumpToString();

        Assert.Contains("truncated", dump);
    }
}
