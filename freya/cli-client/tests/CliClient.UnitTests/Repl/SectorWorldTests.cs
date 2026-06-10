// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using System.Text;
using N7.CliClient.Net;
using N7.CliClient.Repl;
using Xunit;

namespace N7.CliClient.UnitTests.Repl;

public sealed class SectorWorldTests
{
    private static Packet Create(int gameId, short baseAsset, sbyte type)
    {
        byte[] p = new byte[23];
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(0, 4), gameId);
        BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(8, 2), baseAsset);
        p[10] = (byte)type;
        return Packet.ForOpcode(0x0004, p);
    }

    private static Packet ConstantPos(int gameId, float x, float y, float z)
    {
        byte[] p = new byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(0, 4), gameId);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(4, 4), x);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(8, 4), y);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(12, 4), z);
        return Packet.ForOpcode(0x0040, p);
    }

    private static Packet StaticNav(int gameId, byte createType, string name,
        float x, float y, float z, float signature, byte sigFlags = 0)
    {
        byte[] nameBytes = Encoding.ASCII.GetBytes(name);
        byte[] p = new byte[60 + nameBytes.Length];
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(0, 4), gameId);
        p[4] = createType;
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(25, 4), x);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(29, 4), y);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(33, 4), z);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(53, 4), signature);
        p[57] = sigFlags; // sig_flags: 0x20 IS_NAV, 0x10 HAS_NAV, low nibble NavType
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(58, 2), (ushort)nameBytes.Length);
        nameBytes.CopyTo(p.AsSpan(60));
        return Packet.ForOpcode(0x2018, p);
    }

    private static Packet Navigation(int gameId, float sig, bool visited, int navType, byte huge)
    {
        byte[] p = new byte[14];
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(0, 4), gameId);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(4, 4), sig);
        p[8] = (byte)(visited ? 1 : 0);
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(9, 4), navType);
        p[13] = huge;
        return Packet.ForOpcode(0x0099, p);
    }

    private static Packet PlanetPos(int gameId, float x, float y, float z)
    {
        // 0x003F PLANET_POSITIONAL_UPDATE: GameID@0, TimeStamp@4, Position@8.
        byte[] p = new byte[48];
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(0, 4), gameId);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(8, 4), x);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(12, 4), y);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(16, 4), z);
        return Packet.ForOpcode(0x003F, p);
    }

    // Minimal AuxShipIndex 0x001B frame with flag 0 (Name), optionally flag 13
    // (MaxSpeed, F32), and flag 26 (CombatLevel) set, matching the catalog's
    // ShipIndex schema layout. A field with flagNum N is present iff bit (N+4)
    // is set, and present fields serialise in flag order:
    // [u32 GameID][u16 BodyLen=payload-6][u8 ver=1][8 flag bytes][Name][MaxSpeed?][lvl].
    private static Packet ShipAux(uint gameId, string name, uint combatLevel,
        float? maxSpeed = null, string? faction = null)
    {
        byte[] nameB = Encoding.ASCII.GetBytes(name);
        byte[] facB = faction is null ? System.Array.Empty<byte>() : Encoding.ASCII.GetBytes(faction);
        int speedBytes = maxSpeed is null ? 0 : 4;
        int facBytes = faction is null ? 0 : 2 + facB.Length;
        byte[] p = new byte[7 + 8 + (2 + nameB.Length) + speedBytes + 4 + facBytes];
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0, 4), gameId);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(4, 2), (ushort)(p.Length - 6));
        p[6] = 1;                       // version
        p[7 + 0] = 0x10;                // bit 4  -> flag 0  (Name)
        if (maxSpeed is not null) p[7 + 2] = 0x02;   // bit 17 -> flag 13 (MaxSpeed)
        p[7 + 3] = 0x40;                // bit 30 -> flag 26 (CombatLevel)
        if (faction is not null) p[7 + 7] = 0x20;    // bit 61 -> flag 57 (FactionIdentifier)
        int o = 7 + 8;
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(o, 2), (ushort)nameB.Length); o += 2;
        nameB.CopyTo(p.AsSpan(o)); o += nameB.Length;
        if (maxSpeed is { } ms) { BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(o, 4), ms); o += 4; }
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(o, 4), combatLevel); o += 4;
        if (faction is not null)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(o, 2), (ushort)facB.Length); o += 2;
            facB.CopyTo(p.AsSpan(o));
        }
        return Packet.ForOpcode(0x001B, p);
    }

    // 0x0089 RELATIONSHIP (struct Relationship, 9 bytes): ObjectID i32 BE
    // (server applies ntohl at emit), Reaction i32 LE, IsAttacking u8.
    private static Packet Relationship(int gameId, int reaction, bool attacking)
    {
        byte[] p = new byte[9];
        BinaryPrimitives.WriteInt32BigEndian(p.AsSpan(0, 4), gameId);
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(4, 4), reaction);
        p[8] = (byte)(attacking ? 1 : 0);
        return Packet.ForOpcode(0x0089, p);
    }

    [Theory]
    [InlineData(0, "hostile")]
    [InlineData(1, "shun")]
    [InlineData(2, "friendly")]
    [InlineData(3, "adoration")]
    public void Ingest_Relationship_SetsReactionAndDisposition(int reaction, string label)
    {
        // Reaction enum per PacketStructures.h RELATIONSHIP_* and confirmed
        // against an upstream net7 Ishuan capture (only 0/1/2/3 ever on wire).
        var w = new SectorWorld();
        w.Ingest(Create(0x1872C, baseAsset: 398, type: 0));        // a mob
        w.Ingest(Relationship(0x1872C, reaction, attacking: true));

        var obj = w.NearestTo(0)[0].Obj;
        Assert.Equal(reaction, obj.Reaction);
        Assert.True(obj.IsAttacking);
        Assert.Equal(label, SectorWorld.ReactionName(obj.Reaction));
    }

    [Fact]
    public void ReactionName_UnknownReaction_IsNotMisreported()
    {
        Assert.Equal("unknown", SectorWorld.ReactionName(null));
        Assert.Equal("reaction=4", SectorWorld.ReactionName(4)); // no phantom NEUTRAL/FRIENDLY
    }

    [Fact]
    public void Ingest_Aux_DecodesFactionIdentifier()
    {
        // FactionIdentifier is ShipIndex flag 57 (AuxSchemas.ShipIndex); it must
        // surface through the world model so mob/ship disposition has context.
        var w = new SectorWorld();
        w.Ingest(ShipAux(0x40000050, "Sharim Trader", combatLevel: 12, faction: "Sharim"));

        var obj = w.NearestTo(0)[0].Obj;
        Assert.Equal("Sharim Trader", obj.Name);
        Assert.Equal("Sharim", obj.Faction);
    }

    [Fact]
    public void Ingest_CreateAndPosition_TracksTypeAndDistance()
    {
        var w = new SectorWorld();
        w.Ingest(Create(0x1000, baseAsset: 12, type: 12));   // station
        w.Ingest(ConstantPos(0x1000, 30, 40, 0));            // 50 from origin
        w.Ingest(ConstantPos(0x0001, 0, 0, 0));              // self at origin

        var rows = w.NearestTo(0x0001);
        Assert.Single(rows);
        Assert.Equal(0x1000, rows[0].Obj.GameId);
        Assert.Equal("station", SectorWorld.TypeName(rows[0].Obj));
        Assert.NotNull(rows[0].Dist);
        Assert.Equal(50f, rows[0].Dist!.Value, 3);
    }

    [Fact]
    public void Ingest_StaticNav_DecodesNameAndPosition()
    {
        var w = new SectorWorld();
        w.Ingest(StaticNav(0x2000, createType: 37, "Nav Luna Hub", 100, 200, 0, 20000f));

        var rows = w.NearestTo(0); // no self -> distance null but object present
        var nav = Assert.Single(rows).Obj;
        Assert.Equal("Nav Luna Hub", nav.Name);
        Assert.Equal(100f, nav.X, 3);
        Assert.Equal(20000f, nav.Signature!.Value, 1);
    }

    [Fact]
    public void Ingest_Navigation_MarksNavTypeAndVisited()
    {
        var w = new SectorWorld();
        w.Ingest(StaticNav(0x2000, createType: 37, "Luna Station", 0, 0, 0, 73000f));
        w.Ingest(Navigation(0x2000, 78000f, visited: true, navType: 2, huge: 1));

        var nav = w.NearestTo(0)[0].Obj;
        Assert.True(nav.IsNav);
        Assert.Equal(2, nav.NavType);
        Assert.True(nav.Visited);
        Assert.Equal("nav (major)", SectorWorld.TypeName(nav));
    }

    // A HIDDEN nav (clickable but off-minimap: 0x2018 IS_NAV/NavType>0 with
    // HAS_NAV clear) emits NO 0x0099 NAVIGATION frame -- the server gates
    // SendNavigation on AppearsInRadar (NavTypeClass.cpp:417). Before reading
    // sig_flags@57 directly, such a nav (e.g. "Traders Run", "Strange Ship")
    // was mislabelled a deco because IsNav only flipped on a 0x0099.
    [Theory]
    // sigFlags, expect0099, expectedLabel
    [InlineData(0x00, false, "deco")]                // NavType 0, no IS_NAV -> true deco
    [InlineData(0x31, false, "nav")]                 // IS_NAV|HAS_NAV|NavType1 -> visible warp-path nav
    [InlineData(0x32, false, "nav (major)")]         // IS_NAV|HAS_NAV|NavType2 -> visible destination nav
    [InlineData(0x21, false, "nav (hidden)")]        // IS_NAV|NavType1, no HAS_NAV -> hidden nav
    [InlineData(0x22, false, "nav (hidden, major)")] // IS_NAV|NavType2, no HAS_NAV -> hidden destination nav
    public void TypeName_StaticSigFlags_ClassifyNavVsDeco(byte sigFlags, bool _, string expected)
    {
        var w = new SectorWorld();
        w.Ingest(StaticNav(0x3000, createType: 37, "Traders Run 3", 100, 200, 0, 30000f, sigFlags));
        var obj = w.NearestTo(0)[0].Obj;
        Assert.Equal(expected, SectorWorld.TypeName(obj));
    }

    [Fact]
    public void Ingest_Remove_DropsObject()
    {
        var w = new SectorWorld();
        w.Ingest(Create(0x1000, 12, 12));
        Assert.Equal(1, w.Count);
        byte[] rm = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(rm, 0x1000);
        w.Ingest(Packet.ForOpcode(0x0007, rm));
        Assert.Equal(0, w.Count);
    }

    [Fact]
    public void Render_PrintsSelfPosition4dp_And_NearbyRows()
    {
        var w = new SectorWorld();
        w.Ingest(ConstantPos(0x0001, 1.23456f, -2f, 0f));    // self
        w.Ingest(Create(0x1000, 5, 3));                       // a planet
        w.Ingest(ConstantPos(0x1000, 10, 0, 0));

        var sw = new StringWriter();
        w.Render(sw, 0x0001);
        string outp = sw.ToString();

        Assert.Contains("you:", outp);
        Assert.Contains("1.2346", outp);   // 4 d.p. rounding of self X
        Assert.Contains("planet", outp);
        Assert.Contains("nearby:", outp);
    }

    [Fact]
    public void Ingest_PlanetPositionalUpdate_GivesPlanetADistance()
    {
        var w = new SectorWorld();
        w.Ingest(ConstantPos(0x0001, 0, 0, 0));      // self at origin
        w.Ingest(Create(0x1885F, baseAsset: 0xFF, type: 3)); // a planet
        w.Ingest(PlanetPos(0x1885F, 30, 40, 0));     // planet 50 away via 0x003F

        var row = w.NearestTo(0x0001)[0];
        Assert.Equal("planet", SectorWorld.TypeName(row.Obj));
        Assert.NotNull(row.Dist);
        Assert.Equal(50f, row.Dist!.Value, 3);
    }

    [Fact]
    public void Ingest_ShipAux_DecodesNameAndCombatLevel()
    {
        var w = new SectorWorld();
        w.Ingest(Create(0x40000050, baseAsset: 0x649, type: 1));
        w.Ingest(ShipAux(0x40000050, "Trader Bob", combatLevel: 42));

        var obj = w.NearestTo(0)[0].Obj;   // no self -> still tracked
        Assert.Equal("Trader Bob", obj.Name);
        Assert.Equal(42, obj.Level);
    }

    [Fact]
    public void Ingest_ShipAux_WithMaxSpeed_ExposesSelfSpeed()
    {
        // The flight loop reads SelfSpeed to pick its step size; a ship aux that
        // announces MaxSpeed (flag 13) must surface it through the world model.
        var w = new SectorWorld();
        w.Ingest(ShipAux(0x40000050, "Cli Pilot", combatLevel: 7, maxSpeed: 1850.5f));

        Assert.Equal(1850.5f, w.SelfSpeed(0x40000050)!.Value, 3);
        // Name + level still decode alongside the new field.
        var obj = w.NearestTo(0)[0].Obj;
        Assert.Equal("Cli Pilot", obj.Name);
        Assert.Equal(7, obj.Level);
    }

    [Fact]
    public void SelfSpeed_WithoutAMaxSpeedAux_IsNull()
    {
        var w = new SectorWorld();
        w.Ingest(ShipAux(0x40000050, "Slowpoke", combatLevel: 3));   // no MaxSpeed flag
        Assert.Null(w.SelfSpeed(0x40000050));
        Assert.Null(w.SelfSpeed(0x12345678));                        // unknown object
    }

    [Fact]
    public void Ingest_SelfPlayerIndexAux_GameIdZero_IsIgnored()
    {
        // A PlayerIndex-shaped aux keys to GameID 0 and its nested names
        // belong to no single object -- it must not create a phantom gid=0.
        var w = new SectorWorld();
        byte[] p = new byte[7 + 8 + 6];
        // GameID 0, version 1, no flags set -> empty body. Walks as the
        // gid-zero PlayerIndex candidate (or simply yields no object).
        p[6] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(4, 2), (ushort)(p.Length - 6));
        w.Ingest(Packet.ForOpcode(0x001B, p));
        Assert.Equal(0, w.Count);
    }

    [Fact]
    public void Render_ShowsOwnShipNameAndLevel()
    {
        var w = new SectorWorld();
        w.Ingest(ConstantPos(0x40000050, 5, 0, 0));
        w.Ingest(ShipAux(0x40000050, "Cli Pilot", combatLevel: 7));

        var sw = new StringWriter();
        w.Render(sw, 0x40000050);
        string outp = sw.ToString();

        Assert.Contains("you:", outp);
        Assert.Contains("Cli Pilot", outp);
        Assert.Contains("lvl 7", outp);
    }

    // 0x2019 RESOURCE_OBJECT_CREATE (Resource::FormStaticPacket): the compact
    // prospectable-roid create. On the raw MVAS leg the proxy is bypassed so the
    // CLI sees the 0x2019 directly (the proxy would otherwise consume it and
    // re-emit a 0x0004 Type=38). Layout per ResourceObjectCreateRecord:
    // GameID@0, BaseAsset i16@4, Position f32@18/22/26, NameLen i16@46, Name@48.
    private static Packet ResourceCreate(int gameId, short baseAsset, string name,
        float x, float y, float z)
    {
        byte[] nameBytes = Encoding.ASCII.GetBytes(name);
        byte[] p = new byte[48 + nameBytes.Length];
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(0, 4), gameId);
        BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(4, 2), baseAsset);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(18, 4), x);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(22, 4), y);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(26, 4), z);
        BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(46, 2), (short)nameBytes.Length);
        nameBytes.CopyTo(p.AsSpan(48));
        return Packet.ForOpcode(0x2019, p);
    }

    [Fact]
    public void Ingest_ResourceCreate_TracksRoidAsResourceWithPosition()
    {
        var w = new SectorWorld();
        w.Ingest(ConstantPos(0x40000050, 0, 0, 0)); // self at origin
        w.Ingest(ResourceCreate(0x000187EB, baseAsset: 1234, "Carbon", 300, 400, 0));

        // CreateType=38 -> TypeName "resource"; locatable with a real position.
        var row = w.NearestTo(0x40000050).First(r => r.Obj.GameId == 0x000187EB);
        Assert.Equal("resource", SectorWorld.TypeName(row.Obj));
        Assert.Equal("Carbon", row.Obj.Name);
        var pos = w.PositionOf(0x000187EB);
        Assert.NotNull(pos);
        Assert.Equal(300f, pos!.Value.X, 3);
        Assert.Equal(400f, pos.Value.Y, 3);
        Assert.Equal(500f, row.Dist!.Value, 0); // sqrt(300^2+400^2) = 500
    }

    [Fact]
    public void Ingest_ResourceCreate_Truncated_IsIgnored()
    {
        // A frame shorter than the 48-byte fixed header must not throw or create
        // a phantom roid -- the drain swallows malformed frames.
        var w = new SectorWorld();
        w.Ingest(Packet.ForOpcode(0x2019, new byte[20]));
        Assert.Equal(0, w.Count);
    }
}
