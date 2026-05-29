// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using N7.CliClient.Auth;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Outbound;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// Wave 62 indirect-stimulus +2 ratchet: drive both 0x00BE CONFIRMED_ACTION_OFFER
/// AND 0x006A CLIENT_SOUND emits through the <c>/fgps</c> slash command. The
/// test sends a 0x0033 CLIENT_CHAT whose payload string is <c>"/fgps"</c>,
/// which routes through <c>Player::HandleClientChat</c>'s slash-command branch
/// (<c>server/src/PlayerConnection.cpp:4614</c>) into <c>HandleSlashCommands</c>'s
/// single-slash block at <c>PlayerConnection.cpp:5447</c>. The case 'f' arm at
/// <c>PlayerConnection.cpp:6377-6382</c> dispatches:
/// <code>
///   else if (strcmp(pch, "fgps") == 0)
///   {
///       SendConfirmedActionOffer();
///       success = true;
///       msg_sent = true;
///   }
/// </code>
///
/// <para>
/// <c>Player::SendConfirmedActionOffer</c> (<c>PlayerConnection.cpp:861-873</c>)
/// emits TWO opcodes back-to-back on a single stimulus:
/// </para>
/// <list type="number">
///   <item>
///     <b>0x00BE CONFIRMED_ACTION_OFFER</b> — a 17-byte stack-literal payload
///     <c>{0x00,0x00,0x00,0x01, 0x00,0x00,0x00,0x65, 0x07,0x00, 'M','e','s','s','a','g','e'}</c>.
///     Wire layout: <c>[int32 BE 0x00000001][int32 BE 0x00000065=101][int16 LE 0x0007][7 chars "Message"]</c>.
///     The first int32 is in network byte order, 0x00000001 — the
///     action-offer id of "1". The second int32 is the talk-tree node id 101
///     (0x65). The 16-bit length-prefix 0x0007 is little-endian (no
///     <c>htons</c> on the literal). The 7-byte ASCII tail is "Message"
///     (NOT NUL-terminated — the length-prefix bounds the read).
///   </item>
///   <item>
///     <b>0x006A CLIENT_SOUND</b> — emitted by <c>SendClientSound("push_mission_alert_sound", 2, 0)</c>
///     at <c>PlayerConnection.cpp:872</c>. The
///     <c>SendClientSound</c> body (<c>PlayerConnection.cpp:949-968</c>)
///     constructs a packet via <c>AddData</c> calls:
///     <code>
///       long length = strlen(sound_name) + 1;  // 25
///       AddData(packet, length, index);        // 4 bytes (long specialised to int32_t via Phase K Wave 12)
///       AddDataS(packet, sound_name, index);   // 24 chars "push_mission_alert_sound" (no NUL)
///       AddData(packet, char(0), index);       // 1 byte NUL terminator
///       AddData(packet, channel, index);       // 4 bytes (channel=2, long specialised)
///       AddData(packet, queue, index);         // 1 byte (queue=0)
///     </code>
///     Wire payload is exactly 4+24+1+4+1 = 34 bytes. The <c>AddData&lt;long&gt;</c>
///     template specialisation at <c>server/src/PacketMethods.h:37-42</c> forces
///     4-byte emission via an <c>int32_t</c> cast — without that specialisation,
///     Linux x86_64's <c>sizeof(long)==8</c> would emit two 8-byte fields and
///     the wire payload would be 4+24+1+8+1=38B, mis-framing every downstream
///     0x006A consumer.
///   </item>
/// </list>
///
/// <para>
/// Slash-command dispatch. The chat carrier "/fgps" routes through
/// <c>HandleClientChat</c>'s slash check at <c>PlayerConnection.cpp:4614</c>,
/// then <c>HandleSlashCommands</c>'s single-slash block at line 5447, then
/// case 'f' at line 6280. Note case 'f' has TWO <c>if (AdminLevel() &gt;= GM)</c>
/// sub-blocks at lines 6281-6335 (GM commands like /form, /flushinv,
/// /factionset, /factionoverride) and lines 6337-6358 (more GM commands
/// /fetch, /find). The /fgps arm at line 6377 sits OUTSIDE both — it's a
/// top-level if/else-if in case 'f' available to any logged-in player. The
/// chain is: <c>if (strcmp(pch, "face")==0)</c> false, <c>else if
/// (strcmp(pch, "faceme")==0)</c> false, <c>else if (strcmp(pch, "fgps")==0)</c>
/// true → SendConfirmedActionOffer() fires.
/// </para>
///
/// <para>
/// Why /fgps stimulus rather than direct dispatch. 0x00BE is purely server-
/// emitted (its CLIENT-side response is 0x00C0 CONFIRMED_ACTION_RESPONSE, the
/// opcode that Wave 38 already covers). 0x006A CLIENT_SOUND is also purely
/// server-emitted (no client-originated cousin). Both opcodes lack any
/// client-originated stimulus path that would exercise them in a fresh-
/// character station-handshake context. <c>SendConfirmedActionOffer</c> is
/// also invoked at <c>PlayerConnection.cpp:895</c> (the
/// <c>ProcessConfirmedActionOffer</c> retry path) and at line 11084
/// (whatever drives the m_ActionResponseReceived flag), but those require
/// prior state-setup that the fresh-character harness doesn't have. The
/// /fgps slash arm is the simplest unconditional path: no AdminLevel
/// check, no group/target/inventory requirement, no DB state mutation,
/// no observer fan-out — one strcmp match and a back-to-back two-opcode
/// emit.
/// </para>
///
/// <para>
/// Why a +2 ratchet rather than two +1 waves. Both opcodes are produced by
/// the same C++ function on a single SendOpcode chain — they're physically
/// inseparable on the wire. Splitting into two waves would require two
/// separate test fixtures driving the same code path and asserting on
/// disjoint subsets of the reply stream, which doubles flake surface area
/// without adding regression coverage. Wave 54's PLANET_POSITIONAL_UPDATE/
/// NAVIGATION pair and Wave 59's CTA_REQUEST/CTA_RESPONSE pair establish
/// the +2 ratchet pattern; Wave 62 follows.
/// </para>
///
/// <para>
/// Per CLAUDE.md server-integrity: no server change. Wave 62 drives existing
/// server-emit paths with valid client stimulus. The /fgps arm, the
/// <c>SendConfirmedActionOffer</c> body, and the <c>SendClientSound</c> body
/// all behave identically pre- and post-Wave 62. Pure preservation
/// tightening — zero widened input acceptance, zero loosened gating, zero
/// debug-only opcode. The AddData&lt;long&gt; specialisation at
/// PacketMethods.h:37-42 is the Phase K Wave 12 fix that's already in
/// place; Wave 62 pins its load-bearing role in the 0x006A wire shape
/// (revert it and the 0x006A wire payload becomes 38B instead of 34B,
/// and the length-prefix at bytes [0..4] also flips from 4B to 8B,
/// corrupting every consumer).
/// </para>
///
/// <para>
/// Regression classes this catches.
/// </para>
/// <list type="bullet">
///   <item>
///     <b>0x00BE emit removal at <c>PlayerConnection.cpp:871</c>.</b> If
///     the SendOpcode is short-circuited, the 0x00BE never reaches the
///     client and the drain assertion fails.
///   </item>
///   <item>
///     <b>0x006A emit removal at <c>PlayerConnection.cpp:968</c>.</b> If
///     the SendOpcode is short-circuited, the 0x006A never reaches the
///     client.
///   </item>
///   <item>
///     <b><c>SendConfirmedActionOffer</c> body reordering at
///     <c>PlayerConnection.cpp:861-873</c>.</b> The current order is
///     0x00BE then 0x006A. The test asserts both opcodes are received but
///     does NOT assert ordering — a reorder is allowed. A regression that
///     drops one of the two SendOpcodes surfaces immediately.
///   </item>
///   <item>
///     <b>0x00BE payload-literal regression at <c>PlayerConnection.cpp:863-869</c>.</b>
///     The 17-byte hex-literal payload is asserted byte-for-byte. Any edit
///     to the literal (e.g. changing the talk-tree node id from 0x65 to
///     0x66, dropping the "Message" suffix, flipping endianness) surfaces.
///   </item>
///   <item>
///     <b><c>AddData&lt;long&gt;</c> specialisation revert at
///     <c>server/src/PacketMethods.h:37-42</c>.</b> If the specialisation is
///     removed, the generic template at line 23-28 would emit 8 bytes for
///     long fields on Linux x86_64. The 0x006A wire payload would balloon
///     to 38 bytes (4+24+1+8+1) with the length prefix at bytes [0..4]
///     reading 8-byte width — the length-prefix assertion catches both.
///   </item>
///   <item>
///     <b><c>SendClientSound</c> string-encoding regression at
///     <c>PlayerConnection.cpp:962-966</c>.</b> The current call chain is
///     <c>AddData(length)</c> + <c>AddDataS(sound_name)</c> + <c>AddData(char(0))</c>.
///     If <c>AddDataS</c> is swapped for <c>AddDataSN</c> (which would emit
///     the NUL terminator inline), the explicit <c>AddData(char(0))</c>
///     would write a second NUL and the wire payload grows by 1 byte —
///     length-prefix assertion catches.
///   </item>
///   <item>
///     <b><c>SendClientSound</c> argument-order regression at the call
///     site <c>PlayerConnection.cpp:872</c>.</b> The current order is
///     <c>("push_mission_alert_sound", 2, 0)</c>: name, channel=2, queue=0.
///     If channel and queue are swapped, the wire payload re-encodes
///     channel=0, queue=2 — the byte-for-byte channel/queue assertion catches.
///   </item>
///   <item>
///     <b>Single-slash gate regression at <c>PlayerConnection.cpp:5447</c>.</b>
///     Same as Waves 60/61. If the gate tightens to double-slash, adds an
///     AdminLevel check, or rejects Msg[1]='f', the /fgps arm never fires.
///   </item>
///   <item>
///     <b>case 'f' top-level dispatch regression at <c>PlayerConnection.cpp:6360-6388</c>.</b>
///     If the /fgps arm is moved inside one of the two AdminLevel &gt;= GM
///     blocks (lines 6281-6335 or 6337-6358), it would still pass for
///     cli_test* (status=100=ADMIN) but a regression dropping AdminLevel
///     to under GM=50 on the test account would silently break the wave.
///   </item>
///   <item>
///     <b>strcmp("fgps") regression at <c>PlayerConnection.cpp:6377</c>.</b>
///     The current matcher is exact-string strcmp, NOT MatchOptWithParam.
///     A refactor to MatchOptWithParam would change the parsing shape
///     (allow optional args) but still accept "/fgps" — invisible to this
///     test. A refactor renaming the command (e.g. "/fastgps") would
///     break.
///   </item>
///   <item>
///     <b>HandleClientChat slash dispatch regression at
///     <c>PlayerConnection.cpp:4614-4617</c>.</b> Same as Waves 60/61.
///   </item>
///   <item>
///     <b>ClientChatCodec wire-format regression.</b> Same as Waves 60/61.
///   </item>
///   <item>
///     <b>Proxy <c>SendClientPacketSequence</c> inner-opcode guard at
///     <c>proxy/UDPProxyToClient_linux.cpp:568</c> tightening.</b> Both
///     0x006A and 0x00BE are &lt; 0x0FFF so they currently pass; a tighter
///     gate (e.g. &lt; 0x0050) would silently drop both emits.
///   </item>
/// </list>
///
/// <para>
/// Cleanup. <c>/fgps</c> mutates no persistent state — <c>SendConfirmedActionOffer</c>
/// emits two opcodes and returns. The /fgps arm sets <c>success = true; msg_sent = true</c>
/// and returns. Cleanup is the standard 0x00B9 → 0x00BA logoff +
/// <c>GlobalDeleteCharacter</c>, identical to Waves 55-61.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~22s; chat-with-slash round-trip sub-second; LOGOFF
/// sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorFgpsTests
{
    private const int ExpectedConfirmedActionOfferPayloadSize = 17;
    private const int ExpectedClientSoundPayloadSize = 34;
    private const int ClientSoundChannel = 2;
    private const byte ClientSoundQueue = 0;
    private const string ClientSoundName = "push_mission_alert_sound";

    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorFgpsTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task FgpsSlashCommand_OnSlashFgps_ReceivesConfirmedActionOfferAndClientSound()
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
            firstName: "Fugpsi62", shipName: "Fugpsi62Ship", cts.Token);

        try
        {
            // Build a CLIENT_CHAT with "/fgps" — the single-slash prefix
            // routes through HandleSlashCommands' single-slash block
            // (PlayerConnection.cpp:5447); the case 'f' /fgps arm at
            // PlayerConnection.cpp:6377-6382 calls SendConfirmedActionOffer
            // which emits 0x00BE CONFIRMED_ACTION_OFFER (17B literal
            // payload) followed by 0x006A CLIENT_SOUND (34B payload for
            // "push_mission_alert_sound", channel=2, queue=0). The /fgps
            // arm sits OUTSIDE both AdminLevel >= GM sub-blocks in case 'f',
            // so any logged-in player triggers it.
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/fgps");

            await session.Sector.SendAsync(
                Packet.ForOpcode(
                    OpcodeId.Known.ClientChat.Value,
                    codec.EncodeOutbound(chat)),
                cts.Token);

            // Drain up to 400 frames waiting for BOTH 0x00BE and 0x006A.
            // Don't assume ordering — assert both are seen and capture each
            // for separate byte-for-byte assertions below.
            const int maxFrames = 400;
            int seen = 0;
            Packet? confirmedActionOffer = null;
            Packet? clientSound = null;
            var observed = new List<string>();
            using var drainCts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, drainCts.Token);
            try
            {
                while (seen++ < maxFrames)
                {
                    var frame = await session.Sector.ReceiveAsync(linked.Token);
                    Assert.NotNull(frame);
                    observed.Add($"0x{frame!.Header.Opcode:X4}/{frame.Payload.Length}");

                    if (frame.Header.Opcode == OpcodeId.Known.ConfirmedActionOffer.Value
                        && confirmedActionOffer == null)
                    {
                        confirmedActionOffer = frame;
                    }
                    else if (frame.Header.Opcode == OpcodeId.Known.ClientSound.Value
                        && clientSound == null)
                    {
                        clientSound = frame;
                    }

                    if (confirmedActionOffer != null && clientSound != null)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (drainCts.IsCancellationRequested)
            {
                // drain timed out — emit observed list as failure diagnostic
            }

            if (confirmedActionOffer == null || clientSound == null)
            {
                throw new Xunit.Sdk.XunitException(
                    $"Missing 0x00BE={confirmedActionOffer != null} 0x006A={clientSound != null} " +
                    $"after {seen} frames. Observed [{observed.Count}]: {string.Join(" | ", observed)}");
            }

            // === 0x00BE CONFIRMED_ACTION_OFFER byte-for-byte assertion ===
            // Wire literal (17 bytes) from PlayerConnection.cpp:863-869:
            //   00 00 00 01    int32 BE — offer id = 1 (note BE, not LE)
            //   00 00 00 65    int32 BE — talk-tree node id = 0x65 = 101
            //   07 00          int16 LE — string length-prefix = 7
            //   4d 65 73 73 61 67 65  ASCII "Message" (no NUL)
            Assert.Equal(ExpectedConfirmedActionOfferPayloadSize, confirmedActionOffer.Payload.Length);
            var offerSpan = confirmedActionOffer.Payload.Span;
            byte[] expectedOffer = new byte[]
            {
                0x00, 0x00, 0x00, 0x01,
                0x00, 0x00, 0x00, 0x65,
                0x07, 0x00,
                0x4d, 0x65, 0x73, 0x73, 0x61, 0x67, 0x65,
            };
            Assert.Equal(expectedOffer, offerSpan.ToArray());

            // === 0x006A CLIENT_SOUND byte-for-byte assertion ===
            // Wire layout (34 bytes) from PlayerConnection.cpp:949-968:
            //   bytes [0..4]   int32 LE length = strlen("push_mission_alert_sound") + 1 = 25
            //   bytes [4..28]  ASCII "push_mission_alert_sound" (24 chars, no inline NUL)
            //   bytes [28]     1 byte NUL terminator
            //   bytes [29..33] int32 LE channel = 2
            //   bytes [33]     1 byte queue = 0
            Assert.Equal(ExpectedClientSoundPayloadSize, clientSound.Payload.Length);
            var soundSpan = clientSound.Payload.Span;

            int soundLength = BinaryPrimitives.ReadInt32LittleEndian(soundSpan.Slice(0, 4));
            Assert.Equal(ClientSoundName.Length + 1, soundLength);  // 25

            string soundName = System.Text.Encoding.ASCII.GetString(soundSpan.Slice(4, ClientSoundName.Length));
            Assert.Equal(ClientSoundName, soundName);

            Assert.Equal(0x00, soundSpan[4 + ClientSoundName.Length]);  // NUL terminator at offset 28

            int channel = BinaryPrimitives.ReadInt32LittleEndian(soundSpan.Slice(4 + ClientSoundName.Length + 1, 4));
            Assert.Equal(ClientSoundChannel, channel);

            byte queue = soundSpan[4 + ClientSoundName.Length + 1 + 4];  // offset 33
            Assert.Equal(ClientSoundQueue, queue);
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
