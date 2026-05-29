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
/// Wave 66 direct-reply +1 ratchet: client sends a 9-byte 0x004E
/// STARBASE_REQUEST with <c>Action=7</c> (the Job-Description sub-action —
/// click on a row in the Job Terminal list to pull up its description)
/// after landing in Net-7 SOL (sector 10711). The <c>StarbaseID</c> field
/// of the payload is repurposed as the <c>job_id</c> selector when
/// Action=7. We pass <c>0x7FFFFFFF</c> (INT32_MAX) as a sentinel job-id
/// that is provably absent from <c>m_JobList</c> (real job IDs are
/// assigned monotonically from 0 via <c>SectorManager::RefreshJobs</c>'s
/// <c>m_JobListID++</c> increment at <c>SectorManager.cpp:1476-1491</c>;
/// no test run produces enough RefreshJobs cycles to reach INT32_MAX).
///
/// <para>
/// Stimulus wire layout — 9 bytes
/// (<c>common/include/net7/PacketStructures.h:812-817</c>,
/// <c>ATTRIB_PACKED</c>):
/// <code>
///   [0..4)   int32 LE PlayerID    = 0           (ignored)
///   [4..8)   int32 LE StarbaseID  = 0x7FFFFFFF  (job_id selector)
///   [8..9)   byte     Action      = 7           (Job-Description)
/// </code>
/// </para>
///
/// <para>
/// Server dispatch. <c>PlayerConnection.cpp:487</c> routes 0x004E to
/// <c>HandleStarbaseRequest</c>. The case-7 arm at
/// <c>PlayerConnection.cpp:9952-10004</c> calls
/// <c>SectorManager::GetJobDescription(m_ScratchBuffer, pkt-&gt;StarbaseID)</c>
/// and emits 0x0094 when <c>index &gt; 0</c>.
/// </para>
///
/// <para>
/// <c>SectorManager::GetJobDescription</c>
/// (<c>server/src/SectorManager.cpp:1533-1579</c>):
/// </para>
/// <list type="number">
///   <item>
///     Early-return 0 when <c>m_JobListCount == 0</c> (non-JT sector
///     case; doesn't apply here — 10711 is JT with m_JobListCount
///     in 5..9).
///   </item>
///   <item>
///     <c>AddData(ptr, job_id, index)</c> — writes 4 bytes LE of the
///     supplied job_id (AddData&lt;long&gt; specialisation at
///     <c>PacketMethods.h:37-42</c> forces int32_t). <c>index</c>
///     advances to 4.
///   </item>
///   <item>
///     Loop scans <c>m_JobList[0..m_JobListCount)</c> for a node whose
///     <c>jn-&gt;ID == job_id</c>. Real IDs are 0..N-1; the sentinel
///     0x7FFFFFFF never matches.
///   </item>
///   <item>
///     The <c>if (index == 0)</c> fallback block is dead code given
///     the prior <c>AddData(ptr, job_id, index)</c> already advanced
///     index past zero.
///   </item>
///   <item>
///     <c>return index</c> — returns 4.
///   </item>
/// </list>
///
/// <para>
/// The case-7 arm emits 0x0094 with exactly 4 bytes of payload —
/// the echoed job_id as int32 LE. Test asserts (a) reply opcode ==
/// 0x0094 JOB_DESCRIPTION, (b) payload.Length == 4, (c) the int32 LE
/// at [0..4] equals the sentinel 0x7FFFFFFF.
/// </para>
///
/// <para>
/// Handshake — identical two-stage <c>EstablishAsync(10151)</c> +
/// <c>ReestablishAsync(10711)</c> Wave 65 introduced. Cleanup runs
/// the standard 0x00B9 → 0x00BA round-trip + GlobalDeleteCharacter.
/// </para>
///
/// <para>
/// Regression classes this catches.
/// </para>
/// <list type="bullet">
///   <item>
///     <b>0x0094 SendOpcode removal at <c>PlayerConnection.cpp:9968</c>.</b>
///     Drain times out — no other code path emits 0x0094 on a fresh
///     starbase character.
///   </item>
///   <item>
///     <b><c>GetJobDescription</c> early-return regression at
///     <c>SectorManager.cpp:1539</c>.</b> A broadened early-return
///     (e.g. <c>m_JobListCount == 0 || pkt-&gt;StarbaseID == 0</c>) on
///     this Action=7 path would surface as a drain timeout — but we
///     send INT32_MAX, not zero, so this specific test catches the
///     m_JobListCount-side widening only. Documented for completeness.
///   </item>
///   <item>
///     <b><c>AddData&lt;long&gt;</c> specialisation revert at
///     <c>PacketMethods.h:37-42</c>.</b> If the specialisation is
///     removed, the generic template at lines 23-28 emits
///     <c>sizeof(long)</c> bytes — 8 on Linux x86_64. The job_id echo
///     would then be 8B not 4B; length assertion catches.
///   </item>
///   <item>
///     <b><c>GetJobDescription</c> echo regression at
///     <c>SectorManager.cpp:1541</c>.</b> If the leading
///     <c>AddData(ptr, job_id, index)</c> is moved into the inner-loop
///     match arm or replaced by writing a different value, the echo
///     no longer matches the sentinel.
///   </item>
///   <item>
///     <b><c>GetJobDescription</c> case-7 wrong-buffer-pointer
///     regression at <c>SectorManager.cpp:1536</c>.</b> If
///     <c>u8 *ptr = buffer</c> is replaced with a stale pointer, the
///     AddData writes land in scratch memory and the wire payload
///     contains uninitialised bytes — sentinel mismatch.
///   </item>
///   <item>
///     <b>m_JobListID monotonic-assignment regression at
///     <c>SectorManager.cpp:1476-1491</c>.</b> If RefreshJobs's
///     m_JobListID++ is removed or the start value is changed to a
///     non-zero base, real job IDs could collide with the sentinel
///     0x7FFFFFFF (impossible in practice — the seed never runs
///     enough RefreshJobs cycles — but flagged for completeness).
///   </item>
///   <item>
///     <b>STARBASE_REQUEST dispatch-table entry deletion at
///     <c>PlayerConnection.cpp:487-489</c>.</b> Server silently
///     swallows the request, drain times out.
///   </item>
///   <item>
///     <b><c>StarbaseRequest</c> packed-struct layout regression at
///     <c>PacketStructures.h:812-817</c>.</b> 9B canonical with
///     <c>ATTRIB_PACKED</c>; an unpacked 12B revert reads Action from
///     offset 16 and lands in a different case (or no case at all).
///   </item>
///   <item>
///     <b>StarbaseID-field-width regression at
///     <c>PacketStructures.h:812-817</c>.</b> If StarbaseID is widened
///     from int32 to int64, the Action byte shifts to offset 12 (past
///     the 9-byte payload, server reads garbage).
///   </item>
///   <item>
///     <b><c>SendOpcode</c> header-width revert at
///     <c>PlayerConnection.cpp:127</c>.</b> Corrupts every inner
///     opcode in the 0x2016 PACKET_SEQUENCE parser; 0x0094 wouldn't
///     appear under its correct label.
///   </item>
///   <item>
///     <b>Proxy <c>SendClientPacketSequence</c> guard at
///     <c>UDPProxyToClient_linux.cpp:568</c>.</b> Currently passes
///     0x0094 (&lt; 0x0FFF); tightening drops the reply.
///   </item>
///   <item>
///     <b>JT-sector binding regression — sector 10711 thread fails to
///     bind at startup.</b> ReestablishAsync times out before the
///     test stimulus can be sent. Distinguishes from emit-path
///     regressions via the failure point.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). 0x004E with Action=7 is the
/// exact payload the retail Win32 client emits when the user clicks a
/// row in the Job Terminal UI to pull up its description. The retail
/// server's case-7 arm and the GetJobDescription function path are
/// preserved verbatim from the upstream snapshot. The 4-byte echo
/// shape for a non-matching job_id is what the retail server emits.
/// We are not making the server accept any new input shape, not
/// loosening any security posture, not fabricating any reply.
/// </para>
///
/// <para>
/// Budget: 90s. Two-stage handshake ~22s × 2; stimulus + reply sub-
/// second; cleanup sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorJobDescriptionTests
{
    private const int ExpectedPayloadSize = 4;
    private const int SentinelJobId = 0x7FFFFFFF;

    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorJobDescriptionTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task StarbaseJobTerminalAction7_InNet7SolJtSector_OnSentinelJobId_EchoesJobIdAsJobDescription()
    {
        var account = TestAccounts.New(_server);
        const int slot = 0;
        const int startSector = 10151;  // Terran Warrior start: Luna Station
        const int jtSector = 10711;     // Net-7 SOL: the only JT-terminal starbase in the seed

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using (var firstSession = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, startSector,
            firstName: "Jobdesc", shipName: "JobdescShip", cts.Token))
        {
            byte[] logoffPayload = new byte[8];
            await firstSession.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.LogoffRequest.Value, logoffPayload),
                cts.Token);
            await SectorHandshake.DrainUntilOpcode(
                firstSession.Sector, OpcodeId.Known.LogoffConfirmation.Value, cts.Token);
        }

        await using var session = await SectorHandshake.ReestablishAsync(
            _server, login.Ticket!, slot, jtSector, cts.Token);

        try
        {
            // StarbaseRequest wire layout (9 bytes):
            //   [0..4)   int32 LE  PlayerID    = 0
            //   [4..8)   int32 LE  StarbaseID  = 0x7FFFFFFF  (job_id selector)
            //   [8..9)   byte      Action      = 7
            byte[] payload = new byte[9];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), 0);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), SentinelJobId);
            payload[8] = 7;

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.StarbaseRequest.Value, payload),
                cts.Token);

            int framesSeen = 0;
            const int maxFrames = 400;
            var observed = new List<string>();
            while (framesSeen++ < maxFrames)
            {
                var reply = await session.Sector.ReceiveAsync(cts.Token);
                Assert.NotNull(reply);
                observed.Add($"0x{reply!.Header.Opcode:X4}/{reply.Payload.Length}");

                if (reply.Header.Opcode != OpcodeId.Known.JobDescription.Value)
                    continue;

                // Wire layout for non-matching job_id: int32 LE echo of
                // the supplied job_id, exactly 4 bytes.
                Assert.Equal(ExpectedPayloadSize, reply.Payload.Length);
                int echoedJobId = BinaryPrimitives.ReadInt32LittleEndian(reply.Payload.Span[..4]);
                Assert.Equal(SentinelJobId, echoedJobId);
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x004E STARBASE_REQUEST " +
                $"(Action=7 Job-Description, job_id=0x{SentinelJobId:X8}) in sector " +
                $"10711 (Net-7 SOL) without seeing 0x0094 JOB_DESCRIPTION. Observed " +
                $"[{observed.Count}]: {string.Join(" | ", observed)}");
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
