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
/// Wave 37 post-handshake survival round-trip: client sends 0x0051
/// SKILL_STRING_RQ with a fresh-character target (always -1 per
/// <c>Player::FinishInit</c> at <c>PlayerClass.cpp:1085</c>), then
/// verifies the connection survives via 0x0044 REQUEST_TIME.
///
/// <para>
/// Wire layout (mirror of <c>common/include/net7/PacketStructures.h:781-785</c>).
/// </para>
/// <code>
///   [0..4)   int32 PlayerID
///   [4..8)   int32 unknown1
/// </code>
/// <para>
/// 8 bytes total; <c>ATTRIB_PACKED</c>. The handler casts to
/// <c>ClientSkillsRequest*</c> but never field-accesses the cast
/// result — the source-of-truth target is
/// <c>ShipIndex()-&gt;GetTargetGameID()</c>, not the payload's
/// PlayerID. The 8B canonical payload is sent for wire-shape
/// fidelity with the retail Win32 client's emit.
/// </para>
///
/// <para>
/// Server handler. After the dispatcher case at
/// <c>server/src/PlayerConnection.cpp:491</c> calls
/// <c>HandleSkillStringRequest(data)</c>, the handler
/// (<c>server/src/PlayerConnection.cpp:1534-1614</c>) walks:
/// </para>
/// <list type="number">
///   <item>Cast <c>data</c> to <c>ClientSkillsRequest*</c> (the
///     cast result is never used — handler reads target from
///     <c>ShipIndex()-&gt;GetTargetGameID()</c> instead).</item>
///   <item>Fetch the per-player ObjectManager via
///     <c>GetObjectManager()</c>.</item>
///   <item>Read <c>ShipIndex()-&gt;GetTargetGameID()</c> — for a
///     fresh char this is <c>-1</c> per the explicit
///     initialisation in <c>Player::FinishInit</c> at
///     <c>PlayerClass.cpp:1085</c>.</item>
///   <item>Call <c>om-&gt;GetObjectFromID(-1)</c> which returns
///     null (no object with ID==-1 exists in the sector's
///     object map).</item>
///   <item>Test <c>if (obj &amp;&amp; obj-&gt;ObjectType() == OT_HUSK)</c>
///     — short-circuits on <c>obj == null</c>, function returns.</item>
/// </list>
/// <para>
/// Zero state mutation. Zero SendOpcode. Zero observer fan-out.
/// Same favourable post-emit shape as Wave 27 (INVENTORY_MOVE
/// default arm), Wave 29 (PETITION_STUCK), Wave 30 (RELATIONSHIP),
/// Wave 36 (STARBASE_AVATAR_CHANGE early-return).
/// </para>
///
/// <para>
/// Why this wave target. Wave 36 triaged the remaining
/// client→server dispatch arms in <c>PlayerConnection.cpp</c>'s
/// switch-block into safe vs unsafe groups. Wave 37 picks the
/// next safe arm in the list: 0x0051 SKILL_STRING_RQ. Like Wave
/// 36's STARBASE_AVATAR_CHANGE, the handler has a clean
/// short-circuit gate (the <c>obj &amp;&amp; obj-&gt;ObjectType() == OT_HUSK</c>
/// test) that fires on every code path a fresh starbase character
/// can take. The <c>m_ProspectWindow</c> mutation, the
/// <c>SendOpcode(ENB_OPCODE_008C_LOOT_HULK_PERMISSION,...)</c>
/// emit, the credit-award, and the loot-lock state writes all
/// sit BEHIND the OT_HUSK guard — none of them run on this test's
/// payload.
/// </para>
///
/// <para>
/// Concrete regression classes this catches:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>ClientSkillsRequest struct long-revert in
///     PacketStructures.h:781-785.</b> Currently 2 × int32_t = 8B.
///     Widening either field to <c>long</c> on Linux x86_64
///     (sizeof(long)==8) grows the struct. The handler reads
///     neither field so the immediate test still passes; the
///     wire-shape invariant is documented for future tighter
///     wire-shape assertions (e.g. a typed-codec wave for
///     SkillsRequest).
///   </item>
///   <item>
///     <b>Dispatcher mis-route at server/src/PlayerConnection.cpp:491.</b>
///     The 0x0051 case label sits between 0x004E STARBASE_REQUEST
///     and 0x0055 SELECT_TALK_TREE. A copy-paste swap with the
///     SELECT_TALK_TREE arm would route our 8B payload through
///     <c>HandleSelectTalkTree</c>, which expects a 5B
///     SelectTalkTree payload reading Selection from byte 4 (our
///     0-init payload gives Selection=0, falls into the
///     <c>m_CurrentNPC==null</c> branch → SendTalkTreeAction(-32)
///     would emit an unexpected 0x0056 frame before our
///     CLIENT_SET_TIME — survives but surfaces as an unexpected
///     inbound opcode that the drain-until loop would skip past
///     and the test would still pass via CLIENT_SET_TIME). A
///     swap with the WARP arm would be catastrophic per the Wave
///     36 triage (formation iteration on fresh char with no
///     group).
///   </item>
///   <item>
///     <b>HandleSkillStringRequest GetObjectManager() null-deref
///     removal.</b> The current <c>if (om)</c> guard at
///     PlayerConnection.cpp:1543 protects against a sector with
///     no ObjectManager. Removal would SEGV on
///     <c>om-&gt;GetObjectFromID(...)</c>. Test surfaces this as
///     a REQUEST_TIME timeout.
///   </item>
///   <item>
///     <b>ObjectType() == OT_HUSK guard removal.</b> Without the
///     guard at PlayerConnection.cpp:1546, the handler would
///     walk the loot-as-normal branch on a null obj and call
///     <c>obj-&gt;CheckResourceLock()</c> /
///     <c>obj-&gt;GetPlayerLootLock()</c> — null-deref SEGV.
///     Test surfaces this as a REQUEST_TIME timeout.
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at PlayerConnection.cpp:127.</b>
///     The Phase K sizeof(int32_t) opcode-header fix keeps the
///     per-client UDP queue header at the canonical 4-byte width;
///     a revert would corrupt the 0x2016 inner-tuple parser and
///     break the REQUEST_TIME reply path. Same load-bearing
///     SendOpcode invariant as Waves 8/24/26/27/29/30/36.
///   </item>
///   <item>
///     <b>Proxy default-case ForwardClientOpcode regression.</b>
///     0x0051 is NOT explicitly listed in
///     <c>proxy/ClientToServer_linux_stubs.cpp</c>'s
///     <c>ProcessSectorServerOpcode</c> switch, so it falls
///     through to the bottom-of-switch ForwardClientOpcode. A
///     regression dropping the default-case forward would mean
///     the server never sees the skill-string frame (the test
///     still passes via REQUEST_TIME, which IS forwarded
///     explicitly via the proxy's REQUEST_TIME arm). The
///     diagnostic loss is silent for this opcode — but the same
///     class is caught by the petition-stuck / mission-forfeit /
///     starbase-avatar-change tests so a default-arm regression
///     would surface there first.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). 0x0051 SKILL_STRING_RQ
/// is what the retail Win32 client emits when the user clicks
/// the 'Loot' action on a targeted HUSK in space. The 8-byte
/// canonical wire shape is byte-identical to retail. Sending
/// SKILL_STRING_RQ with no current target (this test's case:
/// fresh starbase character with TargetGameID==-1) is a legal
/// but loot-window-noop request — the retail server silently
/// ignores it via the same <c>obj &amp;&amp; obj-&gt;ObjectType() == OT_HUSK</c>
/// short-circuit. Zero permissiveness added; not loosening any
/// security posture; not fabricating any reply.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; SKILL_STRING_RQ + REQUEST_TIME
/// round-trip is sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorSkillStringRqTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorSkillStringRqTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task SkillStringRq_OnFreshCharNoTarget_DoesNotBreakConnection_RequestTimeStillRoundTrips()
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
            firstName: "Skstreq", shipName: "SkstreqShip", cts.Token);

        try
        {
            // 0x0051 SKILL_STRING_RQ — 8B canonical payload.
            //   [0..4)   int32 PlayerID  = 0 (handler never reads this field)
            //   [4..8)   int32 unknown1  = 0 (handler never reads this field)
            byte[] payload = new byte[8];
            // Both fields already zero from new byte[8]; explicit writes
            // for documentation of the wire-shape intent.
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), 0);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), 0);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.SkillStringRq.Value, payload),
                cts.Token);

            // Survival probe: did the connection survive the
            // skill-string-rq handler? Send REQUEST_TIME and assert
            // CLIENT_SET_TIME echoes our sentinel tick.
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
                $"drained {maxFrames} frames after sending 0x0051 SKILL_STRING_RQ + 0x0044 REQUEST_TIME " +
                $"without seeing 0x0034 CLIENT_SET_TIME. " +
                $"Likely the server's HandleSkillStringRequest guard at PlayerConnection.cpp:1546 was removed " +
                $"(would SEGV on null obj->CheckResourceLock()), " +
                $"the GetObjectManager() null-guard at line 1543 was removed " +
                $"(would SEGV on om->GetObjectFromID with om==null), " +
                $"the dispatcher case at PlayerConnection.cpp:491 got mis-routed to a risky handler, " +
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
