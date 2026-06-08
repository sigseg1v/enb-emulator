// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using N7.CliClient.Net;
using N7.CliClient.Opcodes.Inbound;
using N7.CliClient.Opcodes.Outbound;
using Xunit;

namespace N7.CliClient.IntegrationTests.Verification;

/// <summary>
/// Capture-replay tests against the real retail-server bytes in
/// <c>Fixtures/Captures/</c>. Asserts our codecs round-trip captured
/// payloads byte-for-byte (encode-then-decode-then-encode is a strict
/// identity) and that decoded fields match the values transcribed in
/// the fixture comments.
///
/// <para>
/// These tests do NOT require docker — they operate on cached bytes
/// only. They sit in the integration project (rather than UnitTests)
/// because the fixture files are integration-suite artifacts and
/// preservation reference material.
/// </para>
///
/// <para>
/// Per the server-integrity rules in CLAUDE.md, any byte-level
/// divergence between our codec output and the captured bytes is a
/// preservation finding to investigate — NOT an excuse to relax the
/// codec. Documented divergences live in
/// <c>docs/13-integration-tests.md</c>.
/// </para>
/// </summary>
public sealed class CaptureReplayTests
{
    [Fact]
    public void MasterJoin_RealCaptureBytes_RoundTripIdentity()
    {
        byte[] captured = HexFixture.Load("masterjoin_packet220.hex");
        Assert.Equal(MasterJoinCodec.WireSize, captured.Length);

        var codec = new MasterJoinCodec();
        var decoded = (MasterJoinRequest) codec.DecodeInbound(captured);

        // Field-by-field sanity against the fixture-comment transcription.
        Assert.Equal(2,          decoded.Unknown1);
        Assert.Equal(2,          decoded.Unknown2);
        Assert.Equal(0x40E60235, decoded.Unknown3);
        Assert.Equal(0x3E221201, decoded.AvatarIdMsb);
        Assert.Equal(unchecked((int) 0xF7645CC0), decoded.AvatarIdLsb);
        Assert.Equal(0x0000B05F, decoded.ToSectorId);  // 45151
        Assert.Equal(0,          decoded.FromSectorId);
        Assert.Equal(0,          decoded.PlayerLevel);
        Assert.Equal(1,          decoded.Unknown8);
        Assert.Equal(1,          decoded.Unknown9);
        Assert.Equal(0x7FFFFFFF, decoded.Unknown10);
        Assert.Equal(MasterJoinCodec.TicketLength, decoded.Ticket.Length);

        // The round-trip must be exact: re-encoded bytes equal the
        // original bytes. If this ever fails, our codec has drifted
        // from the real retail wire format — investigate before
        // "fixing" anything.
        byte[] reencoded = codec.EncodeOutbound(decoded);
        Assert.Equal(captured, reencoded);
    }

    [Fact]
    public void ServerRedirect_RealCaptureBytes_DecodesAllFields()
    {
        byte[] captured = HexFixture.Load("serverredirect_packet222.hex");
        Assert.Equal(ServerRedirectCodec.WireSize, captured.Length);

        var codec = new ServerRedirectCodec();
        var decoded = (ServerRedirect) codec.DecodeInbound(captured);

        // sector_id read LE = 0xB05F = 45151, matching the ToSectorID
        // the client sent in the preceding MasterJoin frame 220 (also
        // captured in masterjoin_packet220.hex above). This pairing is
        // the byte-level proof that the codec is faithful to retail
        // wire format. Cross-checked against three more redirect frames
        // (capture_1 656, 1062 and capture_2 222) -- all LE-on-wire.
        Assert.Equal(45151, decoded.SectorId);

        // ip_address read LE = int 0x9F99E82E, fed through
        // s_addr -> inet_ntoa it dots out to 159.153.232.46.
        Assert.Equal("159.153.232.46", decoded.ServerEndPoint.Address.ToString());

        // port read LE -> 3500.
        Assert.Equal(3500, decoded.ServerEndPoint.Port);
    }

