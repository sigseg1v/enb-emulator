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
/// Wave 58 direct-reply round-trip (+1 ratchet): client establishes a
/// station-sector handshake at Luna Station (sector 10151), then sends
/// a 9-byte 0x004E STARBASE_REQUEST with Action=4 (Talk to NPC) and an
/// unknown <c>StarbaseID</c> (0xDEADBEEF) and asserts the server emits
/// a 139-byte 0x0054 TALK_TREE back — the deterministic fallback
/// talk-tree the case-4 arm emits when the NPC lookup misses.
///
/// <para>
/// Case 4 (<c>server/src/PlayerConnection.cpp:9893-9931</c>) is the
/// Talk-to-NPC dispatch. With a fresh starbase character on Wave 58's
/// fixture, both early-return guards fall through to false:
/// <code>
///   if (CheckMissions(0, 1, m_StarbaseTargetID, TALK_NPC) ||
///       CheckForNewMissions(0, 1, m_StarbaseTargetID))
///   {
///       return;
///   }
/// </code>
/// <c>CheckMissions</c> early-skips: <c>m_MissionAcceptance</c> defaults
/// false, <c>m_PushMissionID</c> is 0 (the guard requires &gt;0), so it
/// drops into the per-slot loop which finds <c>am-&gt;GetDatabaseID() == -1</c>
/// for every <c>MAX_MISSIONS</c> slot on a fresh character and returns
/// false. <c>CheckForNewMissions</c> iterates the mission list (the
/// Postgres-backed mission DB is empty for the integration-test seed —
/// only the accounts table is populated) so <c>mission_sz=0</c> and the
/// loop body's <c>m_list[0]</c> null-check continues; <c>m_StarterNPCs</c>
/// is empty so <c>GetMissionStartNPC(0xDEADBEEF)</c> returns false and
/// the <c>talk_npc_start</c> branch never fires; returns false.
/// </para>
///
/// <para>
/// Past both guards, <c>g_ServerMgr-&gt;m_StationMgr.GetNPC(0xDEADBEEF)</c>
/// uses <c>std::map::operator[]</c> on a key that doesn't exist — returns
/// a default-constructed <c>NPCTemplate*</c> (nullptr). The
/// <c>USE_MYSQL_STATIONS</c>-guarded talk-tree branch then short-circuits
/// on <c>if (NPC &amp;&amp; NPC-&gt;NPCInteraction.talk_tree.NumNodes &gt; 0)</c>
/// and falls through to the unconditional else-branch:
/// <code>
///   SendOpcode(ENB_OPCODE_0054_TALK_TREE, (unsigned char *) string,
///              sizeof(string));
/// </code>
/// where <c>string</c> is the 138-byte literal-concatenated buffer at
/// <c>PlayerConnection.cpp:9862-9868</c> plus C's compiler-inserted final
/// null terminator (139 bytes total — verified by a one-off <c>cc</c>
/// compile of the same literal). A <c>memcpy</c> at line 9870 splices
/// the player's 9-byte profession name (" Warriors" / "  Traders" /
/// "Explorers") at offset 43 — replaces "Explorers" with the correct
/// profession string but does not change the array length.
/// </para>
///
/// <para>
/// Wire layout of the inbound 0x004E (mirror of
/// <c>common/include/net7/PacketStructures.h:812-817</c>):
/// <code>
///   [0..4) int32 PlayerID    — case 4 ignores
///   [4..8) int32 StarbaseID  — 0xDEADBEEF (intentional unknown-NPC sentinel)
///   [8..9)   char Action     — 4
/// </code>
/// 9 bytes total, <c>ATTRIB_PACKED</c>. Identical shape to Waves 55-57.
/// </para>
///
/// <para>
/// Regression classes this catches.
/// </para>
/// <list type="bullet">
///   <item>
///     <b>HandleStarbaseRequest case-4 branch deletion at
///     <c>PlayerConnection.cpp:9893-9931</c>.</b> If the arm vanishes,
///     the fallback emit never fires — drain times out.
///   </item>
///   <item>
///     <b>USE_MYSQL_STATIONS guard inversion at
///     <c>PlayerConnection.cpp:9907-9923</c>.</b> If the
///     <c>#ifdef USE_MYSQL_STATIONS</c> is flipped (e.g.
///     <c>#ifndef</c>) the talk-tree branch and the else-fallback swap;
///     the else-branch's bare emit still fires regardless on a null
///     NPC, but a refactor that elides the else-branch entirely would
///     surface as a drain timeout.
///   </item>
///   <item>
///     <b>StationLoader::GetNPC default-construction change.</b>
///     <c>std::map[]</c>'s default-construct behaviour for a missing
///     key returns <c>nullptr</c> for a value-type of
///     <c>NPCTemplate*</c>. A refactor that uses <c>.at()</c> would
///     throw <c>std::out_of_range</c>; a refactor that returns a
///     stale-cached non-null pointer would walk the talk-tree branch
///     and either emit a different payload size or hit a different
///     opcode. Either case fails the 139B length assertion.
///   </item>
///   <item>
///     <b>CheckMissions early-return regression at
///     <c>PlayerMissions.cpp:705</c>.</b> If <c>m_MissionAcceptance</c>
///     defaults true (e.g. a constructor change) the handler would
///     never reach the GetNPC call; case-4 would either return
///     prematurely or emit a different opcode (mission-proposal flow
///     emits 0x0055 SELECT_TALK_TREE / 0x0090 MISSION_PROPOSE rather
///     than 0x0054). Failed length-and-opcode assertion catches.
///   </item>
///   <item>
///     <b>CheckForNewMissions iteration overrun.</b> If
///     <c>GetHighestID</c> regresses to a sentinel like
///     <c>LONG_MAX</c> the loop walks the entire address space.
///     Test would time out at the 90s budget.
///   </item>
///   <item>
///     <b>The fallback string literal at
///     <c>PlayerConnection.cpp:9862-9868</c>.</b> Any edit that adds,
///     removes, or shortens the literal changes <c>sizeof(string)</c>.
///     The 139B byte-exact length assertion pins the preserved retail
///     copy. The string contains the "/happy1" emote prefix that the
///     retail client renders, the "I would like to trade" / "Nothing
///     today" branch labels, and the per-profession noun
///     <c>memcpy</c>'d at offset 43 — Wave 58 doesn't assert the
///     profession bytes but a future stricter wave could.
///   </item>
///   <item>
///     <b>The <c>memcpy</c> at <c>PlayerConnection.cpp:9870</c>.</b>
///     Splices 9 profession-name bytes at offset 43. A regression to
///     a different length (e.g. 10 to read past the end of the
///     <c>professions</c> array) would write past offset 51 into the
///     adjacent " are welcome here." text — the length stays 139 but
///     the buffer mutates. Future byte-content assertion would catch.
///     For Wave 58 we only assert length, not content.
///   </item>
///   <item>
///     <b>SendOpcode opcode-id flip at the case-4 emit site.</b> A
///     typo from <c>0x0054</c> to a neighbouring define would emit a
///     frame under the wrong label — drain times out.
///   </item>
///   <item>
///     <b>SendOpcode header-width revert at
///     <c>PlayerConnection.cpp:127</c>.</b> Would corrupt every inner
///     opcode in the 0x2016 PACKET_SEQUENCE parser; 0x0054 would not
///     appear under its correct label.
///   </item>
///   <item>
///     <b>Proxy SendClientPacketSequence inner-opcode guard tightening
///     at <c>proxy/UDPProxyToClient_linux.cpp:568</c>.</b> Currently
///     passes 0x0054 (less than 0x0FFF). A tighter upper bound would
///     silently drop it.
///   </item>
///   <item>
///     <b>StarbaseRequest packed-struct layout regression at
///     <c>PacketStructures.h:812-817</c>.</b> 9B canonical with
///     <c>ATTRIB_PACKED</c>; an unpacked 12B revert would mis-read
///     the Action byte and miss case 4.
///   </item>
///   <item>
///     <b>m_TradeWindow side-effect regression at
///     <c>PlayerConnection.cpp:9875</c>.</b> The handler unconditionally
///     resets <c>m_TradeWindow = false</c> before the switch; a refactor
///     that gated this reset on a sub-case could leak stale trade-window
///     state into the case-4 arm. The fallback emit doesn't directly
///     pin this but the test running cleanly confirms it.
///   </item>
/// </list>
///
/// <para>
/// CLAUDE.md server-integrity. Wave 58 sends an input shape the real
/// retail client emitted (STARBASE_REQUEST with Action=4 is the
/// talk-to-NPC UI commit; the legacy "/happy1 Hello! Hello…" greeter
/// literal at <c>PlayerConnection.cpp:9862-9868</c> is preserved retail
/// server code, including the inline <c>memcpy</c> that splices the
/// player's profession noun) and asserts a server-originated reply
/// (0x0054 TALK_TREE) the real retail server has always produced on
/// that input when the NPC lookup misses. No server change, no widened
/// input acceptance, no loosened gating, no debug-only opcode, no
/// security-posture relaxation. The unknown-NPC sentinel 0xDEADBEEF is
/// a wire-shape exercise of the existing else-branch — the retail
/// server treats every cache-miss NPC identically (the
/// <c>std::map::operator[]</c> default-construction is exactly the
/// retail behaviour).
/// </para>
///
/// <para>
/// Cleanup. Case 4 mutates only <c>m_StarbaseTargetID</c> and
/// <c>m_CurrentNPC=null</c> on the player. No DropPlayerFromSector, no
/// LaunchIntoSpace, no DB commit. The player remains an active,
/// in-station avatar after the reply arrives. Cleanup is the standard
/// 0x00B9 LOGOFF_REQUEST → 0x00BA LOGOFF_CONFIRMATION round-trip +
/// GlobalDeleteCharacter on the global TCP, identical to Waves 55-57.
/// </para>
///
/// <para>
/// Budget: 90s. Handshake ~22s on a cold stack; STARBASE_REQUEST +
/// reply round-trip is sub-second; LOGOFF round-trip is sub-second.
/// </para>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class SectorTalkTreeTests
{
    private const int ExpectedTalkTreeFallbackSize = 139;
    private const uint UnknownNpcId = 0xDEADBEEF;

    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public SectorTalkTreeTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task StarbaseTalkAction4_OnUnknownNpc_ReceivesFallbackTalkTree()
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
            firstName: "Talk4", shipName: "Talk4Ship", cts.Token);

        try
        {
            // Canonical 9B packed StarbaseRequest payload. PlayerID is
            // ignored on the case=4 arm; StarbaseID = 0xDEADBEEF is the
            // intentional unknown-NPC sentinel that drives the
            // std::map[] default-construct return → else-branch
            // fallback emit.
            byte[] payload = new byte[9];
            BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(0, 4), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(4, 4), UnknownNpcId);
            payload[8] = 4;

            await session.Sector.SendAsync(
                Packet.ForOpcode(OpcodeId.Known.StarbaseRequest.Value, payload),
                cts.Token);

            // Drain up to 400 frames waiting for the 0x0054 reply.
            const int maxFrames = 400;
            int seen = 0;
            Packet? reply = null;
            while (seen++ < maxFrames)
            {
                var frame = await session.Sector.ReceiveAsync(cts.Token);
                Assert.NotNull(frame);
                if (frame!.Header.Opcode == OpcodeId.Known.TalkTree.Value)
                {
                    reply = frame;
                    break;
                }
            }

            Assert.NotNull(reply);
            Assert.Equal(ExpectedTalkTreeFallbackSize, reply!.Payload.Length);
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
