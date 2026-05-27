// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using N7.CliClient.Auth;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// Wave 16 post-handshake survival round-trip: client sends 0x004E
/// STARBASE_REQUEST with the Job-Terminal action (the wire frame the
/// retail Win32 client emits when the user clicks the Job Terminal NPC
/// inside a starbase), then verifies the connection survives via a
/// 0x0044 REQUEST_TIME round-trip.
///
/// <para>
/// STARBASE_REQUEST is the umbrella opcode for almost every starbase
/// interaction — exit-to-space (action=1), talk-to-NPC (action=4),
/// job-terminal-open (action=6), job-description (action=7), accept-job
/// (action=8/9), recustomise-avatar/ship (action=10/11). Most paths
/// mutate Player state (m_Gating, m_StarbaseTargetID, m_CurrentNPC,
/// trade window, mission state) or invoke heavy subsystems
/// (LaunchIntoSpace, talk-tree walker, mission generation). The
/// Job-Terminal path (action=6) is the leanest read-only branch: it
/// calls <c>SectorManager::GetJobList</c> and conditionally emits
/// <c>ENB_OPCODE_0093_JOB_LIST</c> when jobs exist. That's what we
/// drive here — it exercises the dispatcher, the proxy fall-through,
/// and the SectorManager lookup, without touching ship/mission state.
/// </para>
///
/// <para>
/// Why survival probe rather than direct reply assertion.
/// <c>Player::HandleStarbaseRequest</c> action=6
/// (<c>server/src/PlayerConnection.cpp:9928</c>) only sends back a
/// JOB_LIST frame when <c>SectorManager::GetJobList</c> returns a
/// non-zero byte count; the seed schema for sector 10151 in our test
/// fixture has no job rows, so the handler silently returns. We cannot
/// fabricate a JOB_LIST reply (CLAUDE.md server-integrity rule — the
/// real server doesn't reply when there are no jobs). Pipe survival
/// is the only assertable post-condition.
/// </para>
///
/// <para>
/// Concrete regression class this catches: if anyone reverts the Phase
/// R StarbaseRequest layout from <c>int32_t</c> to <c>long</c>, the
/// struct grows from 9B (2× 4B + 1B) to 17B on Linux x86_64 and the
/// handler reads <c>StarbaseID</c> from offset 8 (instead of 4) and
/// <c>Action</c> from byte 16 (instead of 8) — both past the end of
/// the 9B wire payload, into undefined memory. The garbage action
/// then routes through the switch arbitrarily; an action=1 garbage
/// read would invoke <c>LaunchIntoSpace</c> on a docked player and
/// drive them into invalid physics state. The survival probe still
/// passes when the garbage happens to be benign, but the sector
/// thread is one bad branch away from an assert.
/// </para>
///
/// <para>
/// Other bugs this test would also catch:
/// </para>
/// <list type="bullet">
///   <item>
///     Proxy <c>ProcessSectorServerOpcode</c> default-case forwarding
///     regression. STARBASE_REQUEST is not explicitly listed in
///     <c>proxy/ClientToServer_linux_stubs.cpp</c> (greps clean) so
///     it hits the <c>default:</c> arm and falls through to the
///     bottom-of-switch <c>ForwardClientOpcode</c>. A regression that
///     accidentally <c>return</c>ed in the default arm — or that
///     added an empty hand-coded case that <c>return</c>ed instead of
///     <c>break</c>ing — would silently drop the opcode and the
///     server would never see the request.
///   </item>
///   <item>
///     <c>SectorManager::GetJobList</c> null-deref or buffer overrun.
///     The handler hands it <c>m_ScratchBuffer</c> as a raw <c>u8 *</c>
///     with no length argument; any return-count overrun, or a null
///     sm pointer (which the code guards against with <c>sm</c>
///     inferred from outer scope before this branch is taken), would
///     fault the sector thread.
///   </item>
///   <item>
///     <c>m_TradeWindow = false</c> assignment racing with the
///     fan-out logic in the trade subsystem; if any code path
///     accidentally took <c>m_Mutex</c> around the trade-window flag,
///     a reentrant grab from action=6 would deadlock the sector
///     thread and the survival probe would never complete.
///   </item>
///   <item>
///     A dispatcher regression that misroutes 0x004E to a different
///     handler (the case label at <c>server/src/PlayerConnection.cpp:487</c>
///     is hand-maintained next to ~200 other opcodes; a renumber or
///     a copy-paste error would surface here as the survival probe
///     timing out because some other handler crashed on the
///     mis-routed payload).
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). The STARBASE_REQUEST payload
/// sent here is exactly the wire shape the retail Win32 client emits:
/// 4B PlayerID + 4B StarbaseID + 1B Action, all int32_t LE. Action=6
/// matches the retail client's Job-Terminal click. We are not making
/// the server accept anything it didn't previously accept. The retail
/// server emits no direct reply on the empty-job-list branch (it just
/// sets <c>m_TradeWindow = false</c> and returns); we don't fabricate
/// one. The test account is a fresh starbase character in Luna Station
/// per the standard test handshake, so dispatch context matches retail.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; STARBASE_REQUEST+REQUEST_TIME
/// round-trip is sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorStarbaseRequestTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorStarbaseRequestTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task JobTerminal_DoesNotBreakConnection_RequestTimeStillRoundTrips()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Jobber", shipName: "JobShip", cts.Token);

        try
        {
            // StarbaseRequest wire layout — 9 bytes:
            //   [0..4)   int32 LE  PlayerID    — retail client sets the
            //                                     actor's avatar id;
            //                                     server resolves the
            //                                     actor via connection
            //                                     binding (this field is
            //                                     effectively unused
            //                                     server-side), but its
            //                                     width matters for
            //                                     struct offsets.
            //   [4..8)   int32 LE  StarbaseID  — for action=6 this is
            //                                     0 (the Job Terminal is
            //                                     identified by action,
            //                                     not by NPC id; action=7
            //                                     puts a job description
            //                                     id here instead).
            //   [8..9)   byte      Action      — 6 = Job Terminal open
            //                                     (per HandleStarbaseRequest
            //                                     case 6 at
            //                                     server/src/PlayerConnection.cpp:9928).
            // common/include/net7/PacketStructures.h:812
            byte[] payload = new byte[9];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), 0);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), 0);
            payload[8] = 6;

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.StarbaseRequest.Value, payload),
                cts.Token);

            // Survival probe.
            int clientTick = unchecked((int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF));

            byte[] reqTimePayload = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(reqTimePayload, clientTick);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.RequestTime.Value, reqTimePayload),
                cts.Token);

            // Drain until 0x0034 CLIENT_SET_TIME. Tolerate interleaved
            // in-sector frames (and a possible JOB_LIST if the seed
            // ever grows job rows) — cap on frame count so a stalled
            // pipeline can't masquerade as the outer-CTS timeout.
            int framesSeen = 0;
            const int maxFrames = 400;
            while (framesSeen++ < maxFrames)
            {
                var reply = await session.Sector.ReceiveAsync(cts.Token);
                Assert.NotNull(reply);

                if (reply!.Header.Opcode != OpcodeId.Known.ClientSetTime.Value)
                    continue;

                var span = reply.Payload.Span;
                Assert.Equal(12, span.Length);

                int echoedClientSent = BinaryPrimitives.ReadInt32LittleEndian(span[..4]);
                Assert.Equal(clientTick, echoedClientSent);

                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x004E STARBASE_REQUEST (action=6 Job Terminal) " +
                $"+ 0x0044 REQUEST_TIME without seeing 0x0034 CLIENT_SET_TIME. " +
                $"Likely the server's HandleStarbaseRequest read past the 9B payload " +
                $"(sizeof(long) regression on StarbaseRequest struct), " +
                $"the proxy default-case forwarding dropped the opcode, " +
                $"or SectorManager::GetJobList faulted on the empty-jobs path.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }
}
