// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

using N7.CliClient.Repl;
using Xunit;

namespace N7.CliClient.UnitTests.Repl;

public sealed class ReplTests
{
    private sealed class CapturingHandler : ICommandHandler
    {
        public string Name { get; }
        public string Summary => "test handler";
        public string Usage   => Name + " [args]";
        public List<IReadOnlyList<string>> Calls { get; } = new();
        public int ExitCode { get; set; }

        public CapturingHandler(string name, int exitCode = 0)
        {
            Name = name;
            ExitCode = exitCode;
        }

        public Task<int> ExecuteAsync(
            IReadOnlyList<string> args, TextWriter output, CancellationToken ct)
        {
            Calls.Add(args.ToArray());
            return Task.FromResult(ExitCode);
        }
    }

    [Fact]
    public void Tokenise_BasicWhitespaceSplit()
    {
        var tokens = N7.CliClient.Repl.Repl.Tokenise("chat hello world");
        Assert.Equal(new[] { "chat", "hello", "world" }, tokens);
    }

    [Fact]
    public void Tokenise_GroupsDoubleQuotedSegments()
    {
        var tokens = N7.CliClient.Repl.Repl.Tokenise("chat \"hello world\" team");
        Assert.Equal(new[] { "chat", "hello world", "team" }, tokens);
    }

    [Fact]
    public void Tokenise_CollapsesRepeatedWhitespace()
    {
        var tokens = N7.CliClient.Repl.Repl.Tokenise("  a   b\tc ");
        Assert.Equal(new[] { "a", "b", "c" }, tokens);
    }

    [Fact]
    public void Tokenise_UnterminatedQuote_AbsorbsRestOfLine()
    {
        var tokens = N7.CliClient.Repl.Repl.Tokenise("say \"hello there");
        Assert.Equal(new[] { "say", "hello there" }, tokens);
    }

    [Fact]
    public async Task Run_DispatchesToRegisteredHandler()
    {
        var repl = new N7.CliClient.Repl.Repl();
        var handler = new CapturingHandler("connect");
        repl.Register(handler);

        var input  = new StringReader("connect 127.0.0.1 3500\nquit\n");
        var output = new StringWriter();
        int rc = await repl.RunAsync(input, output);

        Assert.Equal(0, rc);
        Assert.Single(handler.Calls);
        Assert.Equal(new[] { "127.0.0.1", "3500" }, handler.Calls[0]);
    }

    [Fact]
    public async Task Run_PrintsErrorForUnknownCommand_KeepsLooping()
    {
        var repl = new N7.CliClient.Repl.Repl();
        var input  = new StringReader("nosuch foo\nquit\n");
        var output = new StringWriter();
        await repl.RunAsync(input, output);

        Assert.Contains("unknown command: nosuch", output.ToString());
        Assert.Contains("type 'help' for a list", output.ToString());
    }

    [Fact]
    public async Task Run_HelpListsAllCommands()
    {
        var repl = new N7.CliClient.Repl.Repl();
        repl.Register(new CapturingHandler("connect"));
        var input  = new StringReader("help\nquit\n");
        var output = new StringWriter();
        await repl.RunAsync(input, output);

        var text = output.ToString();
        Assert.Contains("help", text);
        Assert.Contains("quit", text);
        Assert.Contains("connect", text);
    }

    [Fact]
    public async Task Run_HelpForOneCommand_PrintsUsage()
    {
        var repl = new N7.CliClient.Repl.Repl();
        repl.Register(new CapturingHandler("connect"));
        var input  = new StringReader("help connect\nquit\n");
        var output = new StringWriter();
        await repl.RunAsync(input, output);

        Assert.Contains("connect: test handler", output.ToString());
        Assert.Contains("usage: connect [args]", output.ToString());
    }

    [Fact]
    public async Task Run_QuitExits_WithExitCodeZero()
    {
        var repl = new N7.CliClient.Repl.Repl();
        var input  = new StringReader("quit\n");
        var output = new StringWriter();
        Assert.Equal(0, await repl.RunAsync(input, output));
    }

    [Fact]
    public async Task Run_EofExits_WithLastExitCode()
    {
        var repl = new N7.CliClient.Repl.Repl();
        var bad = new CapturingHandler("bad", exitCode: 42);
        repl.Register(bad);
        var input  = new StringReader("bad\n");
        var output = new StringWriter();
        int rc = await repl.RunAsync(input, output);
        Assert.Equal(42, rc);
    }

    [Fact]
    public async Task Run_BlankLinesAreSkipped()
    {
        var repl = new N7.CliClient.Repl.Repl();
        var h = new CapturingHandler("ping");
        repl.Register(h);
        var input  = new StringReader("\n\n   \nping\nquit\n");
        var output = new StringWriter();
        await repl.RunAsync(input, output);
        Assert.Single(h.Calls);
    }

    [Fact]
    public async Task Run_HandlerThrows_IsCaughtAndReported_LoopContinues()
    {
        var repl = new N7.CliClient.Repl.Repl();
        repl.Register(new ThrowingHandler());
        var ping = new CapturingHandler("ping");
        repl.Register(ping);

        var input  = new StringReader("boom\nping\nquit\n");
        var output = new StringWriter();
        await repl.RunAsync(input, output);

        Assert.Contains("error: kaboom", output.ToString());
        Assert.Single(ping.Calls);
    }

    [Fact]
    public void Register_Null_Throws()
    {
        var repl = new N7.CliClient.Repl.Repl();
        Assert.Throws<ArgumentNullException>(() => repl.Register(null!));
    }

    [Fact]
    public void Register_HandlerWithEmptyName_Throws()
    {
        var repl = new N7.CliClient.Repl.Repl();
        Assert.Throws<ArgumentException>(() => repl.Register(new CapturingHandler("")));
    }

    [Fact]
    public void Find_IsCaseInsensitive()
    {
        var repl = new N7.CliClient.Repl.Repl();
        repl.Register(new CapturingHandler("Connect"));
        Assert.NotNull(repl.Find("connect"));
        Assert.NotNull(repl.Find("CONNECT"));
    }

    [Fact]
    public async Task Run_RegisteredCommand_CanReplaceBuiltIn()
    {
        var repl = new N7.CliClient.Repl.Repl();
        var custom = new CapturingHandler("quit");
        repl.Register(custom);
        var input  = new StringReader("quit\n");
        var output = new StringWriter();
        await repl.RunAsync(input, output);
        // Capturing handler returns 0 (not -1), so the loop does NOT exit
        // on its own — but EOF after the single 'quit' line ends it.
        Assert.Single(custom.Calls);
    }

    private sealed class ThrowingHandler : ICommandHandler
    {
        public string Name    => "boom";
        public string Summary => "throws";
        public string Usage   => "boom";
        public Task<int> ExecuteAsync(
            IReadOnlyList<string> args, TextWriter output, CancellationToken ct)
            => throw new InvalidOperationException("kaboom");
    }
}
