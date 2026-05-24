// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

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

        // sector_id is the documented byte-order divergence point. Our
        // codec reads BE and matches our proxy's ntohl-then-dump path
        // — see the fixture comment for the preservation discussion.
        // The number is gibberish-large under BE interpretation; the
        // test asserts what our codec produces today, not what retail
        // "should" have meant. Catches a regression in our codec
        // either way.
        Assert.Equal(unchecked((int) 0x5FB00000), decoded.SectorId);

        // ip_address read as BE → conventional dotted IP.
        Assert.Equal("46.232.153.159", decoded.ServerEndPoint.Address.ToString());

        // port read as LE → the well-known sector port. This is the
        // strongest agreement we have with retail today.
        Assert.Equal(3500, decoded.ServerEndPoint.Port);
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
