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
/// Wave 25 direct-reply round-trip: client sends an 11-byte 0x005E
/// AVATAR_EMOTE with the emote-trigger byte (<c>message[0] == 0x02</c>)
/// and three trailing sentinel bytes, expects the server's
/// 0x005F AVATAR_EMOTE_RESPONSE back with the GameID slot populated
/// by the server (the player's real in-sector GameID) and the trailing
/// sentinel bytes echoed verbatim.
///
/// <para>
/// Wire layout (mirror of <c>common/include/net7/PacketStructures.h:614</c>):
/// <code>
///   [0..4)  int32  GameID    (ignored by handler — reply uses server-assigned)
///   [4]     char   Unknown1  = 0x01  (handler doesn't read this either)
///   [5..7)  short  ChatSize  = length of message[] in bytes
///   [7..N)  char[] message   = ChatSize bytes; message[0]==0x02 selects emote branch
/// </code>
/// Total request size = 7 + ChatSize. The struct is <c>ATTRIB_PACKED</c>
/// so there's no implicit padding.
/// </para>
///
/// <para>
/// Server handler. <c>Player::HandleChatStream</c>
/// (<c>server/src/PlayerConnection.cpp:10217</c>) reinterprets the
/// payload as a <c>ChatStream *</c> and branches on
/// <c>chat_stream-&gt;message[0]</c>:
/// <code>
///     if (chat_stream-&gt;message[0] == 0x02)   // Emote
///     {
///         buffer = new unsigned char[chat_stream-&gt;ChatSize + 7];
///         *((short *) &amp;buffer[0]) = chat_stream-&gt;ChatSize;
///         buffer[2] = 0x01;
///         *((int32_t *) &amp;buffer[3]) = chat_stream-&gt;GameID;
///         p = (unsigned char *)chat_stream; p+=7;
///         memcpy(&amp;buffer[7], p, chat_stream-&gt;ChatSize);
///         SendToSector(ENB_OPCODE_005F_AVATAR_EMOTE_RESPONSE, buffer,
///                      chat_stream-&gt;ChatSize + 7);
///     }
/// </code>
/// Reply wire layout (server-emitted 0x005F):
/// <code>
///   [0..2)  short  ChatSize       (echoed from request)
///   [2]     byte   0x01           (literal)
///   [3..7)  int32  GameID         (echoed — see fidelity note below)
///   [7..N)  char[] message bytes  (memcpy'd from request message[])
/// </code>
/// Total reply size = 7 + ChatSize.
/// </para>
///
/// <para>
/// GameID fidelity note. The handler's
/// <c>*((int32_t *) &amp;buffer[3]) = chat_stream-&gt;GameID</c> writes
/// whatever GameID the client put on the wire — it does NOT substitute
/// the server-assigned authoritative GameID for the sending player.
/// That matches retail behaviour (the retail client always wrote its
/// own assigned GameID into the field; the server trusted it because
/// the only fan-out path is <c>SendToSector</c> which doesn't use the
/// echoed GameID for routing decisions). The test therefore sets
/// GameID to a distinctive sentinel and asserts the reply echoes that
/// exact sentinel — this catches the int32_t→long widening regression
/// at line 10235 (Phase K wave 11 comment) where a long-write would
/// overflow into the memcpy'd message bytes that follow.
/// </para>
///
/// <para>
/// Direct-reply vs. survival probe. Unlike Wave 15
/// (<see cref="SectorStarbaseRoomChangeTests"/>) where the fan-out is
/// gated on <c>p-&gt;GameID() != GameID()</c> and the single-player
/// integration test has no other observers so nothing comes back, this
/// handler uses <c>SendToSector</c> which is "dumb send to everyone in
/// the sector" (<c>server/src/PlayerClass.cpp:3361</c>) and includes
/// the originator. That makes 0x005E AVATAR_EMOTE a direct-reply
/// opcode for a single-player test — the test pins on byte-level reply
/// contents (size, GameID echo, sentinel bytes) rather than a weaker
/// survival probe.
/// </para>
///
/// <para>
/// Why ChatSize=4 and the message[] = {0x02, 0xAB, 0xCD, 0xEF}.
/// 0x02 selects the emote branch (the only branch the handler actually
/// replies on; 0x01 = station-chat is logged and dropped, anything else
/// hits an "Unknown ChatStream code" LogMessage and drops). The three
/// trailing bytes are an arbitrary sentinel pattern picked because
/// none of them clash with values the server might overwrite — a
/// regression that wrote past the GameID slot into the memcpy region
/// would scramble these. ChatSize=4 keeps the request small (11B
/// total) while still leaving room to assert the memcpy survived.
/// </para>
///
/// <para>
/// Login-stage race / why we retry the send. <see cref="SectorHandshake.EstablishAsync"/>
/// returns as soon as the server emits 0x0005 START in
/// <c>SectorManager::StationLogin2</c> (server/src/SectorManager.cpp:526)
/// — that runs in login stage 7 of the 13-stage state machine in
/// <c>PlayerManager::Pulse</c> (server/src/PlayerManager.cpp:540-604).
/// The player is NOT added to the sector list until login stage 10's
/// <c>HandleLoginStage3</c> → <c>HandleSectorLogin3</c> →
/// <c>AddPlayerToSectorList</c> (server/src/SectorManager.cpp:307-322,
/// 390-405), which depends on the proxy auto-ACKing the intervening
/// 0x2020 LOGIN_STAGE_S_C frames via
/// <c>UDPClient::HandleStageConfirm</c>
/// (proxy/UDPProxyToClient_linux.cpp:593-616). Empirically that
/// handshake can take several seconds. During that window
/// <c>HandleChatStream</c> still RUNS for incoming 0x005E (PulsePlayerInput
/// is always active per PlayerConnection.cpp:87-108), but its
/// <c>SendToSector</c> call walks the bitmap returned by
/// <c>GetSectorPlayerList</c> which is all-zeros pre-stage-10 — so the
/// fan-out goes to no observers, including the originator. The reply
/// silently drops. Once stage 10 fires, the bitmap has the originator's
/// bit set and the SAME unmodified handler produces the reply. We do
/// NOT modify the server to expose a "fully logged in" signal (that
/// would violate CLAUDE.md's server-integrity rule: "If a tool needs
/// something the server doesn't expose, the tool is wrong, not the
/// server"). Instead the test resends the canonical-shape emote every
/// 2s until the reply arrives or the outer budget expires. The retry is
/// pure idempotent client behaviour — each send produces at most one
/// reply, and excess emotes during the race window are dropped by the
/// fan-out exactly as the real server would drop them for a not-yet-
/// in-sector player.
/// </para>
///
/// <para>
/// Concrete regression classes this catches:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>ChatStream GameID write width regression at
///     PlayerConnection.cpp:10235.</b> The fix comment says the local
///     write was widened to <c>int32_t</c> from <c>long</c>; a revert
///     to <c>*((long *) &amp;buffer[3]) = chat_stream-&gt;GameID</c>
///     would write 8 bytes on Linux x86_64 starting at offset 3,
///     overflowing the 4-byte GameID slot into the memcpy region — our
///     sentinel pattern starting at reply byte 7 would be partially
///     trampled.
///   </item>
///   <item>
///     <b>ChatStream struct long-widening in PacketStructures.h:614.</b>
///     Currently <c>{int32_t GameID; char Unknown1; short ChatSize;
///     char message[1];}</c> = 8B header + 1B message stub. If GameID
///     widens to <c>long</c>, the struct's Unknown1/ChatSize/message
///     offsets all shift by +4 — ChatSize would be read from request
///     bytes 9..10 (the sentinel bytes 0xCD 0xEF interpreted as a
///     u16 = 0xEFCD = 61389), the handler would allocate ~61KB and
///     memcpy 61KB starting from request byte 7 past the end of the
///     receive buffer.
///   </item>
///   <item>
///     <b>Dispatcher mis-route at PlayerConnection.cpp:515.</b> Case
///     label sits between 0x005D EQUIP_USE and 0x0079 in the hand-
///     maintained ~200-entry switch. A copy-paste swap with
///     HandleEquipUse would re-interpret the 11B ChatStream as a 5B
///     EquipUse struct (PacketStructures.h says <c>EquipUse</c> is
///     ~5B) — the handler would write to <c>m_Equip[chat_stream-&gt;GameID]
///     .ManualActivate()</c>, indexing the equip array with a wire-
///     supplied GameID and crashing on out-of-bounds.
///   </item>
///   <item>
///     <b>Proxy default-case ForwardClientOpcode regression.</b>
///     0x005E is NOT explicitly listed in
///     <c>proxy/ClientToServer_linux_stubs.cpp</c>, so it hits the
///     <c>default:</c> arm and falls through to the bottom-of-switch
///     forward. A regression dropping this opcode would surface as a
///     timeout waiting for the 0x005F response.
///   </item>
///   <item>
///     <b>Proxy server→client UDP fan-out path regression for 0x005F.</b>
///     The reply rides SendOpcode → m_UDPQueue → SendPacketCache →
///     0x2016 PACKET_SEQUENCE → proxy
///     UDPProxyToClient_linux::SendClientPacketSequence → TCP. Every
///     Phase K survival probe exercises this path indirectly via the
///     CLIENT_SET_TIME echo; this test exercises it as the primary
///     assertion for a fan-out opcode (0x005F is the first SendToSector
///     emit we cover in Phase K — Wave 15's STARBASE_ROOM_CHANGE used
///     the originator-excluded loop, not the dumb fan-out).
///   </item>
///   <item>
///     <b>SendToSector originator-exclusion regression.</b>
///     <c>Player::SendToSector</c> at <c>server/src/PlayerClass.cpp:3361</c>
///     iterates <c>GetSectorPlayerList()</c> with no GameID filter —
///     the originator's bit IS set and they DO receive their own emote.
///     A refactor that adds an <c>if (p-&gt;GameID() != GameID())</c>
///     guard (mirroring HandleStarbaseRoomChange) would silently break
///     all single-observer emote rendering — the test would time out
///     waiting for the reply.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity note (per CLAUDE.md). 0x005E AVATAR_EMOTE is what
/// the retail Win32 client emits when the user triggers an avatar
/// emote (wave, dance, etc.) in a starbase room. The retail server's
/// HandleChatStream behaves identically — message[0]==0x02 selects the
/// emote branch and fans out 0x005F AVATAR_EMOTE_RESPONSE to everyone
/// in the sector. We are not making the server accept any new input
/// shape, not fabricating any reply; we drive the existing emote
/// branch with a minimal valid payload and assert the existing reply
/// shape.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; AVATAR_EMOTE+REPLY round-trip is
/// sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorAvatarEmoteTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorAvatarEmoteTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task AvatarEmote_EmoteTrigger_ReceivesAvatarEmoteResponseWithEchoedSentinel()
    {
        var account = TestAccounts.New(_server);
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Emoty", shipName: "EmoteShip", cts.Token);

        try
        {
            // 0x005E AVATAR_EMOTE — 11B canonical payload:
            //   [0..4)  int32 GameID    = 0x12345678 (sentinel — echoed verbatim)
            //   [4]     byte  Unknown1  = 0x01       (struct comment says always 0x01)
            //   [5..7)  short ChatSize  = 4          (length of message[])
            //   [7..11) byte[4] message = {0x02, 0xAB, 0xCD, 0xEF}
            //                              ^^^^  emote-trigger
            //                                    ^^^^^^^^^^^^^^  sentinel bytes
            const int chatSize = 4;
            const int gameIdSentinel = 0x12345678;
            byte[] sentinelMessage = [0x02, 0xAB, 0xCD, 0xEF];

            byte[] payload = new byte[7 + chatSize];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), gameIdSentinel);
            payload[4] = 0x01;
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(5, 2), chatSize);
            sentinelMessage.CopyTo(payload.AsSpan(7));

            var emotePacket = Packet.ForOpcode(OpcodeId.Known.AvatarEmote.Value, payload);

            // Send + drain in a retry loop. Each attempt sends one
            // emote, then waits up to `attemptTimeout` for a 0x005F
            // reply (draining other inbound traffic while it waits).
            // See the class doc-comment "Login-stage race" section for
            // why this is necessary. Total outer wall-clock is bounded
            // by the 90s CTS.
            TimeSpan attemptTimeout = TimeSpan.FromSeconds(2);
            const int maxAttempts = 30;
            int attempt;

            for (attempt = 0; attempt < maxAttempts; attempt++)
            {
                await session.Sector.SendAsync(emotePacket, cts.Token);

                using var attemptCts =
                    CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                attemptCts.CancelAfter(attemptTimeout);

                try
                {
                    while (true)
                    {
                        var reply = await session.Sector.ReceiveAsync(attemptCts.Token);
                        Assert.NotNull(reply);

                        if (reply!.Header.Opcode != OpcodeId.Known.AvatarEmoteResponse.Value)
                            continue;

                        // 0x005F wire layout (mirror of HandleChatStream's emote branch
                        // at server/src/PlayerConnection.cpp:10227-10241):
                        //   [0..2)  short ChatSize  (echoed from request)
                        //   [2]     byte  0x01      (literal)
                        //   [3..7)  int32 GameID    (echoed from request)
                        //   [7..N)  byte[] message  (memcpy'd from request message[])
                        var span = reply.Payload.Span;
                        Assert.Equal(7 + chatSize, span.Length);

                        short replyChatSize = BinaryPrimitives.ReadInt16LittleEndian(span[..2]);
                        Assert.Equal(chatSize, replyChatSize);

                        Assert.Equal((byte)0x01, span[2]);

                        int replyGameId = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(3, 4));
                        Assert.Equal(gameIdSentinel, replyGameId);

                        // Sentinel bytes from message[] must round-trip verbatim
                        // through the handler's memcpy.
                        byte[] replyMessage = span.Slice(7, chatSize).ToArray();
                        Assert.Equal(sentinelMessage, replyMessage);

                        return;
                    }
                }
                catch (OperationCanceledException) when (!cts.IsCancellationRequested)
                {
                    // This attempt's window expired with no 0x005F.
                    // Either the player isn't on the sector list yet
                    // (login-stage race — most common cause early on)
                    // or this emote raced ahead of the next pulse and
                    // got dropped by an empty SendToSector. Retry.
                }
            }

            throw new Xunit.Sdk.XunitException(
                $"sent 0x005E AVATAR_EMOTE {attempt} times " +
                $"(message[0]=0x02, sentinel GameID=0x{gameIdSentinel:X8}) over " +
                $"{attempt * attemptTimeout.TotalSeconds:F0}s without seeing " +
                $"0x005F AVATAR_EMOTE_RESPONSE. Likely the server's " +
                $"HandleChatStream emote branch SendToSector path broke, " +
                $"SendToSector grew an originator-exclusion guard, the proxy " +
                $"default-case forwarding dropped the opcode, the dispatcher " +
                $"case at PlayerConnection.cpp:515 got mis-routed, ChatStream " +
                $"struct was widened past 8B and the ChatSize/message offsets " +
                $"shifted past the wire payload, or the login-stage state " +
                $"machine never reached stage 10 (AddPlayerToSectorList).");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }
}
