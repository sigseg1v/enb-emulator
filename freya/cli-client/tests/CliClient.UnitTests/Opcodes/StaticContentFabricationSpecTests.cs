// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Buffers.Binary;
using System.Text;
using N7.CliClient.Logging;
using N7.CliClient.Opcodes.Records;
using Xunit;

namespace N7.CliClient.UnitTests.Opcodes;

/// <summary>
/// Pins the 0x2018 STATIC_OBJECT_CREATE -&gt; {0x04 CREATE, 0x89 RELATIONSHIP,
/// 0x40 CONSTANT_POSITIONAL_UPDATE, 0x1b AUX-name, 0x99 NAVIGATION} and
/// 0x2019 RESOURCE_OBJECT_CREATE -&gt; {0x04 CREATE, 0x40 position, 0x1b
/// resource-name} fabrication contracts the proxy implements in
/// <c>UDPClient::CreateObject</c> / <c>UDPClient::CreateResource</c>
/// (proxy/UDPProxyToClient_linux.cpp).
///
/// IMPORTANT -- what this is and is NOT (same discipline as
/// <see cref="ProspectBeamFabricationSpecTests"/>):
///   * It is a SPEC mirror: it re-states, in C#, the exact bytes the proxy
///     fabricates from a compact server control packet, then decodes each
///     expanded frame with the real production record
///     (<see cref="CreateRecord"/>, <see cref="RelationshipRecord"/>,
///     <see cref="ConstantPosRecord"/>, <see cref="NavigationRecord"/>) and
///     asserts the field values plus exact byte consumption. The aux frames
///     are pinned at the raw-byte level because <see cref="AuxDataRecord"/>
///     uses a heuristic best-fit schema walk that is not a deterministic
///     decoder for an arbitrary fabricated frame.
///   * It is NOT a live round-trip. The real round-trip (drive the live
///     proxy+server, zone into a sector, capture the client-leg expanded
///     frames) is the client.exe verification deliverable -- see CV-01/CV-02
///     in plans/29-client-verification.md. The client&lt;-&gt;proxy leg is
///     RC4-encrypted, so the proxy's expansion is reconstructed from the
///     server builders, not observed on the wire.
///
/// Because this is a separate C# copy of the layout, it does NOT auto-break if
/// the proxy C++ drifts -- only the live capture catches real proxy drift. Its
/// value is (1) documenting the expansion contract as executable spec, (2)
/// guarding the RELATIONSHIP ObjectID ntohl byte-swap regression (the field
/// whose byte order, if wrong, crashes the real client -- CLAUDE.md "Trap 1"),
/// and (3) pinning the NAVIGATION Signature+5000 / SigFlags-nibble decode that
/// makes navs appear on the map.
/// </summary>
public sealed class StaticContentFabricationSpecTests
{
    public StaticContentFabricationSpecTests() => AnsiPalette.Enabled = false;

    // SigFlags bit layout (server/src/NavTypeClass.cpp; mirrored in
    // StaticObjectCreateRecord). Low nibble = NavType.
    private const byte HAS_NAV     = 0x10;
    private const byte IS_NAV      = 0x20;
    private const byte IS_HUGE     = 0x40;
    private const byte HAS_VISITED = 0x80;

    private const byte PosTypeConstant = 3; // CONSTANT (static objects)

    private static string Dump(ushort opcode, byte[] body) => PacketRecord.Resolve(opcode, body).DumpToString();

    // ──────────────────────────────────────────────────────────────────────
    // Compact server packets (input). Layout matches the proxy parse and the
    // production StaticObjectCreateRecord / ResourceObjectCreateRecord.
    // ──────────────────────────────────────────────────────────────────────

