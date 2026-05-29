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
/// Wave 40 direct-reply round-trip: client sends 0x002C ACTION with
/// Action=16 ("request list of players LFG") on a freshly-handshaken
/// starbase sector connection, then asserts the server emits a 0x0053
/// FIND_MEMBER reply with the canonical 4-byte single-player wire shape
/// (count=0, zero list items).
///
/// <para>
/// Server dispatcher walk (server/src/PlayerConnection.cpp:3895-3899):
/// <code>
///   case 16:    //request list of players LFG
///       {
///           g_ServerMgr-&gt;m_PlayerMgr.RequestAllPlayersLFG(this);
///       }
///       break;
/// </code>
/// Routes the calling Player straight into
/// <c>PlayerManager::RequestAllPlayersLFG</c>
/// (<c>server/src/PlayerManager.cpp:1261</c>).
/// </para>
///
/// <para>
/// RequestAllPlayersLFG walk:
/// </para>
/// <list type="number">
///   <item>Allocates a FindMember struct sized for the number of OTHER
///     players in the same sector who have LFG=true. For a single test
///     character alone in Luna Station, the count loop returns 0
///     (the filter <c>p != player &amp;&amp; p-&gt;IsInSameSector(player)
///     &amp;&amp; p-&gt;PlayerIndex()-&gt;GroupInfo.GetLookingForGroup()</c>
///     excludes self).</item>
///   <item>Builds the 4-byte payload <c>players-&gt;count = 0</c>
///     (host-LE int32).</item>
///   <item>The fill loop iterates the global player list a second time
///     but writes zero entries (same filter excludes self).</item>
///   <item>Calls <c>p-&gt;SendFindMember(players)</c> on whatever
///     <c>p</c> ends as after the second loop. Per
///     <c>PlayerManager::GetNextPlayerOnList</c>
///     (<c>server/src/PlayerManager.cpp:1131</c>) semantics, on a list
///     containing exactly <c>this</c> (the lone test character),
///     <c>p</c> is set to <c>this</c> on the only matching iteration
///     and then the loop exits with <c>found=false</c> on the next
///     iteration -- crucially without resetting <c>p</c>. So
///     <c>p == this</c> at the post-loop call: the server sends the
///     FindMember reply to OURSELVES.</item>
///   <item><c>Player::SendFindMember</c>
///     (<c>server/src/PlayerConnection.cpp:11228</c>) emits
///     <c>SendOpcode(ENB_OPCODE_0053_FIND_MEMBER, players, count*16+4)</c>
///     -- for count=0 the wire payload is exactly 4 bytes, the int32 LE
///     count field with value 0.</item>
/// </list>
///
/// <para>
/// Why this is the only tractable single-player path to 0x0053. The
/// only server-side emit site for FIND_MEMBER is SendFindMember, and
/// the only caller of SendFindMember is RequestAllPlayersLFG (verified
/// by exhaustive grep). RequestAllPlayersLFG is itself only called from
/// HandleAction case 16. So 0x002C with Action=16 is the canonical and
/// only path -- no admin slash command, no multi-player setup required.
/// </para>
///
/// <para>
/// Concrete regression classes this catches:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>HandleAction case-16 dispatch removal.</b> Deletion of the
///     case-16 arm at PlayerConnection.cpp:3895-3899, or replacing the
///     RequestAllPlayersLFG call with a stub, would silently drop the
///     reply -- drain times out without 0x0053.
///   </item>
///   <item>
///     <b>RequestAllPlayersLFG GetNextPlayerOnList iterator semantics
///     regression.</b> The current implementation relies on
///     GetNextPlayerOnList NOT clearing <c>p</c> on the false return so
///     the post-loop <c>p-&gt;SendFindMember(players)</c> hits a valid
///     Player pointer. A "defensive" refactor that resets <c>p=NULL</c>
///     on false return would make the lone-player post-loop call a
///     NULL-deref SEGV. Survival catches the worker death; the direct
///     assertion catches the missing reply.
///   </item>
///   <item>
///     <b>SendFindMember payload-size regression at
///     PlayerConnection.cpp:11230.</b> The current emit is
///     <c>players-&gt;count * 16 + 4</c>. A regression to plain
///     <c>sizeof(struct FindMember)</c> would always send the struct's
///     full 20-byte size (4-byte count + 16-byte list[1] stub) instead
///     of 4 bytes for count=0 -- failed length assertion.
///   </item>
///   <item>
///     <b>FindMember struct layout regression at PacketStructures.h:1075.</b>
///     The fields are 4 x int32_t per fm_item; a long-revert on any
///     would inflate <c>sizeof(fm_item)</c> from 16 to 32 on Linux LP64
///     and the size formula <c>count*16+4</c> would underspend the
///     buffer for any non-empty list (zero-count case still emits 4B so
///     the immediate test passes, but the wire-shape invariant is
///     documented for tighter future assertions).
///   </item>
///   <item>
///     <b>RequestAllPlayersLFG filter inversion at PlayerManager.cpp:1269
///     and 1281.</b> Dropping the <c>p != player</c> guard would
///     include self in the count and the list, walking count=1 with
///     list[0].GameID = ntohl(self.GameID). Length would be
///     1*16+4 = 20B not 4B -- failed length assertion.
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at PlayerConnection.cpp:127.</b>
///     Corrupts the 0x2016 PACKET_SEQUENCE inner-tuple parser; 0x0053
///     wouldn't appear under its correct opcode label in the inbound
///     stream -- drain times out.
///   </item>
///   <item>
///     <b>Proxy default-case ForwardClientOpcode dropping 0x002C.</b>
///     0x002C IS listed in proxy ProcessSectorServerOpcode (line 471-477)
///     but the explicit listing is a Wave 13-era hardening; a regression
///     removing the explicit case AND the default-forward arm would
///     drop the dispatch.
///   </item>
///   <item>
///     <b>ActionPacket struct-layout drift at PacketStructures.h:546.</b>
///     Same Wave 11 long-&gt;int32_t migration the Wave 13 ACTION
///     survival probe documents. Reverting widens the struct from 16B
///     to 32B on Linux x86_64 and the Action field would read from
///     offset 8, missing case 16 entirely -- drain times out.
///   </item>
/// </list>
///
/// <para>
/// Server-integrity (CLAUDE.md). The 0x002C ACTION wire shape with
/// Action=16 is exactly what the retail Win32 client emits when the
/// user opens the LFG / "Looking for Group" UI panel and clicks
/// "Refresh". The retail server's HandleAction case 16 fans into
/// RequestAllPlayersLFG and sends back a FindMember reply listing all
/// other LFG-flagged players in the same sector -- empty list when the
/// requester is alone in the sector. Our test exercises that empty-list
/// path on a freshly-handed-off solo starbase character; no input
/// permissiveness added, no server behaviour widened. The 4-byte
/// payload is the canonical retail wire shape (verified via the
/// SendFindMember source at PlayerConnection.cpp:11230 length formula).
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~2s; ACTION send + 0x0053 drain is
/// sub-second. Wide budget covers stage-ack retry in the login state
/// machine and any handshake-tail debris frames.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorFindMemberTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorFindMemberTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task FindMember_SoloRequester_ReceivesEmptyCountReply()
    {
        var account = TestAccounts.New(_server);
        const int slot = 0;
        const int sectorId = 10151;  // Terran Warrior start: Luna Station

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password), cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        // firstName "Finder" -- contains 'i', 'e' for the vowel-check.
        await using var session = await SectorHandshake.EstablishAsync(
            _server, login.Ticket!, account.Username, slot, sectorId,
            firstName: "Finder", shipName: "FindShip", cts.Token);

        try
        {
            // ActionPacket wire layout -- 16 bytes total, all int32_t LE.
            // common/include/net7/PacketStructures.h:546
            //   [0..4)   GameID       -- actor's game id (server resolves
            //                            actor via the connection binding).
            //   [4..8)   Action       -- 16 = request list of players LFG.
            //   [8..12)  Target       -- unused by case 16.
            //   [12..16) OptionalVar  -- unused by case 16.
            byte[] actionPayload = new byte[16];
            BinaryPrimitives.WriteInt32LittleEndian(actionPayload.AsSpan(0, 4), 0);
            BinaryPrimitives.WriteInt32LittleEndian(actionPayload.AsSpan(4, 4), 16);
            BinaryPrimitives.WriteInt32LittleEndian(actionPayload.AsSpan(8, 4), 0);
            BinaryPrimitives.WriteInt32LittleEndian(actionPayload.AsSpan(12, 4), 0);

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.Action.Value, actionPayload),
                cts.Token);

            // Drain until 0x0053 FIND_MEMBER. Post-handshake the server
            // may interleave other in-sector frames so this loop tolerates
            // unrelated traffic. Cap on frame count so a stalled pipeline
            // can't masquerade as the outer-CTS timeout.
            int framesSeen = 0;
            const int maxFrames = 400;
            while (framesSeen++ < maxFrames)
            {
                var reply = await session.Sector.ReceiveAsync(cts.Token);
                Assert.NotNull(reply);

                if (reply!.Header.Opcode != OpcodeId.Known.FindMember.Value)
                    continue;

                // 0x0053 wire layout for the empty-list case:
                //   [0..4)  int32 LE count = 0
                // No list items follow when count==0 (the C++ side emits
                // exactly count*16+4 bytes via SendFindMember).
                var span = reply.Payload.Span;
                Assert.Equal(4, span.Length);

                int count = BinaryPrimitives.ReadInt32LittleEndian(span[..4]);
                Assert.Equal(0, count);

                return;
            }

            throw new Xunit.Sdk.XunitException(
                $"drained {maxFrames} frames after sending 0x002C ACTION (Action=16 LFG) " +
                $"without seeing 0x0053 FIND_MEMBER. " +
                $"Likely HandleAction case 16 at PlayerConnection.cpp:3895 was deleted, " +
                $"RequestAllPlayersLFG's post-loop p->SendFindMember NULL-derefed because a " +
                $"GetNextPlayerOnList refactor reset p on the false return, " +
                $"SendFindMember's size formula at PlayerConnection.cpp:11230 was broken, " +
                $"ActionPacket struct layout drifted (long-revert shifts Action field offset), " +
                $"the SendOpcode header-width fix at PlayerConnection.cpp:127 was reverted, " +
                $"or the proxy dropped 0x002C dispatch.");
        }
        finally
        {
            using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
            catch { /* best-effort cleanup */ }
        }
    }
}
