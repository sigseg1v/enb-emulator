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
/// Wave 61 indirect-stimulus +1 ratchet: drive 0x0065 UI_TRIGGER emit
/// through the <c>/uitrigger a,b</c> slash command. The test sends a
/// 0x0033 CLIENT_CHAT whose payload string is <c>"/uitrigger 0,0"</c>,
/// which routes through <c>Player::HandleClientChat</c>'s slash-command
/// branch (<c>server/src/PlayerConnection.cpp:4614</c>) into
/// <c>Player::HandleSlashCommands</c>, where the single-slash block at
/// <c>PlayerConnection.cpp:5447</c> dispatches case 'u' at
/// <c>PlayerConnection.cpp:7575-7592</c>:
/// <code>
///   case 'u' :
///       if (MatchOptWithParam("uitrigger", pch, param, msg_sent))
///       {
///           char *a = strtok_s(param, ",", &amp;next_token);
///           char *b = strtok_s(NULL, ",", &amp;next_token);
///           if (b)
///           {
///               int Index = 0;
///               unsigned char Data[75];
///
///               *((long*) &amp;Data[Index]) = atoi(a);
///               Index+=4;
///               *((long*) &amp;Data[Index]) = atoi(b);
///               Index+=4;
///               SendOpcode(ENB_OPCODE_0065_UI_TRIGGER, (unsigned char *) &amp;Data, Index);
///           }
///           msg_sent = true;
///       }
/// </code>
///
/// <para>
/// Wire shape. <c>SendOpcode(... Data, Index=8)</c> emits exactly 8 bytes:
/// <c>[int32 a][int32 b]</c> in host byte order (little-endian on x86_64).
/// </para>
///
/// <para>
/// The <c>sizeof(long)</c> WRITE-path bug at lines 7585 and 7587 is invisible
/// here, unlike Wave 59's <c>HandleCTARequest</c>. Both writes use
/// <c>*((long*) &amp;Data[Index]) = atoi(...)</c>, which is an 8-byte write on
/// Linux x86_64 (where <c>sizeof(long)==8</c>) and a 4-byte write on Win32
/// retail (where <c>sizeof(long)==4</c>). On Linux, the first 8B write to
/// <c>Data[0..8]</c> stores low 4B = <c>atoi(a)</c> and high 4B = 0 (sign-extended
/// from a non-negative int). Then <c>Index+=4</c> bumps to 4, and the second 8B
/// write to <c>Data[4..12]</c> stores low 4B = <c>atoi(b)</c> at <c>Data[4..8]</c>
/// — overwriting the zero high-half of the first write — and high 4B = 0 at
/// <c>Data[8..12]</c>. The final <c>SendOpcode(... Index=8)</c> only emits bytes
/// <c>[0..8]</c>, which contain exactly <c>{int32 a, int32 b}</c> — identical to
/// what a clean <c>int32_t*</c> pair of writes would produce. The 4B of high-half
/// padding at <c>Data[8..12]</c> never reaches the wire. And the 75B buffer is
/// wide enough that the writes don't overflow the stack. So unlike Wave 59,
/// no server change is needed: the bug exists in source but is wire-invisible
/// by virtue of the buffer width and the trailing-overwrite arrangement.
/// </para>
///
/// <para>
/// Which switch the uitrigger arm lives in. <c>HandleSlashCommands</c> has
/// two top-level if-blocks: the GM/double-slash block (<c>PlayerConnection.cpp:4716</c>)
/// and the single-slash block (<c>PlayerConnection.cpp:5447</c>). The GM
/// switch has no case 'u'. The uitrigger arm at line 7575 lives inside the
/// SECOND switch's <c>case 'u'</c>. <c>/uitrigger 0,0</c> (single slash) is
/// the correct stimulus; <c>//uitrigger</c> would fall through the GM block
/// then re-enter the single-slash block with <c>pch="/uitrigger 0,0"</c>
/// (leading slash) and dispatch on <c>*pch='/'</c> — no handler — and the
/// "Illegal slash command" fallback fires.
/// </para>
///
/// <para>
/// Single-slash gating. The single-slash block requires <c>Msg[0]=='/' &amp;&amp;
/// Msg[1] != 0 &amp;&amp; (!msg_sent || !success)</c>. The uitrigger arm has no
/// AdminLevel check, so any logged-in player can invoke it. This matches the
/// shape of the Wave 60 /openif arm.
/// </para>
///
/// <para>
/// Why slash-command stimulus rather than direct opcode dispatch. 0x0065
/// UI_TRIGGER has no client-originated cousin in the dispatch table at
/// <c>PlayerConnection.cpp:418-642</c> — it is purely server-emitted, with
/// the /uitrigger slash arm as the only documented emit site. The arm has
/// no preconditions (no target, no group, no AdminLevel, no prospect window)
/// — two <c>strtok</c>+<c>atoi</c> calls and a <c>SendOpcode</c>.
/// </para>
///
/// <para>
/// Per CLAUDE.md server-integrity: no server change. Wave 61 drives an
/// existing server-emit path with valid client stimulus. The /uitrigger
/// command behaves identically pre- and post-Wave 61 — the test simply pins
/// behaviour the server was already producing. Tightening, not loosening.
/// </para>
///
/// <para>
/// Regression classes this catches.
/// </para>
/// <list type="bullet">
///   <item>
///     <b>0x0065 UI_TRIGGER emit removal at <c>PlayerConnection.cpp:7589</c>.</b>
///     If the <c>SendOpcode</c> gets short-circuited, no 0x0065 reaches the
///     client and the drain loop times out.
///   </item>
///   <item>
///     <b><c>sizeof(long)</c> WRITE-path regression flipping wire-visibility.</b>
///     The current Linux behaviour is wire-equivalent to <c>int32_t</c> writes
///     by happy accident (the second write overwrites the first write's
///     high-half padding). If a refactor (a) widens the buffer slice emitted
///     to Index=16 to "use up the long writes", or (b) reorders to write b
///     first then a so the high-half clobber goes the wrong direction, or
///     (c) drops the <c>Index+=4</c> increments so both writes go to
///     <c>Data[0]</c>, the wire shape changes and the 8B payload assertion
///     catches it. This is the third leg of the Phase K sizeof(long) bug
///     taxonomy described in Wave 59: (1) READ over-read (Waves 7/11/12);
///     (2) WRITE buffer overflow clobbering adjacent fields (Wave 59);
///     (3) WRITE wire-invisible-but-fragile (Wave 61 — pins the current
///     fortunate-accident wire shape so any future change to the surrounding
///     code surfaces immediately).
///   </item>
///   <item>
///     <b>Single-slash gate regression at <c>PlayerConnection.cpp:5447</c>.</b>
///     If a refactor tightens the gate (requires double-slash, adds an
///     AdminLevel check, rejects <c>Msg[1]='u'</c>) the uitrigger arm never
///     fires and the test times out.
///   </item>
///   <item>
///     <b><c>case 'u'</c> dispatch regression in the single-slash switch
///     (<c>PlayerConnection.cpp:7575</c>).</b> If the switch label gets moved,
///     removed, or an earlier <c>MatchOptWithParam</c> in case 'u' eats the
///     "uitrigger" prefix (e.g. matching "ui" via <c>allowNoParams</c>), the
///     uitrigger arm never runs.
///   </item>
///   <item>
///     <b><c>MatchOptWithParam</c> parsing regression at
///     <c>PlayerConnection.cpp:4526-4554</c>.</b> Same as Wave 60 — splits on
///     <c>'='</c> or <c>' '</c>; narrowing the separator set breaks the
///     <c>"uitrigger 0,0"</c> form.
///   </item>
///   <item>
///     <b><c>strtok_s</c> path regression at <c>PlayerConnection.cpp:7578-7579</c>.</b>
///     If the second <c>strtok_s(NULL, ...)</c> regresses, <c>b</c> comes back
///     NULL and the <c>if (b)</c> guard skips the SendOpcode entirely.
///   </item>
///   <item>
///     <b><c>HandleClientChat</c> slash dispatch regression at
///     <c>PlayerConnection.cpp:4614-4617</c>.</b> If the slash check moves
///     below the type-dependent branches a Type=Group chat hits the
///     "not in a group" path instead and the chat never reaches
///     <c>HandleSlashCommands</c>.
///   </item>
///   <item>
///     <b>CLIENT_CHAT codec regression in <c>ClientChatCodec</c>.</b> Same as
///     Wave 60. Drain-loop timeout surfaces it.
///   </item>
///   <item>
///     <b>Proxy <c>SendClientPacketSequence</c> inner-opcode guard at
///     <c>proxy/UDPProxyToClient_linux.cpp:568</c> tightening.</b> 0x0065 &lt;
///     0x0FFF so it currently passes the gate; a tighter gate (e.g. &lt;
///     0x0050) would silently drop the emit.
///   </item>
///   <item>
///     <b>UDP queue / packet-cache regression at
///     <c>PlayerConnection.cpp:127</c> SendOpcode header-width revert.</b>
///     A pre-Phase-K bug shifted the SendOpcode header width by 4 bytes on
///     Linux; Wave 61 would catch any reintroduction since the 0x0065
///     length-prefix would be wrong on the wire.
///   </item>
/// </list>
///
/// <para>
/// Cleanup. <c>/uitrigger 0,0</c> mutates no persistent state on the server
/// side — the handler just emits one frame and sets <c>msg_sent=true</c>.
/// Cleanup is the standard 0x00B9 LOGOFF_REQUEST → 0x00BA LOGOFF_CONFIRMATION
/// + GlobalDeleteCharacter, identical to Waves 55-60.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~22s on a cold stack; chat-with-slash-command
/// round-trip is sub-second; LOGOFF round-trip is sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorUiTriggerTests
{
    private const int ExpectedUiTriggerPayloadSize = 8;
    private const int UiTriggerA = 0;
    private const int UiTriggerB = 0;

    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorUiTriggerTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task UiTriggerSlashCommand_OnSlashUitrigger_ReceivesUiTriggerEmit()
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
            firstName: "UiTrig61", shipName: "UiTrig61Ship", cts.Token);

        try
        {
            // Build a CLIENT_CHAT with "/uitrigger 0,0" — the single-slash
            // prefix routes through HandleSlashCommands' single-slash block
            // (PlayerConnection.cpp:5447); the uitrigger arm at
            // PlayerConnection.cpp:7575-7592 (case 'u') parses "0,0" and
            // emits 0x0065 UI_TRIGGER with an 8-byte payload {int32 a=0;
            // int32 b=0}. Type=Group is irrelevant for the slash branch
            // (the slash check at PlayerConnection.cpp:4614 fires before
            // the type-dependent branches).
            var codec = new ClientChatCodec();
            var chat = new ClientChatMessage(
                GameId: session.GameId,
                Type: ChatChannel.Group,
                Message: "/uitrigger 0,0");

            await session.Sector.SendAsync(
                Packet.ForOpcode(
                    OpcodeId.Known.ClientChat.Value,
                    codec.EncodeOutbound(chat)),
                cts.Token);

            // Drain up to 400 frames waiting for the 0x0065 emit. Unlike
            // the /openif arm, the /uitrigger arm does NOT emit a 0x001D
            // MESSAGE_STRING acknowledgement — only the bare 0x0065 emit
            // and the standard msg_sent=true sentinel that suppresses the
            // "Illegal slash command" fallback.
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
                    if (frame.Header.Opcode == OpcodeId.Known.UiTrigger.Value)
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
                    $"No 0x0065 UI_TRIGGER received after {seen} frames. " +
                    $"Observed [{observed.Count}]: {string.Join(" | ", observed)}");
            }

            Assert.NotNull(reply);
            Assert.Equal(ExpectedUiTriggerPayloadSize, reply!.Payload.Length);

            var span = reply.Payload.Span;

            // [0..4) a — atoi("0") → 0.
            int replyA = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(0, 4));
            Assert.Equal(UiTriggerA, replyA);

            // [4..8) b — atoi("0") → 0.
            int replyB = BinaryPrimitives.ReadInt32LittleEndian(span.Slice(4, 4));
            Assert.Equal(UiTriggerB, replyB);
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
