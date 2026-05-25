// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Opcodes;

namespace N7.CliClient.IntegrationTests.Coverage;

/// <summary>
/// Hand-maintained catalogue of every opcode that has at least one
/// round-trip test in this suite. Drives the Phase T coverage ratchet
/// (<see cref="CoverageRatchetTests"/>).
/// </summary>
/// <remarks>
/// <para>
/// "Round-trip test" = at least one test exercises the opcode through
/// a real (or capture-replay) wire path, not just a unit test of the
/// codec in isolation. Adding an entry here without a corresponding
/// integration test is a lie that ratchets a false floor; the lie
/// will be caught by code review, not by tooling — be honest.
/// </para>
/// <para>
/// The ratchet works in two directions:
/// </para>
/// <list type="bullet">
///   <item>
///     <see cref="CoverageRatchetTests"/> asserts the set's count
///     equals the floor constant <see cref="MinTestedCount"/>. To
///     <i>add</i> an opcode you bump the constant by one and add the
///     entry. To <i>remove</i> an opcode (e.g. you deleted its test)
///     you must drop both — and the count-equality check forces you
///     to think about whether the deletion was intentional.
///   </item>
///   <item>
///     Every entry must resolve to a real opcode in
///     <see cref="OpcodeNames.All"/>. Catches typos and opcodes that
///     were renamed/removed upstream.
///   </item>
/// </list>
/// <para>
/// What is NOT counted as round-trip coverage here:
/// </para>
/// <list type="bullet">
///   <item>
///     <b>Unit tests of the codec only</b> (in
///     <c>tools/cli-client/tests/CliClient.UnitTests/Opcodes/</c>) —
///     those verify byte layout but not "the wire round-trips this".
///   </item>
///   <item>
///     <b>Named-opaque registrations</b> (the 207-opcode
///     <c>RegisterAllNamedOpaque</c> seed in <c>OpcodeRegistry</c>) —
///     opaque means "we recognise the opcode but don't decode the
///     payload". That's not coverage.
///   </item>
///   <item>
///     <b>Opcodes the client sends but whose reply we never
///     assert</b> (e.g. sector LOGIN/0x0002, which the Linux stub
///     today acks at the connection-state level only — no TCP
///     response to assert against). Add them here only when a
///     reply path lights up.
///   </item>
/// </list>
/// </remarks>
public static class TestedOpcodes
{
    /// <summary>
    /// The coverage floor. Bump up by one when you add an entry to
    /// <see cref="Opcodes"/>. NEVER decrease without a commit message
    /// explaining what deleted coverage and why it was OK to delete.
    /// </summary>
    public const int MinTestedCount = 25;

