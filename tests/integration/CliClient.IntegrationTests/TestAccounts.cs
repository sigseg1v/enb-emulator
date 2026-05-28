// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Runtime.CompilerServices;

namespace N7.CliClient.IntegrationTests;

/// <summary>
/// Mirror of <c>Fixtures/seed.sql</c>: the deterministic test-account
/// pool ServerFixture seeds after docker compose comes up. Tests look
/// up their account by test-method name via <see cref="For"/>, which
/// defaults to <see cref="CallerMemberNameAttribute"/> so a test
/// usually writes <c>TestAccounts.For()</c> with no argument and the
/// compiler embeds the calling method name as the lookup key.
/// </summary>
/// <remarks>
/// <para>
/// All accounts share the same plaintext password ("testpw"). The
/// hash stored in <c>accounts.password</c> is <c>UPPER(MD5(plaintext))</c>
/// per <c>login-server/Net7SSL/LinuxAuth.cpp:227</c>.
/// </para>
/// <para>
/// Account IDs start at 9_000_001 to stay clear of any real-account IDs
/// the dumps might one day carry (the dump's accounts.AUTO_INCREMENT is
/// 15_965). Adding a new test that needs its own account: (1) pick an
/// unused id in 9_000_001..9_000_099, (2) add a row to
/// <c>Fixtures/seed.sql</c>, (3) add a key in <see cref="Assignments"/>
/// matching the test method name. Tests that only exercise login or
/// pure-read flows can re-use an existing entry (multiple keys may map
/// to the same <see cref="TestAccount"/>).
/// </para>
/// </remarks>
public sealed record TestAccount(int Id, string Username, string Password);

public static class TestAccounts
{
    public const string SharedPassword = "testpw";

    private static TestAccount A(int id, string username) =>
        new(id, username, SharedPassword);

    // Shared accounts that do not mutate per-avatar state and so can be
    // re-used across multiple tests (auth-only / global-only flows).
    // Keep these as named instances so the sharing is intentional rather
    // than accidental coincidence of indices.
    private static readonly TestAccount Auth01 = A(9_000_001, "cli_test01");

