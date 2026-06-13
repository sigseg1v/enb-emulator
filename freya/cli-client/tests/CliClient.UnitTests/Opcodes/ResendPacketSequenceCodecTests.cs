// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Outbound;
using Xunit;

namespace N7.CliClient.UnitTests.Opcodes;

/// <summary>
/// Byte-pins for the 0x2017 RESEND_PACKET_SEQUENCE request wire format
/// (<c>common/include/net7/PacketStructures.h struct ReSendRequest</c>).
/// No live server required.
///
/// <para>
/// These pins are the understanding-before-change precondition for the
/// server-side <c>Player::ReSendOpcodes</c> parse fix: the canonical layout
/// is the 8-byte {u32 start, u32 count} form the Win32 proxy build has always
/// emitted (its <c>long</c> is 4 bytes), and the 16-byte form is what an LP64
/// proxy build emitted before the shared wire struct pinned the width. The
/// old server parse read both fields as SIGNED SHORTS at offsets 0/4, which
/// goes negative past sequence 32767 -- the long-session "infinite load
/// screen" stall.
/// </para>
/// </summary>
public sealed class ResendPacketSequenceCodecTests
{
    private readonly ResendPacketSequenceCodec _codec = new();

    [Fact]
    public void Encode_PinsCanonicalEightByteLayout()
    {
        byte[] wire = _codec.EncodeOutbound(new ResendPacketSequenceRequest(0x0001_E240u, 3));

        // 123456 = 0x0001E240 LE, count 3 -- exactly two LE uint32s.
        Assert.Equal(new byte[] { 0x40, 0xE2, 0x01, 0x00, 0x03, 0x00, 0x00, 0x00 }, wire);
    }

    [Fact]
    public void Encode_SequenceAbove32767_KeepsFullWidth()
    {
        // The motivating case: a multi-hour session's sequence number. A
        // signed-16-bit reading of these bytes would be negative; the wire
        // bytes themselves are a plain LE uint32.
        byte[] wire = _codec.EncodeOutbound(new ResendPacketSequenceRequest(40000, 1));

        Assert.Equal(new byte[] { 0x40, 0x9C, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00 }, wire);
    }

    [Fact]
    public void Decode_CanonicalForm_RoundTrips()
    {
        var req = new ResendPacketSequenceRequest(70000, 5);

        var decoded = Assert.IsType<ResendPacketSequenceRequest>(
            _codec.DecodeInbound(_codec.EncodeOutbound(req)));

        Assert.Equal(req, decoded);
    }

    [Fact]
    public void Decode_LegacyLp64SixteenByteForm_ReadsLowDwords()
    {
        // {u64 start=40000, u64 count=2} as an LP64 build's struct
        // {long;long} memcpy: low dwords at offsets 0 and 8, high dwords zero.
        byte[] wire =
        {
            0x40, 0x9C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        };

        var decoded = Assert.IsType<ResendPacketSequenceRequest>(_codec.DecodeInbound(wire));

        Assert.Equal(40000u, decoded.PacketStart);
        Assert.Equal(2u, decoded.PacketCount);
    }

    [Fact]
    public void Decode_TooShort_Throws()
    {
        Assert.Throws<InvalidDataException>(() => _codec.DecodeInbound(new byte[4]));
    }

    [Fact]
    public void Opcode_Is2017()
    {
        Assert.Equal((ushort)0x2017, _codec.Opcode.Value);
        Assert.Equal(OpcodeId.Known.ResendPacketSequence, _codec.Opcode);
    }
}
