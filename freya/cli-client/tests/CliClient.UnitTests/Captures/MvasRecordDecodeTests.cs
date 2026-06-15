// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using N7.CliClient.Logging;
using N7.CliClient.Opcodes.Records;
using Xunit;

namespace N7.CliClient.UnitTests.Captures;

/// <summary>
/// Pins the MVAS (Move-Assist) UDP-channel record decoders against verbatim
/// retail bytes from the planet land/fly/dock capture (proxy&lt;-&gt;server
/// cleartext leg). The fixture bodies have the 12-byte EnbUdpHeader stripped --
/// the records parse only the opcode body, exactly like every other
/// <see cref="PacketRecord"/>.
///   * 0x1004 MVAS_SEND_POSITION_C_S -- the 28-byte full form (pos[3] +
///     heading[3] + trailing u32).
///   * 0x1007 MVAS_TOGGLE_SEND_FREQ_S_C -- u32 send-frequency.
/// </summary>
public sealed class MvasRecordDecodeTests
{
    private static readonly IReadOnlyDictionary<string, CaptureFixture> Frames =
        CaptureFixture.Load("mvas-records.txt");

    public MvasRecordDecodeTests()
    {
        AnsiPalette.Enabled = false;
    }

    private static string Dump(string name)
    {
        var f = Frames[name];
        return PacketRecord.Resolve((ushort)f.Opcode, f.Payload).DumpToString();
    }

    [Fact]
    public void Fixture_Loads_BothMvasFrames()
    {
        Assert.Equal(2, Frames.Count);
        Assert.Equal(0x1004, Frames["mvas_send_position_full"].Opcode);
        Assert.Equal(28, Frames["mvas_send_position_full"].Payload.Length);
        Assert.Equal(0x1007, Frames["mvas_toggle_send_freq_2"].Opcode);
        Assert.Equal(4, Frames["mvas_toggle_send_freq_2"].Payload.Length);
    }

    // ── 0x1004 MVAS_SEND_POSITION full form ──────────────────────────────────
    // The 28-byte body: three position floats, three heading floats, and a
    // trailing u32 the server ignores. Every float is byte-pinned -- a LE/BE
    // slip or a wrong offset (e.g. dropping the heading triple) would shift
    // every later field and change these values.

    [Fact]
    public void MvasSendPosition_FullForm_DecodesPositionHeadingAndTrailing()
    {
        string d = Dump("mvas_send_position_full");

        // Position triple (C3 B7 81 46 / E4 70 86 C7 / A1 3F 67 45 LE).
        Assert.Contains("[0000] PosX", d);
        Assert.Contains("= 16603.88", d);
        Assert.Contains("[0004] PosY", d);
        Assert.Contains("= -68833.78", d);
        Assert.Contains("[0008] PosZ", d);
        Assert.Contains("= 3699.977", d);

        // Heading triple -- only reachable if the body is read as the >= 24-byte
        // form (the server gates this read on size > 28).
        Assert.Contains("[000C] HeadX", d);
        Assert.Contains("= -0.864", d);
        Assert.Contains("[0010] HeadY", d);
        Assert.Contains("= 0.289", d);
        Assert.Contains("[0014] HeadZ", d);
        Assert.Contains("= 0.412", d);

        // The trailing u32 the server ignores -- 0 on the wire.
        Assert.Contains("[0018] Trailing", d);
        Assert.Contains("= 0x00000000  (0)", d);

        // Whole 28-byte body consumed: no undecoded gap, no truncation flag.
        Assert.DoesNotContain("???", d);
        Assert.DoesNotContain("truncated", d);
    }

    [Fact]
    public void MvasSendPosition_ShorterForms_DecodeWithoutHeading()
    {
        // 12-byte position-only form: pos[3], nothing after.
        byte[] full = Frames["mvas_send_position_full"].Payload;
        byte[] posOnly = full[..12];
        string d12 = PacketRecord.Resolve(0x1004, posOnly).DumpToString();
        Assert.Contains("PosX", d12);
        Assert.Contains("PosZ", d12);
        Assert.DoesNotContain("HeadX", d12);
        Assert.DoesNotContain("Trailing", d12);
        Assert.DoesNotContain("truncated", d12);

        // 16-byte pos+u32 form: pos[3] then a single trailing u32, no heading.
        byte[] posU32 = full[..16];
        string d16 = PacketRecord.Resolve(0x1004, posU32).DumpToString();
        Assert.Contains("PosX", d16);
        Assert.Contains("[000C] Trailing", d16);
        Assert.DoesNotContain("HeadX", d16);
        Assert.DoesNotContain("truncated", d16);
    }

    [Fact]
    public void MvasSendPosition_Truncated_IsFlagged()
    {
        byte[] tooShort = Frames["mvas_send_position_full"].Payload[..8];
        string d = PacketRecord.Resolve(0x1004, tooShort).DumpToString();
        Assert.Contains("[!]", d);
        Assert.Contains("truncated", d);
    }

    // ── 0x1007 MVAS_TOGGLE_SEND_FREQ ─────────────────────────────────────────

    [Fact]
    public void MvasToggleSendFreq_DecodesFrequency()
    {
        string d = Dump("mvas_toggle_send_freq_2");

        Assert.Contains("[0000] Frequency", d);
        Assert.Contains("= 2", d);
        Assert.DoesNotContain("???", d);
        Assert.DoesNotContain("truncated", d);
    }

    [Fact]
    public void MvasToggleSendFreq_Truncated_IsFlagged()
    {
        string d = PacketRecord.Resolve(0x1007, new byte[] { 0x02, 0x00 }).DumpToString();
        Assert.Contains("[!]", d);
        Assert.Contains("truncated", d);
    }
}
