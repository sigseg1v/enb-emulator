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
/// Wave 51 post-handshake survival round-trip: client sends 0x2017
/// RESEND_PACKET_SEQUENCE with <c>packet_num=0xFFFF</c> — a value
/// that cannot be present in the server's per-player
/// <c>m_ResendQueue</c> on a freshly-logged-in character (the queue
/// is ctor-initialised to <c>{packet_num=0, data=0, message=0,
/// length=0}</c> for every slot). The
/// <c>Player::ReSendOpcodes</c> handler at
/// <c>server/src/PlayerConnection.cpp:263-291</c> walks the queue
/// looking for the requested packet_num, finds no match, and
/// returns silently — no <c>SendOpcode</c>, no
/// <c>m_UDPConnection-&gt;SendOpcode</c>, no state mutation visible
/// over the wire.
///
/// <para>
/// Wire layout (host-LE, derived from <c>ReSendOpcodes</c> body):
/// </para>
/// <code>
///   [0..2)  short packet_num   = 0xFFFF (sign-extends to -1)
///   [2..4)  short pad          = 0     (handler skips these two bytes)
///   [4..6)  short opcode_count = 0     (read but never used in miss path)
/// </code>
/// <para>
/// Note the 2-byte gap baked into the wire shape: the handler reads
/// <c>data[0]</c> as the first short and <c>data[4]</c> as the
/// second — bytes <c>data[2..4]</c> are intentionally skipped. This
/// is the retail wire shape (the field at offset 2 is
/// architecture-padding that the client emits matching the original
/// 4B-aligned struct layout); the handler doesn't field-decode it.
/// 6B total payload is the minimum to cover both reads safely.
/// </para>
///
/// <para>
/// Server handler walk-through:
/// </para>
/// <list type="number">
///   <item>Dispatcher case at
///     <c>server/src/PlayerConnection.cpp:621-623</c>:
///     <c>HandleSectorServerOpcode</c> case label
///     <c>ENB_OPCODE_2017_RESEND_PACKET_SEQUENCE</c> →
///     <c>ReSendOpcodes(data)</c>.</item>
///   <item><c>ReSendOpcodes</c> at
///     <c>server/src/PlayerConnection.cpp:263-291</c>: reads short
///     at offset 0 into <c>long packet_num</c> (sign-extended
///     to -1 for our 0xFFFF), reads short at offset 4 into
///     <c>long opcode_count</c> (0 for our payload), and emits one
///     <c>LogMessage("Opcode re-send #%x ...")</c> line to server
///     stdout — never on the wire.</item>
///   <item><c>if (m_UDPConnection)</c> guard at line 272 is
///     non-null for a logged-in sector character (the UDP plane
///     came up during the STAGE handshake), so the body
///     runs.</item>
///   <item>The for-loop walks
///     <c>m_ResendQueue[0..RESEND_ELEMENTS)</c> looking for a slot
///     with <c>packet_num == -1</c>. The ctor initialisation in
///     <c>Player::Player</c> sets every slot's <c>packet_num</c> to
///     0; no recent send has populated <c>-1</c>; the loop runs to
///     completion without taking the <c>break</c> branch.</item>
///   <item>Function returns. Zero <c>SendOpcode</c>, zero
///     <c>m_UDPConnection-&gt;SendOpcode</c>, zero state mutation
///     visible to the client.</item>
/// </list>
///
/// <para>
/// Why this wave target (and why NOT 0x008D INCAPACITANCE_REQUEST).
/// After Wave 50 closed 0x0028 INVENTORY_SORT's default arm, the
/// dispatch-list candidates surveyed for Wave 51 were 0x0082
/// RECUSTOMIZE_SHIP_DONE (SaveDatabase mutation — too heavy), 0x0084
/// RECUSTOMIZE_AVATAR_DONE (same), 0x008D INCAPACITANCE_REQUEST
/// (ABANDONED — Wave 29 commit <c>c13fbed</c> documented the
/// first-time <c>m_IncapAvatarSent</c> branch at
/// <c>PlayerConnection.cpp:11069-11104</c> deterministically crashes
/// the server via shared NPC template state mutation), 0x0098
/// GALAXY_MAP_REQUEST (emits 0x2011 which the proxy guard at
/// <c>UDPProxyToClient_linux.cpp:568</c> blocks: opcode &gt; 0x0FFF
/// terminates the inner-tuple loop), 0x009B WARP (state-mutating),
/// 0x00BC CTA_REQUEST (sizeof(long) stack-write bug at
/// <c>PlayerConnection.cpp:7740-7741</c>), 0x00C5
/// GUILD_LEADER_ACCEPT_CLIENT (unbounded strncpy at
/// <c>PlayerGuild.cpp:746-751</c>), and 0x3004 PLAYER_SHIP_SENT
/// (FinishLogin re-entry on an already-logged-in player). 0x2017
/// RESEND_PACKET_SEQUENCE survives every concern: pure read of
/// m_ResendQueue, no state mutation, no SendOpcode, no SEGV class,
/// no proxy-layer block.
/// </para>
///
/// <para>
/// Survival probe vs. direct reply. The miss-case emits ZERO
/// observable wire frames — only a server-side LogMessage call.
/// Even a HIT case would fire <c>m_UDPConnection-&gt;SendOpcode</c>
/// (the UDP-direct path) which our TCP-only test client doesn't
/// observe. So this is a pure survival probe (send
/// RESEND_PACKET_SEQUENCE, then REQUEST_TIME, assert
/// CLIENT_SET_TIME echoes our sentinel tick) rather than the
/// stronger direct-reply pattern.
/// </para>
///
/// <para>
/// Why packet_num=0xFFFF specifically. Any value not in
/// <c>m_ResendQueue</c> works for the miss-case; 0xFFFF is
/// canonical because it sign-extends to -1 on both Win32
/// (sizeof(long)==4) and Linux x86_64 (sizeof(long)==8), and -1 is
/// the "no such packet" sentinel that the client itself uses when
/// it has no specific frame to request. The queue's slots are
/// all <c>{packet_num=0, ...}</c> at session start so even 0xFFFE,
/// 0x0001, 0x0002 etc. would also miss; 0xFFFF is the standard
/// retail wire-format "definitely not in the queue" value.
/// </para>
///
/// <para>
/// Concrete regression classes this catches:
/// </para>
/// <list type="bullet">
///   <item><b>Dispatcher mis-route at
///     server/src/PlayerConnection.cpp:621.</b> The 0x2017 case
///     label sits between 0x00D4 GUILD_RANK_NAMES_REQUEST_CLIENT
///     (line 617) and 0x3004 PLAYER_SHIP_SENT (line 625). A
///     copy-paste swap with HandleGuildRankNamesRequestClient at
///     line 617 would mis-interpret our 6B payload as a
///     <c>GuildRankNamesRequestPacket {long gameid; short
///     unknown}</c> and walk the
///     <c>g_PlayerMgr-&gt;GuildFromId(m_GuildID)</c> path which
///     short-circuits on <c>m_GuildID == 0</c> for a fresh char
///     (still survives via REQUEST_TIME but wrong code arm). A
///     swap with the 0x3004 handler would invoke
///     <c>SetNavCommence + FinishLogin(true)</c> on an
///     already-logged-in player — could double-emit
///     StartAck/SetCredits/SaveDatabase frames.</item>
///   <item><b>RESEND_ELEMENTS array-bounds regression at
///     server/src/PlayerClass.h.</b> Currently sized to hold a
///     small window of recently-sent UDP packets; growing the
///     constant doesn't affect this test (zero-match case is
///     loop-iteration-count-agnostic), but shrinking it to 0
///     would short-circuit the for-loop body — still survives via
///     REQUEST_TIME, no behavioural change observable on this
///     payload.</item>
///   <item><b>sizeof(long)/sizeof(short) bug class at
///     PlayerConnection.cpp:265-266.</b> Both reads use
///     <c>*((short*) &amp;data[N])</c> — short is 16-bit on both
///     Win32 and Linux x86_64 so the load is endian/width-stable,
///     but the sign-extension to long differs between platforms
///     (Win32 sizeof(long)==4, Linux x86_64 sizeof(long)==8). For
///     0xFFFF, Win32 reads packet_num=0xFFFFFFFF=-1 (4B
///     sign-extended) and Linux x86_64 reads
///     packet_num=0xFFFFFFFFFFFFFFFF=-1 (8B sign-extended). The
///     comparison at line 276 against the queue's <c>long</c>
///     field has the same sign-extension semantic on each host,
///     so the miss-case behaves identically. A regression that
///     switched the dest type to unsigned long without flipping
///     the source cast would mis-compare 0xFFFF but the queue
///     has no matching slot anyway → no observable wire change.
///     Documented as a known limitation.</item>
///   <item><b>SendOpcode header-width revert at
///     PlayerConnection.cpp:127.</b> Phase K sizeof(int32_t)
///     opcode-header fix keeps the per-client UDP queue header
///     at 4B; a revert corrupts the 0x2016 inner-tuple parser →
///     REQUEST_TIME survival probe silent. Same shape as
///     Waves 11/27/29/30/36/37/38/39/40/41/50.</item>
///   <item><b>Proxy default-case ForwardClientOpcode regression
///     for 0x2017.</b> 0x2017 is NOT explicitly listed in
///     <c>proxy/ClientToServer_linux_stubs.cpp</c>'s
///     ProcessSectorServerOpcode switch (verified by grep — the
///     0x2017 references inside <c>proxy/UDPProxyToClient_linux.cpp</c>
///     are HandleStageConfirm-adjacent on the
///     SERVER→CLIENT inbound path, not the client→server
///     outbound path). It falls through to the bottom-of-switch
///     ForwardClientOpcode default arm; a regression dropping
///     that default would mean the server never sees the
///     RESEND_PACKET_SEQUENCE frame (and would also break
///     REQUEST_TIME which rides the same default arm — surfaces
///     as timeout).</item>
///   <item><b>Proxy SendClientPacketSequence inner-opcode guard
///     tightening at UDPProxyToClient_linux.cpp:568.</b>
///     Currently passes 0x0034 CLIENT_SET_TIME because
///     0x0034 &lt; 0x0FFF; a tightening would break the
///     REQUEST_TIME echo.</item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). 0x2017
/// RESEND_PACKET_SEQUENCE is what the retail Win32 client emits
/// when it detects a UDP packet drop and wants the server to
/// retransmit a specific 0x2016 PACKET_SEQUENCE frame. Sending it
/// with a packet_num the server hasn't queued is a legal but
/// server-side no-op (the for-loop walks the queue, finds
/// nothing, returns). Zero permissiveness added; not loosening
/// any security posture (the queue lookup is strictly defensive);
/// not fabricating any reply (the handler emits none on miss).
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; RESEND_PACKET_SEQUENCE +
/// REQUEST_TIME round-trip is sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorResendPacketSequenceTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorResendPacketSequenceTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task ResendPacketSequence_MissPacketNum_DoesNotBreakConnection_RequestTimeStillRoundTrips()
    {
        var account = TestAccounts.For();
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
            // 0x2017 RESEND_PACKET_SEQUENCE (ReSendOpcodes) —
            // 6B payload, all fields host-LE:
            //   [0..2)  short packet_num   = 0xFFFF (sign-extends to -1
            //                                 long on both Win32 and
            //                                 Linux x86_64 — guaranteed
            //                                 miss against m_ResendQueue
            //                                 whose slots ctor-init to
            //                                 packet_num=0)
            //   [2..4)  short pad          = 0     (handler skips —
            //                                 reads next short at
            //                                 offset 4, not 2)
            //   [4..6)  short opcode_count = 0     (read into local
            //                                 `long opcode_count` but
            //                                 never field-accessed in
            //                                 the miss path)
            byte[] payload = new byte[6];
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(0, 2), unchecked((short)0xFFFF));
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(2, 2), 0);
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(4, 2), 0);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.ResendPacketSequence.Value, payload),
                cts.Token);

            // Survival probe: did the connection survive the
            // RESEND_PACKET_SEQUENCE miss-path handler? Send
            // REQUEST_TIME and assert CLIENT_SET_TIME echoes our
            // sentinel tick.
            int clientTick = unchecked((int)(DateTime.UtcNow.Ticks & 0x7FFFFFFF));

            byte[] reqTimePayload = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(reqTimePayload, clientTick);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.RequestTime.Value, reqTimePayload),
                cts.Token);

            // Drain until 0x0034 CLIENT_SET_TIME. Tolerate any
            // interleaved positional-update frames from in-sector
            // observers. Miss-path RESEND_PACKET_SEQUENCE emits NO
            // reply frame itself (only a server stdout LogMessage).
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
                $"drained {maxFrames} frames after sending 0x2017 RESEND_PACKET_SEQUENCE + 0x0044 REQUEST_TIME " +
                $"without seeing 0x0034 CLIENT_SET_TIME. " +
                $"Likely the dispatcher arm at PlayerConnection.cpp:621 got mis-routed, " +
                $"the ReSendOpcodes for-loop's `break` semantics changed (a regression that took the break on every slot would still survive via REQUEST_TIME), " +
                $"the proxy's bottom-of-switch ForwardClientOpcode default at proxy/ClientToServer_linux_stubs.cpp dropped 0x2017, " +
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
