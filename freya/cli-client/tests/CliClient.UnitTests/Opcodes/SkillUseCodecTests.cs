// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Outbound;
using Xunit;

namespace N7.CliClient.UnitTests.Opcodes;

/// <summary>
/// Byte-pins <see cref="SkillUseCodec"/> (0x0058 SKILL_ABILITY / SkillUse,
/// client -&gt; server) against the live reference server
/// SkillTrainingHostileDevice2 capture. The 12-byte SkillUse struct followed the
/// 12-byte outer sector header on the proxy&lt;-&gt;server leg; the outer header
/// is added by the transport, so we pin only the codec's payload.
///
/// All fields are LITTLE-endian (HandleSkillAbility casts the struct with no
/// ntohl), unlike 0x0027 INVENTORY_MOVE which is big-endian on every field.
/// </summary>
public sealed class SkillUseCodecTests
{
    private const int PlayerGameId = 0x4003992A;

    [Fact]
    public void Opcode_Is_0x0058()
    {
        var codec = new SkillUseCodec();
        Assert.Equal(OpcodeId.Known.SkillAbility, codec.Opcode);
        Assert.Equal(0x0058, codec.Opcode.Value);
    }

    [Fact]
    public void Encode_UseAbility46_MatchesCapture_SkillTraining_dg28()
    {
        // SkillTrainingHostileDevice2 dg #28 body: fire ability index 46.
        //   2A 99 03 40  00 00 00 00  2E 00 00 00
        var msg = new SkillUseMessage(PlayerGameId, Action: 0, AbilityIndex: 46);

        byte[] wire = new SkillUseCodec().EncodeOutbound(msg);

        Assert.Equal(new byte[]
        {
            0x2A, 0x99, 0x03, 0x40,   // GameID 0x4003992A (LE)
            0x00, 0x00, 0x00, 0x00,   // Action = 0 (server-ignored)
            0x2E, 0x00, 0x00, 0x00,   // AbilityIndex = 46 (LE)
        }, wire);
    }

    [Fact]
    public void DefaultActionCtor_UsesCapturedValue()
    {
        // The two-arg ctor must default Action to the captured value (0).
        byte[] wire = new SkillUseCodec().EncodeOutbound(
            new SkillUseMessage(PlayerGameId, abilityIndex: 46));

        Assert.Equal(new byte[]
        {
            0x2A, 0x99, 0x03, 0x40,
            0x00, 0x00, 0x00, 0x00,
            0x2E, 0x00, 0x00, 0x00,
        }, wire);
    }

    [Fact]
    public void EncodeDecode_RoundTrips()
    {
        var codec = new SkillUseCodec();
        var msg = new SkillUseMessage(PlayerGameId, Action: 0, AbilityIndex: 7);
        var back = (SkillUseMessage)codec.DecodeInbound(codec.EncodeOutbound(msg));
        Assert.Equal(msg, back);
    }

    [Fact]
    public void Encode_ProducesExactly12Bytes()
    {
        byte[] wire = new SkillUseCodec().EncodeOutbound(new SkillUseMessage(1, 0, 2));
        Assert.Equal(SkillUseCodec.Size, wire.Length);
    }

    [Fact]
    public void Decode_TooShort_Throws()
    {
        var codec = new SkillUseCodec();
        Assert.Throws<InvalidDataException>(
            () => codec.DecodeInbound(new byte[SkillUseCodec.Size - 1]));
    }

    [Fact]
    public void Encode_WrongMessageType_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new SkillUseCodec().EncodeOutbound("not a skill-use"));
    }
}
