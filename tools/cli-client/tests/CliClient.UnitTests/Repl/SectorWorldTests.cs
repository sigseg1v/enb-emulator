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
        float x, float y, float z, float signature)
    {
        byte[] nameBytes = Encoding.ASCII.GetBytes(name);
        byte[] p = new byte[60 + nameBytes.Length];
        BinaryPrimitives.WriteInt32LittleEndian(p.AsSpan(0, 4), gameId);
        p[4] = createType;
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(25, 4), x);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(29, 4), y);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(33, 4), z);
        BinaryPrimitives.WriteSingleLittleEndian(p.AsSpan(53, 4), signature);
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

        Assert.Contains("location:", outp);
        Assert.Contains("1.2346", outp);   // 4 d.p. rounding of self X
        Assert.Contains("planet", outp);
        Assert.Contains("nearby:", outp);
    }
}