    [Fact]
    public void MvasPosition_1004_RealCaptureBytes_MatchesProxyFeed()
    {
        // The live Net7Proxy 0x1004 datagram is 40 bytes: 12-byte header +
        // 6 floats + a trailing int32 the server never reads (PB-2). Our feed
        // -- both the headless MvasClient and the proxy's SendPositionIfChanged
        // -- emits the 24-byte server-consumed payload (header + 6 floats),
        // which must be byte-identical to the first 36 bytes of the capture.
        byte[] captured = HexFixture.Load("live_mvas_position_1004.hex");
        Assert.Equal(40, captured.Length);

        // Header: size, opcode, player_id, sequence.
        Assert.Equal(40, BinaryPrimitives.ReadUInt16LittleEndian(captured.AsSpan(0, 2)));
        Assert.Equal(0x1004, BinaryPrimitives.ReadUInt16LittleEndian(captured.AsSpan(2, 2)));
        Assert.Equal(unchecked((int) 0x4003992A),
            BinaryPrimitives.ReadInt32LittleEndian(captured.AsSpan(4, 4)));
        int sequence = BinaryPrimitives.ReadInt32LittleEndian(captured.AsSpan(8, 4));
        Assert.Equal(1, sequence);

        // Decode the position + heading exactly as the server does.
        float x = BinaryPrimitives.ReadSingleLittleEndian(captured.AsSpan(12, 4));
        float y = BinaryPrimitives.ReadSingleLittleEndian(captured.AsSpan(16, 4));
        float z = BinaryPrimitives.ReadSingleLittleEndian(captured.AsSpan(20, 4));
        float hx = BinaryPrimitives.ReadSingleLittleEndian(captured.AsSpan(24, 4));
        float hy = BinaryPrimitives.ReadSingleLittleEndian(captured.AsSpan(28, 4));
        float hz = BinaryPrimitives.ReadSingleLittleEndian(captured.AsSpan(32, 4));
        Assert.Equal(59725.73046875f, x);
        Assert.Equal(-5170.1552734375f, y);
        Assert.Equal(-743.9715576171875f, z);
        Assert.Equal(-0.2580232322216034f, hx);
        Assert.Equal(0.9613425135612488f, hy);
        Assert.Equal(0.09615380316972733f, hz);

        // The trailing int32 (server-ignored) is present in the real datagram.
        // It is NOT the packet sequence (that stays 1 across the first three
        // datagrams); it increments by an unknown rule (14, 34, 73, ...), so we
        // do not reproduce it -- doing so would be guessing.
        Assert.Equal(14, BinaryPrimitives.ReadInt32LittleEndian(captured.AsSpan(36, 4)));

        // Our emitter (headless MvasClient; the proxy's SendPositionIfChanged
        // builds the identical shape) reproduces the opcode, player_id,
        // sequence and all 6 floats EXACTLY. Two fields legitimately diverge
        // from retail and both are server-irrelevant:
        //   * size: ours = 36 (our datagram length), retail = 40 (it counts the
        //     trailing int32). The server gates the heading read on size>28 and
        //     reads no length-derived offset past the floats, so 36 and 40 are
        //     equivalent. The verified live feed (task #65) used 36.
        //   * the trailing int32 is absent (see above).
        // This is a documented preservation divergence, NOT a codec defect.
        byte[] ours = MvasClient.BuildDatagram(
            unchecked((int) 0x4003992A), sequence, x, y, z, (hx, hy, hz));
        Assert.Equal(36, ours.Length);

        // opcode + player_id + sequence (bytes 2..12) match retail exactly.
        Assert.Equal(captured.AsSpan(2, 10).ToArray(), ours.AsSpan(2, 10).ToArray());
        // all 6 floats (bytes 12..36) match retail exactly.
        Assert.Equal(captured.AsSpan(12, 24).ToArray(), ours.AsSpan(12, 24).ToArray());
        // size is the only header divergence: ours 36, retail 40.
        Assert.Equal(36, BinaryPrimitives.ReadUInt16LittleEndian(ours.AsSpan(0, 2)));
    }

    [Fact]
    public void HexFixture_RejectsMalformedInput()
    {
        Assert.Throws<FormatException>(() => HexFixture.Parse("ZZ"));
        Assert.Throws<FormatException>(() => HexFixture.Parse("A"));   // odd nibbles
        // Comments + whitespace are not malformed.
        byte[] ok = HexFixture.Parse("# header\nDE AD\n  BE EF  # trailer");
        Assert.Equal(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }, ok);
    }
}
