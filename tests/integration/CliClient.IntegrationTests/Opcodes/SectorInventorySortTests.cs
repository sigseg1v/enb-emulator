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
/// Wave 50 post-handshake survival round-trip: client sends 0x0028
/// INVENTORY_SORT with <c>TargetInv=99</c> — the
/// <c>switch (Data.TargetInv)</c> at
/// <c>server/src/PlayerConnection.cpp:3310</c> covers exactly cases 1
/// (cargo) and 3 (vault); everything else falls into the
/// <c>default:</c> arm at lines 3354-3356 which only calls
/// <c>LogMessage("unknown InvSort request id:%x inv:%d ...")</c> and
/// returns. No <c>memcpy</c>, no <c>qsort</c>, no <c>SendAuxShip</c>,
/// no <c>SendAuxPlayer</c>, no <c>SaveInventoryChange</c>, no
/// <c>SaveVaultChange</c>, no MESSAGE_STRING reply. State mutation
/// is bounded to the per-player <c>m_LastSort</c> tick stamp +
/// <c>InvSortType[0..2]</c> + <c>InvSortRev</c> member fields used as
/// qsort comparator state — observable only through subsequent sorts
/// against the SAME player object, not over the wire.
///
/// <para>
/// Wire layout (from <c>common/include/net7/PacketStructures.h:253-261</c>):
/// </para>
/// <code>
///   struct InvSort
///   {
///       int32_t ID;        // network byte order — handler ntohl-decodes
///       int32_t TargetInv; // network byte order — handler ntohl-decodes
///       int32_t Sort1;     // network byte order — handler ntohl-decodes
///       int32_t Sort2;     // network byte order — handler ntohl-decodes
///       int32_t Sort3;     // network byte order — handler ntohl-decodes
///       char    Reverse;   // single byte — endian-invariant
///   } ATTRIB_PACKED;
/// </code>
/// <para>
/// 21B packed (5×4 + 1) — same wire-shape constraint as Wave 27's
/// 24B InvMove (one fewer int32 + a trailing byte). The handler
/// ntohl-decodes every int32 field on entry at
/// <c>server/src/PlayerConnection.cpp:3299-3303</c>, so each field
/// goes on the wire BIG-ENDIAN (mirror of Wave 27's INVENTORY_MOVE
/// convention, distinct from Wave 26's SKILL_ABILITY host-LE shape).
/// </para>
///
/// <para>
/// Server handler walk-through:
/// </para>
/// <list type="number">
///   <item>Dispatcher case at
///     <c>server/src/PlayerConnection.cpp:459-461</c> — note the
///     macro is mis-prefixed as <c>ENB_OPCODE_0027_INVENTORY_SORT</c>
///     in <c>common/include/net7/Opcodes.h:58</c> but the
///     <c>#define</c> value is <c>0x0028</c>; <c>HandleInventorySort</c>
///     is called.</item>
///   <item><c>HandleInventorySort</c> at
///     <c>server/src/PlayerConnection.cpp:3285-3357</c>: casts data →
///     <c>InvSort *</c>, reads <c>GetNet7TickCount()</c> into local
///     <c>tick</c>.</item>
///   <item><c>if (tick &lt; (m_LastSort + 10000))</c> rate-limit
///     guard at line 3291. <c>m_LastSort</c> is ctor-initialised to
///     <c>0</c> (<c>server/src/PlayerClass.cpp:144</c> + line 271 in
///     the secondary init path), so the guard simplifies to
///     <c>if (tick &lt; 10000)</c> on first invocation. Server has
///     been up for many seconds by handshake-completion time so the
///     guard does NOT fire — proceeds to the switch. (If a future
///     test arrangement somehow tripped this guard, the handler
///     emits ONE 0x001D MESSAGE_STRING "Cargo sort machine
///     resetting, please try in a few seconds." via
///     <c>SendVaMessageC(17, ...)</c>; the REQUEST_TIME survival
///     probe still passes either way.)</item>
///   <item>Stamp <c>m_LastSort = tick</c> at line 3297 — observable
///     side effect, only matters for subsequent sorts.</item>
///   <item>ntohl-decode every int32 field into local
///     <c>InvSort Data</c> at lines 3299-3303 + copy
///     <c>Reverse</c> byte verbatim at line 3304.</item>
///   <item>Stamp <c>InvSortType[0..2]</c> + <c>InvSortRev</c> from
///     the decoded Sort1/2/3/Reverse fields at lines 3306-3309 —
///     this state seeds the qsort comparator <c>InvSortFunc</c> if
///     case 1 or 3 fires.</item>
///   <item><c>switch (Data.TargetInv)</c> at line 3310 dispatches on
///     the host-order <c>TargetInv</c> field. Cases 1 (cargo) and 3
///     (vault) memcpy → qsort → write-back loop → SendAuxShip /
///     SendAuxPlayer. The <c>default:</c> arm at lines 3354-3356
///     fires for anything else — just <c>LogMessage</c> and return.
///     TargetInv=99 is well outside [1..3] so the default arm is
///     guaranteed.</item>
/// </list>
///
/// <para>
/// Why this wave target. After Wave 49 closed the manufacture-action
/// family, the 0x0028 INVENTORY_SORT default-branch is the next
/// cleanest dispatched-but-untested in-sector opcode (the four
/// candidates surveyed during Wave 50 triage — 0x0018
/// REQUEST_TARGETS_TARGET, 0x0027 INVENTORY_MOVE, 0x002D ACTION2,
/// 0x0080 MANUFACTURE_TECH_LEVEL_FILTER — all turned out to be
/// already covered).
/// </para>
///
/// <para>
/// Survival probe vs. direct reply. The default arm emits ZERO
/// observable wire frames — only a server-side <c>LogMessage</c>
/// call. So this is a pure survival probe (send INVENTORY_SORT,
/// then REQUEST_TIME, assert CLIENT_SET_TIME echoes our sentinel
/// tick) rather than the stronger direct-reply pattern Wave 27's
/// 0x0027 UNRECOGNISED INVENTORY MOVE assertion uses. A future
/// stronger wave could trip the 10s rate-limiter on a second sort
/// and pin on the "Cargo sort machine resetting" MESSAGE_STRING
/// literal, but that's racy (depends on per-invocation tick
/// budgeting) — out of scope here.
/// </para>
///
/// <para>
/// Why TargetInv=99 and not 1 or 3. Case 1 dereferences
/// <c>ShipIndex()-&gt;Inventory.CargoInv.GetData()</c> and case 3
/// dereferences <c>PlayerIndex()-&gt;SecureInv.GetData()</c>; a
/// regression in either inventory's GetData() returning a null
/// pointer would SEGV the sort. The default arm is strictly
/// cleaner: no array dereference, no memcpy, no qsort, no
/// SaveInventoryChange chain, no AuxShip / AuxPlayer fan-out.
/// </para>
///
/// <para>
/// Concrete regression classes this catches:
/// </para>
/// <list type="bullet">
///   <item><b>Dispatcher mis-route at server/src/PlayerConnection.cpp:459.</b>
///     Case label sits immediately after 0x0027 INVENTORY_MOVE
///     (line 455-457) and immediately before 0x0029 ITEM_STATE
///     (line 463-465). A copy-paste swap with HandleInventoryMove
///     would mis-interpret the 21B InvSort as a 24B InvMove —
///     reading past the end of our payload for the Num field; a
///     copy-paste swap with HandleItemState would read the first
///     11B as ItemState with Inventory byte at offset 9 = 0
///     (zero-padding past Reverse byte) — would hit
///     UNRECOGNISED-arm sending a "UNRECOGNISED ITEM STATE!"
///     MESSAGE_STRING which would survive REQUEST_TIME but
///     differs from our intended path; byte-comparison wave would
///     surface the divergence.</item>
///   <item><b>PacketStructures.h InvSort long-revert regression at
///     PacketStructures.h:253-261.</b> Currently 5× int32_t +
///     1× char = 21B canonical. Widening any int field to
///     <c>long</c> on Linux x86_64 (sizeof(long)==8) grows the
///     struct — TargetInv would shift from offset 4 to offset 8
///     (re-reading Sort1's slot in our wire layout). With our
///     payload TargetInv-at-offset-8 reads the host-ordered
///     network-encoded Sort1=0 bytes, ntohl(0)=0, which is NOT in
///     the switch — still hits default arm. False negative on this
///     payload but the same Wave 12 long-sweep grep guards apply.</item>
///   <item><b>ntohl/htonl byte-order flip on TargetInv.</b> The
///     handler ntohl-decodes every field. A regression that
///     dropped the ntohl on TargetInv would read our BE 99
///     (0x00000063) as host-LE 0x63000000 = 1_660_944_384 — not in
///     the switch — still hits default arm. False negative on this
///     exact payload but documented as a known limitation.</item>
///   <item><b>m_LastSort init regression at PlayerClass.cpp:144 / 271.</b>
///     If a refactor flipped the init to a huge uint32 value
///     (e.g. <c>(u32)-1</c>), the rate-limit check
///     <c>tick &lt; m_LastSort + 10000</c> would wrap and either
///     always-trip or never-trip depending on the wrap arithmetic.
///     The always-trip case fires the rate-limit MESSAGE_STRING
///     which is harmless — REQUEST_TIME still round-trips. The
///     never-trip case is the current behaviour. Test catches
///     neither directly but documents the contract.</item>
///   <item><b>switch default-arm removal regression at
///     PlayerConnection.cpp:3354.</b> A "tidy up" refactor that
///     deletes the default arm leaves TargetInv=99 silently
///     falling through the switch — no LogMessage, no reply. Test
///     still survives via REQUEST_TIME (the survival probe doesn't
///     depend on the default arm doing anything observable to the
///     client). False negative; would only matter if a future
///     wave moved to assertion on the LogMessage contents (which
///     isn't visible to the client anyway).</item>
///   <item><b>Rate-limit constant regression at
///     PlayerConnection.cpp:3291.</b> Changing 10000ms to 0 would
///     remove rate limiting; changing it to 100000ms would tighten
///     it. Neither affects this test (single-sort payload).</item>
///   <item><b>SendOpcode header-width revert at
///     PlayerConnection.cpp:127.</b> Phase K sizeof(int32_t)
///     opcode-header fix keeps the per-client UDP queue header at
///     4B; a revert corrupts the 0x2016 inner-tuple parser →
///     REQUEST_TIME survival probe silent. Same shape as
///     Waves 11/27/29/30/36/37/38/39/40/41.</item>
///   <item><b>Proxy default-case ForwardClientOpcode regression
///     for 0x0028.</b> 0x0028 INVENTORY_SORT is NOT explicitly
///     listed in <c>proxy/ClientToServer_linux_stubs.cpp</c> or
///     <c>proxy/ClientToSectorServer.cpp</c> (verified by grep) —
///     falls through to the default ForwardClientOpcode arm. A
///     regression dropping the default arm would mean the server
///     never sees the INVENTORY_SORT frame; the test would still
///     pass via the REQUEST_TIME path's explicit forwarding (which
///     also rides the same default arm — a true default-arm drop
///     would also break REQUEST_TIME and surface as timeout).</item>
///   <item><b>Proxy SendClientPacketSequence inner-opcode guard
///     tightening at UDPProxyToClient_linux.cpp:568.</b> Currently
///     passes 0x0034 CLIENT_SET_TIME because 0x0034 &lt; 0x0FFF;
///     a regression tightening that guard would break the
///     REQUEST_TIME echo.</item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). 0x0028 INVENTORY_SORT is
/// what the retail Win32 client emits when the user clicks a column
/// header on the cargo / vault grid. The retail server's
/// <c>HandleInventorySort</c> has the same default arm with the
/// same <c>LogMessage</c> call and same 10s rate limiter — the
/// default arm IS the documented bad-input handler. Zero
/// permissiveness added; not loosening any security posture (the
/// default arm is strictly defensive); not fabricating any reply
/// (the default arm emits none).
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; INVENTORY_SORT + REQUEST_TIME
/// round-trip is sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorInventorySortTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorInventorySortTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task InventorySort_UnrecognisedTargetInv_DoesNotBreakConnection_RequestTimeStillRoundTrips()
    {
        // cli_test47 — Pool[45]. Dedicated to this wave so its
        // Create/Delete cycle doesn't collide with Pool slots owned
        // by earlier waves. seed.sql carries the matching 9_000_047
        // row.
        var account = TestAccounts.Pool[45];
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        // firstName "iris" starts with lowercase 'i' for the
        // AccountManager.cpp:1147 vowel-check footgun (case-sensitive
        // a/e/i/o/u/y BEFORE toupper at line 1153).
        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "iris", shipName: "IrisShip", cts.Token);

        try
        {
            // 0x0028 INVENTORY_SORT (HandleInventorySort) —
            // 21B packed payload, all int32 fields BIG-ENDIAN:
            //   [0..4)   int32 ID        = 0
            //   [4..8)   int32 TargetInv = 99 (BE) — outside the
            //                              switch's {1,3} → default
            //                              arm: LogMessage + return
            //   [8..12)  int32 Sort1     = 0
            //   [12..16) int32 Sort2     = 0
            //   [16..20) int32 Sort3     = 0
            //   [20]     char  Reverse   = 0
            byte[] payload = new byte[21];
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(0, 4), 0);    // ID
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(4, 4), 99);   // TargetInv (default arm)
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(8, 4), 0);    // Sort1
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(12, 4), 0);   // Sort2
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(16, 4), 0);   // Sort3
            payload[20] = 0;                                                  // Reverse

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.InventorySort.Value, payload),
                cts.Token);

            // Survival probe: did the connection survive the
            // INVENTORY_SORT default-arm handler? Send REQUEST_TIME
            // and assert CLIENT_SET_TIME echoes our sentinel tick.
            int clientTick = unchecked((int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF));

            byte[] reqTimePayload = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(reqTimePayload, clientTick);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.RequestTime.Value, reqTimePayload),
                cts.Token);

            // Drain until 0x0034 CLIENT_SET_TIME. Tolerate any
            // interleaved positional-update frames from in-sector
            // observers. Default-arm INVENTORY_SORT emits NO reply
            // frame itself.
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
                $"drained {maxFrames} frames after sending 0x0028 INVENTORY_SORT + 0x0044 REQUEST_TIME " +
                $"without seeing 0x0034 CLIENT_SET_TIME. " +
                $"Likely the dispatcher arm at PlayerConnection.cpp:459 got mis-routed, " +
                $"the switch default-arm at PlayerConnection.cpp:3354 was reshuffled and a case 99 was added (would still survive but with side effects), " +
                $"the proxy's bottom-of-switch ForwardClientOpcode default at proxy/ClientToServer_linux_stubs.cpp dropped 0x0028, " +
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
