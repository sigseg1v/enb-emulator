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
    };
}
