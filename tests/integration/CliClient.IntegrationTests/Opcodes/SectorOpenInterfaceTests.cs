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
/// Wave 60 indirect-stimulus +1 ratchet: drive 0x0066 OPEN_INTERFACE
/// emit through the <c>/openif a,b</c> slash command rather than a
/// direct opcode dispatch. The test sends a 0x0033 CLIENT_CHAT whose
/// payload string is <c>"/openif 0,0"</c>, which routes through
/// <c>Player::HandleClientChat</c>'s slash-command branch
/// (<c>server/src/PlayerConnection.cpp:4614</c>) into
/// <c>Player::HandleSlashCommands</c>
/// (<c>server/src/PlayerConnection.cpp:4682-...</c>), which in turn
/// dispatches the <c>openif</c> arm at <c>PlayerConnection.cpp:6885-6905</c>:
/// <code>
///   else if (MatchOptWithParam("openif", pch, param, msg_sent))
///   {
///       char *a = strtok_s(param, ",", &amp;next_token);
///       char *b = strtok_s(NULL, ",", &amp;next_token);
///       if (b)
///       {
///           OpenInterface(atoi(a), atoi(b));
///           SendVaMessage("OpenInterface (%d,%d):", atoi(a), atoi(b));
///       }
///       msg_sent = true;
///   }
/// </code>
///
/// <para>
/// <c>Player::OpenInterface</c> (<c>server/src/PlayerConnection.cpp:3576-3584</c>)
/// builds an 8-byte <c>SetInterface</c> struct (<c>common/include/net7/PacketStructures.h:534-538</c>:
/// two <c>int32_t</c> fields, <c>UIChange</c> then <c>UIType</c>) and emits
/// <c>SendOpcode(ENB_OPCODE_0066_OPEN_INTERFACE, &amp;set_interface, sizeof(set_interface))</c>.
/// With both fields zero, the wire payload is exactly 8 bytes of all zeros.
/// </para>
///
/// <para>
/// Which switch the openif arm lives in. <c>HandleSlashCommands</c> has
/// two top-level if-blocks: a GM/double-slash block (<c>PlayerConnection.cpp:4716</c>,
/// gated on <c>Msg[0]=='/' &amp;&amp; Msg[1]=='/' &amp;&amp; AdminLevel() &gt;= GM</c>)
/// and a normal single-slash block (<c>PlayerConnection.cpp:5447</c>, gated on
/// <c>Msg[0]=='/' &amp;&amp; Msg[1] != 0 &amp;&amp; (!msg_sent || !success)</c>).
/// The GM switch only has cases a,b,c,d,e,f,h,k,r,s,w,g — there is no
/// <c>case 'o'</c>. The <c>openif</c> arm at line 6885 sits inside the
/// SECOND switch's <c>case 'o'</c>. So a <c>//openif</c> double-slash
/// would dispatch in the GM switch on '/'+'o' with no handler, fall through,
/// then re-enter the single-slash block with <c>pch="/openif 0,0"</c>
/// (leading slash) and the second switch dispatches on <c>*pch='/'</c>
/// — also no handler — and the "Illegal slash command" fallback fires.
/// Single-slash <c>/openif 0,0</c> is the correct stimulus.
/// </para>
///
/// <para>
/// Why slash-command stimulus rather than direct opcode dispatch.
/// 0x0066 has no client-originated cousin in the dispatch table at
/// <c>PlayerConnection.cpp:418-642</c> — it is purely server-emitted.
/// Other call sites (CloseInterfaceIfTargetted, the prospect-window
/// flows, etc.) require seeded combat/prospecting state that doesn't
/// exist on a fresh station-handshake character. The <c>/openif</c>
/// slash-command arm is the cleanest unconditional path: no
/// ObjectManager lookup, no target requirement, no group/formation
/// guard, no AdminLevel guard — just two <c>strtok</c>+<c>atoi</c>
/// calls and a <c>SendOpcode</c>.
/// </para>
///
/// <para>
/// Slash-command gating. <c>HandleClientChat</c> at
/// <c>PlayerConnection.cpp:4614</c> routes any chat whose
/// <c>chat-&gt;String[0] == '/'</c> into <c>HandleSlashCommands</c>
/// before falling into the type-1/2/3/4 branches. <c>HandleSlashCommands</c>
/// then enters the single-slash block at <c>PlayerConnection.cpp:5447</c>
/// when the second char is not NUL. Single-slash arms have their own
/// AdminLevel gates per-command; the <c>openif</c> arm at line 6885 has
/// none, so any logged-in player can invoke it.
/// </para>
///
/// <para>
/// Why a 0x0033 CLIENT_CHAT carrier (not a custom opcode).
/// CLIENT_CHAT is the only client-originated path that routes into
/// <c>HandleSlashCommands</c>; the <c>Login</c>-stage greeting paths
/// (<c>PlayerConnection.cpp:1737</c>, <c>1753</c>) also feed slash
/// commands but only fire during a specific login handoff that the
/// test harness doesn't yet replay. CLIENT_CHAT is also already
/// covered by <see cref="SectorChatTests"/> — Wave 60 piggybacks on
/// the same wire-format codec (<c>ClientChatCodec</c>) and asserts on
/// a different opcode in the reply stream.
/// </para>
///
/// <para>
/// Per CLAUDE.md server-integrity: no server change. Wave 60 drives an
/// existing server-emit path with valid client stimulus. The slash
/// command behaves identically pre- and post-Wave 60 — the test simply
/// pins behaviour the server was already producing. Per the rule's
/// "tightening is welcome" carve-out this is exactly the shape allowed:
/// rejecting more regression classes without widening any input
/// acceptance.
/// </para>
///
/// <para>
/// Regression classes this catches.
/// </para>
/// <list type="bullet">
///   <item>
///     <b>0x0066 OPEN_INTERFACE emit removal from <c>Player::OpenInterface</c>.</b>
///     If the <c>SendOpcode</c> at <c>PlayerConnection.cpp:3583</c>
///     gets short-circuited or guarded behind a flag, no 0x0066 reaches
///     the client and the drain loop times out.
///   </item>
///   <item>
///     <b><c>SetInterface</c> wire-layout regression in
///     <c>common/include/net7/PacketStructures.h:534-538</c>.</b> The
///     test asserts payload length == 8 exactly. Adding/removing a
///     field, changing <c>int32_t</c> → <c>long</c>, or losing
///     <c>ATTRIB_PACKED</c> all surface as a length or content
///     mismatch (the Linux sizeof(long)==8 trap that bit Wave 11 and
///     Wave 59 would here become a 16B payload).
///   </item>
///   <item>
///     <b>Single-slash gate regression at <c>PlayerConnection.cpp:5447</c>.</b>
///     If a refactor tightens the gate (e.g. requires double-slash,
///     adds an AdminLevel check, rejects Msg[1]='o') the openif arm
///     never fires and the test times out.
///   </item>
///   <item>
///     <b><c>case 'o'</c> dispatch regression in the single-slash switch
///     (<c>PlayerConnection.cpp:6885</c>).</b> If the switch label gets
///     moved, removed, or the arm reordered such that an earlier
///     <c>MatchOptWithParam</c> in case 'o' eats the "openif" prefix
///     (e.g. matching "open" via <c>allowNoParams</c>), the openif arm
///     never runs.
///   </item>
///   <item>
///     <b><c>MatchOptWithParam</c> arg-parsing regression at
///     <c>PlayerConnection.cpp:4526-4554</c>.</b> The helper splits on
///     <c>'='</c> or <c>' '</c> and rejects alphabetic continuations.
///     If the helper changes shape (e.g. starts requiring <c>=</c> only
///     or rejecting spaces) the openif arm's <c>" 0,0"</c> form would
///     stop parsing.
///   </item>
///   <item>
///     <b><c>strtok_s</c> path regression at <c>PlayerConnection.cpp:6887-6888</c>.</b>
///     The handler reads <c>a</c> = first comma-split, <c>b</c> =
///     second. If the second <c>strtok_s(NULL, ...)</c> regresses, <c>b</c>
///     comes back NULL and the <c>if (b)</c> guard skips the
///     <c>OpenInterface</c> call — the test times out instead of
///     asserting on a malformed reply.
///   </item>
///   <item>
///     <b><c>HandleClientChat</c> slash dispatch regression at
///     <c>PlayerConnection.cpp:4614-4617</c>.</b> If the slash check
///     moves below the type-1/2/3/4 branches a Type=Group chat with
///     <c>"/openif 0,0"</c> hits the "Error: You are not in a group!"
///     path instead, the chat string never reaches
///     <c>HandleSlashCommands</c>, and 0x0066 never emits.
///   </item>
///   <item>
///     <b>CLIENT_CHAT codec regression in <c>ClientChatCodec</c>.</b>
///     If <c>EncodeOutbound</c> stops emitting the leading-NUL terminator
///     or miscomputes the int16 size field, the server sees a malformed
///     <c>ClientChat</c> struct and falls into the GlobalError path
///     (<c>SendVaMessage</c> would issue a debug log; the
///     <c>HandleSlashCommands</c> branch never fires). Drain-loop
///     timeout surfaces the regression.
///   </item>
///   <item>
///     <b>Proxy <c>SendClientPacketSequence</c> inner-opcode guard at
///     <c>proxy/UDPProxyToClient_linux.cpp:568</c> tightening.</b>
///     0x0066 &lt; 0x0FFF so it currently passes the gate. If the gate
///     tightens past 0x0066 (e.g. to &lt; 0x0050) the emit fires
///     server-side but never reaches the test client.
///   </item>
///   <item>
///     <b>UDP queue / packet-cache regression at
///     <c>PlayerConnection.cpp:127</c> SendOpcode header-width revert.</b>
///     A pre-Phase-K bug shifted the SendOpcode header width by 4
///     bytes on Linux; Wave 60 would catch any reintroduction since
///     the 0x0066 length-prefix would be wrong on the wire and
///     <c>EncryptedTcpConnection.ReceiveAsync</c> would fail to frame
///     the reply.
///   </item>
/// </list>
///
/// <para>
/// Cleanup. <c>/openif 0,0</c> mutates no persistent state: it neither
/// drops the player from the sector nor commits any DB row. Cleanup is
/// the standard 0x00B9 LOGOFF_REQUEST → 0x00BA LOGOFF_CONFIRMATION
/// round-trip + GlobalDeleteCharacter on the global TCP, identical to
/// Waves 55-59.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~22s on a cold stack; chat-with-slash-command
/// round-trip is sub-second; LOGOFF round-trip is sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorOpenInterfaceTests
{
    private const int ExpectedOpenInterfacePayloadSize = 8;
    private const int OpenIfUIChange = 0;
    private const int OpenIfUIType = 0;

    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorOpenInterfaceTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task OpenInterfaceSlashCommand_OnSlashOpenif_ReceivesOpenInterfaceEmit()
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
            firstName: "OpenIf60", shipName: "OpenIf60Ship", cts.Token);

        try
        {
            // Build a CLIENT_CHAT with "/openif 0,0" — the single-slash
            // prefix routes through HandleSlashCommands' single-slash
            // block (PlayerConnection.cpp:5447); the "openif" arm at
            // PlayerConnection.cpp:6885-6905 (case 'o' of the second
            // switch) parses "0,0", calls OpenInterface(0, 0), which
            // emits 0x0066 OPEN_INTERFACE with an 8-byte
            // SetInterface{UIChange=0, UIType=0} payload. Type=Group is
            // irrelevant for the slash branch (the slash check at
            // PlayerConnection.cpp:4614 fires before the type-dependent
            // branches).
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/openif 0,0");

            await session.Sector.SendAsync(
                Packet.ForOpcode(
                    OpcodeId.Known.ClientChat.Value,
                    codec.EncodeOutbound(chat)),
                cts.Token);

            // Drain up to 400 frames waiting for the 0x0066 emit. The
            // server also fires 0x001D MESSAGE_STRING (SendVaMessage at
            // PlayerConnection.cpp:6892) but we don't depend on it —
            // 0x0066 is sufficient for the +1 ratchet and the slash arm
            // emits 0x0066 before SendVaMessage so we'd see it first in
            // any case.
            const int maxFrames = 400;
            int seen = 0;
            Packet? reply = null;
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
                    if (frame.Header.Opcode == OpcodeId.Known.MessageString.Value
                        && frame.Payload.Length > 0)
                    {
                        try
                        {
                            var s = System.Text.Encoding.ASCII.GetString(frame.Payload.Span);
                            observed[^1] += $"[{s.Replace('\0', '.')}]";
                        }
                        catch { }
                    }
                    if (frame.Header.Opcode == OpcodeId.Known.OpenInterface.Value)
                    {
                        reply = frame;
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (drainCts.IsCancellationRequested)
            {
                // drain timed out — emit observed list as failure diagnostic
            }

            if (reply == null)
            {
                throw new Xunit.Sdk.XunitException(
                    $"No 0x0066 OPEN_INTERFACE received after {seen} frames. " +
                    $"Observed [{observed.Count}]: {string.Join(" | ", observed)}");
            }

            Assert.NotNull(reply);
            Assert.Equal(ExpectedOpenInterfacePayloadSize, reply!.Payload.Length);

            var span = reply.Payload.Span;

            // [0..4) UIChange — atoi("0") → 0.
            int replyUIChange = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(0, 4));
            Assert.Equal(OpenIfUIChange, replyUIChange);

            // [4..8) UIType — atoi("0") → 0.
            int replyUIType = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(4, 4));
            Assert.Equal(OpenIfUIType, replyUIType);
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
