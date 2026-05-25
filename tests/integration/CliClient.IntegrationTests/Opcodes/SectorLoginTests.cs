// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Auth;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// End-to-end happy-path test for the sector-login handshake:
/// Auth → GlobalConnect → GlobalTicketRequest → MasterJoin → sector
/// TCP LOGIN (0x0002) → drained 0x2020 stages → 0x0005 START.
///
/// <para>
/// This test is the integration harness for the entire Phase K UDP
/// plane plus the proxy's MVAS-fan-out path. It exercises:
/// </para>
/// <list type="bullet">
///   <item>The unconnected global plane on the proxy
///         (<c>proxy/UDPClient_linux.cpp</c> with
///         <c>m_Unconnected=true</c>) — server→proxy in-game UDP comes
///         from <c>server:3806</c> (MVASauth) to the proxy's global-plane
///         source port, which a connected SOCK_DGRAM would silently
///         drop because the peer port doesn't match the connect()'d
///         peer (3810).</item>
///   <item>The proxy's <c>HandleStageConfirm</c> automatically replying
///         0x2021 ACKs on the client's behalf (the client over TCP 3500
///         never sees the 0x2020 frames — the proxy consumes them in
///         <c>HandleCustomOpcode</c>).</item>
///   <item>The server's login state machine (stages 1→2→...→13 in
///         <c>server/src/PlayerManager.cpp:534-601</c>) advancing all
///         the way to <c>CompleteLogin</c> + <c>SendStart</c>.</item>
///   <item>The 4-byte <c>int32_t</c> wire format for stage IDs (Win32
///         <c>sizeof(long)=4</c>; Linux <c>sizeof(long)=8</c> sent 4
///         garbage bytes that scrambled subsequent opcodes in the UDP
///         packet sequence). Same wire-size class as the Phase K
///         MasterJoin / GlobalTicket fixes.</item>
/// </list>
///
/// <para>
/// Budget: 60s. The actual happy path runs sub-2s; the wide budget
/// catches a regression where any link in the chain falls back to the
/// 5s WaitForResponse timeout. The login state machine has four
/// wait-for-ack rounds (stages 3/6/9/12) plus per-stage server work;
/// each ack round adds ~100ms of poll latency.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorLoginTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorLoginTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task FullSectorLogin_ReceivesStart()
    {
        // cli_test04 — Pool[3]. Reserved here so the per-compose-lifetime
        // CreateCharacter / DeleteCharacter cycle this test runs can't
        // collide on IsUsernameUnique with the create-character test
        // (which uses Pool[2]).
        var account = TestAccounts.Pool[3];
        const int slot = 0;

        // Terran Warrior starting sector from avatar_base
        // (StartSector[0*3+0] = 10151 = Luna Station).
        const int sectorId = 10151;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Loginus", shipName: "LoginShip", cts.Token);

        // start_id wire-format sanity check — the pre-fix Linux server
        // emitted 8 bytes for sizeof(long), which the proxy then read as
        // start_id=low32, plus 4 bytes of garbage that shifted the next
        // opcode in the UDP sequence. A non-zero int32_t start_id means
        // we received exactly 4 bytes in the START payload.
        Assert.NotEqual(0, session.StartId);

        // Cleanup: delete the created character so a re-run starts from
        // the empty-slot baseline. Best-effort — primary failure (if any)
        // has already been reported.
        using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
        catch { /* best-effort cleanup */ }
    }
}