    private static byte[] FabricateCompact2018(
        int gid, byte createType, short baseAsset, float scale,
        float h, float s, float v, byte reaction, byte posType,
        float px, float py, float pz, float o0, float o1, float o2, float o3,
        float signature, byte sigFlags, string name)
    {
        byte[] nb = Encoding.ASCII.GetBytes(name);
        var b = new byte[60 + nb.Length];
        var sp = b.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(sp.Slice(0), gid);
        b[4] = createType;
        BinaryPrimitives.WriteInt16LittleEndian(sp.Slice(5), baseAsset);
        BinaryPrimitives.WriteSingleLittleEndian(sp.Slice(7), scale);
        BinaryPrimitives.WriteSingleLittleEndian(sp.Slice(11), h);
        BinaryPrimitives.WriteSingleLittleEndian(sp.Slice(15), s);
        BinaryPrimitives.WriteSingleLittleEndian(sp.Slice(19), v);
        b[23] = reaction;
        b[24] = posType;
        BinaryPrimitives.WriteSingleLittleEndian(sp.Slice(25), px);
        BinaryPrimitives.WriteSingleLittleEndian(sp.Slice(29), py);
        BinaryPrimitives.WriteSingleLittleEndian(sp.Slice(33), pz);
        BinaryPrimitives.WriteSingleLittleEndian(sp.Slice(37), o0);
        BinaryPrimitives.WriteSingleLittleEndian(sp.Slice(41), o1);
        BinaryPrimitives.WriteSingleLittleEndian(sp.Slice(45), o2);
        BinaryPrimitives.WriteSingleLittleEndian(sp.Slice(49), o3);
        BinaryPrimitives.WriteSingleLittleEndian(sp.Slice(53), signature);
        b[57] = sigFlags;
        BinaryPrimitives.WriteInt16LittleEndian(sp.Slice(58), (short)nb.Length);
        nb.CopyTo(sp.Slice(60));
        return b;
    }

    private static byte[] FabricateCompact2019(
        int gid, short baseAsset, float scale, float hsv0, float hsv1,
        float px, float py, float pz, float o0, float o1, float o2, float o3,
        string name)
    {
        byte[] nb = Encoding.ASCII.GetBytes(name);
        var b = new byte[48 + nb.Length];
        var sp = b.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(sp.Slice(0), gid);
        BinaryPrimitives.WriteInt16LittleEndian(sp.Slice(4), baseAsset);
        BinaryPrimitives.WriteSingleLittleEndian(sp.Slice(6), scale);
        BinaryPrimitives.WriteSingleLittleEndian(sp.Slice(10), hsv0);
        BinaryPrimitives.WriteSingleLittleEndian(sp.Slice(14), hsv1);
        BinaryPrimitives.WriteSingleLittleEndian(sp.Slice(18), px);
        BinaryPrimitives.WriteSingleLittleEndian(sp.Slice(22), py);
        BinaryPrimitives.WriteSingleLittleEndian(sp.Slice(26), pz);
        BinaryPrimitives.WriteSingleLittleEndian(sp.Slice(30), o0);
        BinaryPrimitives.WriteSingleLittleEndian(sp.Slice(34), o1);
        BinaryPrimitives.WriteSingleLittleEndian(sp.Slice(38), o2);
        BinaryPrimitives.WriteSingleLittleEndian(sp.Slice(42), o3);
        BinaryPrimitives.WriteInt16LittleEndian(sp.Slice(46), (short)nb.Length);
        nb.CopyTo(sp.Slice(48));
        return b;
    }

    // ──────────────────────────────────────────────────────────────────────
    // Expanded client-facing frames (output). Each mirrors the corresponding
    // proxy builder byte-for-byte.
    // ──────────────────────────────────────────────────────────────────────

    private static byte[] ExpandCreate(int gid, float scale, short baseAsset, byte type, float h, float s, float v)
    {
        var b = new byte[23];
        var sp = b.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(sp.Slice(0), gid);
        BinaryPrimitives.WriteSingleLittleEndian(sp.Slice(4), scale);
        BinaryPrimitives.WriteInt16LittleEndian(sp.Slice(8), baseAsset);
        b[10] = type;
        BinaryPrimitives.WriteSingleLittleEndian(sp.Slice(11), h);
        BinaryPrimitives.WriteSingleLittleEndian(sp.Slice(15), s);
        BinaryPrimitives.WriteSingleLittleEndian(sp.Slice(19), v);
        return b;
    }

