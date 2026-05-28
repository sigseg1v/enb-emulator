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
/// Wave 57 direct-reply round-trip (+1 ratchet): client establishes a
/// station-sector handshake at Luna Station (sector 10151), then sends
/// a 9-byte 0x004E STARBASE_REQUEST with Action=9 and asserts the
/// server emits a bare 0-byte 0x0096 JOB_ACCEPT_REPLY back.
///
/// <para>
/// Case 9 (<c>server/src/PlayerConnection.cpp:10014-10018</c>) is the
/// simplest possible direct-reply arm in the whole
/// <c>HandleStarbaseRequest</c> switch — a literal one-liner:
/// <code>
///   case 9: // Accept job?
///       LogMessage("Accepting Job 9\n");
///       SendOpcode(ENB_OPCODE_0096_JOB_ACCEPT_REPLY);
///       //g_ServerMgr-&gt;m_Missions.givePlayerMission(this, 1);
///       break;
/// </code>
/// <c>SendOpcode(opcode)</c> uses the default-arg overload at
/// <c>PlayerClass.h:971</c> — <c>SendOpcode(short opcode,
/// unsigned char *data = nullptr, long length = 0, bool issue = false)</c>
/// — so the emitted frame carries a 0-byte payload. No state
/// mutation, no preconditions, no AwardJob, no SendVaMessageC.
/// </para>
///
/// <para>
/// Contrast with case 8 (<c>PlayerConnection.cpp:10001-10012</c>)
/// which ALSO emits 0x0096 but with a <c>sizeof(long)</c> payload:
/// <code>
///   long job_id = pkt-&gt;StarbaseID;
///   SendOpcode(ENB_OPCODE_0096_JOB_ACCEPT_REPLY, (u8*)&amp;job_id, sizeof(job_id));
/// </code>
/// On Linux x86_64 <c>sizeof(long)</c> == 8 — emits an 8B payload
/// containing the int32 StarbaseID sign-extended to int64. On the
/// retail Win32 server, <c>sizeof(long)</c> == 4 emitted a 4B payload.
/// That is a real preservation-fidelity bug (the kind of thing
/// CLAUDE.md welcomes as a "tightening toward retail fidelity") to
/// fix in a future wave; here we steer around it by exercising the
/// simpler case 9 arm whose <c>SendOpcode</c> bare-overload emits
/// the same payload (0 bytes) on both platforms.
/// </para>
///
/// <para>
/// Wire layout of the inbound 0x004E (mirror of
/// <c>common/include/net7/PacketStructures.h:812-817</c>):
/// <code>
///   [0..4) int32 PlayerID    — case 9 ignores
///   [4..8) int32 StarbaseID  — case 9 ignores
///   [8..9)   char Action     — 9
/// </code>
/// 9 bytes total, <c>ATTRIB_PACKED</c>. Identical to Waves 55 and 56's
/// payload shape (only the Action byte differs).
/// </para>
///
/// <para>
/// Regression classes this catches.
/// </para>
/// <list type="bullet">
///   <item>
///     <b>HandleStarbaseRequest case-9 branch deletion at
///     <c>PlayerConnection.cpp:10014-10018</c>.</b> If the arm
///     vanishes (or short-circuits before <c>SendOpcode</c>), the
///     reply never arrives — drain times out.
///   </item>
///   <item>
///     <b>SendOpcode opcode-id flip at the case-9 emit site.</b> A
///     typo from <c>0x0096</c> to a neighbouring define (e.g.
///     <c>0x0093</c> JOB_LIST or <c>0x0094</c> JOB_DESCRIPTION) still
///     emits a frame but under the wrong opcode — drain times out.
///   </item>
///   <item>
///     <b>SendOpcode default-arg removal at <c>PlayerClass.h:971</c>.</b>
///     If the <c>data = nullptr, length = 0</c> defaults are removed,
///     the bare <c>SendOpcode(0x0096)</c> call no longer compiles —
///     server build breaks. If the defaults change semantically
///     (e.g. length is computed from a non-null data ptr), the
///     emitted payload length would no longer be 0 — the byte-exact
///     length assertion catches.
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at
///     <c>PlayerConnection.cpp:127</c>.</b> Would corrupt every inner
///     opcode in the 0x2016 PACKET_SEQUENCE parser; 0x0096 would not
///     appear under its correct label.
///   </item>
///   <item>
///     <b>Proxy SendClientPacketSequence inner-opcode guard tightening
///     at <c>proxy/UDPProxyToClient_linux.cpp:568</c>.</b> Currently
///     passes 0x0096 (less than 0x0FFF). A tighter upper bound would
///     silently drop it.
///   </item>
///   <item>
///     <b>STARBASE_REQUEST dispatch-table entry deletion at
///     <c>PlayerConnection.cpp:487-489</c>.</b> If the case
///     <c>ENB_OPCODE_004E_STARBASE_REQUEST</c> in the main dispatch
///     table is removed or renamed, the server silently swallows the
///     request.
///   </item>
///   <item>
///     <b>StarbaseRequest packed-struct layout regression at
///     <c>PacketStructures.h:812-817</c>.</b> 9B canonical with
///     <c>ATTRIB_PACKED</c>; an unpacked 12B revert would mis-read
///     the Action byte and miss case 9.
///   </item>
///   <item>
///     <b>m_TradeWindow side-effect regression at
///     <c>PlayerConnection.cpp:9875</c>.</b> The handler unconditionally
///     resets <c>m_TradeWindow = false</c> before the switch; a refactor
///     that gated this reset on a sub-case could leak stale trade-window
///     state into the case-9 arm — survival via the bare emit doesn't
///     pin this but the test running cleanly after Wave 55/56's
///     in-place trade-window invariant confirms.
///   </item>
/// </list>
///
/// <para>
/// CLAUDE.md server-integrity. Wave 57 sends an input shape the real
/// retail client emitted (STARBASE_REQUEST with Action=9 is one of
/// the job-terminal UI commit actions; the legacy comment "Accept
/// job?" / commented-out <c>givePlayerMission(this, 1)</c> line at
/// <c>PlayerConnection.cpp:10017</c> is preserved retail server code)
/// and asserts a server-originated reply (0x0096 JOB_ACCEPT_REPLY)
/// the real retail server has always produced on that input. No
/// server change, no widened input acceptance, no loosened gating,
/// no debug-only opcode, no security-posture relaxation.
/// </para>
///
/// <para>
/// Cleanup. Case 9 is a pure bare emit — no player-state mutation,
/// no <c>DropPlayerFrom*</c>, no <c>m_Gating = true</c> like case-1
/// does, no commit-to-DB like case-8's AwardJob path. The player
/// remains an active, in-station avatar after the reply arrives.
/// Cleanup is the standard 0x00B9 LOGOFF_REQUEST → 0x00BA
/// LOGOFF_CONFIRMATION round-trip + GlobalDeleteCharacter on the
/// global TCP, identical to Waves 55 and 56.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~22s on a cold stack; STARBASE_REQUEST +
/// reply round-trip is sub-second; LOGOFF round-trip is sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorStarbaseAcceptJob9Tests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorStarbaseAcceptJob9Tests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task StarbaseAcceptJobAction9_ReceivesBareJobAcceptReply()
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
            firstName: "Job9", shipName: "Job9Ship", cts.Token);

        try
        {
            // Canonical 9B packed StarbaseRequest payload. PlayerID
            // and StarbaseID are ignored on the case=9 arm.
            byte[] payload = new byte[9];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), 0);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), 0);
            payload[8] = 9;

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.StarbaseRequest.Value, payload),
                cts.Token);

            // Drain up to 400 frames waiting for the 0x0096 reply.
            const int maxFrames = 400;
            int seen = 0;
            Packet? reply = null;
            while (seen++ < maxFrames)
            {
                var frame = await session.Sector.ReceiveAsync(cts.Token);
                Assert.NotNull(frame);
                if (frame!.Header.Opcode == OpcodeId.Known.JobAcceptReply.Value)
                {
                    reply = frame;
                    break;
                }
            }

            Assert.NotNull(reply);
            Assert.Equal(0, reply!.Payload.Length);
        }
        finally
        {
            try
            {
                using var logoffCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                byte[] logoffPayload = new byte[8];
                await session.Sector.SendAsync(
                    Packet.ForOpcode(OpcodeId.Known.LogoffRequest.Value, logoffPayload),
                    logoffCts.Token);
                await SectorHandshake.DrainUntilOpcode(
                    session.Sector, OpcodeId.Known.LogoffConfirmation.Value, logoffCts.Token);
            }
            catch { /* best-effort logoff */ }

            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }
}
