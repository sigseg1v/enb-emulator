// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Logging;
using N7.CliClient.Opcodes.Records;
using Xunit;

namespace N7.CliClient.UnitTests.Captures;

/// <summary>
/// Pins the <see cref="PacketRecord"/> decode path against two live retail
/// Net-7 captures taken 2026-06-02 (a login -> undock -> click-navs -> warp ->
/// logout run, and a longer full-workflow run). The bytes are ground truth: the
/// real server talked to the real client, so every frame here MUST decode with
/// no <see cref="GenericRecord"/> fallback, no undecoded-byte gap, and no
/// anomaly flag. A divergence is our decoder, never the fixture.
///
/// This is the regression lock for the sixteen opcodes that the captures
/// exercise but the corpus in <see cref="RetailRecordDecodeTests"/> did not
/// cover: the nav-display family (0x2018 static objects, 0x2019 resources,
/// 0x0099 navigation), the login/session markers (0x2020, 0x2011, 0x2025,
/// 0x00BA), and the in-game opcodes (0x0020 priority message, 0x006A client
/// sound, 0x2014 loot item, 0x000B object-to-object effect, 0x0064 client
/// damage, 0x0066 open interface, 0x008B attacker updates, 0x008C loot-hulk
/// permission, 0x0054 talk tree, 0x0056 talk-tree action).
/// </summary>
public sealed class LiveTraceDecodeTests
{
    private static readonly IReadOnlyDictionary<string, CaptureFixture> Frames =
        CaptureFixture.Load("live-trace-2026-06-02.txt");

    public LiveTraceDecodeTests()
    {
        // Decode to plain text so the content assertions see no ANSI codes.
        AnsiPalette.Enabled = false;
    }

    private static string Dump(string name)
    {
        var f = Frames[name];
        return PacketRecord.Resolve((ushort)f.Opcode, f.Payload).DumpToString();
    }

    public static IEnumerable<object[]> AllFrames() =>
        Frames.Keys.Select(k => new object[] { k });

    // ── The universal contract ───────────────────────────────────────────────
    // Every frame the live captures produced must decode cleanly. "Cleanly"
    // means: a dedicated record (not the unknown-opcode GenericRecord), every
    // byte accounted for (no auto-rendered '??? (NB)' gap), and no anomaly flag
    // ('[!]') or truncation/overrun complaint. The capture is correct by
    // definition, so anything else is a decoder bug.

    [Theory]
    [MemberData(nameof(AllFrames))]
    public void EveryLiveFrame_DecodesWithNoGapOrFlag(string name)
    {
        var f = Frames[name];
        var record = PacketRecord.Resolve((ushort)f.Opcode, f.Payload);

        Assert.IsNotType<GenericRecord>(record);

        string d = record.DumpToString();
        Assert.DoesNotContain("???", d);
        Assert.DoesNotContain("(NB)", d);
        Assert.DoesNotContain("[!]", d);
        Assert.DoesNotContain("truncated", d);
        Assert.DoesNotContain("overruns", d);
        Assert.DoesNotContain("runs past payload", d);
    }

    [Fact]
    public void Fixture_Loads_AllNinetyOneFrames()
    {
        Assert.Equal(91, Frames.Count);
        Assert.Equal(0x2014, Frames["loot_item_blue_dust"].Opcode);
        Assert.Equal(0x0020, Frames["priority_msg_discovered"].Opcode);
        Assert.Equal(0x000B, Frames["obj2obj_weapon_fire"].Opcode);
        Assert.Equal(0x0054, Frames["talk_tree_bow_greeting"].Opcode);
        Assert.Equal(0x006A, Frames["client_sound_mp3"].Opcode);
        Assert.Equal(0x2018, Frames["static_obj_has_nav"].Opcode);
        Assert.Equal(0x0099, Frames["navigation_first"].Opcode);
    }

    // ── 0x2018 STATIC_OBJECT_CREATE -- the nav-display opcode (Luna bug) ──────
    // A nav beacon with the HAS_NAV flag set: this is exactly the class of
    // object whose absence was the "no nodes on the map" symptom. The SigFlags
    // byte 0xB2 decomposes to HAS_VISITED|IS_NAV|HAS_NAV with NavType 2 -- if any
    // bit decoded wrong the client would not draw the nav on the galaxy map.

    [Fact]
    public void StaticObject_NavBeacon_DecodesSigFlagsAndName()
    {
        string d = Dump("static_obj_has_nav");

        Assert.Contains("CreateType        = 11", d);
        Assert.Contains("SigFlags          = 0xB2  (178)", d);
        Assert.Contains("HAS_VISITED|IS_NAV|HAS_NAV NavType=2", d);
        Assert.Contains("NameLen           = 11", d);
        Assert.Contains("Name              = \"Gate to Sol\"", d);
    }