    // ObjectID is byte-swapped on the wire: the proxy emits it with
    // AddDataFlip4 (ntohl), which on a little-endian host writes the GameID in
    // BIG-endian byte order. RelationshipRecord reads it back with ReadI32BE.
    private static byte[] ExpandRelationship(int gid, int reaction)
    {
        var b = new byte[9];
        var sp = b.AsSpan();
        BinaryPrimitives.WriteInt32BigEndian(sp.Slice(0), gid);
        BinaryPrimitives.WriteInt32LittleEndian(sp.Slice(4), reaction);
        b[8] = 0;
        return b;
    }

    private static byte[] ExpandConstantPos(int gid, float px, float py, float pz, float o0, float o1, float o2, float o3)
    {
        var b = new byte[32];
        var sp = b.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(sp.Slice(0), gid);
        BinaryPrimitives.WriteSingleLittleEndian(sp.Slice(4), px);
        BinaryPrimitives.WriteSingleLittleEndian(sp.Slice(8), py);
        BinaryPrimitives.WriteSingleLittleEndian(sp.Slice(12), pz);
        BinaryPrimitives.WriteSingleLittleEndian(sp.Slice(16), o0);
        BinaryPrimitives.WriteSingleLittleEndian(sp.Slice(20), o1);
        BinaryPrimitives.WriteSingleLittleEndian(sp.Slice(24), o2);
        BinaryPrimitives.WriteSingleLittleEndian(sp.Slice(28), o3);
        return b;
    }

    private static byte[] ExpandNavigation(int gid, float signature, byte sigFlags)
    {
        var b = new byte[14];
        var sp = b.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(sp.Slice(0), gid);
        BinaryPrimitives.WriteSingleLittleEndian(sp.Slice(4), signature + 5000.0f);
        b[8] = (byte)((sigFlags & HAS_VISITED) != 0 ? 1 : 0);
        BinaryPrimitives.WriteInt32LittleEndian(sp.Slice(9), sigFlags & 0x0F);
        b[13] = (byte)((sigFlags & IS_HUGE) != 0 ? 1 : 0);
        return b;
    }

    // 0x1b AUX name -- the three formats the proxy emits, each from a server
    // builder. Layouts are pinned at the raw-byte level.

    // SendSimpleAuxName / SendResourceName (CreateType 3/4/11/12, identical):
    //   [GameID i32][innerLen u16 = N+4][0x1201 u16][strLen u16 = N][name].
    private static byte[] ExpandSimpleAuxName(int gid, string name)
    {
        byte[] nb = Encoding.ASCII.GetBytes(name);
        var b = new byte[10 + nb.Length];
        var sp = b.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(sp.Slice(0), gid);
        BinaryPrimitives.WriteUInt16LittleEndian(sp.Slice(4), (ushort)(nb.Length + 4));
        BinaryPrimitives.WriteUInt16LittleEndian(sp.Slice(6), 0x1201);
        BinaryPrimitives.WriteUInt16LittleEndian(sp.Slice(8), (ushort)nb.Length);
        nb.CopyTo(sp.Slice(10));
        return b;
    }

    // SendAuxNameSignature (CreateType 37):
    //   [GameID i32][innerLen u16 = N+9][0x01 u8][0x72 u8][strLen u16 = N]
    //   [name][nav u8][sig f32].
    private static byte[] ExpandAuxNameSignature(int gid, string name, byte nav, float sig)
    {
        byte[] nb = Encoding.ASCII.GetBytes(name);
        var b = new byte[15 + nb.Length];
        var sp = b.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(sp.Slice(0), gid);
        BinaryPrimitives.WriteUInt16LittleEndian(sp.Slice(4), (ushort)(nb.Length + 9));
        b[6] = 0x01;
        b[7] = 0x72;
        BinaryPrimitives.WriteUInt16LittleEndian(sp.Slice(8), (ushort)nb.Length);
        nb.CopyTo(sp.Slice(10));
        b[10 + nb.Length] = nav;
        BinaryPrimitives.WriteSingleLittleEndian(sp.Slice(11 + nb.Length), sig);
        return b;
    }

