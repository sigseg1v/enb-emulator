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
/// Wave 29 post-handshake survival round-trip: client sends 0x0088
/// PETITION_STUCK (the retail Win32 client's "/stuck" GM petition
/// emit), then verifies the connection survives via 0x0044
/// REQUEST_TIME.
///
/// <para>
/// Why survival probe rather than direct reply assertion.
/// <c>Player::HandlePetitionStuck</c>
/// (<c>server/src/PlayerConnection.cpp:11048-11051</c>) is a one-liner
/// that forwards the entire payload to
/// <c>Player::SavePetition</c> (<c>server/src/PlayerSaves.cpp:1222</c>),
/// which simply calls
/// <c>g_SaveMgr-&gt;AddSaveMessage(SAVE_CODE_PETITION, m_CharacterID, bytes, data)</c>.
/// That hands the raw byte stream to the save-manager queue for
/// asynchronous DB write — there is no SendOpcode emit anywhere in
/// the path, the handler doesn't parse the payload at all (the inline
/// <c>struct PetitionStuck</c> documented in the comment block at
/// <c>PlayerConnection.cpp:11019-11037</c> is consumed by the
/// save-manager / DB layer downstream, not the sector handler). So
/// there is no direct reply to assert on; pipe survival is the only
/// post-condition.
/// </para>
///
/// <para>
/// Why this wave target. PETITION_STUCK is one of the few remaining
/// untested opcodes whose handler is provably side-effect-bounded:
/// no NPC template mutation (unlike 0x008D INCAPACITANCE_REQUEST
/// which mutates <c>NPCs-&gt;Avatar.shirt_primary_color</c> shared
/// state and crashed the server in the Wave 29 first-pick attempt),
/// no group/formation walk (unlike 0x009B WARP which iterates
/// <c>g_PlayerMgr-&gt;GetMemberID</c> for the formation and
/// <c>SetupWarpNavs</c> on every group member), no mission-state
/// touch (unlike 0x0086 MISSION_FORFEIT which calls
/// <c>RemoveMission</c> on the zero-initialised fresh-char mission
/// array), and no equipment activation (unlike 0x005D EQUIP_USE
/// which derefs <c>m_Equip[InvSlot].ManualActivate()</c> on a
/// fresh-char empty equipment array). The save-manager queue is
/// thread-safe by construction (every other Save* path in
/// PlayerSaves.cpp uses the same <c>AddSaveMessage</c> entry point
/// and is exercised on every login) so the petition byte stream
/// is simply buffered for DB write and the sector handler returns
/// immediately.
/// </para>
///
/// <para>
/// Wire layout — the comment block above
/// <c>HandlePetitionStuck</c> at <c>PlayerConnection.cpp:11019-11037</c>
/// documents the retail PetitionStuck struct as
/// <c>long GameID; long ProblemType; char Subject[]; char Complaint[]; char PlayerList[];</c>
/// — two 4-byte little-endian fields followed by three variable-length
/// null-terminated strings. The handler itself does not parse this
/// struct on the sector side; the save manager / DB layer is responsible
/// for that. We send a minimal canonical payload: GameID=0, ProblemType=0,
/// empty Subject, empty Complaint, empty PlayerList — i.e. 4 + 4 + 1 + 1 + 1
/// = 11 bytes with the three trailing bytes being the NUL terminators of
/// the three empty strings.
/// </para>
///
/// <para>
/// Regression classes this catches.
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Dispatcher mis-route at server/src/PlayerConnection.cpp:559.</b>
///     The 0x0088 case label sits between 0x0087 MISSION_DISMISSAL
///     (which takes <c>data</c>, calls
///     <c>HandleMissionDismissal(data)</c>) and 0x008D
///     INCAPACITANCE_REQUEST (which takes <c>data</c>, calls
///     <c>HandleIncapacitanceRequest(data)</c>). A copy-paste swap
///     that routed our 11B payload into HandleIncapacitanceRequest
///     would trigger that handler's known-crashing first-time
///     <c>m_IncapAvatarSent</c> branch
///     (PlayerConnection.cpp:11069-11104, which mutates shared NPC
///     template state and SEGVs) — surfaces as a connection drop and
///     the REQUEST_TIME survival probe times out. PETITION_STUCK is
///     unique in this dispatcher slice in that the handler signature
///     is <c>(data, bytes)</c> — a regression that dropped the
///     <c>bytes</c> argument would mean SavePetition stores 0 bytes
///     (and the petition DB row is empty), still survives but a
///     content-level audit on the petition DB row would expose it.
///   </item>
///   <item>
///     <b>Proxy default-case ForwardClientOpcode regression.</b>
///     0x0088 is NOT explicitly listed in
///     <c>proxy/ClientToServer_linux_stubs.cpp</c>'s
///     <c>ProcessSectorServerOpcode</c> switch (lines 383-518), so it
///     falls through to the bottom-of-switch forward. A regression
///     that filtered 0x0088 (or any explicit case that
///     <c>return</c>ed instead of <c>break</c>ing for this opcode)
///     would mean the server never sees the petition — the connection
///     survives so the test passes via REQUEST_TIME, but petition data
///     is silently dropped. (The same silent-divergence failure mode
///     applies to STARBASE_REQUEST and STARBASE_ROOM_CHANGE; we
///     accept this gap because the survival probe's primary job is to
///     catch crashes and the proxy switch is otherwise covered by
///     review and the explicit-listed opcodes.)
///   </item>
///   <item>
///     <b>SavePetition memory corruption / queue overflow.</b>
///     <c>g_SaveMgr-&gt;AddSaveMessage</c> copies the
///     <c>bytes</c>-sized byte buffer into the save-message queue. A
///     regression that read past <c>bytes</c> (e.g. a typo passing
///     <c>strlen(data)</c> or a fixed constant) would surface as
///     either a fan-out crash on the save thread (if the over-read
///     SEGV'd) or as a silent DB row corruption (over-read into
///     adjacent heap that happens to be readable). The connection-
///     survival half catches only the crash case.
///   </item>
///   <item>
///     <b>m_CharacterID staleness.</b> SavePetition stamps the
///     petition row with the current <c>m_CharacterID</c>; if a Phase
///     K refactor desynced <c>m_CharacterID</c> from the active
///     session's character (e.g. dropping the
///     <c>SetCharacterID</c> call in <c>PlayerManager::CompleteLogin</c>),
///     the petition would be tied to character ID 0 and the
///     DB-side FK constraint would either reject or orphan the row.
///     Connection survives; would need a follow-up content-level
///     test to catch.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). The PETITION_STUCK payload
/// sent here is exactly the wire shape the retail Win32 client emits
/// when the user types <c>/stuck</c>: two int32_t LE fields followed
/// by three NUL-terminated strings (here all empty). Zero
/// permissiveness added; we are not making the server accept anything
/// it didn't previously accept. The retail server simply queued the
/// petition for GM review — there was no direct reply on the wire,
/// and we do not fabricate one.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; PETITION_STUCK + REQUEST_TIME
/// round-trip is sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorPetitionStuckTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorPetitionStuckTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task PetitionStuck_DoesNotBreakConnection_RequestTimeStillRoundTrips()
    {
        // cli_test26 — Pool[24]. Dedicated to this wave so its
        // Create/Delete cycle doesn't collide with the Pool slots
        // owned by earlier waves. seed.sql carries the matching
        // 9_000_026 row.
        var account = TestAccounts.Pool[24];
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Stuck", shipName: "StuckShip", cts.Token);

        try
        {
            // PetitionStuck canonical 11B wire payload:
            //   [0..4)   long GameID       = 0          (int32_t LE)
            //   [4..8)   long ProblemType  = 0          (int32_t LE)
            //   [8]      char Subject[1]   = 0x00       (empty NUL-term string)
            //   [9]      char Complaint[1] = 0x00       (empty NUL-term string)
            //   [10]     char PlayerList[1]= 0x00       (empty NUL-term string)
            // Per the doc comment at server/src/PlayerConnection.cpp:11019-11037.
            byte[] payload = new byte[11];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), 0);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), 0);
            // Trailing 3 NUL bytes already zero from new byte[11].

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.PetitionStuck.Value, payload),
                cts.Token);

            // Survival probe: did the connection survive the petition
            // handler? Send REQUEST_TIME and assert CLIENT_SET_TIME
            // echoes our sentinel tick.
            int clientTick = unchecked((int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF));

            byte[] reqTimePayload = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(reqTimePayload, clientTick);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.RequestTime.Value, reqTimePayload),
                cts.Token);

            // Drain until 0x0034 CLIENT_SET_TIME. Tolerate interleaved
            // in-sector frames; cap on frame count so a stalled
            // pipeline doesn't masquerade as the outer-CTS timeout.
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
                $"drained {maxFrames} frames after sending 0x0088 PETITION_STUCK + 0x0044 REQUEST_TIME " +
                $"without seeing 0x0034 CLIENT_SET_TIME. " +
                $"Likely the server's HandlePetitionStuck SEGV'd inside SavePetition " +
                $"(g_SaveMgr->AddSaveMessage memory corruption or m_CharacterID null-deref), " +
                $"the proxy default-case ForwardClientOpcode dropped 0x0088, " +
                $"the dispatcher case at PlayerConnection.cpp:559 got mis-routed " +
                $"(swap with HandleIncapacitanceRequest at line 563 would trigger that handler's " +
                $"known-crashing first-time AvatarDescription emit), " +
                $"or the SendOpcode header-width fix at PlayerConnection.cpp:127 was reverted " +
                $"(would corrupt the 0x2016 inner-tuple parser and break the entire reply path).");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }
}
