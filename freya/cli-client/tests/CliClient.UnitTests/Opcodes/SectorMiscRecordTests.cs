// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Buffers.Binary;
using N7.CliClient.Logging;
using N7.CliClient.Opcodes.Records;
using Xunit;

namespace N7.CliClient.UnitTests.Opcodes;

/// <summary>
/// Byte-pins three previously-unparsed server-emitted opcodes (they fell
/// through to <see cref="GenericRecord"/> before): 0x0041
/// FORMATION_POSITIONAL_UPDATE, 0x0083 RECUSTOMIZE_AVATAR_START, and 0x0096
/// JOB_ACCEPT_REPLY. Each test synthesises the EXACT bytes the server emitter
/// writes (PlayerConnection.cpp) and asserts the decoder reads every field AND
/// consumes every byte (no <c>???</c> gap = full coverage).
/// </summary>
public sealed class SectorMiscRecordTests
{
    public SectorMiscRecordTests() => AnsiPalette.Enabled = false;

    private static string Dump(ushort opcode, byte[] payload)
        => PacketRecord.Resolve(opcode, payload).DumpToString();

    // ── 0x0041 FORMATION_POSITIONAL_UPDATE (fixed 20 bytes) ──────────────────
    // struct FormationPositionalUpdate { int32 TargetID; int32 LeaderID;
    //   float Position[3]; }  -- emit order = declaration order, all LE.

    [Fact]
    public void Formation_DecodesAllFields_AndConsumesEveryByte()
    {
        var b = new byte[20];
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(0), 0x000A1234);   // TargetID
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(4), 0x000B5678);   // LeaderID
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(8), 100.0f);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(12), -50.5f);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(16), 12.25f);

        var dump = Dump(0x0041, b);

        Assert.Contains("TargetID", dump);
        Assert.Contains("0x000A1234", dump);
        Assert.Contains("LeaderID", dump);
        Assert.Contains("0x000B5678", dump);
        Assert.Contains("Position", dump);
        Assert.Contains("100", dump);
        Assert.Contains("-50.5", dump);
        Assert.Contains("12.25", dump);

        Assert.DoesNotContain("???", dump);
        Assert.DoesNotContain("truncated", dump);
    }

    [Fact]
    public void Formation_Truncated_Flags()
    {
        var dump = Dump(0x0041, new byte[19]);
        Assert.Contains("truncated", dump);
    }

    // ── 0x0083 RECUSTOMIZE_AVATAR_START (fixed 60 bytes) ─────────────────────
    // struct RecustomizeAvatarStart { int32 costs[14]; int32 playerid; }
    //   costs LE, playerid emitted BE (htonl).

    [Fact]
    public void RecustomizeAvatar_DecodesAllFields_AndConsumesEveryByte()
    {
        var b = new byte[60];
        for (int i = 0; i < 14; i++)
            BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(i * 4), 100 + i);
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(56), 0x40000029);     // playerid (BE on wire)

        var dump = Dump(0x0083, b);

        Assert.Contains("Cost[0]", dump);
        Assert.Contains("100", dump);
        Assert.Contains("Cost[13]", dump);
        Assert.Contains("113", dump);
        Assert.Contains("PlayerID", dump);
        Assert.Contains("0x40000029", dump);

        Assert.DoesNotContain("???", dump);
        Assert.DoesNotContain("truncated", dump);
    }

    [Fact]
    public void RecustomizeAvatar_Truncated_Flags()
    {
        var dump = Dump(0x0083, new byte[59]);
        Assert.Contains("truncated", dump);
    }

    // ── 0x0096 JOB_ACCEPT_REPLY (4 bytes, or empty) ──────────────────────────

    [Fact]
    public void JobAcceptReply_WithJobId_DecodesAndConsumesEveryByte()
    {
        var b = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(0), 0x00012345);

        var dump = Dump(0x0096, b);

        Assert.Contains("JobID", dump);
        Assert.Contains("0x00012345", dump);
        Assert.DoesNotContain("???", dump);
        Assert.DoesNotContain("truncated", dump);
    }

    [Fact]
    public void JobAcceptReply_Empty_FlagsGenericAccept()
    {
        var dump = Dump(0x0096, System.Array.Empty<byte>());
        Assert.Contains("generic accept", dump);
        Assert.DoesNotContain("???", dump);
    }
}
