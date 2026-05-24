// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Inbound;
using N7.CliClient.Opcodes.Outbound;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// 0x0000 VersionRequest → 0x0001 VersionResponse round-trip against
/// the live proxy on the global server port. Asserts the server's
/// 3-way status logic from proxy/ClientToServer_linux_stubs.cpp:50-53:
/// major=42 minor=0 → status=0; major&lt;42 → status=1; major&gt;42 → status=2.
///
/// <para>
/// This is the first full opcode round-trip in the suite — connect →
/// handshake → send typed packet → receive typed packet → assert. No
/// avatar, no DB, no UDP plane; the global server handles VersionRequest
/// inline as the very first dispatch after the encrypted channel is up.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class VersionRequestTests
{
    private readonly ServerFixture _server;

    public VersionRequestTests(ServerFixture server)
    {
        _server = server;
    }

    [Fact]
    public async Task CurrentVersion_ReturnsStatusZero()
    {
        var response = await SendVersionAndReceive(major: 42, minor: 0);
        Assert.Equal(0, response.Status);
        Assert.True(response.ClientUpToDate);
    }

    [Fact]
    public async Task OlderClient_ReturnsStatusOne()
    {
        var response = await SendVersionAndReceive(major: 41, minor: 0);
        Assert.Equal(1, response.Status);
        Assert.True(response.ClientTooOld);
    }

    [Fact]
    public async Task NewerClient_ReturnsStatusTwo()
    {
        var response = await SendVersionAndReceive(major: 43, minor: 0);
        Assert.Equal(2, response.Status);
        Assert.True(response.ClientNewer);
    }

    private async Task<VersionResponse> SendVersionAndReceive(int major, int minor)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        await using var conn = await EncryptedTcpConnection.ConnectAsync(
            _server.GlobalHost, _server.GlobalPort, cts.Token);

        var codec = new VersionRequestCodec();
        var packet = Packet.ForOpcode(
            OpcodeId.Known.VersionRequest.Value,
            codec.EncodeOutbound(new VersionRequest(major, minor)));

        await conn.SendAsync(packet, cts.Token);

        // Drain inbound until we see the version response. The global
        // server is allowed to emit other packets in response to the
        // connection (it doesn't today, but the test should still work
        // if Phase K wires more pre-version traffic later — we react
        // to opcode, not to ordering).
        while (true)
        {
            var reply = await conn.ReceiveAsync(cts.Token);
            Assert.NotNull(reply);
            if (reply!.Header.Opcode == OpcodeId.Known.VersionResponse.Value)
            {
                return (VersionResponse) new VersionResponseCodec()
                    .DecodeInbound(reply.Payload.Span);
            }
        }
    }
}