    /// <summary>
    /// Single source of truth: test method name → account. Keys are
    /// resolved automatically via <see cref="CallerMemberNameAttribute"/>
    /// at <see cref="For"/>'s call site; tests usually do not pass the
    /// name explicitly.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, TestAccount> Assignments =
        new Dictionary<string, TestAccount>(StringComparer.Ordinal)
        {
            // ---- Pure auth / TLS handshake — share Auth01 (no avatar mutation) ----
            ["ValidAccount_ReturnsValidTicket"]                                                       = Auth01,
            ["WrongPassword_ReturnsInvalid"]                                                          = Auth01,
            ["ValidAccount_LandsInGlobalStage_NoHealthTrip"]                                          = Auth01,
            ["WrongPassword_AbortsWithLoginRejection"]                                                = Auth01,
            ["ValidTicket_RoundTripsThroughUdpGlobalPlane_ReturnsAvatarList"]                         = Auth01,
            ["ValidSlot_ReturnsSuccessTicketWithGameId"]                                              = Auth01,
            ["ValidMasterJoin_ReceivesServerRedirect"]                                                = Auth01,

            // ---- Per-test dedicated accounts (create/delete characters → must not race) ----
            ["EmptySlot_ReturnsRefreshedAvatarList"]                                                  = A(9_000_002, "cli_test02"),
            ["ValidAvatar_AppearsInRefreshedAvatarList_AndCleanlyDeletes"]                            = A(9_000_003, "cli_test03"),
            ["FullSectorLogin_ReceivesStart"]                                                         = A(9_000_004, "cli_test04"),
            ["GroupChat_WhenUngrouped_ReceivesNotInGroupErrorString"]                                 = A(9_000_005, "cli_test05"),
            ["RequestTime_RoundTripsClientSentTickAndReturnsServerTimes"]                             = A(9_000_006, "cli_test06"),
            ["StartAck_DoesNotBreakConnection_RequestTimeStillRoundTrips"]                            = A(9_000_007, "cli_test07"),
            ["TurnAndTilt_DoNotBreakConnection_RequestTimeStillRoundTrips"]                           = A(9_000_008, "cli_test08"),
            ["Action_NoOpSubAction_DoesNotBreakConnection_RequestTimeStillRoundTrips"]                = A(9_000_009, "cli_test09"),
            ["Move_EngineOn_DoesNotBreakConnection_RequestTimeStillRoundTrips"]                       = A(9_000_011, "cli_test11"),
            ["RoomChange_DoesNotBreakConnection_RequestTimeStillRoundTrips"]                          = A(9_000_012, "cli_test12"),
            ["JobTerminal_DoesNotBreakConnection_RequestTimeStillRoundTrips"]                         = A(9_000_013, "cli_test13"),
            ["SkillUp_OnUntrainedSkill_DoesNotBreakConnection_RequestTimeStillRoundTrips"]            = A(9_000_014, "cli_test14"),
            ["VerbRequest_OnNonMatchingSubject_DoesNotBreakConnection_RequestTimeStillRoundTrips"]    = A(9_000_015, "cli_test15"),
            ["RequestTarget_OnNullTarget_ReceivesSetTargetWithSentinelTargetIdMinusOne"]              = A(9_000_016, "cli_test16"),
            ["RequestTargetsTarget_OnUnknownPlayer_ReceivesSetTargetWithLiteralZeroGameId"]           = A(9_000_017, "cli_test17"),
            ["Action2_NoOpSubActionAndEmptyName_DoesNotBreakConnection_RequestTimeStillRoundTrips"]   = A(9_000_018, "cli_test18"),
            ["Option_UnhandledOptionType_DoesNotBreakConnection_RequestTimeStillRoundTrips"]          = A(9_000_019, "cli_test19"),
            ["Debug_EmptyPayload_DoesNotBreakConnection_RequestTimeStillRoundTrips"]                  = A(9_000_020, "cli_test20"),
            ["ItemState_UnrecognisedInventoryByte_ReceivesUnrecognisedErrorString"]                   = A(9_000_021, "cli_test21"),
            ["AvatarEmote_EmoteTrigger_ReceivesAvatarEmoteResponseWithEchoedSentinel"]                = A(9_000_022, "cli_test22"),
            ["SkillAbility_OnUnknownAbilityIndex_ReceivesPriorityMessageNotYetWorking"]               = A(9_000_023, "cli_test23"),
            ["InventoryMove_UnrecognisedFromInv_ReceivesUnrecognisedErrorString"]                     = A(9_000_024, "cli_test24"),
            ["SelectTalkTree_NoCurrentNpc_ReceivesTalkTreeActionCloseSentinel"]                       = A(9_000_025, "cli_test25"),
            ["PetitionStuck_DoesNotBreakConnection_RequestTimeStillRoundTrips"]                       = A(9_000_026, "cli_test26"),
            ["MissionForfeit_EmptySlotZero_ReceivesNonForfeitableErrorString"]                        = A(9_000_027, "cli_test27"),
            ["LogoffRequest_AllZeroPayload_ReceivesLogoffConfirmationWithEmptyBody"]                  = A(9_000_028, "cli_test28"),
            ["ClientChatRequest_FriendStatusOnlyBranch_ReceivesClientChatEvent"]                      = A(9_000_029, "cli_test29"),
            ["TriggerEmote_DefaultPayload_ReceivesNotifyEmoteEchoed"]                                 = A(9_000_030, "cli_test30"),
            ["HandshakeEmitsClientShipAndAvatarDescription"]                                          = A(9_000_031, "cli_test31"),
            ["HandshakeEmitsFullSendLoginShipDataFanout"]                                             = A(9_000_032, "cli_test32"),
            ["StarbaseAvatarChange_OnUnknownAvatarId_DoesNotBreakConnection_RequestTimeStillRoundTrips"] = A(9_000_033, "cli_test33"),
            ["SkillStringRq_OnFreshCharNoTarget_DoesNotBreakConnection_RequestTimeStillRoundTrips"]   = A(9_000_034, "cli_test34"),
            ["ConfirmedActionResponse_NonMatchingPlayerId_DoesNotBreakConnection_RequestTimeStillRoundTrips"] = A(9_000_035, "cli_test35"),
            ["GuildRankNamesRequestClient_OnFreshCharNoGuild_DoesNotBreakConnection_RequestTimeStillRoundTrips"] = A(9_000_036, "cli_test36"),
            ["GuildSimpleClientSector_OnZeroTypePayload_DoesNotBreakConnection_RequestTimeStillRoundTrips"] = A(9_000_037, "cli_test37"),
            ["MissionDismissal_OutOfRangeMissionId_DoesNotBreakConnection_RequestTimeStillRoundTrips"] = A(9_000_038, "cli_test38"),
            ["EquipUse_OnEmptySlot_DoesNotBreakConnection_RequestTimeStillRoundTrips"]                = A(9_000_039, "cli_test39"),
            ["ManufactureTechLevelFilter_DisableZeroBitField_DoesNotBreakConnection_RequestTimeStillRoundTrips"] = A(9_000_040, "cli_test40"),
            ["ManufactureTerminal_TerminalZeroExit_DoesNotBreakConnection_RequestTimeStillRoundTrips"] = A(9_000_041, "cli_test41"),
            ["ManufactureCategorySelect_CategoryZeroDefault_DoesNotBreakConnection_RequestTimeStillRoundTrips"] = A(9_000_042, "cli_test42"),
            ["ManufactureAction_ModeNoneOuterSwitchDefault_DoesNotBreakConnection_RequestTimeStillRoundTrips"] = A(9_000_043, "cli_test43"),
            ["StarbaseLoginComplete_OnFreshChar_DoesNotBreakConnection_RequestTimeStillRoundTrips"]   = A(9_000_044, "cli_test44"),
            ["ManufactureSetItem_InvalidItemZero_DoesNotBreakConnection_RequestTimeStillRoundTrips"]  = A(9_000_045, "cli_test45"),
            ["RefinerySetItem_InvalidItemZero_DoesNotBreakConnection_RequestTimeStillRoundTrips"]     = A(9_000_046, "cli_test46"),
            ["InventorySort_UnrecognisedTargetInv_DoesNotBreakConnection_RequestTimeStillRoundTrips"] = A(9_000_047, "cli_test47"),
            ["ResendPacketSequence_MissPacketNum_DoesNotBreakConnection_RequestTimeStillRoundTrips"]  = A(9_000_048, "cli_test48"),
            ["HandshakeEmitsClientTypeAndGalaxyMapOnSpaceSectorLogin"]                                = A(9_000_049, "cli_test49"),
            ["HandshakeEmitsServerParametersOnSpaceSectorLogin"]                                      = A(9_000_050, "cli_test50"),
            ["HandshakeEmitsPlanetPositionalUpdateAndNavigationOnSpaceSectorLogin"]                   = A(9_000_051, "cli_test51"),
            ["StarbaseExitAction_ReceivesServerHandoffFrame"]                                         = A(9_000_052, "cli_test52"),
            ["StarbaseRecustomizeActions_ReceivesShipAndAvatarStartFrames"]                           = A(9_000_053, "cli_test53"),
            ["StarbaseAcceptJobAction9_ReceivesBareJobAcceptReply"]                                   = A(9_000_054, "cli_test54"),
            ["StarbaseTalkAction4_OnUnknownNpc_ReceivesFallbackTalkTree"]                             = A(9_000_055, "cli_test55"),
            ["CtaRequest_OnDefaultArmAction_ReceivesCtaResponseWithSuccessByte"]                      = A(9_000_056, "cli_test56"),
            ["OpenInterfaceSlashCommand_OnSlashOpenif_ReceivesOpenInterfaceEmit"]                     = A(9_000_057, "cli_test57"),
            ["UiTriggerSlashCommand_OnSlashUitrigger_ReceivesUiTriggerEmit"]                          = A(9_000_058, "cli_test58"),
            ["FgpsSlashCommand_OnSlashFgps_ReceivesConfirmedActionOfferAndClientSound"]               = A(9_000_059, "cli_test59"),
            ["ClientChatRequest_ListFriendsEmptyFriendList_ReceivesClientChatList"]                   = A(9_000_060, "cli_test60"),
            ["ClientChatRequest_SpeakLocallyNonexistentRecipient_ReceivesClientChatError"]            = A(9_000_061, "cli_test61"),
            ["StarbaseJobTerminalAction6_InNet7SolJtSector_ReceivesWellFormedJobList"]                = A(9_000_062, "cli_test62"),
            ["StarbaseJobTerminalAction7_InNet7SolJtSector_OnSentinelJobId_EchoesJobIdAsJobDescription"] = A(9_000_063, "cli_test63"),
            ["StarbaseAcceptJobAction8_OnSentinelJobId_ReceivesByteExact4ByteJobAcceptReply"]           = A(9_000_064, "cli_test64"),
            ["ManufactureSetManufactureId_EmittedDuringHandshake_HasExactly4BytePayload"]              = A(9_000_065, "cli_test65"),
            ["GalaxyMapRequest_OnFreshSession_DoesNotBreakConnection_RequestTimeStillRoundTrips"]      = A(9_000_066, "cli_test66"),
        };

