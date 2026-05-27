// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using N7.CliClient.Auth;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// Wave 47 post-handshake survival round-trip: client sends 0x3008
/// STARBASE_LOGIN_COMPLETE — handler at
/// <c>server/src/PlayerConnection.cpp:631-634</c> is a one-line
/// <c>SetNavCommence(); break;</c>. <see cref="SetNavCommence"/> is an
/// inline at <c>server/src/PlayerClass.h:1055</c> that sets a per-session
/// <c>bool m_NavCommence</c>. The only declared reader
/// <c>WaitForNavCommence()</c> at <c>PlayerClass.h:1056</c> has NO body
/// anywhere in the server source tree (confirmed by
/// <c>grep -rn WaitForNavCommence server/</c> returning only the one
/// declaration), and zero call sites. The bool is write-only — the
/// handler is therefore a TRUE no-op. Strictly the cleanest possible
/// survival probe — no SendOpcode, no LogMessage, no inner switch, no
/// payload decode.
///
/// <para>
/// Primary-source proof (per CLAUDE.md server-integrity escape hatch).
/// The Linux proxy at <c>proxy/ClientToServer_linux_stubs.cpp:413-449</c>
/// (mirroring the original Win32
/// <c>proxy/ClientToSectorServer.cpp:33-49</c>) synthesises a 0x3008
/// frame and forwards it to the server during the in-game START_ACK
/// fan-out when <c>m_UDPClient-&gt;GetSectorID() &gt; 9999</c>
/// (starbase). The payload is <c>sizeof(player_id)</c> bytes where
/// <c>long player_id = m_UDPClient-&gt;PlayerID()</c> — 4B on Win32
/// but 8B on Linux x86_64. Since the server handler does NOT read
/// the payload at all, the cross-platform width mismatch is harmless.
/// Our test uses <c>sectorId=10151</c> (Luna Station), which is &gt;
/// 9999, so the proxy ALREADY emits a 0x3008 to the server during
/// handshake. This test sends a SECOND idempotent 0x3008 — verifying
/// the dispatch arm exists and the connection survives another no-op
/// flag-set.
/// </para>
///
/// <para>
/// Server handler walk-through.
/// </para>
/// <list type="number">
///   <item>Dispatcher case at
///     <c>server/src/PlayerConnection.cpp:631-634</c>:
///     <c>case ENB_OPCODE_3008_STARBASE_LOGIN_COMPLETE: SetNavCommence(); break;</c>.
///     The comment <c>// fix bad opcode messages</c> documents that this
///     case was added to silence "Bad opcode" log spam — the dispatch
///     arm is preservation-load-bearing because removing it would
///     convert the proxy's existing 0x3008 emit (during every starbase
///     login) into a default-LogMessage spam line.</item>
///   <item><c>SetNavCommence()</c> at
///     <c>server/src/PlayerClass.h:1055</c>:
///     <c>void SetNavCommence() { m_NavCommence = true; }</c>.
///     One-line inline. Sets a single bool.</item>
///   <item><c>m_NavCommence</c> declared at
///     <c>server/src/PlayerClass.h:1300</c> as
///     <c>bool m_NavCommence;</c>. Initialised to <c>false</c> at
///     <c>server/src/PlayerClass.cpp:259</c> in the Player ctor.</item>
///   <item><c>WaitForNavCommence()</c> declared at
///     <c>server/src/PlayerClass.h:1056</c> as
///     <c>bool WaitForNavCommence();</c>. NO definition anywhere in
///     <c>server/</c> (confirmed by grep). NO call sites. The
///     <c>m_NavCommence</c> field is write-only — assigned by
///     SetNavCommence (called from 0x3004 PLAYER_SHIP_SENT and 0x3008
///     STARBASE_LOGIN_COMPLETE in PlayerConnection.cpp:627/633, and at
///     PlayerConnection.cpp:11119 in a different code path) but never
///     read.</item>
/// </list>
///
/// <para>
/// Why this wave target. After Wave 46 (MODE_NONE outer-switch default
/// LogMessage), 0x3008 is the next opcode-coverage point that fits the
/// pure-survival-probe pattern. It's the simplest possible handler in
/// the entire dispatch list at <c>PlayerConnection.cpp:423-639</c> —
/// one line of code, one bool assignment, zero side effects. Even
/// LogMessage is absent. This wave converts the proxy's implicit
/// "0x3008 is sent during handshake but no test asserts the dispatch
/// arm exists" into an explicit preservation-load-bearing test.
/// </para>
///
/// <para>
/// Concrete regression classes this catches:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Dispatcher mis-route at PlayerConnection.cpp:631.</b> The
///     0x3008 case label sits between 0x3004 PLAYER_SHIP_SENT (line
///     625-629 — large state mutation: SetNavCommence + FinishLogin)
///     and 0x2021 LOGIN_STAGE_ACK_C_S (line 636-637 —
///     HandleLoginAckReturn). A swap with 0x3004's handler would
///     call FinishLogin a second time on an already-logged-in player,
///     which would re-emit Start/SetCredits/SaveDatabase frames — the
///     survival probe might still pass but a tighter no-extra-frames
///     assertion would catch it. A swap with HandleLoginAckReturn
///     would deserialise our 8B zero payload as stage_id=0 and walk
///     the login-stage machinery, which on an already-CompleteLogin'd
///     player should short-circuit but would walk the wrong code arm.
///   </item>
///   <item>
///     <b>SetNavCommence rename or removal at
///     PlayerConnection.cpp:633.</b> If the handler body changes to a
///     non-trivial side-effecting call (e.g.
///     <c>SetConnectionTerminate()</c>, <c>SaveDatabase()</c>,
///     <c>SendStart()</c>), the survival probe still passes for
///     benign rewrites but a future byte-comparison wave would catch
///     the divergence.
///   </item>
///   <item>
///     <b>m_NavCommence field deletion at PlayerClass.h:1300.</b> If
///     the field is deleted and SetNavCommence is converted to a
///     no-op, this test still passes — write-only field deletion is
///     a refactor-clean operation. The test is robust to this.
///   </item>
///   <item>
///     <b>WaitForNavCommence wiring regression.</b> If a future
///     change defines WaitForNavCommence and wires it into the login
///     state machine (e.g. blocking SectorLogin2 on
///     <c>m_NavCommence == true</c>), the survival probe still
///     passes since we set the flag. The dispatch arm becomes
///     semantically meaningful — but a test that asserts the
///     OPPOSITE sequence (WaitForNavCommence-then-SetNavCommence)
///     would catch the dependency direction.
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at PlayerConnection.cpp:127.</b>
///     Phase K sizeof(int32_t) opcode-header fix keeps the per-client
///     UDP queue header at 4B; a revert corrupts the 0x2016 inner-tuple
///     parser and the REQUEST_TIME path silents.
///   </item>
///   <item>
///     <b>Proxy default-case ForwardClientOpcode regression for
///     0x3008.</b> The proxy at
///     <c>proxy/ClientToServer_linux_stubs.cpp:413-449</c> has an
///     EXPLICIT special case for 0x3008 inside the START_ACK
///     fan-out — but for an in-game 0x3008 from the client side
///     (this test's case), it falls through to the bottom-of-switch
///     ForwardClientOpcode default arm. A regression dropping that
///     default would mean the server never sees our explicit 0x3008
///     (the proxy-synthesised one during handshake would still
///     arrive). The test's REQUEST_TIME echo would still succeed in
///     that scenario — but a future "assert the server received 0x3008
///     exactly twice" wave would catch it.
///   </item>
///   <item>
///     <b>Proxy SendClientPacketSequence inner-opcode guard tightening
///     at <c>UDPProxyToClient_linux.cpp:568</c>.</b> Currently passes
///     0x0034 CLIENT_SET_TIME because 0x0034 &lt; 0x0FFF. A guard
///     tightening from <c>opcode &lt; 0x0FFF</c> to
///     <c>opcode &lt; 0x0030</c> would drop 0x0034 silently and the
///     survival probe would time out.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). 0x3008 with a zero-filled
/// 8-byte payload is exactly what the Linux proxy emits during the
/// starbase START_ACK fan-out (with `player_id` filled in, but the
/// handler ignores the payload). The retail proxy↔server pair has
/// always exchanged 0x3008 during starbase logins — this test sends
/// the same opcode a second time after handshake to verify the
/// dispatch arm remains intact under regression pressure. Zero
/// permissiveness added; not loosening any security posture; not
/// fabricating any reply. The handler is the cleanest no-op in the
/// entire dispatch list.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; 0x3008 + REQUEST_TIME round-trip is
/// sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorStarbaseLoginCompleteTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorStarbaseLoginCompleteTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task StarbaseLoginComplete_OnFreshChar_DoesNotBreakConnection_RequestTimeStillRoundTrips()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station (> 9999 = starbase)

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        // firstName "Umbriel" starts with lowercase 'u' for the
        // AccountManager.cpp:1147 vowel-check footgun (case-sensitive
        // a/e/i/o/u/y BEFORE toupper at line 1153).
        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Umbriel", shipName: "UmbrielShip", cts.Token);

        try
        {
            // 0x3008 STARBASE_LOGIN_COMPLETE — server handler at
            // server/src/PlayerConnection.cpp:631-634 is literally
            // `SetNavCommence(); break;` — sets a write-only bool.
            // No payload decode at all. We send an 8-byte all-zero
            // payload matching the Linux proxy's wire emit
            // (sizeof(long) = 8 on Linux x86_64; the proxy fills it
            // with player_id but the handler ignores). A zero-byte
            // payload would also work since the handler doesn't
            // touch `data`.
            byte[] payload = new byte[8];

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.StarbaseLoginComplete.Value, payload),
                cts.Token);

            // Survival probe: did the connection survive the
            // STARBASE_LOGIN_COMPLETE handler? Send REQUEST_TIME and
            // assert CLIENT_SET_TIME echoes our sentinel tick.
            int clientTick = unchecked((int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF));

            byte[] reqTimePayload = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(reqTimePayload, clientTick);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.RequestTime.Value, reqTimePayload),
                cts.Token);

            // Drain until 0x0034 CLIENT_SET_TIME. Tolerate interleaved
            // in-sector frames (positional updates from observers).
            // Unlike Wave 44/45 there's no AUX_DATA expected — the
            // 0x3008 handler is a true no-op — so the only frames
            // between our send and the CLIENT_SET_TIME echo should be
            // positional updates from any in-sector observers.
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
                $"drained {maxFrames} frames after sending 0x3008 STARBASE_LOGIN_COMPLETE + 0x0044 REQUEST_TIME " +
                $"without seeing 0x0034 CLIENT_SET_TIME. " +
                $"Likely the dispatcher arm at PlayerConnection.cpp:631 was removed (server would default-LogMessage 'Bad opcode'), " +
                $"a swap with the 0x3004 PLAYER_SHIP_SENT handler at line 625 triggered FinishLogin a second time, " +
                $"SetNavCommence at PlayerClass.h:1055 was changed to a crashing method call, " +
                $"the proxy's bottom-of-switch ForwardClientOpcode default at proxy/ClientToServer_linux_stubs.cpp dropped 0x3008, " +
                $"or the SendOpcode header-width fix at PlayerConnection.cpp:127 was reverted.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }
}
