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
/// Wave 31 direct-reply round-trip: client sends an 8-byte 0x00B9
/// LOGOFF_REQUEST on the sector connection, expects the server's
/// unconditional 0x00BA LOGOFF_CONFIRMATION reply (zero-byte body).
///
/// <para>
/// Wire layout (mirror of <c>common/include/net7/PacketStructures.h:583</c>):
/// <code>
///   [0..4) int32 PlayerID
///   [4..8) int32 LogOutType
/// </code>
/// 8 bytes total; <c>ATTRIB_PACKED</c> so no implicit padding.
/// Both fields are commented out at the cast site in the handler
/// (<c>server/src/PlayerConnection.cpp:7711</c>) — the handler does
/// not read the payload at all. We send all-zero bytes.
/// </para>
///
/// <para>
/// Server handler. The dispatcher case at
/// <c>server/src/PlayerConnection.cpp:591</c> calls
/// <c>HandleLogoffRequest(data)</c> which runs three statements in
/// strict order (<c>PlayerConnection.cpp:7709-7720</c>):
/// <code>
///     g_ServerMgr-&gt;m_PlayerMgr.LeaveGroup(GroupID(), GameID());
///     SendLogoffConfirmation();
///     g_ServerMgr-&gt;m_PlayerMgr.DropPlayerFromGalaxy(this);
/// </code>
/// For a freshly-created starbase character, <c>GroupID() == -1</c>.
/// <c>PlayerManager::LeaveGroup</c> (<c>server/src/GroupManager.cpp:549</c>)
/// calls <c>GetGroupFromID(-1)</c> which returns NULL via the early
/// <c>if (GroupID == -1) return NULL;</c> guard at GroupManager.cpp:39
/// (the same guard Wave 22's OPTION test exercised). The handler then
/// falls into the null-group branch which calls
/// <c>SendEmptyGroupAux(PlayerID)</c> — for a fresh char with
/// <c>GroupID == -1</c>, that function's <c>SendVaMessage</c> is gated
/// off, and the set-to-default operations on <c>GroupInfo</c> are
/// idempotent (already-cleared state). The trailing
/// <c>SendAuxPlayer()</c> emits a 0x0061 AUX_PLAYER frame only if
/// <c>HasDiff()</c> is true — may or may not fire depending on
/// post-CompleteLogin GroupInfo deltas. The drain loop tolerates the
/// interleaved frame either way.
/// </para>
///
/// <para>
/// SendLogoffConfirmation (<c>server/src/PlayerConnection.cpp:7746-7751</c>):
/// <code>
///     SendOpcode(ENB_OPCODE_00BA_LOGOFF_CONFIRMATION, 0, 0);
///     SendPacketCache();
/// </code>
/// — per-client UDP queue add + immediate cache flush, the same
/// SendOpcode+SendPacketCache pattern Waves 8/24/26/27/28/30 rode.
/// Zero-byte body (length=0 argument). No SendToSector race because
/// SendOpcode writes directly to the per-client UDP queue (the Phase
/// K sizeof(int32_t) header fix at PlayerConnection.cpp:127 keeps the
/// per-client UDP queue header at the canonical 4-byte width).
/// </para>
///
/// <para>
/// DropPlayerFromGalaxy runs after SendLogoffConfirmation. By the
/// time it executes, SendPacketCache has already flushed the 0x00BA
/// out over the UDP wire (m_UDPConnection-&gt;SendResponse is called
/// synchronously inside SendPacketCache — see
/// PlayerConnection.cpp:235-246). The post-emit teardown sequence
/// (<c>DropPlayerFromSector → SetRemove → SaveLogout →
/// ReleasePlayerNode → SetActive(false) → UnSetIndex →
/// SetLoginStage(-1)</c> per <c>PlayerManager.cpp:119-140</c>) is
/// asynchronous to the wire emit, so the test reliably sees the
/// LOGOFF_CONFIRMATION before any teardown side-effect could
/// race. SaveLogout (<c>PlayerSaves.cpp:1638</c>) queues a
/// SAVE_CODE_LOGOUT message — the DB row persists, so the global-
/// plane <c>DeleteCreatedCharacterAsync</c> cleanup at the test's
/// tail still works (the GLOBAL connection is a separate UDP
/// channel on UDP 3810, independent of the now-dropped sector
/// player).
/// </para>
///
/// <para>
/// Why this wave target. 0x00B9 LOGOFF_REQUEST is the cleanest
/// remaining direct-reply candidate: the handler emits ONE
/// deterministic frame (0x00BA, length 0) on EVERY call, with no
/// payload-driven branching to qualify. The Wave 30 methodology
/// refinement ("read past every CALL the handler makes AND identify
/// which branch the wire payload selects") is satisfied vacuously
/// here — there is no payload-selected branch. The PRE-emit
/// LeaveGroup call is bounded (fresh-char no-op via the
/// GetGroupFromID(-1) early-return guard from Wave 22) and the
/// POST-emit DropPlayerFromGalaxy is asynchronous to the wire emit.
/// The first direct-reply wave whose reply opcode sits in the
/// 0x00BA-class proxy-forwarded band (0x0086 MISSION_FORFEIT and
/// 0x0088 PETITION_STUCK were 0x0086/0x0088 client→server; here we
/// cover the 0x00B9/0x00BA pair end-to-end).
/// </para>
///
/// <para>
/// Why a positive reply assertion rather than a survival probe.
/// 0x00BA LOGOFF_CONFIRMATION is unconditional — every call to
/// HandleLogoffRequest emits exactly one 0x00BA frame regardless of
/// player state. A survival probe via REQUEST_TIME would not work
/// here because DropPlayerFromGalaxy runs after the emit and tears
/// down the sector Player — subsequent REQUEST_TIME would arrive
/// at a Player node that's been released back to the pool, and the
/// CLIENT_SET_TIME reply path goes through the now-inactive player.
/// The direct-reply assertion is strictly stronger anyway: we pin
/// the exact reply opcode (0x00BA) and the exact body length (0)
/// that retail emits.
/// </para>
///
/// <para>
/// Why all-zero payload. HandleLogoffRequest casts the data pointer
/// to <c>LogoffRequest *</c> but immediately comments out the cast
/// (line 7711: <c>//LogoffRequest * request = (LogoffRequest *) data;</c>).
/// The handler never reads either field. All-zero bytes are the
/// safest payload: no field-order assumptions, no byte-order
/// assumptions, no parser side-effects. The retail Win32 client
/// sends the player's actual GameID + a LogOutType code; for the
/// preservation test we just need to walk the dispatcher into the
/// handler and observe the unconditional emit.
/// </para>
///
/// <para>
/// Regression classes this catches.
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Dispatcher mis-route at server/src/PlayerConnection.cpp:591.</b>
///     The case label sits between 0x0088 PETITION_STUCK and 0x00BC
///     CTA_REQUEST in the ~200-entry dispatcher switch. A copy-paste
///     swap with HandleCTARequest would route our 8B payload through
///     that handler's <c>*((long*) &amp;CTAResponse[N])</c> writes
///     (PlayerConnection.cpp:7740-7741) — sizeof(long)==8 on Linux
///     x86_64 means each write spills 4 bytes past the 9-byte
///     CTAResponse stack buffer, corrupting adjacent stack
///     (potentially the return address). Surfaces as a SEGV on
///     return → connection drops → no 0x00BA → test times out.
///   </item>
///   <item>
///     <b>Proxy 0x00B9 handling regression.</b> The proxy's
///     <c>case ENB_OPCODE_00B9_LOGOFF_REQUEST</c> at
///     <c>proxy/ClientToServer_linux_stubs.cpp:498-507</c> sets
///     <c>g_LoggedIn=true</c> then falls through to the
///     bottom-of-switch ForwardClientOpcode. A regression that
///     returned early (instead of breaking) would mean the server
///     never receives 0x00B9 → no 0x00BA emit → test times out. A
///     regression that dropped the bottom-of-switch forward would
///     produce the same failure.
///   </item>
///   <item>
///     <b>SendLogoffConfirmation length argument flip.</b>
///     PlayerConnection.cpp:7749 passes <c>length=0</c> to
///     SendOpcode. A regression to <c>sizeof(LogoffRequest)</c> or
///     similar would emit a non-empty body — the test pins the
///     body length to exactly 0 so any payload corruption is caught.
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at PlayerConnection.cpp:127.</b>
///     The Phase K sizeof(int32_t) fix keeps the per-client UDP
///     queue opcode header at 4 bytes (was sizeof(long)==8 on Linux
///     pre-fix). A revert would shift every subsequent reply opcode
///     in the 0x2016 PACKET_SEQUENCE inner-tuple parser by 4 bytes —
///     0x00BA would be unparseable and the test times out.
///   </item>
///   <item>
///     <b>0x00BA proxy SendClientPacketSequence guard.</b> The
///     proxy's <c>UDPClient::SendClientPacketSequence</c> at
///     <c>proxy/UDPProxyToClient_linux.cpp:568</c> gates inner
///     opcodes on <c>opcode &gt; 0x0000 &amp;&amp; opcode &lt; 0x0FFF</c>.
///     0x00BA passes (0x00BA &lt; 0x0FFF). A regression that
///     tightened the guard to e.g. <c>&lt; 0x00BA</c> would silently
///     drop this reply — test times out.
///   </item>
///   <item>
///     <b>HandleLogoffRequest pre-emit LeaveGroup crash.</b> The
///     <c>g_ServerMgr-&gt;m_PlayerMgr.LeaveGroup(GroupID(), GameID())</c>
///     call runs BEFORE the SendLogoffConfirmation emit. A
///     regression in GetGroupFromID's <c>GroupID == -1</c> early
///     return (GroupManager.cpp:39) would walk m_GroupList; with
///     no groups present the loop falls through, but a regression
///     that dereferenced a stale m_GroupList head would crash the
///     sector thread before the 0x00BA emit → test times out.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). 0x00B9 LOGOFF_REQUEST is
/// exactly what the retail Win32 client emits when the user clicks
/// the Quit/Logoff button — two int32_t fields the retail server
/// ignored entirely (the cast site has been commented out since the
/// kyp/tada-o upstream and matches the retail server's observed
/// behaviour of never reading the payload). The 0x00BA
/// LOGOFF_CONFIRMATION zero-body reply is the verbatim retail-server
/// response — preservation-grade fidelity, not a fabrication. We are
/// not making the server accept any new input shape, not loosening
/// any security posture, not fabricating any reply.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; LOGOFF_REQUEST + 0x00BA round-trip
/// is sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorLogoffRequestTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorLogoffRequestTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task LogoffRequest_AllZeroPayload_ReceivesLogoffConfirmationWithEmptyBody()
    {
        var account = TestAccounts.New(_server);
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Quitter", shipName: "QuitterShip", cts.Token);

        try
        {
            // 0x00B9 LOGOFF_REQUEST — 8B canonical payload. The
            // handler casts to LogoffRequest* but the cast is
            // commented out (PlayerConnection.cpp:7711) so neither
            // field is read. All-zero bytes are the safest payload.
            //   [0..4) int32 PlayerID    = 0 (handler ignores)
            //   [4..8) int32 LogOutType  = 0 (handler ignores)
            byte[] payload = new byte[8];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), 0);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), 0);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.LogoffRequest.Value, payload),
                cts.Token);

            // Drain inbound until we see a 0x00BA LOGOFF_CONFIRMATION
            // with a zero-byte body. The handler may also emit a
            // 0x0061 AUX_PLAYER frame from the pre-emit
            // SendEmptyGroupAux path if HasDiff() is true; we
            // tolerate that and keep draining.
            int framesSeen = 0;
            const int maxFrames = 400;
            while (framesSeen++ < maxFrames)
            {
                var reply = await session.Sector.ReceiveAsync(cts.Token);
                Assert.NotNull(reply);

                if (reply!.Header.Opcode != OpcodeId.Known.LogoffConfirmation.Value)
                    continue;

                // 0x00BA wire layout: zero-byte body (per
                // SendLogoffConfirmation's SendOpcode(opcode, 0, 0)
                // at PlayerConnection.cpp:7749). Pin the exact
                // length so any payload corruption is caught.
                Assert.Equal(0, reply.Payload.Length);
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x00B9 LOGOFF_REQUEST " +
                $"without seeing 0x00BA LOGOFF_CONFIRMATION. " +
                $"Likely the server's HandleLogoffRequest path broke " +
                $"(SendLogoffConfirmation length-arg flip, SendOpcode header-width revert, " +
                $"pre-emit LeaveGroup/GetGroupFromID(-1) early-return guard removed, " +
                $"or DropPlayerFromGalaxy ran synchronously and tore down the connection " +
                $"before the UDP queue flushed), the proxy's 0x00B9 case at " +
                $"ClientToServer_linux_stubs.cpp:498 stopped falling through to " +
                $"ForwardClientOpcode, the dispatcher case at PlayerConnection.cpp:591 " +
                $"got mis-routed (swap with HandleCTARequest would trigger that handler's " +
                $"sizeof(long) stack-clobber bug in the CTAResponse buffer), or the proxy's " +
                $"SendClientPacketSequence guard at UDPProxyToClient_linux.cpp:568 " +
                $"(opcode < 0x0FFF) was tightened.");
        }
        finally
        {
            // Cleanup via the GLOBAL plane (separate UDP channel on
            // UDP 3810, independent of the now-dropped sector
            // player). SaveLogout queues a SAVE_CODE_LOGOUT message
            // but the DB row persists so the global delete works.
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }
}
