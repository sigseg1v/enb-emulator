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

    // ---- CompleteArgument (Tab fills the suggested argument value) ----

    private static readonly IReadOnlyList<CommandSpec> ArgSpecs = new[]
    {
        new CommandSpec("connect", true,  "<ip:127.0.0.1>"),
        new CommandSpec("login",   true,  "<user> <pass>"),               // no defaults
        new CommandSpec("chat",    true,  "[sector|gm|dev|beta|whisper] <message>"),
        new CommandSpec("off",     false, "<ip:127.0.0.1>"),              // unavailable
    };

    [Fact]
    public void CompleteArgument_FreshArg_FillsDefault()
    {
        Assert.Equal("connect 127.0.0.1", Completion.CompleteArgument("connect ", ArgSpecs));
    }

    [Fact]
    public void CompleteArgument_PartialIsPrefix_FillsDefault()
    {
        Assert.Equal("connect 127.0.0.1", Completion.CompleteArgument("connect 127", ArgSpecs));
    }

    [Fact]
    public void CompleteArgument_PartialNotPrefix_ReturnsNull()
    {
        Assert.Null(Completion.CompleteArgument("connect 999", ArgSpecs));
    }

    [Fact]
    public void CompleteArgument_AlreadyComplete_ReturnsNull()
    {
        // Buffer already equals the suggestion -> nothing to fill.
        Assert.Null(Completion.CompleteArgument("connect 127.0.0.1", ArgSpecs));
    }

    [Fact]
    public void CompleteArgument_SlotHasNoDefault_ReturnsNull()
    {
        // `<user>` carries no `:default`, so Tab has nothing to offer.
        Assert.Null(Completion.CompleteArgument("login ", ArgSpecs));
    }

    [Fact]
    public void CompleteArgument_BracketOptions_FillsFirst()
    {
        Assert.Equal("chat sector", Completion.CompleteArgument("chat ", ArgSpecs));
    }

    [Fact]
    public void CompleteArgument_BracketOptions_FillsFirstMatchingPrefix()
    {
        Assert.Equal("chat gm", Completion.CompleteArgument("chat g", ArgSpecs));
    }

    [Fact]
    public void CompleteArgument_BeforeCommandWord_ReturnsNull()
    {
        // Not past the command word yet -> arg completion is inert.
        Assert.Null(Completion.CompleteArgument("conn", ArgSpecs));
    }

    [Fact]
    public void CompleteArgument_PastLastSlot_ReturnsNull()
    {
        // connect has a single slot; a second arg has no slot to fill.
        Assert.Null(Completion.CompleteArgument("connect 127.0.0.1 ", ArgSpecs));
    }

    [Fact]
    public void CompleteArgument_UnavailableCommand_ReturnsNull()
    {
        Assert.Null(Completion.CompleteArgument("off ", ArgSpecs));
    }

    [Fact]
    public void CompleteArgument_SecondSlotNoDefault_ReturnsNull()
    {
        // First arg given; second slot `<pass>` has no default.
        Assert.Null(Completion.CompleteArgument("login alice ", ArgSpecs));
    }

    // ---- AvailableNames priority ordering ----

    [Fact]
    public void AvailableNames_HigherPriority_LeadsThenAlphabetical()
    {
        var specs = new[]
        {
            new CommandSpec("help",   true, null, 0),
            new CommandSpec("enter",  true, null, 100),
            new CommandSpec("create", true, null, 100),
            new CommandSpec("list",   true, null, 0),
        };
        // Priority 100 group first (create < enter alphabetically), then the
        // priority-0 group (help < list).
        Assert.Equal(
            new[] { "create", "enter", "help", "list" },
            Completion.AvailableNames(specs));
    }
}
