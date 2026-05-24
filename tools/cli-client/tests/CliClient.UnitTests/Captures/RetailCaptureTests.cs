// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Net;
using N7.CliClient.Opcodes.Inbound;
using N7.CliClient.Opcodes.Outbound;
using Xunit;

namespace N7.CliClient.UnitTests.Captures;

/// <summary>
/// Cross-checks the codec implementations against actual bytes pulled
/// from a retail Westwood Earth &amp; Beyond client/server session
/// (capture_3.rar in archive/kyp-snapshot/capturedPackets/). The fixture
/// file <c>capture3-frames.txt</c> carries the verbatim payloads with
/// in-file provenance for each frame.
///
/// Per Phase S Item 14: every codec we ship must demonstrably decode
/// retail bytes correctly. Round-trip (decode→encode→byte-equal) is
/// the gold standard; where the wire carries optional/server-side
/// fields we don't model on the outbound path, we assert a prefix
/// match against the leading mandatory section.
/// </summary>
public sealed class RetailCaptureTests
{
    private static readonly IReadOnlyDictionary<string, CaptureFixture> Frames =
        CaptureFixture.Load("capture3-frames.txt");

    [Fact]
    public void Fixture_Loads_AllThreeFrames()
    {
        // Sanity-check the loader before the codec tests use it.
        Assert.Equal(3, Frames.Count);
        Assert.True(Frames.ContainsKey("master_join"));
        Assert.True(Frames.ContainsKey("server_redirect"));
        Assert.True(Frames.ContainsKey("client_chat"));

        Assert.Equal(64, Frames["master_join"].Payload.Length);
        Assert.Equal(10, Frames["server_redirect"].Payload.Length);
        Assert.Equal(14, Frames["client_chat"].Payload.Length);
    }

    [Fact]
    public void ServerRedirect_RetailCapture_Decodes()
    {
        var frame = Frames["server_redirect"];
        Assert.Equal(0x36, frame.Opcode);

        var codec = new ServerRedirectCodec();
        var redirect = (ServerRedirect)codec.DecodeInbound(frame.Payload);

        // Wire bytes: 19 29 00 00 | 2C E8 99 9F | AF 0D
        // sector_id    = BE int32 = 0x19290000 = 422576128
        // ip_address   = BE int32 → bytes [0x2C, 0xE8, 0x99, 0x9F] → 44.232.153.159
        // port         = LE uint16 = 0x0DAF = 3503
        Assert.Equal(0x19290000, redirect.SectorId);
        Assert.Equal(IPAddress.Parse("44.232.153.159"), redirect.ServerEndPoint.Address);
        Assert.Equal(3503, redirect.ServerEndPoint.Port);
    }

    [Fact]
    public void MasterJoin_RetailCapture_RoundTrips_Exactly()
    {
        var frame = Frames["master_join"];
        Assert.Equal(0x35, frame.Opcode);

        var codec = new MasterJoinCodec();
        var join = (MasterJoinRequest)codec.DecodeInbound(frame.Payload);

        // Spot-check the geometry — these are the high-leverage fields
        // (avatar id + sector + ticket). If any field offset / endianness
        // were wrong, one of these would be a smoking gun.
        Assert.Equal(2, join.Unknown1);
        Assert.Equal(2, join.Unknown2);
        Assert.Equal(0x40E5E7E8, join.Unknown3);
        Assert.Equal(0x3E221201, join.AvatarIdMsb);
        Assert.Equal(unchecked((int)0xF7645CC0), join.AvatarIdLsb);
        Assert.Equal(0x2919, join.ToSectorId);
        Assert.Equal(0, join.FromSectorId);
        Assert.Equal(0, join.PlayerLevel);
        Assert.Equal(1, join.Unknown8);
        Assert.Equal(1, join.Unknown9);
        Assert.Equal(0x7FFFFFFF, join.Unknown10);

        // Retail ticket is binary, not ASCII — this is the case the
        // Net-7 emulator never produces.
        byte[] expectedTicket = new byte[]
        {
            0x89, 0x77, 0x24, 0xDF, 0x40, 0x36, 0x32, 0xDD,
            0x42, 0x34, 0xA7, 0x59, 0x6F, 0xDF, 0x5A, 0x82,
            0x13, 0xB2, 0x70, 0xE8,
        };
        Assert.Equal(expectedTicket, join.Ticket);

        // Now the closing of the loop: re-encode and assert
        // byte-for-byte equality with the captured payload. If any
        // field rounds-trip lossily, the byte arrays diverge.
        byte[] reencoded = codec.EncodeOutbound(join);
        Assert.Equal(frame.Payload, reencoded);
    }

    [Fact]
    public void ClientChat_RetailCapture_DecodesAndPrefixRoundTrips()
    {
        var frame = Frames["client_chat"];
        Assert.Equal(0x33, frame.Opcode);

        var codec = new ClientChatCodec();
        var chat = (ClientChatMessage)codec.DecodeInbound(frame.Payload);

        Assert.Equal(0x00AACCEE, chat.GameId);
        Assert.Equal(ChatChannel.Broadcast, chat.Type);
        Assert.Equal("/who", chat.Message);

        // Codec re-encodes only the mandatory leading section: 4 + 1 + 2
        // header + "/who\0" = 12 bytes. The captured frame has 14 bytes
        // because the real client also emits the optional trailing
        // `_data_size` / `_unknown_data` block (both zero here) — see
        // struct ClientChat in common/include/net7/PacketStructures.h.
        // Asserting prefix-equality is the strict version of "the codec
        // round-trips every field it claims to model."
        byte[] reencoded = codec.EncodeOutbound(chat);
        Assert.Equal(12, reencoded.Length);
        Assert.Equal(frame.Payload[..12], reencoded);

        // And document explicitly what we're choosing not to model.
        Assert.Equal(0, frame.Payload[12]);
        Assert.Equal(0, frame.Payload[13]);
    }
}
