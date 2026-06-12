// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Outbound;
using Xunit;

namespace N7.CliClient.UnitTests.Opcodes;

/// <summary>
/// Byte-pins <see cref="SkillUpCodec"/> (0x0057 SKILL_UP / SkillAction,
/// client -&gt; server) against the live reference server
/// SkillTrainingHostileDevice2 capture. The 12-byte retail frame followed the
/// 12-byte outer sector header on the proxy&lt;-&gt;server leg; the outer header
/// is added by the transport, so we pin only the codec's payload.
///
/// All fields are LITTLE-endian (HandleSkillAction casts the struct with no
/// ntohl), unlike 0x0027 INVENTORY_MOVE which is big-endian on every field.
/// </summary>
public sealed class SkillUpCodecTests
{
    private const int PlayerGameId = 0x4003992A;

    [Fact]
    public void Opcode_Is_0x0057()
    {
        var codec = new SkillUpCodec();
        Assert.Equal(OpcodeId.Known.SkillUp, codec.Opcode);
        Assert.Equal(0x0057, codec.Opcode.Value);
    }

    [Fact]
    public void Encode_TrainSkill55_MatchesCapture_SkillTraining_dg18()
    {
        // SkillTrainingHostileDevice2 dg #18 body: raise skill id 55.
        //   2A 99 03 40  01 00 00 00  37 00 00 00
        var msg = new SkillUpMessage(PlayerGameId, SkillPoints: 1, SkillId: 55);

        byte[] wire = new SkillUpCodec().EncodeOutbound(msg);

        Assert.Equal(new byte[]
        {
            0x2A, 0x99, 0x03, 0x40,   // GameID 0x4003992A (LE)
            0x01, 0x00, 0x00, 0x00,   // SkillPoints = 1 (server-ignored)
            0x37, 0x00, 0x00, 0x00,   // SkillID = 55 (LE)
        }, wire);
    }

    [Fact]
    public void DefaultSkillPointsCtor_UsesCapturedValue()
    {
        // The two-arg ctor must default SkillPoints to the captured value (1).
        byte[] wire = new SkillUpCodec().EncodeOutbound(
            new SkillUpMessage(PlayerGameId, skillId: 55));

        Assert.Equal(new byte[]
        {
            0x2A, 0x99, 0x03, 0x40,
            0x01, 0x00, 0x00, 0x00,
            0x37, 0x00, 0x00, 0x00,
        }, wire);
    }

    [Fact]
    public void EncodeDecode_RoundTrips()
    {
        var codec = new SkillUpCodec();
        var msg = new SkillUpMessage(PlayerGameId, SkillPoints: 1, SkillId: 12);
        var back = (SkillUpMessage)codec.DecodeInbound(codec.EncodeOutbound(msg));
        Assert.Equal(msg, back);
    }

    [Fact]
    public void Encode_ProducesExactly12Bytes()
    {
        byte[] wire = new SkillUpCodec().EncodeOutbound(new SkillUpMessage(1, 1, 2));
        Assert.Equal(SkillUpCodec.Size, wire.Length);
    }

    [Fact]
    public void Decode_TooShort_Throws()
    {
        var codec = new SkillUpCodec();
        Assert.Throws<InvalidDataException>(
            () => codec.DecodeInbound(new byte[SkillUpCodec.Size - 1]));
    }

    [Fact]
    public void Encode_WrongMessageType_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new SkillUpCodec().EncodeOutbound("not a skill-up"));
    }
}
