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
/// Wave 59 direct-reply round-trip (+2 ratchet, 0x00BC and 0x00BD): client
/// establishes a station-sector handshake at Luna Station (sector 10151),
/// then sends a 12-byte 0x00BC CTA_REQUEST with Action=0 (forces the
/// default arm in <c>PlayerManager::GroupAction</c>) and asserts the
/// server emits a 9-byte 0x00BD CTA_RESPONSE back with our
/// <c>SourceID</c> echoed, <c>Action</c> echoed, and the
/// <c>Success</c> byte set to 0x01.
///
/// <para>
/// 0x00BC dispatch chain (<c>server/src/PlayerConnection.cpp:7723-7744</c>):
/// <code>
///   void Player::HandleCTARequest(unsigned char *data)
///   {
///       CTARequest * myCTARequest = (CTARequest *) data;
///       g_ServerMgr-&gt;m_PlayerMgr.GroupAction(
///           myCTARequest-&gt;SourceID, myCTARequest-&gt;TargetID,
///           myCTARequest-&gt;Action);
///       unsigned char CTAResponse[] = {
///           0x00, 0x00, 0x00, 0x00,  // GameID
///           0x0F, 0x00, 0x00, 0x00,  // RequestType
///           0x01                     // Success
///       };
///       *((int32_t*) &amp;CTAResponse[0]) = myCTARequest-&gt;SourceID;
///       *((int32_t*) &amp;CTAResponse[4]) = myCTARequest-&gt;Action;
///       SendOpcode(ENB_OPCODE_00BD_CTA_RESPONSE, ...,
///                  sizeof(CTAResponse));
///   }
/// </code>
/// <c>PlayerManager::GroupAction</c> (defined out-of-line per the
/// binary; not present as text in <c>PlayerManager.cpp</c>) dispatches on
/// <c>(Action - 4)</c> with cases 0..8 (so Action values 4..12 fan out to
/// <c>SetFormation</c>, <c>FormUp</c>, <c>RequestTargetMyTarget</c>, etc.);
/// anything outside that range jumps straight to a <c>LogMessage</c> default
/// arm and returns without dereferencing <c>SourceID</c> or <c>TargetID</c>.
/// Wave 59 uses <b>Action=0</b> to force the default arm — keeps
/// <c>GroupAction</c> as a true no-op on a fresh starbase character with
/// no group state, no formation, and no target.
/// </para>
///
/// <para>
/// <b>Server tightening landed in this wave.</b> The pre-fix code was:
/// <code>
///   *((long*) &amp;CTAResponse[0]) = myCTARequest-&gt;SourceID;
///   *((long*) &amp;CTAResponse[4]) = myCTARequest-&gt;Action;
/// </code>
/// On Win32 retail <c>sizeof(long)==4</c> so each write covered exactly
/// 4 bytes of the 9-byte <c>CTAResponse</c> stack array — bytes [0..4]
/// for SourceID, [4..8] for Action, and the literal 0x01 at byte 8
/// (Success) was preserved. On Linux x86_64 <c>sizeof(long)==8</c>, so:
/// <list type="bullet">
///   <item>The first write covered [0..8] — fine, no overflow.</item>
///   <item>
///     The second write covered [4..12] — overflowed the 9-byte buffer
///     by 3 bytes AND clobbered the Success byte at [8] with the
///     sign-extended high byte of <c>Action</c> (0x00 for any
///     non-negative <c>Action</c>, 0xFF for negative <c>Action</c>).
///   </item>
/// </list>
/// Wave 59 narrows both writes to <c>int32_t*</c>, restoring retail's
/// 4-byte semantics and eliminating the 3-byte stack overflow. Same
/// preservation-grade tightening class as Phase K Waves 7/11/12 — the
/// commit-message escape hatch in CLAUDE.md explicitly welcomes "rejecting
/// an input the real server rejected but we currently accept", and here
/// the Linux divergence corrupted a server-emitted reply field
/// (<c>Success=0x00</c> instead of 0x01) that retail always set to 0x01.
/// </para>
///
/// <para>
/// Wire layout of the inbound 0x00BC
/// (<c>common/include/net7/PacketStructures.h:974-979</c>):
/// <code>
///   [0..4)  int32 SourceID  — echoed back as CTAResponse bytes [0..4)
///   [4..8)  int32 TargetID  — passed to GroupAction; default arm ignores
///   [8..12) int32 Action    — echoed back as CTAResponse bytes [4..8)
/// </code>
/// 12 bytes total, <c>ATTRIB_PACKED</c>.
/// </para>
///
/// <para>
/// Wire layout of the reply 0x00BD (no struct in
/// <c>PacketStructures.h</c>; emitted as the inline <c>CTAResponse[]</c>
/// stack literal):
/// <code>
///   [0..4) int32 GameID    — set from SourceID via the int32_t* write
///   [4..8) int32 RequestType — set from Action via the int32_t* write
///   [8..9)   byte Success  — literal 0x01 preserved after the tightening
/// </code>
/// 9 bytes total.
/// </para>
///
/// <para>
/// Regression classes this catches.
/// </para>
/// <list type="bullet">
///   <item>
///     <b>The Phase K <c>long</c>→<c>int32_t</c> tightening regression at
///     <c>PlayerConnection.cpp:7740-7741</c>.</b> A revert to <c>long*</c>
///     writes would reintroduce the 3-byte stack overflow AND clobber
///     <c>Success</c> to 0x00 (for Action=0) — the byte-exact
///     <c>Success==0x01</c> assertion catches this directly.
///   </item>
///   <item>
///     <b>HandleCTARequest case-0x00BC branch deletion at
///     <c>PlayerConnection.cpp:595-596</c>.</b> If the dispatch case
///     vanishes, the server silently drops the request — drain times out.
///   </item>
///   <item>
///     <b>SendOpcode opcode-id flip at the emit site.</b> A typo from
///     <c>ENB_OPCODE_00BD_CTA_RESPONSE</c> to a neighbouring define
///     emits under the wrong label — drain times out at the 0x00BD
///     filter.
///   </item>
///   <item>
///     <b>The 9-byte <c>sizeof(CTAResponse)</c> emit-length pin.</b>
///     The <c>SendOpcode(..., sizeof(CTAResponse))</c> call sends exactly
///     9 bytes; the length-9 assertion catches any refactor that
///     widens the array or shifts the emit boundary.
///   </item>
///   <item>
///     <b>CTARequest struct layout regression at
///     <c>PacketStructures.h:974-979</c>.</b> 12B canonical
///     (3× int32_t with <c>ATTRIB_PACKED</c>). A long-revert on any
///     field would shift the others, mis-reading <c>SourceID</c> or
///     <c>Action</c> — caught by the byte-exact echo asserts.
///   </item>
///   <item>
///     <b>PlayerManager::GroupAction dispatch-table corruption.</b>
///     The (Action-4) switch's default arm is a logging no-op; any
///     refactor that adds a side effect (DB write, fan-out, crash on
///     null target) under the default arm would surface as either a
///     different reply opcode count or a session crash.
///   </item>
///   <item>
///     <b>GroupAction null-pointer regression on null source/target
///     player lookups.</b> Action=0 forces the default arm BEFORE any
///     player lookup; a refactor that moved the lookup to the function
///     entry would crash on the bogus SourceID/TargetID values we send.
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at
///     <c>PlayerConnection.cpp:127</c>.</b> Would corrupt every inner
///     opcode in the 0x2016 PACKET_SEQUENCE parser; 0x00BD would not
///     appear under its correct label.
///   </item>
///   <item>
///     <b>Proxy SendClientPacketSequence inner-opcode guard tightening
///     at <c>proxy/UDPProxyToClient_linux.cpp:568</c>.</b> Currently
///     passes 0x00BD (less than 0x0FFF). A tighter upper bound would
///     silently drop the response.
///   </item>
///   <item>
///     <b>Proxy default-case ForwardClientOpcode dropping 0x00BC.</b>
///     0x00BC is not explicitly listed in
///     <c>proxy/ClientToServer_linux_stubs.cpp</c> so it relies on the
///     bottom-of-switch fallthrough. A tightening that white-lists
///     forwarded opcodes would drop 0x00BC and the server never sees it.
///   </item>
/// </list>
///
/// <para>
/// CLAUDE.md server-integrity. Wave 59 sends a packed-struct input the
/// real retail client emitted (0x00BC CTA_REQUEST is what the retail UI
/// emits when the user clicks a group action button — "Call To Arms"
/// per the inline comment at <c>PlayerConnection.cpp:7722</c>) and
/// asserts a server-originated reply (0x00BD CTA_RESPONSE) the real
/// retail server has always produced on that input. The
/// <c>long</c>→<c>int32_t</c> tightening at lines 7740-7741 is a
/// preservation-grade fidelity fix: Win32 retail's
/// <c>sizeof(long)==4</c> made the original writes correct on the
/// platform retail ran on, and Wave 59 restores that 4-byte semantic on
/// Linux x86_64 (eliminating both the divergent Success-byte clobber AND
/// a 3-byte stack overflow). No widened input acceptance, no loosened
/// gating, no debug-only opcode, no security-posture relaxation —
/// purely tightening toward retail behaviour.
/// </para>
///
/// <para>
/// Seam-discovery. Wave 59 introduces a NEW Phase K seam class:
/// <b>"server reply field clobbered by sizeof(long) stack overflow"</b>.
/// Prior Phase K sizeof(long) fixes (Waves 7/11/12) targeted client→server
/// READ paths (struct over-reads past wire-payload end). Wave 59 is the
/// first to target a server→client WRITE path (response-buffer overflow
/// corrupting an adjacent field within the same emit). Future audits of
/// <c>*((long*) &amp;buf[N])</c>-style writes anywhere in
/// <c>server/src/</c> should treat this pattern as the third leg of the
/// Phase K sizeof(long) bug taxonomy.
/// </para>
///
/// <para>
/// Cleanup. CTA_REQUEST mutates no player state on the default
/// (Action=0) arm — <c>GroupAction</c> returns without touching the
/// player object; no DropPlayerFromSector, no LaunchIntoSpace, no DB
/// commit. Cleanup is the standard 0x00B9 LOGOFF_REQUEST → 0x00BA
/// LOGOFF_CONFIRMATION round-trip + GlobalDeleteCharacter on the global
/// TCP, identical to Waves 55-58.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~22s on a cold stack; CTA_REQUEST + reply
/// round-trip is sub-second; LOGOFF round-trip is sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorCtaRequestTests
{
    private const int ExpectedCtaResponseSize = 9;
    private const int CtaSourceId = 0x12345678;
    private const int CtaTargetId = 0;
    private const int CtaActionDefaultArm = 0;
    private const byte ExpectedSuccessByte = 0x01;

    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorCtaRequestTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task CtaRequest_OnDefaultArmAction_ReceivesCtaResponseWithSuccessByte()
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
            firstName: "Cta59", shipName: "Cta59Ship", cts.Token);

        try
        {
            // Canonical 12B packed CTARequest payload. Action=0 forces
            // the default arm in PlayerManager::GroupAction (the
            // (Action-4) switch has cases 0..8 covering Action=4..12; any
            // other value lands on the logging-no-op default). SourceID
            // is a sentinel we assert is echoed unchanged through the
            // server's int32_t* write — the post-tightening byte path.
            byte[] payload = new byte[12];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), CtaSourceId);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), CtaTargetId);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), CtaActionDefaultArm);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.CtaRequest.Value, payload),
                cts.Token);

            // Drain up to 400 frames waiting for the 0x00BD reply.
            const int maxFrames = 400;
            int seen = 0;
            Packet? reply = null;
            while (seen++ < maxFrames)
            {
                var frame = await session.Sector.ReceiveAsync(cts.Token);
                Assert.NotNull(frame);
                if (frame!.Header.Opcode == OpcodeId.Known.CtaResponse.Value)
                {
                    reply = frame;
                    break;
                }
            }

            Assert.NotNull(reply);
            Assert.Equal(ExpectedCtaResponseSize, reply!.Payload.Length);

            var span = reply.Payload.Span;

            // [0..4) GameID — echoed from SourceID via the int32_t* write.
            int replySourceId = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(0, 4));
            Assert.Equal(CtaSourceId, replySourceId);

            // [4..8) RequestType — echoed from Action via the int32_t* write.
            int replyAction = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(4, 4));
            Assert.Equal(CtaActionDefaultArm, replyAction);

            // [8..9) Success — pre-fix this was clobbered to 0x00 by the
            // long* write at offset 4 overflowing into byte 8. Post-fix
            // the int32_t* write stays within [4..8] and the literal 0x01
            // at byte 8 is preserved.
            Assert.Equal(ExpectedSuccessByte, span[8]);
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
