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
/// Wave 37 survival probe: client sends 0x0084 RECUSTOMIZE_AVATAR_DONE
/// with a canonical 257-byte all-zero RecustomizeAvatarDone payload
/// from a freshly-handshaken starbase character; assert the server
/// survives by round-tripping REQUEST_TIME after.
///
/// <para>
/// Wire shape (common/include/net7/PacketStructures.h:1109):
/// <code>
///   struct RecustomizeAvatarDone {
///       struct AvatarData avatar;  // 241 bytes (see :95)
///       int32_t playerid;          // 4 bytes
///       bool    unknown;           // 1 byte
///       char    _unknown[11];      // 11 bytes
///   } ATTRIB_PACKED;               // sizeof = 257
/// </code>
/// </para>
///
/// <para>
/// Server handler walk-through (server/src/PlayerConnection.cpp:10097).
/// Structurally symmetric to HandleRecustomizeShipDone (Wave 36) but
/// targets the avatar half of character customisation.
/// </para>
/// <list type="number">
///   <item>Cast data to <c>RecustomizeAvatarDone *</c>. 257-byte buffer
///     fits the struct exactly; no OOB read.</item>
///   <item>Compute recustomisation cost via 13 comparisons between
///     packet-&gt;avatar and m_Database.avatar fields (hair_num,
///     beard_num, hair_color, skin_color, eye_color, goggle_num,
///     ear_num, body_type, pants_type, shirt_primary_color,
///     shirt_secondary_color, pants_primary_color,
///     pants_secondary_color). All-zero packet differs from the fresh
///     character's defaults in most fields, exercising the
///     near-maximum cost path.</item>
///   <item>SetCredits(GetCredits() - cost). May underflow on a fresh
///     character; SetCredits accepts the signed result silently.</item>
///   <item>m_Database.avatar = packet-&gt;avatar -- OVERWRITES the
///     character's avatar with zeros. Destructive but contained:
///     character is deleted at end-of-test via
///     SectorHandshake.DeleteCreatedCharacterAsync.</item>
///   <item>SaveDatabase, SendStarbaseAvatarList, SendAuxPlayer. No
///     specific reply opcode is the canonical post-condition;
///     survival via REQUEST_TIME -&gt; CLIENT_SET_TIME echo is the
///     test signal.</item>
/// </list>
///
/// <para>
/// Concrete regression classes this catches:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Dispatcher mis-route at PlayerConnection.cpp:547.</b>
///     The 0x0084 case sits between 0x0082 RECUSTOMIZE_SHIP_DONE
///     (line 543) and 0x0086 MISSION_FORFEIT (line 551). Swap with
///     0x0082 mis-casts our 257B buffer as RecustomizeShipDone (210B)
///     and reads correct-size-or-less; survival probe still catches
///     side effects (the ship_data overwrite would corrupt the avatar
///     character row's ship_data column instead of the avatar column).
///     Swap with 0x0086 mis-casts to MissionDismissal (8B) and
///     interprets the first 8 bytes of avatar_first_name as
///     {PlayerID, MissionID} -- the latter would almost certainly fall
///     outside [0..12) so HandleMissionForfeit's guard rejects and
///     the test times out waiting for CLIENT_SET_TIME (REQUEST_TIME
///     would still echo though, so this swap actually wouldn't fail
///     the survival check -- a typed-codec wave is needed).
///   </item>
///   <item>
///     <b>AvatarData struct layout drift at PacketStructures.h:95.</b>
///     The 9 int32_t fields (avatar_type, race, profession, gender,
///     mood_type, shirt_primary_metal, shirt_secondary_metal,
///     pants_primary_metal, pants_secondary_metal) on LP64 Linux
///     widen to long = 8 bytes each, inflating sizeof(AvatarData)
///     from 241 to 241 + 9*4 = 277 bytes and
///     sizeof(RecustomizeAvatarDone) from 257 to 293 -- a 36-byte
///     divergence. The server would read 36 bytes past our 257B
///     buffer end. SEGV catches.
///   </item>
///   <item>
///     <b>SendStarbaseAvatarList crash on the synthetic all-zero
///     avatar.</b> hair_num=0, beard_num=0, body_type=0 etc. may not
///     correspond to any valid mesh ID in the asset table; lookup
///     paths must tolerate this or crash. Survival catches the crash.
///   </item>
///   <item>
///     <b>SendAuxPlayer NULL-deref after avatar wipe.</b> The avatar
///     overwrite may leave the per-session AuxPlayerIndex pointing
///     at no-longer-valid data; a regression that drops the
///     null-check would NULL-deref. Survival catches.
///   </item>
///   <item>
///     <b>SaveDatabase race against ongoing reads.</b> The avatar
///     blob is written via SaveDatabase while other server threads
///     may be reading m_Database.avatar; lock-ordering or
///     copy-shadow regressions would surface as memory corruption.
///     Survival catches the visible-crash subset.
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at
///     PlayerConnection.cpp:127.</b> Corrupts the 0x2016 inner-
///     tuple parser; REQUEST_TIME echo never observed.
///   </item>
///   <item>
///     <b>Proxy default-case ForwardClientOpcode dropping 0x0084.</b>
///     0x0084 is not explicitly listed in
///     proxy/ClientToServer_linux_stubs.cpp ProcessSectorServerOpcode
///     switch (verified by grep); falls through to bottom default-
///     forward arm. Regression dropping that arm silents both 0x0084
///     dispatch AND the REQUEST_TIME echo.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). The 257-byte
/// RecustomizeAvatarDone is the canonical retail Win32 client wire
/// shape emitted when the player confirms an avatar recustomisation
/// at a recustomiser terminal. All-zero is structurally well-formed
/// (per the ATTRIB_PACKED struct) even though no real recustomiser
/// UI would emit zero hair_num/body_type/etc. -- the server's
/// handler trusts the wire shape and the assumption that the client
/// has pre-validated. We are not loosening any input check; this is
/// the canonical wire byte count.
/// </para>
///
/// <para>
/// Destructive nature note. This test deliberately corrupts the
/// character's avatar row in the database. The character is deleted
/// at end-of-test, so the corruption is contained. Sibling tests
/// each get their own TestAccounts.New scope; orphaned rows are only
/// visible via direct DB inspection.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; RECUSTOMIZE_AVATAR_DONE +
/// REQUEST_TIME round-trip sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorRecustomizeAvatarDoneTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorRecustomizeAvatarDoneTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task RecustomizeAvatarDone_OnFreshStarbaseSession_DoesNotBreakConnection_RequestTimeStillRoundTrips()
    {
        var account = TestAccounts.New(_server);
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        // firstName "Avara" -- contains 'a' for the vowel-check.
        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Avara", shipName: "AvaraShip", cts.Token);

        try
        {
            // RecustomizeAvatarDone canonical 257-byte all-zero shape:
            //   [0..241)   AvatarData (20 first_name + 20 last_name + ...)
            //   [241..245) int32 playerid
            //   [245]      bool unknown
            //   [246..257) char _unknown[11]
            byte[] payload = new byte[257];
            // all zero already; payload[0..257] = 0.

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.RecustomizeAvatarDone.Value, payload),
                cts.Token);

            // Survival probe: send REQUEST_TIME, assert CLIENT_SET_TIME
            // echoes our sentinel tick.
            int clientTick = unchecked((int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF));
            byte[] reqTimePayload = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(reqTimePayload, clientTick);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.RequestTime.Value, reqTimePayload),
                cts.Token);

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
                $"drained {maxFrames} frames after sending 0x0084 RECUSTOMIZE_AVATAR_DONE + 0x0044 REQUEST_TIME " +
                $"without seeing 0x0034 CLIENT_SET_TIME. " +
                $"Likely HandleRecustomizeAvatarDone SEGV'd inside SendStarbaseAvatarList on the all-zero avatar, " +
                $"SendAuxPlayer NULL-deref'd after the avatar wipe, " +
                $"AvatarData struct layout drifted (int32_t -> long widening on the 9 int32 fields shifts the OOB boundary by 36 bytes), " +
                $"the dispatcher case at PlayerConnection.cpp:547 got mis-routed, " +
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
