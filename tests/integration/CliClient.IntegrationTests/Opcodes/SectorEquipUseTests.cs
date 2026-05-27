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
/// Wave 42 post-handshake survival round-trip: client sends 0x005D
/// EQUIP_USE on an empty device slot (InvSlot=15), then verifies the
/// connection survives via 0x0044 REQUEST_TIME.
///
/// <para>
/// Wire layout (from the canonical packet struct at
/// <c>common/include/net7/PacketStructures.h:929-934</c>):
/// </para>
/// <code>
///   struct EquipUse
///   {
///       int32_t GameID;
///       char    InvNum;
///       char    InvSlot;
///   };
/// </code>
/// <para>
/// 6-byte payload. The handler reads the struct via direct cast — no
/// ntohl on any field, no byte-order normalisation. The GameID and
/// InvNum fields are ignored by the handler (the connection identity
/// already carries the player) and InvSlot is a single byte
/// (endianness-invariant). The handler then dispatches to
/// <c>m_Equip[InvSlot].ManualActivate()</c> with no bounds check —
/// faithful to retail (CLAUDE.md forbids tightening input acceptance
/// beyond the real server).
/// </para>
///
/// <para>
/// Server handler walk-through.
/// </para>
/// <list type="number">
///   <item>Dispatcher case at
///     <c>server/src/PlayerConnection.cpp:511-513</c> calls
///     <c>HandleEquipUse(data)</c>.</item>
///   <item><c>HandleEquipUse</c> at
///     <c>server/src/PlayerConnection.cpp:4556-4561</c>:
///     casts <c>data</c> to <c>EquipUse *</c> and calls
///     <c>m_Equip[myUse-&gt;InvSlot].ManualActivate()</c>. No
///     intermediate logging, no SendOpcode, no bounds check on
///     <c>InvSlot</c>.</item>
///   <item><c>Equipable::ManualActivate</c> at
///     <c>server/src/Equipable.cpp:548-575</c>: the very first line
///     after entry is the early-bail guard
///     <c>if ((m_AuxEquipItem == NULL) || (m_AuxEquipItem-&gt;GetData()
///     == NULL) || (m_AuxEquipItem-&gt;GetItemTemplateID() &lt; 0))
///     return;</c>. For our InvSlot=15 (the 16th equipable slot)
///     on a freshly-created Terran Warrior character, the underlying
///     <c>ShipIndex()-&gt;Inventory.EquipInv.EquipItem[15]</c> entry
///     has <c>GetItemTemplateID() == -1</c> / <c>GetData() == NULL</c>
///     (the starter ship template doesn't equip any device — slot 15
///     falls in the device band <c>SlotNum &gt;= 9 &amp;&amp;
///     SlotNum &lt;= 14</c> from Equipable.cpp:54-57 plus slots 15-19
///     which Init handles uniformly via the same m_AuxEquipItem alias
///     at line 61). The early-bail returns immediately. Zero
///     <c>Activate()</c> call, zero <c>SendAuxShip()</c>, zero
///     observer fan-out, zero state mutation.</item>
/// </list>
/// <para>
/// Same favourable post-emit shape as Wave 27 / 29 / 30 / 36 / 37 /
/// 38 / 39 / 40 / 41 — the handler dispatches, reads payload fields,
/// then the guard inside the call chain rejects the empty-slot input.
/// </para>
///
/// <para>
/// Why this wave target (post-pivot from 0x0098 and 0x002E).
/// 0x0098 GALAXY_MAP_REQUEST emits 0x2011 GALAXY_MAP_CACHE which the
/// proxy guard at UDPProxyToClient_linux.cpp:568 (<c>opcode &gt;
/// 0x0000 &amp;&amp; opcode &lt; 0x0FFF</c>) silently drops with a
/// connection-terminate — that's a real Linux-proxy gap tracked
/// separately. 0x002E OPTION was already covered by Wave 22's
/// <c>SectorOptionTests.Option_UnhandledOptionType_*</c>. 0x005D
/// EQUIP_USE was the next direct-dispatch handler with a known
/// empty-input arm and no SendOpcode side-effects — perfect for the
/// survival-probe pattern.
/// </para>
///
/// <para>
/// Concrete regression classes this catches:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>m_Equip out-of-bounds OOB at PlayerConnection.cpp:4560.</b>
///     The handler indexes <c>m_Equip[InvSlot]</c> with NO bounds
///     check. m_Equip is sized 20 at PlayerClass.h:1320. Sending
///     InvSlot in [0..19] is in-bounds; we use 15 (well-defined
///     device slot). A regression that REMOVES the
///     PlayerClass.cpp:387-391 init loop's <c>i&lt;20</c> bound
///     (say shortens to <c>i&lt;8</c> in a refactor) would leave
///     m_Equip[15].m_AuxEquipItem uninitialized — the ManualActivate
///     NULL-check at Equipable.cpp:551 would dereference an undefined
///     pointer. Test would crash or time out. Conversely, a future
///     ADDED bounds check tighter than retail (e.g.
///     <c>if (InvSlot &lt; 0 || InvSlot &gt;= 20) return;</c> — which
///     itself would be a CLAUDE.md violation; tightening input
///     acceptance requires primary-source proof retail did the same)
///     wouldn't affect this test since slot 15 is in-bounds.
///   </item>
///   <item>
///     <b>PacketStructures.h EquipUse struct layout regression.</b>
///     Currently 6B packed: <c>int32_t GameID; char InvNum; char
///     InvSlot;</c>. A long-widening of GameID to <c>long</c> on
///     Linux x86_64 (the Phase K Wave 11 regression class) would
///     shift InvSlot from offset 5 to offset 9 — reading from
///     offset 5 of our 6B payload reads past the buffer (UB). Even
///     under MSVC-style char-padding the wrong offset would land on
///     0 (InvNum byte), which selects slot 0 (SHIELD) — for a
///     starter Terran Warrior that slot may be populated, in which
///     case ManualActivate would proceed past the early-bail to the
///     autofire-cancel arm (sends 0x0047 CLIENT_SHIP). Survival
///     probe still passes but a future byte-comparison wave catches
///     the divergence.
///   </item>
///   <item>
///     <b>Dispatcher mis-route at server/src/PlayerConnection.cpp:511-513.</b>
///     The 0x005D case sits between 0x005A VERB_REQUEST (line 507)
///     and 0x005E AVATAR_EMOTE (line 515). A copy-paste swap with
///     HandleAvatarEmote (which calls HandleChatStream casting to
///     the 11B ChatStream struct) on our 6B payload would over-read
///     the receive buffer for the ChatSize field at offset 5..6
///     (UB; connection might or might not survive). A swap with
///     HandleVerbRequest (which ntohl-decodes a 12B VerbRequest)
///     would also over-read. The survival probe catches the
///     survive-vs-crash boundary.
///   </item>
///   <item>
///     <b>Equipable::ManualActivate guard-order regression at
///     Equipable.cpp:551.</b> Current order is NULL check FIRST,
///     then GetData() NULL check, then GetItemTemplateID() &lt; 0.
///     A refactor that reorders the short-circuit (e.g. checking
///     GetItemTemplateID() before the pointer NULL check) on a slot
///     where m_AuxEquipItem happened to be NULL would dereference
///     NULL. For slot 15 the pointer is non-NULL post-Init, but the
///     regression class is real-world possible during cleanup
///     refactors.
///   </item>
///   <item>
///     <b>Init-loop boundary regression at PlayerClass.cpp:387.</b>
///     The loop <c>for (int i=0; i&lt;20; i++) m_Equip[i].Init(this,
///     i)</c> initializes m_AuxEquipItem for all 20 slots. A
///     shortened loop (e.g. <c>i&lt;15</c>) would leave m_Equip[15]
///     uninitialised. ManualActivate's NULL-check dereferences
///     undefined memory.
///   </item>
///   <item>
///     <b>Proxy default-case ForwardClientOpcode dropping 0x005D.</b>
///     0x005D is NOT explicitly listed in
///     <c>proxy/ClientToServer_linux_stubs.cpp</c>'s
///     ProcessSectorServerOpcode switch (verified by grep), so it
///     falls through to the bottom-of-switch default
///     ForwardClientOpcode arm at line 514-517. A regression
///     dropping that default arm would also break REQUEST_TIME
///     (same default arm), so the test times out instead of
///     silently passing.
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at PlayerConnection.cpp:127.</b>
///     Phase K sizeof(int32_t) opcode-header fix keeps the per-
///     client UDP queue header at 4B; a revert corrupts the 0x2016
///     inner-tuple parser → REQUEST_TIME survival probe silent.
///   </item>
///   <item>
///     <b>Proxy SendClientPacketSequence inner-opcode guard
///     tightening at <c>UDPProxyToClient_linux.cpp:568</c>.</b>
///     Currently passes 0x0034 CLIENT_SET_TIME because 0x0034 &lt;
///     0x0FFF. This is the same proxy guard that blocks 0x2011
///     GALAXY_MAP_CACHE for the unrelated 0x0098 dispatch — see
///     Wave 42 triage notes in plans/00-master.md.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). 0x005D EQUIP_USE is what
/// the retail Win32 client emits when the user clicks the "use"
/// button on an inventory slot in the ship UI. Sending it for an
/// empty slot is a legal client behaviour — the UI may show a slot
/// as occupied due to lag or stale state while the server-side
/// inventory has emptied; the retail server's empty-slot no-op is
/// exactly what's preserved here. Zero permissiveness added; not
/// loosening any security posture; not fabricating any reply.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; EQUIP_USE + REQUEST_TIME round-trip
/// is sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorEquipUseTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorEquipUseTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task EquipUse_OnEmptySlot_DoesNotBreakConnection_RequestTimeStillRoundTrips()
    {
        // cli_test39 — Pool[37]. Dedicated to this wave so its
        // Create/Delete cycle doesn't collide with Pool slots owned
        // by earlier waves. seed.sql carries the matching 9_000_039
        // row.
        var account = TestAccounts.Pool[37];
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        // firstName "Equse" — lowercase 'e' for the
        // AccountManager.cpp:1147 vowel-check footgun (case-sensitive
        // a/e/i/o/u/y BEFORE toupper at line 1153).
        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Equse", shipName: "EquseShip", cts.Token);

        try
        {
            // 0x005D EQUIP_USE — 6B payload:
            //   int32_t GameID    (handler ignores; identity from connection)
            //   char    InvNum    (handler ignores)
            //   char    InvSlot   (15 — device slot, empty for fresh
            //                      Terran Warrior; ManualActivate
            //                      early-bails on the empty-item check)
            byte[] payload = new byte[6];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), 0);  // GameID
            payload[4] = 0;   // InvNum
            payload[5] = 15;  // InvSlot — empty device slot

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.EquipUse.Value, payload),
                cts.Token);

            // Survival probe: did the connection survive the
            // EQUIP_USE handler? Send REQUEST_TIME and assert
            // CLIENT_SET_TIME echoes our sentinel tick.
            int clientTick = unchecked((int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF));

            byte[] reqTimePayload = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(reqTimePayload, clientTick);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.RequestTime.Value, reqTimePayload),
                cts.Token);

            // Drain until 0x0034 CLIENT_SET_TIME. Tolerate
            // interleaved in-sector frames (positional updates from
            // observers, etc.).
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
                $"drained {maxFrames} frames after sending 0x005D EQUIP_USE + 0x0044 REQUEST_TIME " +
                $"without seeing 0x0034 CLIENT_SET_TIME. " +
                $"Likely the dispatcher arm at PlayerConnection.cpp:511 got mis-routed " +
                $"(swap with HandleAvatarEmote → ChatStream over-reads our 6B payload), " +
                $"the m_Equip init loop at PlayerClass.cpp:387 had its `<20` bound shortened " +
                $"(slot 15 uninitialised → NULL-deref in ManualActivate), " +
                $"the Equipable::ManualActivate guard at Equipable.cpp:551 was reordered " +
                $"(NULL-deref via GetItemTemplateID() before pointer check), " +
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
