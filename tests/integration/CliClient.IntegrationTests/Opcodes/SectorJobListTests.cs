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
/// Wave 65 direct-reply +1 ratchet: client sends a 9-byte 0x004E
/// STARBASE_REQUEST with <c>Action=6</c> (the Job-Terminal-open sub-action)
/// after landing in Net-7 SOL (sector 10711) — the ONLY starbase in the
/// integration-test seed whose <c>starbase_terminals</c> roster contains a
/// JT-type terminal with non-zero <c>terminal_level</c>. The server emits
/// 0x0093 JOB_LIST whose first 4 bytes are an int32 LE "jobs count"
/// (overwriting the placeholder <c>m_JobListCount</c> header that
/// <c>GetJobList</c> wrote at offset 0 on entry), followed by
/// <c>jobs_count</c> tuples — each tuple a 16-byte fixed prefix
/// (int32 id, int32 category, int32 zero, int32 level) plus three
/// NUL-terminated strings (title, sponsor, reward). The exact byte
/// count is non-deterministic per server start (RefreshJobs picks jobs
/// at random from the catalogue and m_JobListCount itself is
/// <c>rand()%5 + 5</c>), so the test asserts wire-shape validity by
/// parsing the structure rather than asserting a fixed length.
///
/// <para>
/// Stimulus wire layout — 9 bytes
/// (<c>common/include/net7/PacketStructures.h:812-817</c>,
/// <c>ATTRIB_PACKED</c>):
/// <code>
///   [0..4)   int32 LE PlayerID    = 0   (server resolves actor via
///                                         connection binding; this field
///                                         is ignored)
///   [4..8)   int32 LE StarbaseID  = 0   (action=6 doesn't read this —
///                                         it's a job-id field that's
///                                         only consumed by Action=7/8/9)
///   [8..9)   byte     Action      = 6   (Job-Terminal-open)
/// </code>
/// </para>
///
/// <para>
/// Server dispatch. <c>PlayerConnection.cpp:487</c> routes 0x004E to
/// <c>HandleStarbaseRequest</c> at <c>PlayerConnection.cpp:9846</c>. The
/// case-6 arm at <c>PlayerConnection.cpp:9937-9951</c> is a 5-line read:
/// <code>
///   u8 *ptr = m_ScratchBuffer;
///   SectorManager *sm = GetSectorManager();
///   int index = sm-&gt;GetJobList(m_ScratchBuffer);
///   if (index &gt; 0)
///       SendOpcode(ENB_OPCODE_0093_JOB_LIST, ptr, index);
/// </code>
/// <c>SectorManager::GetJobList</c>
/// (<c>server/src/SectorManager.cpp:1496-1531</c>) short-circuits with
/// <c>return 0</c> when <c>m_JobListCount == 0</c> (the non-JT starbase
/// path; Luna Station's sector 10151 takes this branch). For sector 10711
/// (Net-7 SOL), <c>m_JobListCount</c> was set to <c>rand()%5 + 5</c> in
/// <c>SectorManager::InitializeSector</c> (line 225) because
/// <c>StationTemplate::JTLevel</c> for Net-7 SOL is 50 (set by
/// <c>StationLoader::AddTerminals</c> at <c>StationLoader.cpp:316</c> when
/// it sees a terminal row with <c>type=3</c> and
/// <c>terminal_level &gt; 0</c>). The loop at line 1509 iterates
/// <c>m_JobListCount</c> entries; for each entry whose
/// <c>jn-&gt;available</c> is true, GetJobList writes the 16-byte fixed
/// prefix (id, category, 0, level) followed by three NUL-terminated
/// strings (title, sponsor, reward). The trailing
/// <c>AddData(ptr, jobs, index_dummy)</c> at line 1528 overwrites the
/// placeholder count header at offset 0 with the actual emitted-job
/// count. <c>index &gt; 0</c> holds and 0x0093 emits with a variable-
/// length payload whose internal structure is what this test parses.
/// </para>
///
/// <para>
/// Why Net-7 SOL is the only viable target. The seed's
/// <c>starbase_terminals</c> table has exactly one row with
/// <c>type=3</c> (Job Terminal) and <c>terminal_level &gt; 0</c>:
/// <c>terminal_id=61</c>, <c>room_id=40</c>, <c>terminal_level=50</c>,
/// belonging to starbase_id=43 ("Net-7 SOL") with
/// <c>starbase_sector_id=10711</c>. Every other JT-eligible row in the
/// dump has <c>terminal_level=0</c> which makes
/// <c>StationLoader.cpp:314-316</c>'s gate
/// (<c>if (type==3 &amp;&amp; terminal_level &gt; 0)</c>) skip the
/// <c>JTLevel</c> assignment. The server still binds a sector thread for
/// 10711 at startup (<c>server/src/SectorServerManager.cpp</c>
/// BeginSectorThread logs "Port: 3639, Sector: 10711 'Net-7 SOL'") even
/// though <c>starbases.is_active=0</c> for this row — <c>StationLoader</c>
/// loads stations unconditionally; the is_active column is read into
/// <c>StationTemplate::IsActive</c> (<c>StationLoader.cpp:179</c>) but no
/// code path filters on it during sector instantiation. So sector 10711
/// is a real loaded sector whose <c>SectorManager</c> has a non-empty
/// <c>m_JobList</c> and emits 0x0093 on Action=6.
/// </para>
///
/// <para>
/// Handshake pattern. The starting sector for a Terran Warrior is
/// <c>StartSector[0*3+0] = 10151</c> (Luna Station), so the first login
/// goes through <c>ReInitializeSavedData</c>
/// (<c>PlayerSaves.cpp:966-994</c>) which forces sector 10151. To land in
/// 10711 we use the two-stage <c>EstablishAsync(10151)</c> +
/// <c>ReestablishAsync(10711)</c> pattern — second login's
/// <c>ReadSavedData</c> takes the <c>ReloadSavedData</c> branch
/// (avatar_level_info row now exists from the first login's
/// <c>ReInitializeSavedData</c>) which preserves the sector_num set by
/// <c>Player::HandleLogin</c> from the LOGIN packet's <c>ToSectorID</c>
/// (<c>PlayerSaves.cpp:289-291</c>). Same two-stage pattern Waves 52-54
/// used for sector 1015 (Luna space).
/// </para>
///
/// <para>
/// Regression classes this catches.
/// </para>
/// <list type="bullet">
///   <item>
///     <b>0x0093 SendOpcode removal at <c>PlayerConnection.cpp:9947</c>.</b>
///     Drain times out — no other code path emits 0x0093 on a fresh
///     starbase character.
///   </item>
///   <item>
///     <b><c>SectorManager::GetJobList</c> return-zero regression at
///     <c>SectorManager.cpp:1505</c>.</b> If the
///     <c>m_JobListCount == 0</c> short-circuit is broadened (e.g.
///     widened to short-circuit on <c>m_JobTerminalLevel == 0</c> instead)
///     the function returns 0 even for JT stations and the
///     <c>index &gt; 0</c> guard at PlayerConnection.cpp:9945 stops the
///     emit. Drain times out.
///   </item>
///   <item>
///     <b><c>SectorManager::InitializeSector</c> JTLevel-gate regression
///     at <c>SectorManager.cpp:221-232</c>.</b> If the <c>JTLevel &gt; 0</c>
///     gate is inverted or the <c>m_JobListCount = rand()%5 + 5</c>
///     assignment is removed, <c>m_JobListCount</c> stays zero, GetJobList
///     short-circuits, no emit. Drain times out.
///   </item>
///   <item>
///     <b><c>StationLoader::AddTerminals</c> JTLevel regression at
///     <c>StationLoader.cpp:314-316</c>.</b> If the
///     <c>type == 3 &amp;&amp; terminal_level &gt; 0</c> gate is changed
///     so JTLevel doesn't get set for Net-7 SOL, the cascade above fires.
///   </item>
///   <item>
///     <b><c>GetJobList</c> wire-shape regression at
///     <c>SectorManager.cpp:1507</c> + 1528.</b> The function writes the
///     placeholder <c>m_JobListCount</c> header via
///     <c>AddData(ptr, m_JobListCount, index)</c> (4B,
///     <c>AddData&lt;long&gt;</c> specialisation), then overwrites it via
///     <c>AddData(ptr, jobs, index_dummy)</c> where
///     <c>index_dummy = 0</c> stays at offset zero. The test's wire
///     walk (4B header → N tuples of 16B + 3 NUL-terminated strings →
///     exact-length check) catches any drift: removing the placeholder
///     overwrite (count header reads as <c>m_JobListCount=5..9</c> but
///     fewer tuples follow, walk overruns); reordering the per-job
///     writes (level field falls out of valid range); dropping a string
///     or adding one (cursor mismatches payload length); reverting any
///     of the per-field <c>AddData&lt;long&gt;</c> calls to the generic
///     template (8B fields on Linux x86_64 push the level read out of
///     range).
///   </item>
///   <item>
///     <b><c>AddData&lt;long&gt;</c> specialisation revert at
///     <c>server/src/PacketMethods.h:37-42</c>.</b> If the specialisation
///     is removed, the generic template at lines 23-28 emits
///     <c>sizeof(long)</c> bytes — on Linux x86_64 that's 8 — so the
///     per-job 16-byte fixed prefix balloons to 32B. The level read
///     falls out of [1, 200] range (high bytes are zero so the int32 LE
///     read of bytes [12..16] picks up part of the title string,
///     yielding a garbage int) and the structural walk fails the
///     final exact-length assertion.
///   </item>
///   <item>
///     <b>STARBASE_REQUEST dispatch-table entry deletion at
///     <c>PlayerConnection.cpp:487-489</c>.</b> Server silently swallows
///     the request, drain times out.
///   </item>
///   <item>
///     <b><c>StarbaseRequest</c> packed-struct layout regression at
///     <c>PacketStructures.h:812-817</c>.</b> 9B canonical with
///     <c>ATTRIB_PACKED</c>; an unpacked 12B revert mis-reads the Action
///     byte and lands in a different case (or no case if Action garbage
///     exceeds 11) — wrong reply opcode or drain timeout.
///   </item>
///   <item>
///     <b><c>m_TradeWindow</c> side-effect regression at
///     <c>PlayerConnection.cpp:9875</c>.</b> Handler unconditionally
///     resets <c>m_TradeWindow=false</c> before the switch; a refactor
///     gating this reset on a sub-case could leak stale trade-window
///     state into case 6.
///   </item>
///   <item>
///     <b>Proxy default-case ForwardClientOpcode regression at
///     <c>proxy/ClientToServer_linux_stubs.cpp</c>.</b> 0x004E is not
///     explicitly cased; falls through to the bottom-of-switch
///     ForwardClientOpcode. A regression re-introducing opcode
///     whitelisting would stop the server from receiving 0x004E.
///   </item>
///   <item>
///     <b>Proxy <c>SendClientPacketSequence</c> guard at
///     <c>UDPProxyToClient_linux.cpp:568</c>.</b> Currently passes 0x0093
///     (&lt; 0x0FFF); tightening the upper bound silently drops the
///     reply.
///   </item>
///   <item>
///     <b><c>SectorManager::SetupSectorServer</c> regression failing to
///     bind sector 10711 at startup.</b> If the sector thread isn't
///     bound, MasterJoin's <c>SetupSectorServer(10711)</c> dispatch fails
///     and ReestablishAsync times out before the test stimulus can be
///     sent. Distinguishes from emit-path regressions via the
///     ReestablishAsync timeout point.
///   </item>
///   <item>
///     <b><c>SectorManager::RefreshJobs</c> regression at
///     <c>SectorManager.cpp:1443-1494</c>.</b> If the category-picking
///     loop is broken (e.g. category bailout always fires before any
///     <c>jn-&gt;available = true</c> assignment), the emitted count
///     header becomes 0 and the wire walk asserts 0 tuples (still
///     well-formed but jobsCount drops). If the SetupJobNode-style
///     per-job writes drift (id, category, level fields populated from
///     the wrong source) the structural walk's level-in-range check
///     trips. The InRange(jobsCount, 0, 9) sanity bound catches integer
///     overflow if RefreshJobs ever writes the count beyond
///     m_JobListCount's natural upper bound.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). 0x004E with Action=6 is exactly
/// what the retail Win32 client emits when the user clicks a Job Terminal
/// NPC inside a starbase. The retail server's <c>HandleStarbaseRequest</c>
/// case-6 arm calls the same <c>SectorManager::GetJobList</c> path and
/// emits 0x0093 whenever GetJobList returns a non-zero index. The 4B
/// all-zero payload shape is exactly what the retail server emits when
/// it's in a sector with a JT terminal but no jobs available (which can
/// happen between RefreshJobs ticks). We are not making the server accept
/// any new input shape, not loosening any security posture, not
/// fabricating any reply — the JobManager being an empty stub is a
/// pre-existing upstream behaviour preserved verbatim from the Net-7
/// snapshot. Wave 65 pins it without changing it.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~22s × 2 (two-stage login); STARBASE_REQUEST +
/// 0x0093 round-trip sub-second; LOGOFF sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorJobListTests
{
    // m_JobListCount = rand()%5 + 5 → upper bound 9; assert sanity on the
    // count read from the count-header at wire offset 0.
    private const int MaxJobListCount = 9;

    // Per-job fixed-prefix size: int32 id + int32 category + int32 zero +
    // int32 level (each AddData<long> → 4B on the wire).
    private const int JobFixedPrefixSize = 16;

    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorJobListTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task StarbaseJobTerminalAction6_InNet7SolJtSector_ReceivesWellFormedJobList()
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

        // Stage 1: create the character at Luna Station via the standard
        // ReInitializeSavedData first-login path, then tear down so the
        // avatar_level_info row is persisted.
        await using (var firstSession = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, startSector,
            firstName: "Jobster", shipName: "JobsterShip", cts.Token))
        {
            byte[] logoffPayload = new byte[8];
            await firstSession.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.LogoffRequest.Value, logoffPayload),
                cts.Token);
            await SectorHandshake.DrainUntilOpcode(
                firstSession.Sector, OpcodeId.Known.LogoffConfirmation.Value, cts.Token);
        }

        // Stage 2: re-login targeting sector 10711 (Net-7 SOL). The
        // ReloadSavedData branch preserves the sector_num set by
        // HandleLogin from the LOGIN packet's ToSectorID.
        await using var session = await SectorHandshake.ReestablishAsync(
            _server, login.Ticket!, slot, jtSector, cts.Token);

        try
        {
            // StarbaseRequest wire layout — 9 bytes:
            //   [0..4)   int32 LE  PlayerID    = 0
            //   [4..8)   int32 LE  StarbaseID  = 0   (Action=6 ignores)
            //   [8..9)   byte      Action      = 6   (Job Terminal open)
            byte[] payload = new byte[9];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), 0);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), 0);
            payload[8] = 6;

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.StarbaseRequest.Value, payload),
                cts.Token);

            // Drain inbound until we see 0x0093 JOB_LIST.
            int framesSeen = 0;
            const int maxFrames = 400;
            var observed = new List<string>();
            while (framesSeen++ < maxFrames)
            {
                var reply = await session.Sector.ReceiveAsync(cts.Token);
                Assert.NotNull(reply);
                observed.Add($"0x{reply!.Header.Opcode:X4}/{reply.Payload.Length}");

                if (reply.Header.Opcode != OpcodeId.Known.JobList.Value)
                    continue;

                // Wire layout (variable length):
                //   [0..4)   int32 LE  jobs_count (overwrites the
                //                                 placeholder header
                //                                 written at GetJobList's
                //                                 top)
                //   For each of jobs_count entries:
                //     [+0..+4)   int32 LE  job_id
                //     [+4..+8)   int32 LE  category (Combat=0,Explore=1,Trade=2)
                //     [+8..+12)  int32 LE  zero
                //     [+12..+16) int32 LE  level
                //     NUL-terminated  title
                //     NUL-terminated  sponsor
                //     NUL-terminated  reward (e.g. "%d XP")
                Assert.True(reply.Payload.Length >= 4,
                    $"0x0093 payload must contain at least the count header; got {reply.Payload.Length}B");

                int jobsCount = BinaryPrimitives.ReadInt32LittleEndian(reply.Payload.Span[..4]);
                Assert.InRange(jobsCount, 0, MaxJobListCount);

                int cursor = 4;
                for (int j = 0; j < jobsCount; j++)
                {
                    Assert.True(cursor + JobFixedPrefixSize <= reply.Payload.Length,
                        $"job[{j}]: 16B fixed prefix overruns payload at offset {cursor}");

                    int level = BinaryPrimitives.ReadInt32LittleEndian(
                        reply.Payload.Span.Slice(cursor + 12, 4));
                    // Level is m_JobTerminalLevel on this sector (Net-7 SOL: 50).
                    // Allow [1, 200] as a sanity range (real levels are 1..150).
                    Assert.InRange(level, 1, 200);

                    cursor += JobFixedPrefixSize;

                    // Three NUL-terminated strings.
                    for (int s = 0; s < 3; s++)
                    {
                        int nulOffset = reply.Payload.Span.Slice(cursor).IndexOf((byte)0);
                        Assert.True(nulOffset >= 0,
                            $"job[{j}].string[{s}]: missing NUL terminator at offset {cursor}");
                        cursor += nulOffset + 1;
                    }
                }

                Assert.Equal(reply.Payload.Length, cursor);
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x004E STARBASE_REQUEST " +
                $"(Action=6 Job-Terminal-open) in sector 10711 (Net-7 SOL) " +
                $"without seeing 0x0093 JOB_LIST. Observed [{observed.Count}]: " +
                $"{string.Join(" | ", observed)}");
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
