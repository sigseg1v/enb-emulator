// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Opcodes;
using Xunit;

namespace N7.CliClient.IntegrationTests.Smoke;

/// <summary>
/// Tests for the test harness itself: do not require docker, do not
/// require a running server. They validate that the project references
/// Phase S's <c>CliClient.Core</c> correctly, that <see cref="RepoRoot"/>
/// resolves, and that the xUnit collection-fixture binding compiles.
///
/// <para>
/// Real server-touching tests come in Items 3+ and live in subdirs
/// like <c>Handshake/</c>, <c>Opcodes/</c>, <c>Workflows/</c>. Those
/// take a <c>ServerFixture</c> constructor parameter and are gated by
/// <c>[Collection(ServerCollection.Name)]</c>.
/// </para>
/// </summary>
public sealed class HarnessSmokeTest
{
    [Fact]
    public void RepoRoot_Resolves_ToDirectoryContainingDockerCompose()
    {
        var path = RepoRoot.Path;
        Assert.True(File.Exists(Path.Combine(path, "docker-compose.yml")),
            $"docker-compose.yml not found at '{path}'");
    }

    [Fact]
    public void CliClientCore_IsReferenced_OpcodeRegistryConstructs()
    {
        var registry = new OpcodeRegistry();
        int added = registry.RegisterAllNamedOpaque();
        Assert.Equal(OpcodeNames.Count, added);
        Assert.Equal(207, added);
    }

    [Fact]
    public void SeedSql_IsCopiedToOutput_AndMentionsEveryPooledAccount()
    {
        var seedPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "seed.sql");
        Assert.True(File.Exists(seedPath),
            $"Fixtures/seed.sql not next to the test assembly at '{seedPath}'");

        var seed = File.ReadAllText(seedPath);
        foreach (var account in TestAccounts.Pool)
        {
            Assert.Contains(account.Username, seed);
            Assert.Contains(account.Id.ToString(), seed);
        }
        Assert.Contains("UPPER(MD5('testpw'))", seed);
    }
}
