// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using N7.CliClient.Logging;
using N7.CliClient.Opcodes.Records;
using Xunit;

namespace N7.CliClient.UnitTests.Opcodes;

/// <summary>
/// Byte-pins the last four server-emitted opcodes that fell through to
/// <see cref="GenericRecord"/>: 0x0003 LOGOFF, 0x0065 UI_TRIGGER, 0x0093
/// JOB_LIST, 0x0094 JOB_DESCRIPTION. AddDataSN strings = raw bytes + NUL (no
/// length prefix). JOB_LIST's leading count is a placeholder upper bound, not
/// the true entry count, so the parser is end-of-buffer driven.
/// </summary>
public sealed class JobAndLogoffRecordTests
{
    public JobAndLogoffRecordTests() => AnsiPalette.Enabled = false;

    private static string Dump(ushort opcode, byte[] payload)
        => PacketRecord.Resolve(opcode, payload).DumpToString();

    private static byte[] CStr(string s)
    {
        var body = Encoding.Latin1.GetBytes(s);
        var b = new byte[body.Length + 1];
        body.CopyTo(b, 0);   // trailing NUL already 0
        return b;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var list = new List<byte>();
        foreach (var p in parts) list.AddRange(p);
        return list.ToArray();
    }

    [Fact]
    public void Logoff_FullParse()
    {
        var b = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(0), 0x000A1234);
        var dump = Dump(0x0003, b);
        Assert.Contains("GameID", dump);
        Assert.Contains("0x000A1234", dump);
        Assert.DoesNotContain("???", dump);
        Assert.DoesNotContain("truncated", dump);
    }

    [Fact]
    public void UiTrigger_FullParse()
    {
        var b = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(0), 101);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(4), 7);
        var dump = Dump(0x0065, b);
        Assert.Contains("ParamA", dump);
        Assert.Contains("101", dump);
        Assert.Contains("ParamB", dump);
        Assert.DoesNotContain("???", dump);
        Assert.DoesNotContain("truncated", dump);
    }

    [Fact]
    public void JobDescription_FullParse()
    {
        var head = new byte[5];
        BinaryPrimitives.WriteInt32LittleEndian(head.AsSpan(0), 0x0000ABCD);
        head[4] = 1;
        var b = Concat(head, CStr("Patrol the belt"), CStr("Clear the asteroids near Luna."));
        var dump = Dump(0x0094, b);
        Assert.Contains("JobID", dump);
        Assert.Contains("0x0000ABCD", dump);
        Assert.Contains("Available", dump);
        Assert.Contains("Title", dump);
        Assert.Contains("Patrol the belt", dump);
        Assert.Contains("Description", dump);
        Assert.DoesNotContain("???", dump);
        Assert.DoesNotContain("truncated", dump);
    }

    [Fact]
    public void JobList_TwoEntries_FullParse_EndDriven()
    {
        byte[] Entry(int id, int cat, int level, string title, string sponsor, string reward)
        {
            var h = new byte[16];
            BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(0), id);
            BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(4), cat);
            BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(8), 0);
            BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(12), level);
            return Concat(h, CStr(title), CStr(sponsor), CStr(reward));
        }

        var count = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(count.AsSpan(0), 5);  // placeholder > entries
        var b = Concat(count,
            Entry(0x10, 0, 5, "Bounty", "RedDragon", "250 XP"),
            Entry(0x11, 2, 9, "Haul",   "InfinityE", "450 XP"));

        var dump = Dump(0x0093, b);
        Assert.Contains("CountPlaceholder", dump);
        Assert.Contains("[0].ID", dump);
        Assert.Contains("[0].Title", dump);
        Assert.Contains("Bounty", dump);
        Assert.Contains("[1].ID", dump);
        Assert.Contains("[1].Reward", dump);
        Assert.Contains("450 XP", dump);
        Assert.DoesNotContain("???", dump);
        Assert.DoesNotContain("truncated", dump);
    }
}
