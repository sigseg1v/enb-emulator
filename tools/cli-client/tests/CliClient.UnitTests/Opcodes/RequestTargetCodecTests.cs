// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Outbound;
using Xunit;

namespace N7.CliClient.UnitTests.Opcodes;

/// <summary>
/// Byte-pins <see cref="RequestTargetCodec"/> (0x0017 REQUEST_TARGET,
/// client -&gt; server) against the live reference server (216.219.87.147)
/// SkillTrainingHostileDevice2 capture. The 8-byte RequestTarget struct followed
/// the 12-byte outer sector header on the proxy&lt;-&gt;server leg; the outer
/// header is added by the transport, so we pin only the codec's payload.
///
/// Both fields are LITTLE-endian (HandleRequestTarget reads TargetID raw, no
/// ntohl), unlike 0x0027 INVENTORY_MOVE which is big-endian on every field.
/// </summary>
public sealed class RequestTargetCodecTests
{
    private const int PlayerGameId = 0x4003992A;

    [Fact]
    public void Opcode_Is_0x0017()
    {
        var codec = new RequestTargetCodec();
        Assert.Equal(OpcodeId.Known.RequestTarget, codec.Opcode);
        Assert.Equal(0x0017, codec.Opcode.Value);
    }

    [Fact]
    public void Encode_TargetMob_MatchesCapture_SkillTraining_dg35()
    {
        // SkillTrainingHostileDevice2 dg #35 body: target object 0x000187ED.
        //   2A 99 03 40  ED 87 01 00
        var msg = new RequestTargetMessage(PlayerGameId, TargetId: 0x000187ED);

        byte[] wire = new RequestTargetCodec().EncodeOutbound(msg);

        Assert.Equal(new byte[]
        {
            0x2A, 0x99, 0x03, 0x40,   // GameID 0x4003992A (LE)
            0xED, 0x87, 0x01, 0x00,   // TargetID 0x000187ED (100333) (LE)
        }, wire);
    }

    [Fact]
    public void Encode_NoTarget_IsNegativeOne()
    {
        byte[] wire = new RequestTargetCodec().EncodeOutbound(
            new RequestTargetMessage(PlayerGameId, RequestTargetMessage.NoTarget));

        Assert.Equal(new byte[]
        {
            0x2A, 0x99, 0x03, 0x40,
            0xFF, 0xFF, 0xFF, 0xFF,
        }, wire);
    }

    [Fact]
    public void EncodeDecode_RoundTrips()
    {
        var codec = new RequestTargetCodec();
        var msg = new RequestTargetMessage(PlayerGameId, 0x000187ED);
        var back = (RequestTargetMessage)codec.DecodeInbound(codec.EncodeOutbound(msg));
        Assert.Equal(msg, back);
    }

    [Fact]
    public void Encode_ProducesExactly8Bytes()
    {
        byte[] wire = new RequestTargetCodec().EncodeOutbound(new RequestTargetMessage(1, 2));
        Assert.Equal(RequestTargetCodec.Size, wire.Length);
    }

    [Fact]
    public void Decode_TooShort_Throws()
    {
        var codec = new RequestTargetCodec();
        Assert.Throws<InvalidDataException>(
            () => codec.DecodeInbound(new byte[RequestTargetCodec.Size - 1]));
    }

    [Fact]
    public void Encode_WrongMessageType_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new RequestTargetCodec().EncodeOutbound("not a request-target"));
    }
}