    // SendAuxNameResource (CreateType 38 / 0x2019):
    //   [GameID i32][innerLen u16 = N+5][0x01 u8][0x16 u8][0x04 u8][strLen u16 = N][name].
    private static byte[] ExpandAuxNameResource(int gid, string name)
    {
        byte[] nb = Encoding.ASCII.GetBytes(name);
        var b = new byte[11 + nb.Length];
        var sp = b.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(sp.Slice(0), gid);
        BinaryPrimitives.WriteUInt16LittleEndian(sp.Slice(4), (ushort)(nb.Length + 5));
        b[6] = 0x01;
        b[7] = 0x16;
        b[8] = 0x04;
        BinaryPrimitives.WriteUInt16LittleEndian(sp.Slice(9), (ushort)nb.Length);
        nb.CopyTo(sp.Slice(11));
        return b;
    }

    // Mirrors UDPClient::CreateObject aux-format selection by CreateType.
    private static byte[]? ExpandAux2018(int gid, byte createType, byte sigFlags, string name, float signature)
    {
        if (createType == 37)
        {
            byte nav = (byte)((sigFlags & IS_NAV) != 0 ? 1 : 0);
            string auxName = nav != 0 ? name : "d";
            float sig = signature < 3000.0f ? 3000.0f : signature;
            return ExpandAuxNameSignature(gid, auxName, nav, sig);
        }
        if (createType is 3 or 4 or 11 or 12)
            return ExpandSimpleAuxName(gid, name);
        return null; // CreateType 40/41/other: no aux name (matches the server switch)
    }

    // ──────────────────────────────────────────────────────────────────────
    // 0x2018 -- nav (the case that makes navs appear on the map)
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Static2018_Nav_Expands_To_Create_Relationship_Pos_Aux_Nav()
    {
        const int   gid       = 0x00012345;
        const byte  createType = 37;
        const short baseAsset = 1463;
        const float scale     = 1.0f;
        const byte  reaction  = 5;            // FRIENDLY (compact @23, passed through)
        const float signature = 2000.0f;
        const byte  sigFlags  = HAS_NAV | IS_NAV | HAS_VISITED | 0x01; // NavType=1, visited
        const string name     = "Luna Nav 1";

        var compact = FabricateCompact2018(
            gid, createType, baseAsset, scale,
            h: 0f, s: 0f, v: 1f, reaction, PosTypeConstant,
            px: 100f, py: 200f, pz: 300f, o0: 0f, o1: 0f, o2: 0f, o3: 1f,
            signature, sigFlags, name);

        // The compact source decodes cleanly with the production record.
        var compactDump = Dump(0x2018, compact);
        Assert.Contains("Luna Nav 1", compactDump);
        Assert.Contains("HAS_NAV", compactDump);
        Assert.Contains("IS_NAV", compactDump);
        Assert.Contains("NavType=1", compactDump);
        Assert.DoesNotContain("truncated", compactDump);
        Assert.DoesNotContain("overruns", compactDump);

        // 0x04 CREATE -- 23 bytes, Type == CreateType, decodes with no gaps.
        var create = ExpandCreate(gid, scale, baseAsset, createType, 0f, 0f, 1f);
        Assert.Equal(23, create.Length);
        var createDump = Dump(0x0004, create);
        Assert.Contains("0x00012345", createDump);   // GameID
        Assert.Contains("BaseAsset", createDump);
        Assert.Contains("0x05B7", createDump);        // BaseAsset 1463 shown as hex u16
        Assert.Contains("Type", createDump);
        Assert.DoesNotContain("???", createDump);
        Assert.DoesNotContain("truncated", createDump);
        Assert.Equal(createType, create[10]);         // Type == CreateType (byte @10)

        // 0x89 RELATIONSHIP -- the ObjectID ntohl byte-swap regression guard.
        var rel = ExpandRelationship(gid, reaction);
        Assert.Equal(9, rel.Length);
        // On the wire the id is BIG-endian (ntohl on a LE host). A LE read of
        // the same bytes must NOT equal the GameID -- that is the swap.
        Assert.Equal(gid, BinaryPrimitives.ReadInt32BigEndian(rel.AsSpan(0)));
        Assert.NotEqual(gid, BinaryPrimitives.ReadInt32LittleEndian(rel.AsSpan(0)));
        var relDump = Dump(0x0089, rel);
        Assert.Contains("ObjectID", relDump);
        Assert.Contains("0x00012345", relDump);       // record reads it BE -> original id
        Assert.Contains("Reaction", relDump);
        Assert.DoesNotContain("truncated", relDump);

        // 0x40 CONSTANT_POSITIONAL_UPDATE -- 32 bytes, decodes with no gaps.
        var pos = ExpandConstantPos(gid, 100f, 200f, 300f, 0f, 0f, 0f, 1f);
        Assert.Equal(32, pos.Length);
        var posDump = Dump(0x0040, pos);
        Assert.Contains("0x00012345", posDump);
        Assert.Contains("Position", posDump);
        Assert.Contains("Orientation", posDump);
        Assert.DoesNotContain("???", posDump);
        Assert.DoesNotContain("truncated", posDump);

        // 0x1b AUX -- nav signature format: name preserved, nav=1, sig clamped >= 3000.
        var aux = ExpandAux2018(gid, createType, sigFlags, name, signature)!;
        Assert.Equal(0x01, aux[6]);
        Assert.Equal(0x72, aux[7]);
        int nameLenInAux = BinaryPrimitives.ReadUInt16LittleEndian(aux.AsSpan(8));
        Assert.Equal(name.Length, nameLenInAux);
        Assert.Equal(name, Encoding.ASCII.GetString(aux, 10, name.Length));
        Assert.Equal((ushort)(name.Length + 9), BinaryPrimitives.ReadUInt16LittleEndian(aux.AsSpan(4)));
        Assert.Equal(1, aux[10 + name.Length]); // nav byte
        Assert.Equal(3000.0f, BinaryPrimitives.ReadSingleLittleEndian(aux.AsSpan(11 + name.Length))); // sig clamped from 2000

        // 0x99 NAVIGATION -- present because HAS_NAV is set; Signature == sig + 5000.
        var nav = ExpandNavigation(gid, signature, sigFlags);
        Assert.Equal(14, nav.Length);
        var navDump = Dump(0x0099, nav);
        Assert.Contains("0x00012345", navDump);
        Assert.Contains("Signature", navDump);
        Assert.Contains("7000", navDump);             // 2000 + 5000
        Assert.Contains("NavType", navDump);
        Assert.Contains("PlayerHasVisited", navDump);
        Assert.DoesNotContain("truncated", navDump);
        // NavType == low nibble; PlayerHasVisited == 1; IsHuge == 0.
        Assert.Equal(1, BinaryPrimitives.ReadInt32LittleEndian(nav.AsSpan(9)));
        Assert.Equal(1, nav[8]);
        Assert.Equal(0, nav[13]);
    }