    // ── 0x0020 PRIORITY_MESSAGE -- the on-screen "Discovered ..." banner ──────
    // The exact banner the user reported seeing ("discovered abandoned scrap
    // yard" on Luna; here the Ishuan gate). Two null-terminated strings then
    // Time/Priority int32s -- a missed terminator would swallow the second
    // string and desync Time.

    [Fact]
    public void PriorityMessage_DiscoveredBanner_DecodesBothLinesAndTrailer()
    {
        string d = Dump("priority_msg_discovered");

        Assert.Contains(
            "Message1          = \"Discovered Sector Gate to Ishuan 800 Explore experience earned.\"",
            d);
        Assert.Contains("Message2          = \"MessageLine\"", d);
        Assert.Contains("Time              = 4000", d);
        Assert.Contains("Priority          = 4", d);
    }

    // ── 0x000B OBJECT_TO_OBJECT_EFFECT -- weapon fire ─────────────────────────
    // Bitmask 0x0007 == EffectID|TimeStamp|Duration. The conditional bit-walk is
    // the whole bug surface; the named effect string and the three conditional
    // fields all landing means the header offsets and the bit map are right.

    [Fact]
    public void ObjectToObjectEffect_WeaponFire_DecodesBitmaskAndConditionals()
    {
        string d = Dump("obj2obj_weapon_fire");

        Assert.Contains("Bitmask           = 0x0007  (7)", d);
        Assert.Contains("EffectID|TimeStamp|Duration", d);
        Assert.Contains("Message           = \"~02/~WEAP_01\"", d);
        Assert.Contains("EffectID", d);
        Assert.Contains("TimeStamp", d);
        Assert.Contains("Duration", d);
    }

    // ── 0x0054 TALK_TREE -- NPC dialog ────────────────────────────────────────
    // The off-by-one-pad-byte layout (emitter does `ptr += 2 + len`). NumBranches
    // landing on the right byte and both branch labels decoding proves the pad
    // byte is consumed.

    [Fact]
    public void TalkTree_BowGreeting_DecodesMainTextAndBranches()
    {
        string d = Dump("talk_tree_bow_greeting");

        Assert.Contains("/bow Greetings Warrior!", d);
        Assert.Contains("NumBranches       = 2", d);
        Assert.Contains("Most kind honored explorer.", d);
        Assert.Contains("Thanks...", d);
    }

    // ── 0x006A CLIENT_SOUND -- server cues a sound file ──────────────────────
    // Length is strlen+1 (includes the null); the name is Length-1 chars then a
    // null, then Channel/Queue. A low-by-one Length read would clip the ".MP3".

    [Fact]
    public void ClientSound_Mp3_DecodesNameLengthChannelQueue()
    {
        string d = Dump("client_sound_mp3");

        Assert.Contains("Length            = 18", d);
        Assert.Contains("SoundName         = \"1512_00_032Se.MP3\"", d);
        Assert.Contains("Channel           = 0", d);
        Assert.Contains("Queue             = 0", d);
    }

    // ── 0x2014 LOOT_ITEM -- a tractorable item floating in space ─────────────
    // The LS-prefixed name then the tractor/position trailer. "Blue Dust2"
    // decoding and the trailer landing proves NameLen drove the offset right.

    [Fact]
    public void LootItem_BlueDust_DecodesNameAndTrailer()
    {
        string d = Dump("loot_item_blue_dust");

        Assert.Contains("NameLen           = 10", d);
        Assert.Contains("ItemName          = \"Blue Dust2\"", d);
        Assert.Contains("TractorSpeed      = 350.0", d);
        Assert.Contains("Position", d);
    }

    // ── 0x2019 RESOURCE_OBJECT_CREATE -- two HSV channels before position ─────
    // The @14 float is 0.0 in every sampled frame, so it cannot be a position
    // coordinate -- it is the 2nd HSV channel (HSV1). The retail layout is
    // Scale + HSV0 + HSV1 + Pos(3) + Orient(4). Our Resource::FormStaticPacket
    // originally had the HSV1 emit commented out, dropping this float and
    // shifting the whole tail; the server was fixed to un-comment it. This pins
    // HSV1 as a distinct field and the position landing AFTER it (X is the large
    // ~49370 coordinate, not the 0.0 that the pre-fix mislabelling produced).
    [Fact]
    public void ResourceObjectCreate_Asteroid_DecodesTwoHsvChannelsThenPosition()
    {
        string d = Dump("resource_obj_00");

        Assert.Contains("Scale             = 1.0", d);
        Assert.Contains("HSV0              = 20.0", d);
        Assert.Contains("HSV1              = 0.0", d);
        Assert.Contains("Position          = (49370.6, 283.1, 885)", d);
        Assert.Contains("NameLen           = 8", d);
        Assert.Contains("Name              = \"Asteroid\"", d);
        Assert.DoesNotContain("Extra", d);
    }
}
