// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using N7.CliClient.Logging;
using N7.CliClient.Opcodes.Records;
using Xunit;

namespace N7.CliClient.UnitTests.Opcodes;

/// <summary>
/// Byte-pins five previously-unparsed server-emitted opcodes built with the
/// AddData/AddDataLS string builders: 0x001E GROUP, 0x00A6 CLIENT_CHAT_ERROR,
/// 0x00C8 GUILD_RECRUIT_CONFIRM_SECTOR, 0x00CC GUILD_SIMPLE_SECTOR_CLIENT,
/// 0x00D3 GUILD_RANK_NAMES_SECTOR. Each synthesises the exact emitter bytes and
/// asserts every byte is consumed (no <c>???</c> gap). AddDataLS = uint16 LE
/// length + raw bytes (no NUL); the guild count/index fields are BIG-ENDIAN
/// (htonl) and written BE here to match.
/// </summary>
public sealed class GuildAndGroupRecordTests
{
    public GuildAndGroupRecordTests() => AnsiPalette.Enabled = false;

    private static string Dump(ushort opcode, byte[] payload)
        => PacketRecord.Resolve(opcode, payload).DumpToString();

    private static byte[] AddDataLS(string s)
    {
        var body = Encoding.Latin1.GetBytes(s);
        var b = new byte[2 + body.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(0), (ushort)body.Length);
        body.CopyTo(b, 2);
        return b;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var list = new List<byte>();
        foreach (var p in parts) list.AddRange(p);
        return list.ToArray();
    }

    [Fact]
    public void Group_FullParse_NulTerminatedMessage()
    {
        // Len = strlen+1 (includes trailing NUL); flag 0x01; "join?" + NUL.
        string text = "join?";
        var msg = Encoding.Latin1.GetBytes(text);
        var b = new byte[3 + msg.Length + 1];
        BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(0), (short)(msg.Length + 1));
        b[2] = 0x01;
        msg.CopyTo(b, 3);   // trailing byte already 0

        var dump = Dump(0x001E, b);
        Assert.Contains("Len", dump);
        Assert.Contains("Flag", dump);
        Assert.Contains("join?", dump);
        Assert.DoesNotContain("???", dump);
        Assert.DoesNotContain("truncated", dump);
    }

    [Fact]
    public void ClientChatError_FullParse()
    {
        var b = Concat(
            new byte[8],                       // reason + type placeholders, set below
            AddDataLS("Bob"),
            AddDataLS("#trade"),
            AddDataLS("extra"));
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(0), 17);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(4), 3);

        var dump = Dump(0x00A6, b);
        Assert.Contains("Reason", dump);
        Assert.Contains("17", dump);
        Assert.Contains("Type", dump);
        Assert.Contains("Player", dump);
        Assert.Contains("Bob", dump);
        Assert.Contains("Channel", dump);
        Assert.Contains("#trade", dump);
        Assert.Contains("Other", dump);
        Assert.DoesNotContain("???", dump);
        Assert.DoesNotContain("truncated", dump);
    }

    [Fact]
    public void GuildRecruitConfirm_FullParse()
    {
        var b = Concat(AddDataLS("Recruiter"), AddDataLS("MyGuild"));
        var dump = Dump(0x00C8, b);
        Assert.Contains("RecruiterName", dump);
        Assert.Contains("Recruiter", dump);
        Assert.Contains("GuildName", dump);
        Assert.Contains("MyGuild", dump);
        Assert.DoesNotContain("???", dump);
        Assert.DoesNotContain("truncated", dump);
    }

    [Fact]
    public void GuildSimpleSector_FullParse()
    {
        var b = Concat(new byte[4], AddDataLS("param"));
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(0), 6);
        var dump = Dump(0x00CC, b);
        Assert.Contains("Type", dump);
        Assert.Contains("OptionalParam", dump);
        Assert.Contains("param", dump);
        Assert.DoesNotContain("???", dump);
        Assert.DoesNotContain("truncated", dump);
    }

    [Fact]
    public void GuildRankNames_FullParse_BigEndianCountAndIndex()
    {
        // 2 ranks (test uses 2, server always sends 10 -- the parser is count-driven).
        var count = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(count.AsSpan(0), 2);
        var idx1 = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(idx1.AsSpan(0), 1);
        var idx2 = new byte[4]; BinaryPrimitives.WriteInt32BigEndian(idx2.AsSpan(0), 2);
        var b = Concat(count, AddDataLS("Recruit"), idx1, AddDataLS("Officer"), idx2);

        var dump = Dump(0x00D3, b);
        Assert.Contains("Count", dump);
        Assert.Contains("[0].RankName", dump);
        Assert.Contains("Recruit", dump);
        Assert.Contains("[0].Index", dump);
        Assert.Contains("[1].RankName", dump);
        Assert.Contains("Officer", dump);
        Assert.DoesNotContain("???", dump);
        Assert.DoesNotContain("truncated", dump);
    }
}
