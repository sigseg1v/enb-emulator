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
/// Wave 67 preservation-fidelity tightening: case-8 of HandleStarbaseRequest
/// (<c>server/src/PlayerConnection.cpp:10006-10017</c>) used to emit
/// 0x0096 JOB_ACCEPT_REPLY with <c>sizeof(long)</c> bytes — 8 on
/// LP64 Linux x86_64, 4 on Win32. The retail Win32 server emitted 4
/// bytes (Win32 LP32 long is 4-byte). Wave 67 tightens the Linux
/// server to emit 4 bytes via an <c>int32_t job_id</c> local in the
/// case-8 arm (was: <c>long job_id</c>) and pins the byte-exact
/// 4-byte echo via this test.
///
/// <para>
/// Why this is "tightening toward fidelity" not "loosening to satisfy
/// a tooling consumer" per CLAUDE.md server-integrity rule: the
/// emitted wire is what the retail client expects to read. Emitting
/// 8 bytes on Linux meant a 4-byte preservation drift away from the
/// retail format (and risked the retail client mis-framing a
/// subsequent inner opcode). The fix is purely a Linux/Win32 ABI
/// alignment — no behaviour change for any input the real server
/// accepted.
/// </para>
///
/// <para>
/// Stimulus wire layout — 9 bytes
/// (<c>common/include/net7/PacketStructures.h:812-817</c>,
/// <c>ATTRIB_PACKED</c>):
/// <code>
///   [0..4)   int32 LE PlayerID    = 0           (ignored)
///   [4..8)   int32 LE StarbaseID  = 0x12345678  (job_id echoed back)
///   [8..9)   byte     Action      = 8           (Accept Job)
/// </code>
/// </para>
///
/// <para>
/// Server dispatch. <c>PlayerConnection.cpp:487</c> routes 0x004E to
/// <c>HandleStarbaseRequest</c>. The case-8 arm at
/// <c>PlayerConnection.cpp:10006-10017</c> is now:
/// <code>
///   int32_t job_id = pkt-&gt;StarbaseID;
///   SendOpcode(ENB_OPCODE_0096_JOB_ACCEPT_REPLY, (u8*)&amp;job_id, sizeof(job_id));
///   if (sm &amp;&amp; !sm-&gt;AwardJob(this, job_id))
///       SendVaMessageC(17, "Job unavailable.");
/// </code>
/// <c>SectorManager::AwardJob</c>
/// (<c>server/src/SectorManager.cpp:1581+</c>) returns false for any
/// job_id not present in <c>m_JobList</c>; the sentinel 0x12345678 is
/// provably absent (real IDs are monotonic from 0 via
/// <c>m_JobListID++</c>), so the awarder fails and the trailing
/// <c>SendVaMessageC(17, "Job unavailable.")</c> fires emitting a
/// secondary 0x001D MESSAGE_STRING. We don't assert on the secondary
/// frame here — that's already covered by the existing
/// MESSAGE_STRING coverage from prior waves; the load-bearing
/// assertion is the byte-exact 4-byte 0x0096 shape.
/// </para>
///
/// <para>
/// Handshake — uses the Wave 65/66 two-stage pattern into Net-7 SOL
/// (sector 10711) because case-8 routes through the JT-equipped
/// starbase code path (the m_TradeWindow reset at PlayerConnection.cpp
/// runs unconditionally; the case-8 arm itself doesn't gate on JT
/// status, but exercising it inside the JT venue is the natural
/// extension of the Waves 65+66 dispatcher-coverage pattern and
/// keeps state co-located).
/// </para>
///
/// <para>
/// Regression classes this catches.
/// </para>
/// <list type="bullet">
///   <item>
///     <b>sizeof(long) revert at <c>PlayerConnection.cpp:10009</c>.</b>
///     The whole point of Wave 67 — reverting <c>int32_t job_id</c>
///     back to <c>long job_id</c> would inflate <c>sizeof(job_id)</c>
///     to 8 on Linux x86_64, ballooning the 0x0096 payload to 8 bytes
///     and shifting the high 4 bytes off the wire as garbage. The
///     byte-exact 4B length assertion catches.
///   </item>
///   <item>
///     <b>SendOpcode case-8 emit removal at
///     <c>PlayerConnection.cpp:10010</c>.</b> Drain times out — case-9
///     (already covered by Wave 57) is a separate code path.
///   </item>
///   <item>
///     <b>Echo regression — if <c>(u8*)&amp;job_id</c> is changed to a
///     different source (e.g. a hardcoded literal or a stale buffer
///     pointer)</b>, the byte-equal sentinel assertion catches.
///   </item>
///   <item>
///     <b><c>StarbaseRequest</c> packed-struct layout regression at
///     <c>PacketStructures.h:812-817</c>.</b> 9B canonical with
///     <c>ATTRIB_PACKED</c>; an unpacked 12B revert mis-reads Action
///     from offset 16 and lands in a different case.
///   </item>
///   <item>
///     <b>StarbaseID-field-width regression at
///     <c>PacketStructures.h</c>.</b> Widening from int32 to int64
///     shifts Action past payload end, server reads garbage Action.
///   </item>
///   <item>
///     <b>STARBASE_REQUEST dispatch-table entry deletion at
///     <c>PlayerConnection.cpp:487-489</c>.</b> Server silently
///     swallows the request, drain times out.
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at
///     <c>PlayerConnection.cpp:127</c>.</b> Corrupts every inner
///     opcode in 0x2016 PACKET_SEQUENCE.
///   </item>
///   <item>
///     <b>Proxy <c>SendClientPacketSequence</c> guard at
///     <c>UDPProxyToClient_linux.cpp:568</c>.</b> Currently passes
///     0x0096 (&lt; 0x0FFF); tightening drops the reply.
///   </item>
///   <item>
///     <b>m_TradeWindow side-effect regression at
///     <c>PlayerConnection.cpp:9875</c>.</b> Handler unconditionally
///     resets <c>m_TradeWindow=false</c> before the switch; a
///     refactor gating this on a sub-case could leak stale trade-
///     window state into case 8.
///   </item>
///   <item>
///     <b>AwardJob signature drift at <c>SectorManager.h:173</c> /
///     <c>SectorManager.cpp:1581</c>.</b> If the parameter type
///     changes from <c>long</c> to a narrower type and case-8's
///     <c>int32_t job_id</c> assignment is left intact, no
///     observable wire change here — but the AwardJob return value
///     could mis-evaluate against the new type and stop firing the
///     unavailable-message fallback. Out of scope for the byte-exact
///     0x0096 assertion (would need an additional 0x001D drain).
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). The case-8 sizeof(long) →
/// sizeof(int32_t) change is a "tightening" — bringing our Linux
/// server into byte-exact agreement with the retail Win32 wire format
/// the real client expects. No widened input acceptance, no relaxed
/// posture, no fabricated replies. The test validates the tightening
/// landed and stays landed.
/// </para>
///
/// <para>
/// Budget: 90s. Two-stage handshake ~22s × 2; stimulus + reply sub-
/// second; cleanup sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorStarbaseAcceptJob8Tests
{
    private const int ExpectedPayloadSize = 4;
    private const int SentinelJobId = 0x12345678;

    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorStarbaseAcceptJob8Tests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task StarbaseAcceptJobAction8_OnSentinelJobId_ReceivesByteExact4ByteJobAcceptReply()
    {
        var account = TestAccounts.New(_server);
        const int slot = 0;
        const int startSector = 10151;
        const int jtSector = 10711;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using (var firstSession = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, startSector,
            firstName: "Acceptr", shipName: "AcceptrShip", cts.Token))
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
            //   [4..8)   int32 LE  StarbaseID  = 0x12345678  (job_id echo)
            //   [8..9)   byte      Action      = 8
            byte[] payload = new byte[9];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), 0);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), SentinelJobId);
            payload[8] = 8;

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

                if (reply.Header.Opcode != OpcodeId.Known.JobAcceptReply.Value)
                    continue;

                // Byte-exact 4-byte payload — the case-8 tightening.
                Assert.Equal(ExpectedPayloadSize, reply.Payload.Length);
                int echoedJobId = BinaryPrimitives.ReadInt32LittleEndian(reply.Payload.Span[..4]);
                Assert.Equal(SentinelJobId, echoedJobId);
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x004E STARBASE_REQUEST " +
                $"(Action=8 Accept-Job, job_id=0x{SentinelJobId:X8}) in sector 10711 " +
                $"(Net-7 SOL) without seeing 0x0096 JOB_ACCEPT_REPLY. Observed " +
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
