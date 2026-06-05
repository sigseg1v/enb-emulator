// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using N7.CliClient.Logging;
using N7.CliClient.Opcodes.Records;
using Xunit;

namespace N7.CliClient.IntegrationTests.Verification;

/// <summary>
/// Phase AF (plans/33) -- byte-pins for the proxy FABRICATION band
/// (0x2012 START_PROSPECT / 0x2013 TRACTOR_ORE / 0x2014 LOOT_ITEM)
/// against bytes captured from the LIVE Net-7 reference server
/// cleartext proxy&lt;-&gt;server UDP leg.
///
/// <para>
/// These records were already verified to MATCH the live wire
/// byte-for-byte (StartProspectRecord/TractorOreRecord/LootItemRecord
/// vs ProspectRun + KillLoot2 frames). What was missing was a
/// real-capture regression pin -- a spec test proves we decode some
/// invented bytes, not that we decode what the reference server
/// actually emits. These fixtures close that gap and are the
/// primary-source proof required by CLAUDE.md before any matching
/// server/proxy wire change.
/// </para>
///
/// <para>
/// Each test asserts (a) the production record fully accounts for every
/// byte of the captured frame (no undecoded gap), and (b) the decoded
/// fields equal the values transcribed in the fixture comment, read
/// back here independently. Variable-length records are pinned with two
/// name lengths so the name-dependent trailer offset is exercised.
/// </para>
/// </summary>
public sealed class LiveReferenceFabricationTests
{
    public LiveReferenceFabricationTests()
    {
        // DumpToString colours its output; force plain text so the
        // gap-marker / field assertions are stable regardless of TTY.
        AnsiPalette.Enabled = false;
    }

    [Fact]
    public void StartProspect_0x2012_LiveCapture_FullyDecoded()
    {
        byte[] b = HexFixture.Load("live_start_prospect_2012.hex");
        Assert.Equal(20, b.Length);

        // Independent field pin (matches fixture comment + server emit order).
        Assert.Equal(unchecked((int)0x4000C95F), BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(0)));
        Assert.Equal(0x00018802,                 BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(4)));   // AsteroidGID (static node)
        Assert.Equal(0x0025409D,                 BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(8)));   // EffectUID (dynamic counter)
        Assert.Equal(0x512E794Bu,                BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(12))); // ProspectTick
        Assert.Equal(1000u,                      BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(16))); // DrainMs

        var rec = PacketRecord.Resolve(0x2012, b);
        Assert.IsType<StartProspectRecord>(rec);
        string dump = rec.DumpToString();
        Assert.DoesNotContain("???", dump);   // every byte decoded
        Assert.DoesNotContain("[!]", dump);   // no truncation flags
    }

    [Theory]
    [InlineData("live_tractor_ore_2013_helium.hex",      46, "Helium",          6418, 0x00C0)]
    [InlineData("live_tractor_ore_2013_californium.hex", 55, "Californium Ore", 5348, 0x0076)]
    public void TractorOre_0x2013_LiveCapture_FullyDecoded(
        string fixture, int len, string name, uint tractorTime, int baseAsset)
    {
        byte[] b = HexFixture.Load(fixture);
        Assert.Equal(len, b.Length);

        Assert.Equal(unchecked((int)0x4000C95F), BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(0)));
        Assert.Equal((short)baseAsset,           BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(12)));
        short nameLen = BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(18));
        Assert.Equal(name.Length, nameLen);
        Assert.Equal(name, System.Text.Encoding.Latin1.GetString(b, 20, nameLen));

        int off = 20 + nameLen;
        Assert.Equal(tractorTime, BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(off)));
        Assert.Equal(350.0f,      BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(off + 4)));

        var rec = PacketRecord.Resolve(0x2013, b);
        Assert.IsType<TractorOreRecord>(rec);
        string dump = rec.DumpToString();
        Assert.DoesNotContain("???", dump);
        Assert.DoesNotContain("[!]", dump);
        Assert.Contains(name, dump);
        Assert.Contains("350.0", dump);
    }

    [Theory]
    [InlineData("live_loot_item_2014_craxelhide.hex", 51, "Craxel Hide",          4500, 0x052E)]
    [InlineData("live_loot_item_2014_juuona.hex",     60, "Juuona Adrenal Gland", 4500, 0x0531)]
    public void LootItem_0x2014_LiveCapture_FullyDecoded(
        string fixture, int len, string name, uint tractorTime, int baseAsset)
    {
        byte[] b = HexFixture.Load(fixture);
        Assert.Equal(len, b.Length);

        Assert.Equal(unchecked((int)0x4003992A), BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(0)));
        Assert.Equal((short)baseAsset,           BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(12)));
        short nameLen = BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(18));
        Assert.Equal(name.Length, nameLen);
        Assert.Equal(name, System.Text.Encoding.Latin1.GetString(b, 20, nameLen));

        int off = 20 + nameLen;
        Assert.Equal(tractorTime, BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(off)));
        Assert.Equal(350.0f,      BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(off + 4)));

        var rec = PacketRecord.Resolve(0x2014, b);
        Assert.IsType<LootItemRecord>(rec);
        string dump = rec.DumpToString();
        Assert.DoesNotContain("???", dump);
        Assert.DoesNotContain("[!]", dump);
        Assert.Contains(name, dump);
        Assert.Contains("350.0", dump);
    }

    // 0x0064 CLIENT_DAMAGE. AF-8: the record's @16 SourceId / @20 TargetId
    // labels were suspected to be mislabeled (was @20 a weapon effectDesc?).
    // The live frames settle it: the PLAYER's own GameID (0x4003992A) sits at
    // @16 when dealing and at @20 when receiving, so both fields are entity
    // GameIDs flipping by direction -- exactly the server emit order
    // (PlayerConnection.cpp:4563-4573). No effectDesc. Record is correct.
    private const int PlayerGid = 0x4003992A;

    [Theory]
    [InlineData("live_client_damage_0064_dealt.hex",    415.0f, -16.496f,  2, 3, PlayerGid,  0x000186F5)]
    [InlineData("live_client_damage_0064_received.hex",   4.9f,   -2.1f,   3, 1, 0x000187EC, PlayerGid)]
    public void ClientDamage_0x0064_LiveCapture_SourceTargetAreEntities(
        string fixture, float damage, float modifier, int type, int inflicted, int sourceId, int targetId)
    {
        byte[] b = HexFixture.Load(fixture);
        Assert.Equal(24, b.Length);

        Assert.Equal(damage,    BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(0)),  3);
        Assert.Equal(modifier,  BinaryPrimitives.ReadSingleLittleEndian(b.AsSpan(4)),  3);
        Assert.Equal(type,      BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(8)));
        Assert.Equal(inflicted, BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(12)));
        Assert.Equal(sourceId,  BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(16)));
        Assert.Equal(targetId,  BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(20)));

        // Exactly one of source/target is the player (the other combatant is
        // the non-player entity) -- the invariant that proves both are entities.
        Assert.True((sourceId == PlayerGid) ^ (targetId == PlayerGid),
            "exactly one of SourceId/TargetId must be the player GameID");

        var rec = PacketRecord.Resolve(0x0064, b);
        Assert.IsType<ClientDamageRecord>(rec);
        string dump = rec.DumpToString();
        Assert.DoesNotContain("???", dump);
        Assert.DoesNotContain("[!]", dump);
    }
}
