// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Buffers.Binary;
using System.Text;
using N7.CliClient.Auth;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using N7.CliClient.Repl;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// 0x3009 CliStatusRequest -> 0x300A CliStatusReply against the live proxy on
/// the global plane (3805). This is a CLI&lt;-&gt;proxy-only introspection
/// exchange: the proxy answers it from its own link state and never forwards it
/// to the server, so it touches no server security surface. The test proves the
/// round-trip works over the encrypted global channel and that the proxy
/// truthfully reports the dev stack's plaintext (DTLS-disabled) posture.
///
/// <para>
/// The dev compose proxy runs with NET7_DTLS_ALLOW_PLAINTEXT set, so the proxy
/// reports <c>dtls_required=0</c>. A prod proxy enforcing DTLS would report
/// <c>dtls_required=1</c> with a live association count -- but we cannot assert
/// that here because the dev stack is the plaintext leg by design.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class ProxyStatusRequestTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public ProxyStatusRequestTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [RetryFact]
    public async Task StatusRequest_OverGlobalPlane_ReportsConnectedAndPlaintext()
    {
        var account = TestAccounts.New(_server);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // A real ticket + GlobalConnect first, so the proxy has actually
        // established its server-side UDP link before we ask about it.
        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var conn = await EncryptedTcpConnection.ConnectAsync(
            _server.GlobalHost, _server.GlobalPort, cts.Token);

        await SectorEnterDriver.SendGlobalConnectAsync(conn, login.Ticket!, cts.Token);
        await SectorEnterDriver.DrainUntilOpcode(
            conn, OpcodeId.Known.GlobalAvatarList.Value, cts.Token);

        // Now the actual subject under test: the proxy status probe.
        var status = await ProxyStatus.QueryAsync(conn, TimeSpan.FromSeconds(5), cts.Token);

        Assert.NotNull(status);                 // the proxy answered 0x300A
        Assert.True(status!.Connected);         // server-side UDP link is live
        Assert.False(status.DtlsRequired);      // dev stack opted into plaintext
        Assert.Equal(0, status.LiveDtlsAssociations);  // no DTLS in plaintext mode

        // And the rendered login line must say DISABLED (red caps), never "on".
        string line = ProxyStatus.DtlsLine(status);
        Assert.Contains("DISABLED", line);
    }
}
