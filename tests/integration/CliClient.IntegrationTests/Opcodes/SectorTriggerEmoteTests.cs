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
/// Wave 33 direct-reply round-trip: client sends an 8-byte 0x00A1
/// TRIGGER_EMOTE on the sector connection; expects the server's
/// unconditional 0x00A2 NOTIFY_EMOTE reply with the request's Emote
/// field echoed verbatim.
///
/// <para>
/// Wire layout (mirror of
/// <c>common/include/net7/PacketStructures.h:589-599</c>):
/// <code>
///   TriggerEmote (request, 8 bytes):
///     [0..4)   int32 GameID  = 0          (handler reads but only echoes)
///     [4..8)   int32 Emote   = 0x4D454D45 (sentinel — int32 LE)
///
///   NotifyEmote (reply, 8 bytes):
///     [0..4)   int32 GameID  = (echoed from request)
///     [4..8)   int32 Emote   = (echoed from request — verifies field-copy)
/// </code>
/// Both structs are <c>ATTRIB_PACKED</c>, no implicit padding. They have
/// identical layouts (2× int32_t), which is why <c>SendNotifyEmote</c>
/// can take the two fields as separate parameters and rebuild the reply
/// from them.
/// </para>
///
/// <para>
/// Server handler. The dispatcher case at
/// <c>server/src/PlayerConnection.cpp:583</c> calls
/// <c>HandleTriggerEmote(data)</c>. The handler at line 10209 casts the
/// data pointer to <c>TriggerEmote*</c> and unconditionally calls
/// <c>SendNotifyEmote(emote-&gt;GameID, emote-&gt;Emote)</c>. There is
/// no payload-selected branch — the handler is the same shape for every
/// emote.
/// </para>
///
/// <para>
/// SendNotifyEmote emit path
/// (<c>server/src/PlayerConnection.cpp:10379-10386</c>). Builds a
/// <c>NotifyEmote response</c> with <c>response.GameID = game_id</c> and
/// <c>response.Emote = emote</c>, then calls
/// <c>SendToRangeList(ENB_OPCODE_00A2_NOTIFY_EMOTE, &amp;response,
/// sizeof(response))</c>. <c>SendToRangeList</c>
/// (<c>server/src/PlayerClass.cpp:3325-3347</c>) walks <c>m_RangeList</c>
/// via <c>g_PlayerMgr-&gt;GetNextPlayerOnList</c> and calls
/// <c>p-&gt;SendOpcode(0x00A2, &amp;response, 8)</c> for each <c>p</c>.
/// </para>
///
/// <para>
/// Why the originator receives its own emote (the round-trip
/// pre-condition). <c>m_RangeList</c> is a per-player visibility bitmap
/// of "Players who can see this object" (per the comment at
/// <c>ObjectClass.h:429</c>). <c>SectorLogin</c>
/// (<c>server/src/PlayerClass.cpp:467</c>) explicitly seeds the
/// originator's own slot into <c>m_RangeList</c> via
/// <c>AddPlayerToRangeList(this); //add ourselves to the range list -
/// we're always in range of ourselves</c>. The single-player test
/// therefore receives the 0x00A2 NOTIFY_EMOTE back at itself, even
/// though no other players exist in the sector — the same self-receive
/// pattern that lets Wave 16's AVATAR_EMOTE round-trip via
/// SendToSector. The <c>SendToRangeList</c> loop body has no
/// <c>p != this</c> guard (cf. <c>SendToVisibilityList</c> which does),
/// so the originator's <c>SendOpcode</c> fires unconditionally.
/// </para>
///
/// <para>
/// Reply assertion. Three checks pin three independent invariants:
/// <list type="number">
///   <item>
///     <b>Opcode == 0x00A2</b> — proves the dispatcher case at
///     PlayerConnection.cpp:583 routed to HandleTriggerEmote and that
///     SendNotifyEmote emits the right opcode constant.
///   </item>
///   <item>
///     <b>Payload length == 8</b> — proves NotifyEmote stayed at
///     2× int32_t (the Phase R canonical wire size). A
///     long-revert on either field would balloon the struct on
///     Linux x86_64 and break the assertion.
///   </item>
///   <item>
///     <b>body[4..7] == request Emote sentinel</b> — proves
///     SendNotifyEmote's field-copy
///     (<c>response.Emote = emote</c>) didn't lose or transform
///     the value. A non-zero sentinel (0x4D454D45 = "EMEM" in
///     little-endian bytes) is chosen so a zero-defaulted struct
///     would be visibly wrong; the sentinel is also distinct from
///     any retail-meaningful emote ID.
///   </item>
/// </list>
/// </para>
///
/// <para>
/// Why this wave target. 0x00A1 TRIGGER_EMOTE is +2 ratchet (both
/// 0x00A1 and 0x00A2 previously uncovered), the handler is a one-liner
/// with no payload-selected branch, the reply is unconditional (no
/// guards beyond the m_RangeList iteration), and the wire shape is the
/// minimal 8B canonical fixed-length struct. Compared to other
/// remaining candidates:
/// <list type="bullet">
///   <item>0x008D INCAPACITANCE_REQUEST: SEGVs the server (Wave 29
///         abandoned).</item>
///   <item>0x00BC CTA_REQUEST: sizeof(long) stack-clobber.</item>
///   <item>0x0098 GALAXY_MAP_REQUEST: reply 0x2011 dropped by proxy
///         0x0FFF guard at UDPProxyToClient_linux.cpp:568.</item>
///   <item>Manufacture / Guild handlers (0x0079-0x0080, 0x00C5/C9/CD):
///         declared but not defined in any .cpp.</item>
///   <item>0x009B WARP, 0x00C0 CONFIRMED_ACTION_RESPONSE,
///         0x009D STARBASE_AVATAR_CHANGE (filters self),
///         0x0051 SKILL_STRING_RQ (needs HUSK target),
///         0x0087 MISSION_DISMISSAL (MissionID=-1 fails guard):
///         no direct reply on fresh-char path.</item>
///   <item>0x0082 RECUSTOMIZE_SHIP_DONE / 0x0084
///         RECUSTOMIZE_AVATAR_DONE: mutate m_Database, SaveDatabase —
///         risky state mutation.</item>
/// </list>
/// </para>
///
/// <para>
/// Regression classes this catches.
/// </para>
/// <list type="bullet">
///   <item>
///     <b>PacketStructures.h TriggerEmote/NotifyEmote long-revert at
///     lines 589-599.</b> Currently both are 2× int32_t = 8B canonical.
///     Widening either field on Linux x86_64 grows the struct and the
///     Emote field either reads past the end of the 8B payload into
///     stack garbage (request side) or writes past the end of the 8B
///     receive-buffer expectation (reply side). Pinning reply payload
///     length to exactly 8 catches both directions.
///   </item>
///   <item>
///     <b>Dispatcher mis-route at PlayerConnection.cpp:583.</b> The
///     case label sits between 0x00A0 STARBASE_ROOM_CHANGE_S_C and
///     0x00A3 CLIENT_CHAT_REQUEST. A copy-paste swap with
///     HandleClientChatRequest would route 8B into that handler's
///     variable-length string walker which expects at least 18B —
///     reads past end of 8B payload into garbage, switches on a
///     garbage type byte, and either silently returns (default branch)
///     or hits an unrelated case that emits 0x00A5 instead of 0x00A2
///     (test times out OR sees wrong opcode).
///   </item>
///   <item>
///     <b>SectorLogin self-add removal at PlayerClass.cpp:467.</b> If
///     <c>AddPlayerToRangeList(this)</c> is removed or guarded
///     incorrectly, <c>m_RangeList</c> is empty for a single-player
///     and the <c>SendToRangeList</c> loop never executes. The reply
///     never emits, the test times out. This is also a regression
///     that would break Wave 16's AVATAR_EMOTE self-receive — both
///     tests would surface it.
///   </item>
///   <item>
///     <b>SendNotifyEmote field-copy regression at lines 10381-10383.</b>
///     If <c>response.Emote = emote</c> were hardcoded to a literal
///     (e.g. always 0) or read from <c>this-&gt;Emote</c> instead of
///     the parameter, the sentinel echo would fail. The 0x4D454D45
///     sentinel is distinct enough from zero / retail emote IDs that
///     any such regression surfaces immediately.
///   </item>
///   <item>
///     <b>Proxy default-case ForwardClientOpcode dropping 0x00A1.</b>
///     0x00A1 is NOT explicitly cased in
///     proxy/ClientToServer_linux_stubs.cpp so it falls through to
///     the bottom-of-switch default-case forward. A regression that
///     re-introduced opcode whitelisting or that broke the default-
///     case forward would stop the server from ever receiving 0x00A1
///     → no 0x00A2 reply → test times out.
///   </item>
///   <item>
///     <b>Proxy SendClientPacketSequence guard at
///     UDPProxyToClient_linux.cpp:568.</b> Currently gates inner
///     opcodes on <c>opcode &gt; 0x0000 &amp;&amp; opcode &lt; 0x0FFF</c>.
///     0x00A2 passes (0x00A2 &lt; 0x0FFF). A regression that tightened
///     the upper bound to e.g. &lt; 0x0080 would silently drop the
///     reply — test times out.
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at PlayerConnection.cpp:127.</b>
///     Would corrupt the 0x2016 PACKET_SEQUENCE inner-tuple parser
///     for every reply opcode, not just NotifyEmote. Caught here as
///     part of the per-client UDP queue path.
///   </item>
///   <item>
///     <b>SendToRangeList originator-exclusion regression at
///     PlayerClass.cpp:3325-3347.</b> If a future refactor adds a
///     <c>p != this</c> guard (mirroring SendToVisibilityList's
///     pattern at line 3134), the originator-receive path would
///     break for solo players — test times out.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). 0x00A1 TRIGGER_EMOTE is what
/// the retail Win32 client emits when the user triggers an emote in
/// space or starbase — two int32_t LE fields carrying the user's
/// GameID and the chosen Emote code (the same wire shape on every
/// platform; sizeof(long)==4 on Win32 made the original Win32 source
/// accidentally portable for this struct). The
/// <c>SendToRangeList(0x00A2, NotifyEmote{GameID, Emote}, 8)</c> fan-
/// out is verbatim retail server behaviour: the originator receives
/// the emote-broadcast just like any other observer in scan range
/// because there's no client-side prediction (the retail client
/// always rendered the emote on its own avatar via this round-trip).
/// We are not making the server accept any new input shape, not
/// loosening any security posture, not fabricating any reply.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; TRIGGER_EMOTE + 0x00A2 round-trip is
/// sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorTriggerEmoteTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorTriggerEmoteTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task TriggerEmote_DefaultPayload_ReceivesNotifyEmoteEchoed()
    {
        var account = TestAccounts.For();
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station
        const int emoteSentinel = 0x4D454D45;  // "EMEM" LE — distinct from any retail emote ID and from zero.

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Emoter", shipName: "EmoterShip", cts.Token);

        try
        {
            // 0x00A1 TRIGGER_EMOTE — 8B canonical layout.
            //   [0..4)   int32 GameID = 0           (handler reads but only echoes; the server's
            //                                        own resolved GameID is irrelevant to the
            //                                        reply's Emote-field round-trip).
            //   [4..8)   int32 Emote  = 0x4D454D45  (sentinel — proves SendNotifyEmote's
            //                                        field-copy didn't lose or transform the
            //                                        value).
            byte[] payload = new byte[8];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), 0);
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(4, 4), emoteSentinel);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.TriggerEmote.Value, payload),
                cts.Token);

            // Drain inbound until we see 0x00A2 NOTIFY_EMOTE with our
            // Emote sentinel echoed in body[4..7]. SectorLogin's
            // self-add to m_RangeList (PlayerClass.cpp:467) guarantees
            // the originator receives the SendToRangeList fan-out
            // even for a single-player test.
            int framesSeen = 0;
            const int maxFrames = 400;
            while (framesSeen++ < maxFrames)
            {
                var reply = await session.Sector.ReceiveAsync(cts.Token);
                Assert.NotNull(reply);

                if (reply!.Header.Opcode != OpcodeId.Known.NotifyEmote.Value)
                    continue;

                // 0x00A2 wire layout: 8 bytes total {int32 GameID;
                // int32 Emote}. Emote at offset 4 is what we pin
                // against the request's sentinel.
                Assert.Equal(8, reply.Payload.Length);
                var echoedEmote = BinaryPrimitives.ReadInt32LittleEndian(reply.Payload.Span.Slice(4, 4));
                Assert.Equal(emoteSentinel, echoedEmote);
                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x00A1 " +
                $"TRIGGER_EMOTE without seeing 0x00A2 NOTIFY_EMOTE. " +
                $"Likely causes: (a) dispatcher mis-route at " +
                $"PlayerConnection.cpp:583 (swap with " +
                $"HandleClientChatRequest reads 8B as the start of an " +
                $"18B variable-length payload, walks past end into " +
                $"garbage); (b) PacketStructures.h TriggerEmote/" +
                $"NotifyEmote long-revert (struct grows to 16B on " +
                $"Linux x86_64, Emote reads past end of 8B payload " +
                $"into stack garbage); (c) SectorLogin self-add " +
                $"removal at PlayerClass.cpp:467 (m_RangeList empty " +
                $"for solo player, SendToRangeList loop never " +
                $"executes); (d) proxy default-case ForwardClientOpcode " +
                $"dropping 0x00A1 (not explicitly cased in " +
                $"ClientToServer_linux_stubs.cpp so falls through to " +
                $"bottom-of-switch default-case forward); (e) proxy " +
                $"SendClientPacketSequence guard tightening at " +
                $"UDPProxyToClient_linux.cpp:568 (opcode < 0x0FFF " +
                $"currently passes 0x00A2); (f) SendToRangeList " +
                $"originator-exclusion regression (a future `p != " +
                $"this` guard mirroring SendToVisibilityList's " +
                $"pattern at line 3134 would skip the originator).");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }
}