    /// <summary>
    /// Every opcode with round-trip coverage in this suite, with an
    /// inline citation to the test that proves it. Sorted by opcode
    /// value for stable diffs.
    /// </summary>
    public static readonly IReadOnlyList<TestedOpcode> Opcodes = new[]
    {
        new TestedOpcode(0x0000, "VERSION_REQUEST",
            "Opcodes/VersionRequestTests.cs — client sends VersionRequest, asserts on the typed-decoded VersionResponse status."),
        new TestedOpcode(0x0001, "VERSION_RESPONSE",
            "Opcodes/VersionRequestTests.cs — server reply decoded via VersionResponseCodec, status field asserted for all three branches (current/old/new)."),
        new TestedOpcode(0x0002, "LOGIN",
            "Opcodes/SectorLoginTests.cs FullSectorLogin_ReceivesStart — client sends the 14-byte sector LOGIN payload over TCP 3500 after MasterJoin handoff; observed by the server reaching login stage 1 (PlayerManager.cpp:534) and emitting the first 0x2020 LOGIN_STAGE_S_C frame in reply."),
        new TestedOpcode(0x0005, "START",
            "Opcodes/SectorLoginTests.cs FullSectorLogin_ReceivesStart — received on TCP 3500 as the server's final emit after all 13 login stages complete (PlayerManager::CompleteLogin → SendStart, PlayerConnection.cpp:1068). Test asserts the start_id field comes through as a non-zero int32_t — the wire form that was off-by-4-bytes before the sizeof(long)→int32_t sweep."),
        new TestedOpcode(0x0006, "START_ACK",
            "Opcodes/SectorStartAckTests.cs StartAck_DoesNotBreakConnection_RequestTimeStillRoundTrips — client sends an empty-payload 0x0006 on the sector connection after receiving 0x0005 START. Server Player::HandleStartAck (server/src/PlayerConnection.cpp:1603) calls SetActive(true) and runs the MAX_SECTOR_ID-gated SendLoginCamera call; proxy ClientToServer_linux_stubs.cpp:413-449 forwards START_ACK and synthesises a 0x3008 STARBASE_LOGIN_COMPLETE (sector>9999) or 0x3004 PLAYER_SHIP_SENT (sector<9999) follow-up to the server. There is no direct reply for a freshly-created starbase character (every StartSector[] entry in StaticData.h:63-74 is >9999, so SendLoginCamera's CAMERA_CONTROL emit is skipped — the retail server explicitly does this and CLAUDE.md forbids fabricating a reply). Test instead asserts pipe survival: send START_ACK, send REQUEST_TIME, observe CLIENT_SET_TIME with our echoed sentinel tick. Times out (rather than asserting) if the server crashes on HandleStartAck, the proxy crashes on the synthesised 0x3008 follow-up, or the proxy tears down the UDP plane on the SetLoginComplete(true) transition."),
        new TestedOpcode(0x0012, "TURN",
            "Opcodes/SectorTurnTiltTests.cs TurnAndTilt_DoNotBreakConnection_RequestTimeStillRoundTrips — client sends a canonical 8-byte TURN payload {int32 GameID; float Intensity} on the sector connection after STAGE handshake completes. Server Player::HandleTurn (server/src/PlayerConnection.cpp:1793) reads the local PacketTurn struct and calls Moveable::Turn(intensity) which updates m_Turn_Intensity on the player's physics object. Fan-out happens later via SendPositionalUpdate on a sector tick and is sent to OTHER observers in the visibility list (never back to the originator) so we can't directly assert. Wave 11 regression: local PacketTurn was `{ long GameID; float Intensity }` — on Win32 sizeof(long)==4 so the struct matched the 8B wire payload exactly; on Linux x86_64 sizeof(long)==8 so the struct became 12B and Intensity read from offset 8 — past the end of the 8B payload into undefined memory, potentially yielding a non-finite float that would corrupt the physics tick on the next move. Test is a survival probe: send TURN, send TILT, send REQUEST_TIME, assert CLIENT_SET_TIME echoes our sentinel tick — proves server didn't crash, proxy didn't drop the UDP plane, recv path didn't desync."),
        new TestedOpcode(0x0013, "TILT",
            "Opcodes/SectorTurnTiltTests.cs TurnAndTilt_DoNotBreakConnection_RequestTimeStillRoundTrips — client sends a canonical 8-byte TILT payload {int32 GameID; float Intensity} on the sector connection after TURN. Server Player::HandleTilt (server/src/PlayerConnection.cpp:1815) reads the local PacketTurn struct and calls Moveable::Tilt(intensity). Same Wave 11 sizeof(long) regression as 0x0012 TURN — the same local struct definition was shared by both handlers. Same survival-probe shape and rationale as TURN."),
        new TestedOpcode(0x0014, "MOVE",
            "Opcodes/SectorMoveTests.cs Move_EngineOn_DoesNotBreakConnection_RequestTimeStillRoundTrips — client sends a canonical 5-byte MovePacket payload {int32 GameID; byte type=1} on the sector connection after STAGE handshake completes. Server Player::HandleMove (server/src/PlayerConnection.cpp:1843) reads the MovePacket struct and dispatches: type==4 = engine off, anything else = engine on. The handler calls AbortProspecting, optionally LeaveFormation, FormationEngineOperation, and Move(type) — all pure state mutators; visible effect fans out later via SendPositionalUpdate to OTHER observers (never back to originator). Test is a survival probe: send MOVE, send REQUEST_TIME, assert CLIENT_SET_TIME echoes our sentinel tick. Catches: (a) PacketStructures.h MovePacket long→int32_t regression (would grow struct to 9B and read type from offset 8 past end of 5B payload into garbage); (b) proxy ProcessSectorServerOpcode for MOVE (proxy/ClientToServer_linux_stubs.cpp:487-489) dropping the bottom-of-switch ForwardClientOpcode; (c) WarpDrive() guard inversion or FormationEngineOperation/AbortProspecting null-deref."),
        new TestedOpcode(0x001D, "MESSAGE_STRING",
            "Opcodes/SectorChatTests.cs GroupChat_WhenUngrouped_ReceivesNotInGroupErrorString — received on TCP 3500 as the server's reply to a Group-channel 0x0033 sent by an ungrouped player. The frame rides the full server→client UDP fan-out path: Player::SendMessageString (PlayerConnection.cpp:10918) → SendOpcode(0x001D) (PlayerConnection.cpp:127, the Phase K sizeof(int32_t) header fix) → m_UDPQueue → SendPacketCache → 0x2016 PACKET_SEQUENCE on UDP → proxy UDPClient::SendClientPacketSequence (proxy/UDPProxyToClient_linux.cpp:531) → SendResponse over TCP. Test decodes the [u16 length][u8 color][string\\0] payload and asserts the body contains the literal substring \"not in a group\"."),
        new TestedOpcode(0x002C, "ACTION",
            "Opcodes/SectorActionTests.cs Action_NoOpSubAction_DoesNotBreakConnection_RequestTimeStillRoundTrips — client sends a canonical 16-byte ActionPacket payload {int32 GameID; int32 Action=23; int32 Target=0; int32 OptionalVar=0} on the sector connection after STAGE handshake completes. Server Player::HandleAction (server/src/PlayerConnection.cpp:3708) dispatches on Action through a 30-ish entry switch; sub-action 23 (\"keep trading???\") is a literal commented-out no-op (line 4104). No reply emitted — the retail server doesn't emit one on this branch either, and CLAUDE.md forbids fabricating one. Test is a survival probe: send ACTION, send REQUEST_TIME, assert CLIENT_SET_TIME echoes our sentinel tick. Catches: (a) PacketStructures.h ActionPacket long→int32_t regression (would make Action read from offset 8 instead of 4, miss case 23, hit default UNRECOGNIZED ACTION printf); (b) proxy ProcessSectorServerOpcode for ACTION (proxy/ClientToServer_linux_stubs.cpp:471-477) dropping or double-forwarding; (c) HandleAction's pre-switch GetObjectFromID(Target=0) null-deref."),
        new TestedOpcode(0x0033, "CLIENT_CHAT",
            "Opcodes/SectorChatTests.cs GroupChat_WhenUngrouped_ReceivesNotInGroupErrorString — client sends a Type=Group ClientChatMessage with non-slash content; server's Player::HandleClientChat (PlayerConnection.cpp:4544) dispatches the chat->Type==1 branch, sees GroupID()==-1, and routes to SendVaMessage with the literal \"Error: You are not in a group!\" — the simplest server-state-independent CLIENT_CHAT branch that produces a deterministic single-frame reply."),
        new TestedOpcode(0x0034, "CLIENT_SET_TIME",
            "Opcodes/SectorRequestTimeTests.cs RequestTime_RoundTripsClientSentTickAndReturnsServerTimes — server emits the 12-byte ClientSetTime struct {ClientSent, ServerReceived, ServerSent} (all int32_t LE per common/include/net7/PacketStructures.h:563) in reply to 0x0044 REQUEST_TIME. Test asserts (a) payload size is exactly 12B (would be 24B if anyone reverted the Phase R PacketStructures long→int32_t migration on this struct), (b) ClientSent equals the unique tick we sent (catches HandleRequestTime sizeof(long) over-reads — pre-Wave-9 it read 8B from a 4B wire slot and echoed garbage), (c) ServerSent >= ServerReceived (catches field-order or byte-order regressions)."),
        new TestedOpcode(0x0035, "MASTER_JOIN",
            "Opcodes/MasterJoinTests.cs — live send into proxy on 3801, ServerRedirect reply asserted; AND Verification/CaptureReplayTests.cs — retail capture_1 frame 220 decoded + codec round-trip identity."),
        new TestedOpcode(0x0036, "SERVER_REDIRECT",
            "Opcodes/MasterJoinTests.cs — received as the reply to MASTER_JOIN; AND Verification/CaptureReplayTests.cs — retail capture_1 frame 222 decoded with field-by-field assertions."),
        new TestedOpcode(0x0044, "REQUEST_TIME",
            "Opcodes/SectorRequestTimeTests.cs RequestTime_RoundTripsClientSentTickAndReturnsServerTimes — client sends a 4-byte int32_t LE tick on the sector connection after STAGE handshake completes. Server Player::HandleRequestTime (server/src/PlayerConnection.cpp:1619) reads the tick and calls SendClientSetTime which replies with 0x0034 carrying the echoed tick + two server-side ticks. Pre-Wave-9 the handler read `*((long *) data)` = 8 bytes on Linux from a 4-byte wire slot; the Wave 9 fix casts to `int32_t*` so the round-trip echoes the exact value we sent."),
        new TestedOpcode(0x006D, "GLOBAL_CONNECT",
            "Opcodes/GlobalConnectTests.cs — client sends GlobalConnect with a real Net7SSL-issued ticket; round-trip drives the Phase K proxy↔server global UDP plane (UDP 3810)."),
        new TestedOpcode(0x006E, "GLOBAL_TICKET_REQUEST",
            "Opcodes/GlobalTicketRequestTests.cs — client sends GlobalTicketRequest with slot=0 against a seeded account that has no avatars; exercises HandleGlobalTicketRequest's wire-size fix (server/src/UDP_Global.cpp:200,237) — without those fixes the slot index decoded into the username's length prefix and the AVATARLOGIN_CONFIRM reply was 8B instead of 4B."),
        new TestedOpcode(0x006F, "GLOBAL_TICKET",
            "Opcodes/GlobalTicketRequestTests.cs — received as the proxy's failure-path reply (response_code=1002 galaxy full) after SendAvatarLogin's WaitForResponse times out; decoded via GlobalTicketCodec which verifies the Phase K 68B canonical Win32 size (was 72B on Linux pre-int32_t migration)."),
        new TestedOpcode(0x0070, "GLOBAL_AVATAR_LIST",
            "Opcodes/GlobalConnectTests.cs — received as the reply to GlobalConnect after the proxy's SendTicket UDP round-trip to the server's HandleGlobalOpcode dispatcher and back; ALSO Opcodes/GlobalDeleteCharacterTests.cs — received as the refreshed-list reply after 0x0071 DELETE."),
        new TestedOpcode(0x0071, "GLOBAL_DELETE_CHARACTER",
            "Opcodes/GlobalDeleteCharacterTests.cs — client sends GlobalDeleteCharacter with slot=0 against a seeded account with no avatars; exercises the proxy+server PacketMethods.h ExtractLong wire-size fix (cast long* → int32_t* so the read width matches the 4-byte wire width on Linux x86_64). Without the fix, ExtractLong pulls 8 bytes from a 4-byte slot field plus 4 bytes of the next LP-string length prefix, the server's GetAvatarID rejects the bogus slot, the delete silently no-ops, and the 0x200D → 0x2003 round-trip times out at WaitForResponse(~5s). ALSO Opcodes/GlobalCreateCharacterTests.cs — used at the test's tail to clean up the created character."),
        new TestedOpcode(0x0072, "GLOBAL_CREATE_CHARACTER",
            "Opcodes/GlobalCreateCharacterTests.cs — client sends a 539-byte canonical Win32 GlobalCreateCharacter payload (Terran Warrior, slot 0, 'Testavus' / 'TestShip') against the seeded cli_test03 account; the proxy forwards as 0x200B CREATE_AVATAR over UDP 3810 and the test asserts the refreshed GlobalAvatarList carries the new character (race=0, profession=0, sector=10151 Luna, account_id=9000003). Failure detector for the Phase K ColorInfo wire-size fix (`long metal` → `int32_t metal` — pre-fix ColorInfo was 21B, ShipData 226B, GlobalCreateCharacter 571B vs canonical 539B), and for the GlobalAvatarListCodec AvatarData offset fix (race/profession/gender/mood at 46/50/54/58, not 48/52/56/60 — the struct is __attribute__((packed)) and has no implicit padding after filler1+avatar_version)."),
        new TestedOpcode(0x0075, "GLOBAL_ERROR",
            "Opcodes/GlobalConnectTests.cs — StressTestClosedAccount_GlobalConnect_ReturnsGlobalErrorCode12 sends GlobalConnect for a status=0 (STRESS_TEST_CLOSED) seed account; the server emits 0x2004 GLOBAL_ERROR err=12 on UDP 3810, the proxy forwards it as 0x0075, and the test asserts both the error code (12) and that the message text from the proxy's g_GlobalErrorMsg[12] table comes through (validates the table wasn't truncated at 11 entries)."),
        new TestedOpcode(0x009F, "STARBASE_ROOM_CHANGE",
            "Opcodes/SectorStarbaseRoomChangeTests.cs RoomChange_DoesNotBreakConnection_RequestTimeStillRoundTrips — client sends a canonical 12-byte StarbaseRoomChange payload {int32 AvatarID=0; int32 NewRoom=1; int32 OldRoom=0} on the sector connection after STAGE handshake completes. Server Player::HandleStarbaseRoomChange (server/src/PlayerClass.cpp:631) reads the struct, mutates m_Room/m_Oldroom under m_Mutex, and fans out 0x00A0 STARBASE_ROOM_CHANGE to OTHER players in the sector (single-player integration test has no other observers so fan-out loop is empty — no direct reply to assert). Test is a survival probe: send STARBASE_ROOM_CHANGE, send REQUEST_TIME, assert CLIENT_SET_TIME echoes our sentinel tick. Catches: (a) PacketStructures.h StarbaseRoomChange long→int32_t regression (would grow struct to 24B; NewRoom would read from offset 8 instead of 4 and OldRoom from offset 16 instead of 8, both past end of 12B payload into undefined memory); (b) proxy ProcessSectorServerOpcode for STARBASE_ROOM_CHANGE (proxy/ClientToServer_linux_stubs.cpp:491-496) failing to fall through to bottom-of-switch ForwardClientOpcode; (c) m_Mutex deadlock or GetSectorPlayerList iteration crash. Per CLAUDE.md server-integrity: the wire shape is exactly what retail Win32 client emits when user clicks a starbase doorway — OldRoom=0/NewRoom=1 is a typical first-room-transition value."),
        new TestedOpcode(0x2020, "LOGIN_STAGE_S_C",
            "Opcodes/SectorLoginTests.cs FullSectorLogin_ReceivesStart — emitted by the server inside 0x2016 PACKET_SEQUENCE wrappers as the login state machine advances (PlayerManager.cpp:540-609). The proxy's HandleStageConfirm consumes them (UDPProxyToClient_linux.cpp:HandleStageConfirm) and auto-replies 0x2021 ACKs on the client's behalf; the test observes the round-trip indirectly by seeing the server progress to SendStart (0x0005) within the test deadline."),
        new TestedOpcode(0x2021, "LOGIN_STAGE_ACK_C_S",
            "Opcodes/SectorLoginTests.cs FullSectorLogin_ReceivesStart — auto-emitted by the proxy after each 0x2020 LOGIN_STAGE_S_C; consumed by the server's HandleLoginAckReturn (PlayerConnection.cpp:661) which uses the 4-byte int32_t stage_id (was 8-byte sizeof(long) pre-fix). Test observes correct round-trip via the server reaching CompleteLogin within the deadline — wire-size regression would stall the stage progression."),
    };
}

/// <summary>One row in the <see cref="TestedOpcodes.Opcodes"/> table.</summary>
public sealed record TestedOpcode(ushort Value, string SymbolicName, string TestCitation)
{
    public OpcodeId AsOpcodeId() => new OpcodeId(Value);
}
