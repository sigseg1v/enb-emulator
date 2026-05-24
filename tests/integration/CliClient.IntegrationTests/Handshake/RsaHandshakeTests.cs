// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

using N7.CliClient.Net;
using Xunit;

namespace N7.CliClient.IntegrationTests.Handshake;

/// <summary>
/// Asserts the proxy's three handshake endpoints (master 3801,
/// global 3805, sector 3500) all accept the
/// <see cref="EncryptedTcpConnection"/> RSA + RC4 client-key exchange.
/// A handshake that throws means the server rejected our pubkey
/// reply, hung up early, or sent something we couldn't parse.
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class RsaHandshakeTests
{
    private readonly ServerFixture _server;

    public RsaHandshakeTests(ServerFixture server)
    {
        _server = server;
    }

    [Fact]
    public async Task GlobalServer_AcceptsClientKeyExchange()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var conn = await EncryptedTcpConnection.ConnectAsync(
            _server.GlobalHost, _server.GlobalPort, cts.Token);

        Assert.Equal(_server.GlobalHost, conn.Host);
        Assert.Equal(_server.GlobalPort, conn.Port);
    }

    [Fact]
    public async Task MasterServer_AcceptsClientKeyExchange()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var conn = await EncryptedTcpConnection.ConnectAsync(
            _server.MasterHost, _server.MasterPort, cts.Token);

        Assert.Equal(_server.MasterHost, conn.Host);
        Assert.Equal(_server.MasterPort, conn.Port);
    }

    [Fact]
    public async Task SectorServer_AcceptsClientKeyExchange()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var conn = await EncryptedTcpConnection.ConnectAsync(
            _server.SectorHost, _server.SectorPort, cts.Token);

        Assert.Equal(_server.SectorHost, conn.Host);
        Assert.Equal(_server.SectorPort, conn.Port);
    }
}
