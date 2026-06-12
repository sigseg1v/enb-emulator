// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Buffers.Binary;
using System.Collections.Generic;
using N7.CliClient.Opcodes.Records;
using N7.CliClient.Repl.Commands;
using Xunit;

namespace N7.CliClient.UnitTests.Opcodes;

/// <summary>
/// Byte-pins <see cref="AuxDataRecord.TryExtractGroupState"/> -- the self
/// group/formation state the <c>group</c>/<c>formation</c> command gating reads
/// out of the self <c>PlayerIndex</c> aux (0x001B, GameID == 0). The schema is
/// AuxSchemas.PlayerIndex field 17 -> GroupInfo (server AuxPlayerIndex.cpp /
/// AuxGroupInfo.cpp): IsGroupLeader(flag0,U8), Formation(flag8,U32),
/// Members(flag10 -> 5x GroupMember, each Name(0,Str)+GameID(1,U32)). The wire
/// rule (server/src/GroupManager.cpp): leader sits at Member[0] with
/// IsGroupLeader=1; a non-leader member has IsGroupLeader=0 yet IS grouped, so
/// "in a group" comes from the populated member count, not the leader flag.
/// Empty member slots carry GameID == -1.
///
/// Also pins the 0x00BC CTA_REQUEST emitter (<see cref="CtaRequestBuilder"/>).
/// </summary>
public sealed class SelfGroupStateTests
{
    /// <summary>
    /// Build a self PlayerIndex 0x001B payload (GameID == 0) carrying ONLY the
    /// GroupInfo field (flag 17), in plain (BuildPacket) form. GroupInfo carries
    /// IsGroupLeader, Formation, and a Members container with
    /// <paramref name="populatedMembers"/> filled slots (each with a real, non
    /// -sentinel GameID). Empty slots are simply absent (present-bit clear).
    /// </summary>
    private static byte[] SelfGroupAux(bool isLeader, uint formation, int populatedMembers)
    {
        // GroupMember element: Name(flag0,Str) + GameID(flag1,U32). 1 flag byte.
        static byte[] Member(uint gid)
        {
            var b = new List<byte> { 0x30 };          // bits 4,5 -> Name(0), GameID(1)
            b.Add(1); b.Add(0); b.Add((byte)'M');     // Name = "M" (u16 len + bytes)
            for (int k = 0; k < 4; k++) b.Add((byte)((gid >> (8 * k)) & 0xFF));
            return b.ToArray();
        }

        // GroupMembers container: 5 slots, 2 flag bytes. Set bit (i+4) per slot.
        var members = new List<byte>();
        {
            var mflags = new byte[2];
            for (int i = 0; i < populatedMembers; i++) { int bit = i + 4; mflags[bit / 8] |= (byte)(1 << (bit % 8)); }
            members.AddRange(mflags);
            for (int i = 0; i < populatedMembers; i++) members.AddRange(Member(0x40000010u + (uint)i));
        }

        // GroupInfo: 11 fields, 2 flag bytes. Present: IsGroupLeader(0), Formation(8), Members(10).
        var groupInfo = new List<byte>();
        {
            var gflags = new byte[2];
            void Set(int f) { int bit = f + 4; gflags[bit / 8] |= (byte)(1 << (bit % 8)); }
            Set(0); Set(8); Set(10);
            groupInfo.AddRange(gflags);
            groupInfo.Add((byte)(isLeader ? 1 : 0));                                  // IsGroupLeader U8
            for (int k = 0; k < 4; k++) groupInfo.Add((byte)((formation >> (8 * k)) & 0xFF)); // Formation U32
            groupInfo.AddRange(members);                                             // Members nested
        }

        // PlayerIndex: 18 fields, 3 flag bytes, with header. Present: GroupInfo(17).
        var body = new List<byte>();
        var pflags = new byte[3];
        { int bit = 17 + 4; pflags[bit / 8] |= (byte)(1 << (bit % 8)); }
        body.AddRange(pflags);
        body.AddRange(groupInfo);

        var p = new byte[7 + body.Count];
        // header: GameID = 0 (self), BodyLen = len-6, version = 1.
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(4, 2), (ushort)(p.Length - 6));
        p[6] = 1;
        body.CopyTo(p, 7);
        return p;
    }

    [Fact]
    public void Solo_NoPopulatedMembers_IsNotInGroup()
    {
        var gs = AuxDataRecord.TryExtractGroupState(SelfGroupAux(isLeader: false, formation: 0, populatedMembers: 0));
        Assert.NotNull(gs);
        Assert.False(gs!.Value.InGroup);
        Assert.False(gs.Value.IsLeader);
        Assert.False(gs.Value.InFormation);
    }

    [Fact]
    public void Leader_NoFormation()
    {
        var gs = AuxDataRecord.TryExtractGroupState(SelfGroupAux(isLeader: true, formation: 0, populatedMembers: 2));
        Assert.NotNull(gs);
        Assert.True(gs!.Value.InGroup);
        Assert.True(gs.Value.IsLeader);
        Assert.False(gs.Value.InFormation);
    }

    [Fact]
    public void Leader_FormationActive()
    {
        // formation 6 == Pipe (GroupManager action code) -- any non-zero means "in a formation".
        var gs = AuxDataRecord.TryExtractGroupState(SelfGroupAux(isLeader: true, formation: 6, populatedMembers: 3));
        Assert.NotNull(gs);
        Assert.True(gs!.Value.InGroup);
        Assert.True(gs.Value.IsLeader);
        Assert.True(gs.Value.InFormation);
    }

    [Fact]
    public void Member_NotFormedUp()
    {
        var gs = AuxDataRecord.TryExtractGroupState(SelfGroupAux(isLeader: false, formation: 0, populatedMembers: 2));
        Assert.NotNull(gs);
        Assert.True(gs!.Value.InGroup);
        Assert.False(gs.Value.IsLeader);
        Assert.False(gs.Value.InFormation);
    }

    [Fact]
    public void Member_FormedUp()
    {
        var gs = AuxDataRecord.TryExtractGroupState(SelfGroupAux(isLeader: false, formation: 5, populatedMembers: 2));
        Assert.NotNull(gs);
        Assert.True(gs!.Value.InGroup);
        Assert.False(gs.Value.IsLeader);
        Assert.True(gs.Value.InFormation);
    }

    [Fact]
    public void NonSelfAux_ReturnsNull()
    {
        // A non-zero GameID is never the self PlayerIndex -- must not be read as group state.
        byte[] p = SelfGroupAux(isLeader: true, formation: 0, populatedMembers: 2);
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(0, 4), 0x40000050);
        Assert.Null(AuxDataRecord.TryExtractGroupState(p));
    }

    [Fact]
    public void DiffAuxWithoutGroupInfo_ReturnsNull_SoCachedStateIsNotClobbered()
    {
        // A self aux that carries only Credits (field 0, U64) -- e.g. a credits
        // diff -- has no GroupInfo, so the extractor declines and the caller
        // keeps its last known group state.
        var body = new List<byte>();
        var pflags = new byte[3];
        { int bit = 0 + 4; pflags[bit / 8] |= (byte)(1 << (bit % 8)); }  // Credits present
        body.AddRange(pflags);
        for (int k = 0; k < 8; k++) body.Add(0x11);                      // Credits U64

        var p = new byte[7 + body.Count];
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(4, 2), (ushort)(p.Length - 6));
        p[6] = 1;
        body.CopyTo(p, 7);

        Assert.Null(AuxDataRecord.TryExtractGroupState(p));
    }

    [Fact]
    public void TruncatedOrWrongVersion_ReturnsNull()
    {
        Assert.Null(AuxDataRecord.TryExtractGroupState(new byte[7]));        // < 8 bytes
        byte[] p = SelfGroupAux(isLeader: true, formation: 0, populatedMembers: 2);
        p[6] = 2;                                                            // version must be 1
        Assert.Null(AuxDataRecord.TryExtractGroupState(p));
    }

    [Theory]
    [InlineData(0x40000123, 6)]   // pipe
    [InlineData(0x40000123, 5)]   // block
    [InlineData(0x40000123, 9)]   // break
    public void CtaRequest_IsByteExact(int sourceId, int action)
    {
        byte[] p = CtaRequestBuilder.Build(sourceId, action);
        Assert.Equal(12, p.Length);
        Assert.Equal(sourceId, BinaryPrimitives.ReadInt32LittleEndian(p.AsSpan(0, 4)));   // SourceID
        Assert.Equal(sourceId, BinaryPrimitives.ReadInt32LittleEndian(p.AsSpan(4, 4)));   // TargetID == SourceID
        Assert.Equal(action,   BinaryPrimitives.ReadInt32LittleEndian(p.AsSpan(8, 4)));   // Action
    }
}
