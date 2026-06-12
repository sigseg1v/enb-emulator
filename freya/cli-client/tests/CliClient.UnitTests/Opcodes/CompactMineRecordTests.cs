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
/// Byte-pins the two compact-band decoders added in Phase AB:
/// <see cref="StartProspectRecord"/> (0x2012) and <see cref="TractorOreRecord"/>
/// (0x2013). These opcodes never reach the client -- the proxy expands them
/// into client-facing packets (plans/27 §3a) -- so there is no retail
/// client-leg capture to replay; instead we synthesise the EXACT bytes the
/// server emitter produces (server/src/PlayerSkills.cpp MineResource /
/// UseTractorBeam) and assert the decoder reads every field AND consumes every
/// byte (no <c>???</c> gap = full coverage).
///
/// This is the cross-check half of the three-way invariant: the byte layout
/// encoded here is the same one Player::MineResource / UseTractorBeam writes
/// and the same one the proxy fabricator reads. If any of the three drifts,
/// this test (or the proxy) breaks.
/// </summary>
public sealed class CompactMineRecordTests
{
    public CompactMineRecordTests() => AnsiPalette.Enabled = false;

    private static string Dump(ushort opcode, byte[] payload)
        => PacketRecord.Resolve(opcode, payload).DumpToString();

    // ── 0x2012 START_PROSPECT (fixed 20 bytes) ──────────────────────────────

    private static byte[] BuildStartProspect(
        int playerGid, int asteroidGid, int effectUid, uint prospectTick, uint drainMs)
    {
        var b = new byte[20];
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(0), playerGid);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(4), asteroidGid);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(8), effectUid);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(12), prospectTick);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(16), drainMs);
        return b;
    }

    [Fact]
    public void StartProspect_DecodesAllFields_AndConsumesEveryByte()
    {
        var dump = Dump(0x2012, BuildStartProspect(0x000A1234, 0x000B5678, 0x0000ABCD, 0x11223344, 5000));

        Assert.Contains("PlayerGID", dump);
        Assert.Contains("0x000A1234", dump);
        Assert.Contains("AsteroidGID", dump);
        Assert.Contains("0x000B5678", dump);
        Assert.Contains("EffectUID", dump);
        Assert.Contains("0x0000ABCD", dump);
        Assert.Contains("ProspectTick", dump);
        Assert.Contains("0x11223344", dump);
        Assert.Contains("DrainMs", dump);
        Assert.Contains("5000", dump);

        Assert.DoesNotContain("???", dump);          // every byte decoded
        Assert.DoesNotContain("truncated", dump);
    }

    [Fact]
    public void StartProspect_Truncated_Flags()
    {
        var dump = Dump(0x2012, new byte[19]);        // one byte short of the fixed 20
        Assert.Contains("truncated", dump);
    }

    // ── 0x2013 TRACTOR_ORE (variable; same layout as 0x2014 LOOT_ITEM) ───────

    private static byte[] BuildTractorOre(
        int gameId, int articleUid, int articleEffectUid, short baseAsset, uint prospectTick,
        string name, uint tractorTime, float tractorSpeed, float px, float py, float pz)
    {
        var nameBytes = Encoding.Latin1.GetBytes(name);
        var b = new byte[20 + nameBytes.Length + 20];
        var s = b.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(s.Slice(0), gameId);
        BinaryPrimitives.WriteInt32LittleEndian(s.Slice(4), articleUid);
        BinaryPrimitives.WriteInt32LittleEndian(s.Slice(8), articleEffectUid);
        BinaryPrimitives.WriteInt16LittleEndian(s.Slice(12), baseAsset);
        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(14), prospectTick);
        BinaryPrimitives.WriteInt16LittleEndian(s.Slice(18), (short)nameBytes.Length);
        nameBytes.CopyTo(b, 20);
        int off = 20 + nameBytes.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(s.Slice(off), tractorTime);
        BinaryPrimitives.WriteSingleLittleEndian(s.Slice(off + 4), tractorSpeed);
        BinaryPrimitives.WriteSingleLittleEndian(s.Slice(off + 8), px);
        BinaryPrimitives.WriteSingleLittleEndian(s.Slice(off + 12), py);
        BinaryPrimitives.WriteSingleLittleEndian(s.Slice(off + 16), pz);
        return b;
    }

    [Fact]
    public void TractorOre_DecodesAllFields_AndConsumesEveryByte()
    {
        var dump = Dump(0x2013, BuildTractorOre(
            0x000A1234, 0x00010001, 0x00010002, 4321, 0x11223344,
            "Tungsten", 3000, 250.0f, 100.5f, -200.25f, 300.0f));

        Assert.Contains("GameID", dump);
        Assert.Contains("0x000A1234", dump);
        Assert.Contains("ArticleUID", dump);
        Assert.Contains("0x00010001", dump);
        Assert.Contains("ArticleEffectUID", dump);
        Assert.Contains("0x00010002", dump);
        Assert.Contains("BaseAsset", dump);
        Assert.Contains("4321", dump);
        Assert.Contains("ProspectTick", dump);
        Assert.Contains("0x11223344", dump);
        Assert.Contains("ResourceName", dump);
        Assert.Contains("Tungsten", dump);
        Assert.Contains("TractorTime", dump);
        Assert.Contains("3000", dump);
        Assert.Contains("TractorSpeed", dump);
        Assert.Contains("250.0", dump);
        Assert.Contains("Position", dump);
        Assert.Contains("100.5", dump);
        Assert.Contains("-200.25", dump);

        Assert.DoesNotContain("???", dump);           // name + trailer fully consumed
        Assert.DoesNotContain("overruns", dump);
        Assert.DoesNotContain("missing", dump);
    }

    [Fact]
    public void TractorOre_EmptyName_StillConsumesTrailer()
    {
        var dump = Dump(0x2013, BuildTractorOre(
            1, 2, 3, 7, 0x55667788, "", 1000, 10.0f, 0f, 0f, 0f));
        Assert.Contains("NameLen", dump);
        Assert.DoesNotContain("???", dump);
        Assert.DoesNotContain("overruns", dump);
    }
}