    // ──────────────────────────────────────────────────────────────────────
    // 0x2018 -- decoration (no nav): aux name placeholder "d", no 0x99 frame
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Static2018_Deco_NoNavBit_UsesPlaceholderName_AndEmitsNoNavigation()
    {
        const int   gid       = 0x00067890;
        const byte  createType = 37;
        const byte  sigFlags  = 0x00;    // no HAS_NAV, no IS_NAV
        const float signature = 100.0f;  // below the 3000 clamp floor
        const string name     = "Some Deco";

        // No HAS_NAV -> the proxy emits no 0x99 frame.
        Assert.True((sigFlags & HAS_NAV) == 0);

        // Aux uses the placeholder name "d" and nav=0 (non-nav deco), sig clamped to 3000.
        var aux = ExpandAux2018(gid, createType, sigFlags, name, signature)!;
        Assert.Equal(0x72, aux[7]);
        int len = BinaryPrimitives.ReadUInt16LittleEndian(aux.AsSpan(8));
        Assert.Equal(1, len);                         // "d"
        Assert.Equal("d", Encoding.ASCII.GetString(aux, 10, 1));
        Assert.Equal(0, aux[10 + 1]);                 // nav byte == 0
        Assert.Equal(3000.0f, BinaryPrimitives.ReadSingleLittleEndian(aux.AsSpan(11 + 1)));
    }

