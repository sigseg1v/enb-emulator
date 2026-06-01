// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Repl;
using Xunit;

namespace N7.CliClient.UnitTests.Repl;

public sealed class CompletionTests
{
    private static readonly IReadOnlyList<CommandSpec> Specs = new[]
    {
        new CommandSpec("connect",  true,  "<ip:127.0.0.1>"),
        new CommandSpec("dump-on",  true,  null),
        new CommandSpec("dump-off", false, null),          // not available
        new CommandSpec("help",     true,  null),
        new CommandSpec("quit",     true,  null),
        new CommandSpec("login",    false, "<user> <pass>"), // not available
    };

    [Fact]
    public void AvailableNames_OnlyAvailable_Sorted()
    {
        Assert.Equal(
            new[] { "connect", "dump-on", "help", "quit" },
            Completion.AvailableNames(Specs));
    }

    [Fact]
    public void FirstTokenCandidates_EmptyBuffer_ReturnsAllAvailable()
    {
        Assert.Equal(
            new[] { "connect", "dump-on", "help", "quit" },
            Completion.FirstTokenCandidates("", Specs));
    }

    [Fact]
    public void FirstTokenCandidates_FiltersByPrefix_CaseInsensitive()
    {
        Assert.Equal(new[] { "connect" }, Completion.FirstTokenCandidates("CO", Specs));
    }

    [Fact]
    public void FirstTokenCandidates_UnavailableCommand_NotOffered()
    {
        Assert.Empty(Completion.FirstTokenCandidates("login", Specs));
    }

    [Fact]
    public void FirstTokenCandidates_PastFirstToken_Empty()
    {
        Assert.Empty(Completion.FirstTokenCandidates("connect ", Specs));
        Assert.True(Completion.PastFirstToken("connect 127.0.0.1"));
    }

    [Fact]
    public void Ghost_EmptyBuffer_ListsAvailableCommands()
    {
        string ghost = Completion.Ghost("", Specs);
        Assert.Contains("connect", ghost);
        Assert.Contains("dump-on", ghost);
        Assert.DoesNotContain("login", ghost); // unavailable
    }

    [Fact]
    public void Ghost_PartialCommand_ShowsRemainderOfBestMatch()
    {
        Assert.Equal("nnect", Completion.Ghost("co", Specs));
    }

    [Fact]
    public void Ghost_MultipleMatches_ShowsCountTail()
    {
        // "dump-on" is the only available dump* command (dump-off hidden),
        // so a single match -> no (+N) tail.
        string ghost = Completion.Ghost("d", Specs);
        Assert.Equal("ump-on", ghost);
    }

    [Fact]
    public void Ghost_CommandCompleteNoArg_ShowsPlaceholder()
    {
        Assert.Equal("<ip:127.0.0.1>", Completion.Ghost("connect ", Specs));
    }

    [Fact]
    public void Ghost_ArgBeingTyped_HidesPlaceholder()
    {
        Assert.Equal("", Completion.Ghost("connect 127", Specs));
    }

    [Fact]
    public void Ghost_NoArgCommand_NoPlaceholder()
    {
        Assert.Equal("", Completion.Ghost("help ", Specs));
    }
}
