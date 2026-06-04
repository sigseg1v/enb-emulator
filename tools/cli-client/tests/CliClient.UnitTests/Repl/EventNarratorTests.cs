// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using System.Text;
using N7.CliClient.Net;
using N7.CliClient.Repl;
using Xunit;

namespace N7.CliClient.UnitTests.Repl;

/// <summary>
/// Covers the SectorWorld Appeared/Departed events and the EventNarrator's
/// formatting of them plus the directly-parsed opcode notices (damage, warp,
/// sector load, system message). Assertions match on the plaintext fragment,
/// which survives intact inside the ANSI colour wrapper.
/// </summary>
public sealed class EventNarratorTests
{
    private const int SelfId = 0x00000001;

    private static EventNarrator NarratorFor(SectorWorld w, int self = SelfId) =>
        new(w, () => self);

    // --- frame builders (mirror SectorWorldTests) ---

    private static Packet Create(int gameId, sbyte type)
    {
        byte[] p = new byte[23];
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(0, 4), gameId);
        BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(8, 2), 100);
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

    private static Packet Relationship(int gameId, int reaction)
    {
        byte[] p = new byte[9];
        BinaryPrimitives.WriteInt32BigEndian(p.AsSpan(0, 4), gameId);
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(4, 4), reaction);
        p[8] = 1;
        return Packet.ForOpcode(0x0089, p);
    }

    private static Packet Avatar(int gameId, string name)
    {
        byte[] p = new byte[24];
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(0, 4), gameId);
        Encoding.ASCII.GetBytes(name).CopyTo(p.AsSpan(4));
        return Packet.ForOpcode(0x0061, p);
    }

    private static Packet ShipAux(uint gameId, string name, uint combatLevel)
    {
        byte[] nameB = Encoding.ASCII.GetBytes(name);
        byte[] p = new byte[7 + 8 + (2 + nameB.Length) + 4];
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0, 4), gameId);
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(4, 2), (ushort)(p.Length - 6));
        p[6] = 1;                  // version
        p[7 + 0] = 0x10;           // bit 4  -> flag 0  (Name)
        p[7 + 3] = 0x40;           // bit 30 -> flag 26 (CombatLevel)
        int o = 7 + 8;
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(o, 2), (ushort)nameB.Length); o += 2;
        nameB.CopyTo(p.AsSpan(o)); o += nameB.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(o, 4), combatLevel);
        return Packet.ForOpcode(0x001B, p);
    }

    private static Packet Remove(int gameId)
    {
        byte[] p = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(p, gameId);
        return Packet.ForOpcode(0x0007, p);
    }

    private static Packet Damage(float dmg, int sourceId, int targetId)
    {
        byte[] p = new byte[24];
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(0, 4), dmg);
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(16, 4), sourceId);
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(20, 4), targetId);
        return Packet.ForOpcode(0x0064, p);
    }

    private static Packet WarpIndex(int index)
    {
        byte[] p = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(p, index);
        return Packet.ForOpcode(0x009C, p);
    }

    private static Packet Handoff(int toSectorId)
    {
        byte[] p = new byte[24];
        BinaryPrimitives.WriteInt32BigEndian(p.AsSpan(20, 4), toSectorId); // BE, like the server
        return Packet.ForOpcode(0x003A, p);
    }

    private static Packet MessageString(string msg, byte color = 7)
    {
        byte[] m = Encoding.ASCII.GetBytes(msg);
        byte[] p = new byte[3 + m.Length + 1];
        BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(0, 2), (short)(m.Length + 1));
        p[2] = color;
        m.CopyTo(p.AsSpan(3));
        return Packet.ForOpcode(0x001D, p);
    }

    // --- scanner contact: appeared ---

    [Fact]
    public void Appeared_Mob_NarratesLevelHostilityNameAndDistance()
    {
        var w = new SectorWorld();
        const int mob = 0x1872C;
        w.Ingest(Create(mob, type: 1));
        w.Ingest(ConstantPos(SelfId, 0, 0, 0));
        w.Ingest(ConstantPos(mob, 30, 40, 0));      // 50 units away
        w.Ingest(Relationship(mob, 0));             // hostile
        var aux = ShipAux((uint)mob, "Mbonae Whatever", combatLevel: 17);
        var events = w.Ingest(aux);                 // names the mob -> announce

        var lines = NarratorFor(w).Narrate(aux, events);

        string line = Assert.Single(lines);
        Assert.Contains("appeared in scanner range", line);
        Assert.Contains("L17", line);
        Assert.Contains("hostile", line);
        Assert.Contains("Mbonae Whatever", line);
        Assert.Contains("d=50", line);
    }

    [Fact]
    public void Appeared_FiresExactlyOnce_DespiteLaterFrames()
    {
        var w = new SectorWorld();
        const int mob = 0x40000050;
        w.Ingest(Create(mob, type: 1));
        var first = w.Ingest(ShipAux((uint)mob, "Trader Bob", 42));
        Assert.Single(first);                       // announced here
        var again = w.Ingest(ShipAux((uint)mob, "Trader Bob", 42));
        Assert.Empty(again);                        // no re-announce
        var moved = w.Ingest(ConstantPos(mob, 9, 9, 9));
        Assert.Empty(moved);
    }

    [Fact]
    public void Appeared_PlayerAvatar_Narrates()
    {
        var w = new SectorWorld();
        const int other = 0x20000099;
        var ev = w.Ingest(Avatar(other, "Yee"));
        var lines = NarratorFor(w).Narrate(Avatar(other, "Yee"), ev);
        string line = Assert.Single(lines);
        Assert.Contains("Avatar", line);
        Assert.Contains("Yee", line);
        Assert.Contains("appeared in scanner range", line);
    }

    [Fact]
    public void Appeared_StaticContent_DoesNotNarrate()
    {
        var w = new SectorWorld();
        // A station (CreateType 12) named via a 0x2018 frame is scenery, not a
        // scanner contact.
        byte[] p = new byte[60 + 4];
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(0, 4), 0x1000);
        p[4] = 12;                                  // station
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(58, 2), 4);
        Encoding.ASCII.GetBytes("Stat").CopyTo(p.AsSpan(60));
        var events = w.Ingest(Packet.ForOpcode(0x2018, p));
        Assert.Empty(events);
    }

    [Fact]
    public void Appeared_Self_IsFilteredByNarrator()
    {
        var w = new SectorWorld();
        var ev = w.Ingest(Avatar(SelfId, "Me"));
        Assert.Single(ev);                          // world still emits it
        var lines = NarratorFor(w).Narrate(Avatar(SelfId, "Me"), ev);
        Assert.Empty(lines);                        // narrator drops own ship
    }

    // --- scanner contact: departed ---

    [Fact]
    public void Departed_AnnouncedEntity_Narrates()
    {
        var w = new SectorWorld();
        const int mob = 0x1872C;
        w.Ingest(Create(mob, type: 1));
        w.Ingest(ShipAux((uint)mob, "Mbonae", 17)); // announce
        var ev = w.Ingest(Remove(mob));
        var lines = NarratorFor(w).Narrate(Remove(mob), ev);
        string line = Assert.Single(lines);
        Assert.Contains("Mbonae", line);
        Assert.Contains("left scanner range", line);
    }

    [Fact]
    public void Departed_NeverAnnouncedEntity_IsSilent()
    {
        var w = new SectorWorld();
        const int mob = 0x1872C;
        w.Ingest(Create(mob, type: 1));             // no name -> never announced
        var ev = w.Ingest(Remove(mob));
        Assert.Empty(ev);
    }

    // --- damage ---

    [Fact]
    public void Damage_ToSelf_NarratesWithSourceName()
    {
        var w = new SectorWorld();
        const int mob = 0x1872C;
        w.Ingest(Create(mob, type: 1));
        w.Ingest(ShipAux((uint)mob, "Mbonae", 17));
        var lines = NarratorFor(w).Narrate(Damage(412.7f, mob, SelfId),
            System.Array.Empty<WorldEvent>());
        string line = Assert.Single(lines);
        Assert.Contains("Took 413 damage", line);   // rounds to nearest
        Assert.Contains("from Mbonae", line);
    }

    [Fact]
    public void Damage_ToOther_IsSilent()
    {
        var w = new SectorWorld();
        var lines = NarratorFor(w).Narrate(Damage(99f, 0x1872C, 0x9999),
            System.Array.Empty<WorldEvent>());
        Assert.Empty(lines);
    }

    // --- warp ---

    [Fact]
    public void Warp_NegativeIndex_NarratesEnded()
    {
        var w = new SectorWorld();
        var lines = NarratorFor(w).Narrate(WarpIndex(-1), System.Array.Empty<WorldEvent>());
        Assert.Contains("Warp ended", Assert.Single(lines));
    }

    [Fact]
    public void Warp_NonNegativeIndex_NarratesLeg()
    {
        var w = new SectorWorld();
        var lines = NarratorFor(w).Narrate(WarpIndex(2), System.Array.Empty<WorldEvent>());
        Assert.Contains("Warping (leg 2)", Assert.Single(lines));
    }

    // --- sector load ---

    [Fact]
    public void Handoff_NarratesLoadingSector()
    {
        var w = new SectorWorld();
        var lines = NarratorFor(w).Narrate(Handoff(1710), System.Array.Empty<WorldEvent>());
        Assert.Contains("Loading sector 1710", Assert.Single(lines));
    }

    // --- system message (covers leave-group) ---

    [Fact]
    public void MessageString_NarratesAsSystemNotice()
    {
        var w = new SectorWorld();
        var lines = NarratorFor(w).Narrate(MessageString("You have left the group"),
            System.Array.Empty<WorldEvent>());
        Assert.Contains("You have left the group", Assert.Single(lines));
    }

    [Fact]
    public void Narrate_NeverThrows_OnTruncatedFrames()
    {
        var w = new SectorWorld();
        var n = NarratorFor(w);
        // Each of these is shorter than its record expects.
        Assert.Empty(n.Narrate(Packet.ForOpcode(0x0064, new byte[4]), System.Array.Empty<WorldEvent>()));
        Assert.Empty(n.Narrate(Packet.ForOpcode(0x009C, new byte[1]), System.Array.Empty<WorldEvent>()));
        Assert.Empty(n.Narrate(Packet.ForOpcode(0x003A, new byte[8]), System.Array.Empty<WorldEvent>()));
        Assert.Empty(n.Narrate(Packet.ForOpcode(0x001D, new byte[2]), System.Array.Empty<WorldEvent>()));
    }
}