    /// <summary>
    /// Look up the test account assigned to the calling test method.
    /// The default <see cref="CallerMemberNameAttribute"/> binding means
    /// the call site is just <c>TestAccounts.For()</c>; pass an explicit
    /// name only when the caller is a helper rather than the test itself.
    /// </summary>
    public static TestAccount For([CallerMemberName] string testName = "")
    {
        if (string.IsNullOrEmpty(testName))
            throw new ArgumentException(
                "TestAccounts.For() needs a test method name (either via CallerMemberName " +
                "from inside a test method, or passed explicitly).");

        if (Assignments.TryGetValue(testName, out var account))
            return account;

        throw new KeyNotFoundException(
            $"No TestAccount assigned for test '{testName}'. Add an entry to " +
            $"TestAccounts.Assignments and a matching row in Fixtures/seed.sql.");
    }

    /// <summary>
    /// All distinct accounts referenced by <see cref="Assignments"/>.
    /// Used by the HarnessSmokeTest seed validator; tests should call
    /// <see cref="For"/> instead.
    /// </summary>
    public static IReadOnlyCollection<TestAccount> All { get; } =
        Assignments.Values.Distinct().ToArray();

    /// <summary>
    /// Out-of-pool fixture for the STRESS_TEST_CLOSED path:
    /// <c>accounts.status = 0</c> in <c>seed.sql</c>. LinuxAuth
    /// (<c>login-server/Net7SSL/LinuxAuth.cpp</c>) does NOT inspect
    /// status, so login succeeds and a ticket is issued; the global
    /// UDP server (<c>server/src/UDP_Global.cpp:ProcessTicketInfo</c>)
    /// is what rejects with G_ERROR_STRESS_TEST_CLOSED (12), which the
    /// proxy then forwards to the client as a 0x0075 GLOBAL_ERROR.
    /// Kept out of <see cref="Assignments"/> so the harness smoke-test's
    /// per-account checks don't have to special-case it.
    /// </summary>
    public static TestAccount StressTestClosed { get; } =
        new(9_000_010, "cli_test_status0", SharedPassword);
}
