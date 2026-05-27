// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

namespace N7.CliClient.IntegrationTests;

/// <summary>
/// Mirror of <c>Fixtures/seed.sql</c>: the deterministic test-account
/// pool ServerFixture seeds after docker compose comes up. Tests pick
/// an account by index. Within a single test run the accounts and
/// their IDs are stable.
/// </summary>
/// <remarks>
/// <para>
/// All accounts share the same plaintext password ("testpw"). The
/// hash stored in <c>accounts.password</c> is <c>UPPER(MD5(plaintext))</c>
/// per <c>login-server/Net7SSL/LinuxAuth.cpp:227</c>.
/// </para>
/// <para>
/// IDs start at 9_000_001 to stay clear of any real-account IDs the
/// dumps might one day carry (the dump's accounts.AUTO_INCREMENT is
/// 15_965). If you bump <see cref="Pool"/>, also update
/// <c>Fixtures/seed.sql</c>.
/// </para>
/// </remarks>
public sealed record TestAccount(int Id, string Username, string Password);

public static class TestAccounts
{
    public const string SharedPassword = "testpw";

    public static IReadOnlyList<TestAccount> Pool { get; } = new TestAccount[]
    {
        new(9_000_001, "cli_test01", SharedPassword),
        new(9_000_002, "cli_test02", SharedPassword),
        new(9_000_003, "cli_test03", SharedPassword),
        new(9_000_004, "cli_test04", SharedPassword),
        new(9_000_005, "cli_test05", SharedPassword),
        new(9_000_006, "cli_test06", SharedPassword),
        new(9_000_007, "cli_test07", SharedPassword),
        new(9_000_008, "cli_test08", SharedPassword),
        new(9_000_009, "cli_test09", SharedPassword),
        new(9_000_011, "cli_test11", SharedPassword),
        new(9_000_012, "cli_test12", SharedPassword),
        new(9_000_013, "cli_test13", SharedPassword),
        new(9_000_014, "cli_test14", SharedPassword),
        new(9_000_015, "cli_test15", SharedPassword),
        new(9_000_016, "cli_test16", SharedPassword),
        new(9_000_017, "cli_test17", SharedPassword),
        new(9_000_018, "cli_test18", SharedPassword),
        new(9_000_019, "cli_test19", SharedPassword),
        new(9_000_020, "cli_test20", SharedPassword),
        new(9_000_021, "cli_test21", SharedPassword),
        new(9_000_022, "cli_test22", SharedPassword),
        new(9_000_023, "cli_test23", SharedPassword),
        new(9_000_024, "cli_test24", SharedPassword),
        new(9_000_025, "cli_test25", SharedPassword),
        new(9_000_026, "cli_test26", SharedPassword),
        new(9_000_027, "cli_test27", SharedPassword),
        new(9_000_028, "cli_test28", SharedPassword),
        new(9_000_029, "cli_test29", SharedPassword),
    };

    /// <summary>
    /// Out-of-pool fixture for the STRESS_TEST_CLOSED path:
    /// <c>accounts.status = 0</c> in <c>seed.sql</c>. LinuxAuth
    /// (<c>login-server/Net7SSL/LinuxAuth.cpp</c>) does NOT inspect
    /// status, so login succeeds and a ticket is issued; the global
    /// UDP server (<c>server/src/UDP_Global.cpp:ProcessTicketInfo</c>)
    /// is what rejects with G_ERROR_STRESS_TEST_CLOSED (12), which the
    /// proxy then forwards to the client as a 0x0075 GLOBAL_ERROR.
    /// Kept out of <see cref="Pool"/> so the harness smoke-test's
    /// per-account checks don't have to special-case it.
    /// </summary>
    public static TestAccount StressTestClosed { get; } =
        new(9_000_010, "cli_test_status0", SharedPassword);
}
