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
/// Byte-pins three previously-unparsed variable-length server-emitted opcodes
/// (all fell through to <see cref="GenericRecord"/>): 0x0053 FIND_MEMBER,
/// 0x005F AVATAR_EMOTE_RESPONSE, 0x00BE CONFIRMED_ACTION_OFFER. Each synthesises
/// the exact bytes the server emitter writes and asserts every field reads back
/// AND every byte is consumed (no <c>???</c> gap).
/// </summary>
public sealed class VariableLengthRecordTests
{
    public VariableLengthRecordTests() => AnsiPalette.Enabled = false;

    private static string Dump(ushort opcode, byte[] payload)
        => PacketRecord.Resolve(opcode, payload).DumpToString();

    // ── 0x0053 FIND_MEMBER: count(LE) + count*{GameID,Level,Race,Prof}(BE) ───

    [Fact]
    public void FindMember_TwoEntries_FullParse()
    {
        var b = new byte[4 + 2 * 16];
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(0), 2);              // count LE
        // entry 0 -- all BE
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(4), 0x000A0001);
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(8), 50);
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(12), 1);
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(16), 2);
        // entry 1
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(20), 0x000A0002);
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(24), 99);
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(28), 3);
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(32), 4);

        var dump = Dump(0x0053, b);
        Assert.Contains("Count", dump);
        Assert.Contains("[0].GameID", dump);
        Assert.Contains("0x000A0001", dump);
        Assert.Contains("[1].GameID", dump);
        Assert.Contains("0x000A0002", dump);
        Assert.Contains("[1].Level", dump);
        Assert.Contains("99", dump);
        Assert.DoesNotContain("???", dump);
        Assert.DoesNotContain("truncated", dump);
    }

    [Fact]
    public void FindMember_EmptyList_ConsumesCountOnly()
    {
        var b = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(0), 0);
        var dump = Dump(0x0053, b);
        Assert.Contains("Count", dump);
        Assert.DoesNotContain("???", dump);
    }

    [Fact]
    public void FindMember_CountOverrunsBuffer_Flags()
    {
        var b = new byte[4 + 16];
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(0), 5);  // claims 5, only 1 present
        Assert.Contains("needs", Dump(0x0053, b));
    }

    // ── 0x005F AVATAR_EMOTE_RESPONSE: short ChatSize, type, int32 GameID, msg ─

    [Fact]
    public void AvatarEmoteResponse_FullParse()
    {
        string msg = "waves";
        var body = Encoding.Latin1.GetBytes(msg);
        var b = new byte[7 + body.Length];
        BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(0), (short)body.Length);
        b[2] = 0x01;
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(3), 0x000A1234);
        body.CopyTo(b, 7);

        var dump = Dump(0x005F, b);
        Assert.Contains("ChatSize", dump);
        Assert.Contains("Type", dump);
        Assert.Contains("GameID", dump);
        Assert.Contains("0x000A1234", dump);
        Assert.Contains("waves", dump);
        Assert.DoesNotContain("???", dump);
        Assert.DoesNotContain("truncated", dump);
    }

    [Fact]
    public void AvatarEmoteResponse_DeclaredSizeOverruns_Flags()
    {
        var b = new byte[7];
        BinaryPrimitives.WriteInt16LittleEndian(b.AsSpan(0), 10);  // claims 10 msg bytes, none present
        b[2] = 0x01;
        Assert.Contains("Message needs", Dump(0x005F, b));
    }

    // ── 0x00BE CONFIRMED_ACTION_OFFER: int32 BE, int32 BE, short len, text ───

    [Fact]
    public void ConfirmedActionOffer_MatchesServerLiteral()
    {
        // Exact bytes Player::SendConfirmedActionOffer emits
        // (PlayerConnection.cpp:863): ActionType=1 (BE), ActionId=0x65 (BE),
        // TextLen=7 (LE), "Message".
        byte[] b =
        {
            0x00, 0x00, 0x00, 0x01,
            0x00, 0x00, 0x00, 0x65,
            0x07, 0x00,
            0x4d, 0x65, 0x73, 0x73, 0x61, 0x67, 0x65,
        };
        var dump = Dump(0x00BE, b);
        Assert.Contains("ActionType", dump);
        Assert.Contains("0x00000001", dump);
        Assert.Contains("ActionId", dump);
        Assert.Contains("0x00000065", dump);
        Assert.Contains("TextLen", dump);
        Assert.Contains("Message", dump);
        Assert.DoesNotContain("???", dump);
        Assert.DoesNotContain("truncated", dump);
    }
}
