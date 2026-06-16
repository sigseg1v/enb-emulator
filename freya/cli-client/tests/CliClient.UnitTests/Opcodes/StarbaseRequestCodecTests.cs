// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Outbound;
using Xunit;

namespace N7.CliClient.UnitTests.Opcodes;

/// <summary>
/// Byte-pins <see cref="StarbaseRequestCodec"/> (0x004E STARBASE_REQUEST,
/// client -&gt; server) against the live reference server
/// VendorInvEco capture. The 9-byte StarbaseRequest struct followed the 12-byte
/// outer sector header on the proxy&lt;-&gt;server leg; the outer header is added
/// by the transport, so we pin only the codec's payload.
///
/// Both ints are LITTLE-endian (HandleStarbaseRequest casts the struct with no
/// ntohl), unlike 0x0027 INVENTORY_MOVE which is big-endian on every field.
/// </summary>
public sealed class StarbaseRequestCodecTests
{
    // The PlayerID field carries the masked avatar id: the capture's outer
    // session GameID is 0x4003992A but StarbaseRequest.PlayerID is 0x0003992A.
    private const int CaptureMaskedPlayerId = 0x0003992A;

    [Fact]
    public void Opcode_Is_0x004E()
    {
        var codec = new StarbaseRequestCodec();
        Assert.Equal(OpcodeId.Known.StarbaseRequest, codec.Opcode);
        Assert.Equal(0x004E, codec.Opcode.Value);
    }

    [Fact]
    public void Encode_TalkToNpc_MatchesCapture_VendorInvEco_dg4()
    {
        // VendorInvEco dg #4 body: open the vendor NPC's talk tree.
        //   2A 99 03 00  C4 01 00 00  04
        var msg = new StarbaseRequestMessage(
            PlayerId: CaptureMaskedPlayerId,
            StarbaseId: 452,
            Action: StarbaseRequestMessage.ActionTalkToNpc);

        byte[] wire = new StarbaseRequestCodec().EncodeOutbound(msg);

        Assert.Equal(new byte[]
        {
            0x2A, 0x99, 0x03, 0x00,   // PlayerID 0x0003992A (LE)
            0xC4, 0x01, 0x00, 0x00,   // StarbaseID 452 (LE)
            0x04,                     // Action = 4 (talk to NPC)
        }, wire);
    }

    [Fact]
    public void Encode_ExitStation_HasNoTarget()
    {
        // Action 1 (exit) needs no fixture id -- StarbaseID 0.
        byte[] wire = new StarbaseRequestCodec().EncodeOutbound(
            new StarbaseRequestMessage(CaptureMaskedPlayerId, 0,
                StarbaseRequestMessage.ActionExitStation));

        Assert.Equal(new byte[]
        {
            0x2A, 0x99, 0x03, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x01,
        }, wire);
    }

    [Fact]
    public void EncodeDecode_RoundTrips()
    {
        var codec = new StarbaseRequestCodec();
        var msg = new StarbaseRequestMessage(0x0003992A, 452,
            StarbaseRequestMessage.ActionTalkToNpc);
        var back = (StarbaseRequestMessage)codec.DecodeInbound(codec.EncodeOutbound(msg));
        Assert.Equal(msg, back);
    }

    [Fact]
    public void Encode_ProducesExactly9Bytes()
    {
        byte[] wire = new StarbaseRequestCodec().EncodeOutbound(
            new StarbaseRequestMessage(1, 2, 4));
        Assert.Equal(StarbaseRequestCodec.Size, wire.Length);
    }

    [Fact]
    public void Decode_TooShort_Throws()
    {
        var codec = new StarbaseRequestCodec();
        Assert.Throws<InvalidDataException>(
            () => codec.DecodeInbound(new byte[StarbaseRequestCodec.Size - 1]));
    }

    [Fact]
    public void Encode_WrongMessageType_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new StarbaseRequestCodec().EncodeOutbound("not a starbase request"));
    }
}
