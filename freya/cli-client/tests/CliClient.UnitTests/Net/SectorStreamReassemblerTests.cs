// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using N7.CliClient.Net;
using Xunit;

namespace N7.CliClient.UnitTests.Net;

/// <summary>
/// Byte-pins the downstream MVAS/sector reassembly: 0x2016 batches, 0x201A
/// split-frame continuations, 0x1007 frequency, and the desync/resync guard.
/// The server-side framing this mirrors lives in server/src/UDP_MVAS.cpp +
/// PlayerConnection.cpp; the inner EnbTcpHeader is common/include/net7.
/// </summary>
public sealed class SectorStreamReassemblerTests
{
    private const ushort PacketSequence  = 0x2016;
    private const ushort PacketCSequence = 0x201A;
    private const ushort ToggleSendFreq  = 0x1007;

    /// <summary>Wrap a payload in a 12-byte EnbUdpHeader with the given UDP opcode.</summary>
    private static byte[] Datagram(ushort udpOpcode, params byte[] payload)
    {
        var dg = new byte[12 + payload.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(dg.AsSpan(0, 2), (ushort)dg.Length); // size (unread by reasm)
        BinaryPrimitives.WriteUInt16LittleEndian(dg.AsSpan(2, 2), udpOpcode);
        // player_id [4..8] + packet_sequence [8..12] left zero -- not read downstream.
        payload.CopyTo(dg.AsSpan(12));
        return dg;
    }

    /// <summary>One inner EnbTcpHeader frame: {ushort size = 4+body; ushort opcode} + body.</summary>
    private static byte[] Frame(ushort opcode, params byte[] body)
    {
        var f = new byte[4 + body.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(f.AsSpan(0, 2), (ushort)f.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(f.AsSpan(2, 2), opcode);
        body.CopyTo(f.AsSpan(4));
        return f;
    }

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    private static void AssertFrame(Packet p, ushort opcode, byte[] body)
    {
        Assert.Equal(opcode, p.Header.Opcode);
        Assert.Equal((ushort)(4 + body.Length), p.Header.Size);
        Assert.Equal(body, p.Payload.ToArray());
    }

    [Fact]
    public void SingleSequence_OneCompleteFrame_EmitsItExactly()
    {
        var r = new SectorStreamReassembler();
        byte[] frame = Frame(0x00A1, 0xDE, 0xAD, 0xBE, 0xEF);

        var emitted = r.Push(Datagram(PacketSequence, frame));

        Assert.True(r.Aligned);
        Packet only = Assert.Single(emitted);
        AssertFrame(only, 0x00A1, new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
    }

    [Fact]
    public void SingleSequence_TwoFramesBackToBack_EmitsBothInOrder()
    {
        var r = new SectorStreamReassembler();
        byte[] a = Frame(0x0010, 0x01);
        byte[] b = Frame(0x0020, 0x02, 0x03);

        var emitted = r.Push(Datagram(PacketSequence, Concat(a, b)));

        Assert.Equal(2, emitted.Count);
        AssertFrame(emitted[0], 0x0010, new byte[] { 0x01 });
        AssertFrame(emitted[1], 0x0020, new byte[] { 0x02, 0x03 });
    }

    [Fact]
    public void EmptyBodyFrame_IsEmitted_PayloadIsEmpty()
    {
        var r = new SectorStreamReassembler();
        var emitted = r.Push(Datagram(PacketSequence, Frame(0x0006)));   // size=4, no body

        Packet only = Assert.Single(emitted);
        Assert.Equal(0x0006, only.Header.Opcode);
        Assert.Equal(4, only.Header.Size);
        Assert.Empty(only.Payload.ToArray());
    }

    [Fact]
    public void FrameSplitAcrossSequenceThenContinuation_CompletesOnContinuation()
    {
        var r = new SectorStreamReassembler();
        byte[] frame = Frame(0x00B2, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66); // 10 bytes total
        byte[] head = frame[..6];   // header + 2 body bytes
        byte[] tail = frame[6..];   // remaining 4 body bytes

        var first = r.Push(Datagram(PacketSequence, head));
        Assert.Empty(first);                       // partial -- nothing yet
        Assert.True(r.Aligned);

        var second = r.Push(Datagram(PacketCSequence, tail));
        Packet only = Assert.Single(second);
        AssertFrame(only, 0x00B2, new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55, 0x66 });
    }

    [Fact]
    public void Continuation_BeforeAnySequence_IsIgnored_StaysUnaligned()
    {
        var r = new SectorStreamReassembler();
        var emitted = r.Push(Datagram(PacketCSequence, Frame(0x0001, 0x09)));

        Assert.Empty(emitted);
        Assert.False(r.Aligned);
    }

    [Fact]
    public void UnknownUdpOpcode_BetweenSplit_DoesNotCorruptReassembly()
    {
        var r = new SectorStreamReassembler();
        byte[] frame = Frame(0x00C3, 0xAB, 0xCD, 0xEF, 0x01);
        byte[] head = frame[..5];
        byte[] tail = frame[5..];

        Assert.Empty(r.Push(Datagram(PacketSequence, head)));
        Assert.Empty(r.Push(Datagram(0x2010, 0xFF, 0xFF))); // unknown control op -- ignored
        var done = r.Push(Datagram(PacketCSequence, tail));

        AssertFrame(Assert.Single(done), 0x00C3, new byte[] { 0xAB, 0xCD, 0xEF, 0x01 });
    }

    [Theory]
    [InlineData(20, 20)]
    [InlineData(1, 1)]
    [InlineData(60, 60)]
    [InlineData(61, 60)]    // clamped to ceiling
    [InlineData(1000, 60)]
    [InlineData(0, 1)]      // clamped to floor
    [InlineData(-5, 1)]
    public void ToggleSendFreq_SetsAndClampsFrequency(int wire, int expected)
    {
        var r = new SectorStreamReassembler();
        var payload = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(payload, wire);

        var emitted = r.Push(Datagram(ToggleSendFreq, payload));

        Assert.Empty(emitted);
        Assert.Equal(expected, r.Frequency);
    }

    [Fact]
    public void ToggleSendFreq_DefaultIs20_UntilSet()
    {
        Assert.Equal(20, new SectorStreamReassembler().Frequency);
    }

    [Fact]
    public void ToggleSendFreq_ShortPayload_LeavesFrequencyUnchanged()
    {
        var r = new SectorStreamReassembler();
        r.Push(Datagram(ToggleSendFreq, 0x05, 0x00));  // only 2 bytes -- ignored
        Assert.Equal(20, r.Frequency);
    }

    [Fact]
    public void DatagramShorterThanUdpHeader_IsIgnored()
    {
        var r = new SectorStreamReassembler();
        var emitted = r.Push(new byte[] { 0x01, 0x02, 0x03 });  // < 12 bytes
        Assert.Empty(emitted);
        Assert.False(r.Aligned);
    }

    [Fact]
    public void BogusSize_BelowHeaderSize_DesyncsClearsAndDealigns()
    {
        var logs = new List<string>();
        var r = new SectorStreamReassembler(logs.Add);

        // size field = 2 is < WireSize(4): impossible, signals a torn stream.
        byte[] torn = { 0x02, 0x00, 0x99, 0x00, 0xFF, 0xFF };
        var emitted = r.Push(Datagram(PacketSequence, torn));

        Assert.Empty(emitted);
        Assert.False(r.Aligned);                            // de-aligned
        Assert.Contains(logs, l => l.Contains("desync"));
    }

    [Fact]
    public void Desync_DiscardsAnythingBufferedBeforeTheBadHeader()
    {
        var r = new SectorStreamReassembler();
        // A valid frame, then a torn one in the same batch: the valid frame is
        // still emitted, then the bad size trips desync and clears the rest.
        byte[] good = Frame(0x0030, 0x77);
        byte[] torn = { 0x03, 0x00, 0x40, 0x00 };          // size=3 < 4 -> bogus
        var emitted = r.Push(Datagram(PacketSequence, Concat(good, torn)));

        AssertFrame(Assert.Single(emitted), 0x0030, new byte[] { 0x77 });
        Assert.False(r.Aligned);
    }

    [Fact]
    public void Desync_ThenContinuationIgnored_UntilFreshSequenceRealigns()
    {
        var r = new SectorStreamReassembler();
        // Trip a desync.
        r.Push(Datagram(PacketSequence, new byte[] { 0x02, 0x00, 0x00, 0x00 }));
        Assert.False(r.Aligned);

        // A continuation now lands while unaligned -- must be dropped.
        Assert.Empty(r.Push(Datagram(PacketCSequence, Frame(0x0040, 0x01))));
        Assert.False(r.Aligned);

        // A fresh 0x2016 re-establishes alignment and emits cleanly.
        var back = r.Push(Datagram(PacketSequence, Frame(0x0041, 0x02, 0x03)));
        AssertFrame(Assert.Single(back), 0x0041, new byte[] { 0x02, 0x03 });
        Assert.True(r.Aligned);
    }

    [Fact]
    public void MaxSizeFrame_DoesNotTripDesync()
    {
        var r = new SectorStreamReassembler();
        // size == 65535 is the largest legal value; body = 65531 bytes.
        var body = new byte[65531];
        for (int i = 0; i < body.Length; i++) body[i] = (byte)i;
        byte[] frame = Frame(0x00FF, body);
        Assert.Equal(65535, frame.Length);

        var emitted = r.Push(Datagram(PacketSequence, frame));
        AssertFrame(Assert.Single(emitted), 0x00FF, body);
        Assert.True(r.Aligned);
    }
}