    // ──────────────────────────────────────────────────────────────────────
    // 0x2018 -- station (CreateType 12): simple-name aux, no nav
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Static2018_Station_UsesSimpleAuxName_Format()
    {
        const int    gid        = 0x00099999;
        const byte   createType = 12;     // station
        const string name       = "Luna Station";

        var aux = ExpandAux2018(gid, createType, 0x00, name, 0f)!;
        // [gid][innerLen=N+4][0x1201][strLen=N][name]
        Assert.Equal((ushort)(name.Length + 4), BinaryPrimitives.ReadUInt16LittleEndian(aux.AsSpan(4)));
        Assert.Equal(0x1201, BinaryPrimitives.ReadUInt16LittleEndian(aux.AsSpan(6)));
        Assert.Equal(name.Length, BinaryPrimitives.ReadUInt16LittleEndian(aux.AsSpan(8)));
        Assert.Equal(name, Encoding.ASCII.GetString(aux, 10, name.Length));
        Assert.Equal(10 + name.Length, aux.Length);
    }

    [Fact]
    public void Static2018_BareDecoTypes_EmitNoAuxName()
    {
        // CreateType 40/41 emit no aux name (matches the server switch).
        Assert.Null(ExpandAux2018(0x1, 40, 0x00, "x", 0f));
        Assert.Null(ExpandAux2018(0x1, 41, 0x00, "x", 0f));
    }

    // ──────────────────────────────────────────────────────────────────────
    // 0x2019 -- resource (asteroid field)
    // ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void Resource2019_Expands_To_Create_Pos_ResourceName()
    {
        const int    gid       = 0x000A1B2C;
        const short  baseAsset = 7777;
        const float  scale     = 2.5f;
        const float  hsv0      = 0.3f;
        const float  hsv1      = 0.0f;   // resources carry only two HSV channels; HSV1 is 0
        const string name      = "Ore Field";

        var compact = FabricateCompact2019(
            gid, baseAsset, scale, hsv0, hsv1,
            px: 10f, py: 20f, pz: 30f, o0: 0f, o1: 0f, o2: 0f, o3: 1f, name);

        var compactDump = Dump(0x2019, compact);
        Assert.Contains("Ore Field", compactDump);
        Assert.DoesNotContain("truncated", compactDump);
        Assert.DoesNotContain("overruns", compactDump);

        // 0x04 CREATE -- Type == 38 (resource), HSV2 == 0 (only two channels in source).
        var create = ExpandCreate(gid, scale, baseAsset, 38, hsv0, hsv1, 0f);
        Assert.Equal(23, create.Length);
        var createDump = Dump(0x0004, create);
        Assert.Contains("0x000A1B2C", createDump);
        Assert.Contains("Type", createDump);
        Assert.DoesNotContain("???", createDump);
        Assert.DoesNotContain("truncated", createDump);
        Assert.Equal(38, create[10]);                 // Type == 38 (resource), byte @10
        Assert.Equal(0.0f, BinaryPrimitives.ReadSingleLittleEndian(create.AsSpan(19))); // HSV2

        // 0x40 position.
        var pos = ExpandConstantPos(gid, 10f, 20f, 30f, 0f, 0f, 0f, 1f);
        var posDump = Dump(0x0040, pos);
        Assert.Contains("0x000A1B2C", posDump);
        Assert.DoesNotContain("???", posDump);
        Assert.DoesNotContain("truncated", posDump);

        // 0x1b resource-name aux -- [gid][innerLen=N+5][0x01][0x16][0x04][strLen=N][name].
        var aux = ExpandAuxNameResource(gid, name);
        Assert.Equal((ushort)(name.Length + 5), BinaryPrimitives.ReadUInt16LittleEndian(aux.AsSpan(4)));
        Assert.Equal(0x01, aux[6]);
        Assert.Equal(0x16, aux[7]);
        Assert.Equal(0x04, aux[8]);
        Assert.Equal(name.Length, BinaryPrimitives.ReadUInt16LittleEndian(aux.AsSpan(9)));
        Assert.Equal(name, Encoding.ASCII.GetString(aux, 11, name.Length));
        Assert.Equal(11 + name.Length, aux.Length);
    }
}
