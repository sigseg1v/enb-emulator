// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Collections.Generic;
using N7.CliClient.Repl;
using Xunit;

namespace N7.CliClient.UnitTests.Repl;

public sealed class LineEditorTests
{
    // ── interactive editing-loop coverage (the path piped stdin never hits) ──

    private sealed class FakeKeys : IKeySource
    {
        private readonly Queue<ConsoleKeyInfo> _q;
        public FakeKeys(IEnumerable<ConsoleKeyInfo> keys) => _q = new Queue<ConsoleKeyInfo>(keys);
        public ConsoleKeyInfo? ReadKey(System.Threading.CancellationToken ct)
            => _q.Count > 0 ? _q.Dequeue() : (ConsoleKeyInfo?)null;
    }

    private static ConsoleKeyInfo Ch(char c)
        => new(c, (ConsoleKey)char.ToUpperInvariant(c), false, false, false);
    private static readonly ConsoleKeyInfo Tab     = new('\t', ConsoleKey.Tab, false, false, false);
    private static readonly ConsoleKeyInfo ShiftTab = new('\t', ConsoleKey.Tab, true, false, false);
    private static readonly ConsoleKeyInfo Enter   = new('\r', ConsoleKey.Enter, false, false, false);
    private static readonly ConsoleKeyInfo Back    = new('\b', ConsoleKey.Backspace, false, false, false);
    private static readonly ConsoleKeyInfo Right   = new('\0', ConsoleKey.RightArrow, false, false, false);
    private static readonly ConsoleKeyInfo Left    = new('\0', ConsoleKey.LeftArrow, false, false, false);

    private static readonly IReadOnlyList<CommandSpec> Specs = new[]
    {
        new CommandSpec("connect", true,  "<ip:127.0.0.1>"),
        new CommandSpec("create",  true,  "<class> <firstname>"),
        new CommandSpec("help",    true,  null),
        new CommandSpec("quit",    true,  null),
        new CommandSpec("enter",   false, "<firstname>"),   // unavailable -> not a candidate
    };

    private static string? Run(params ConsoleKeyInfo[] keys)
    {
        var editor = new LineEditor(() => Specs);
        return editor.RunInteractive("> ", new FakeKeys(keys), new StringWriter(),
            System.Threading.CancellationToken.None);
    }

    [Fact]
    public void Tab_SingleCandidate_CompletesAndAppendsSpaceForArg()
    {
        // "conn" uniquely completes to "connect"; it takes an arg, so a
        // trailing space is added (so the placeholder ghost shows next).
        string? line = Run(Ch('c'), Ch('o'), Ch('n'), Ch('n'), Tab, Enter);
        Assert.Equal("connect ", line);
    }

    [Fact]
    public void Tab_CyclesForwardThroughMultipleCandidates()
    {
        // "c" matches connect, create (sorted). Tab -> connect, Tab -> create.
        // First Enter accepts the highlighted item (stays on the line so args
        // can follow); second Enter submits.
        string? line = Run(Ch('c'), Tab, Tab, Enter, Enter);
        Assert.Equal("create ", line);
    }

    [Fact]
    public void ShiftTab_CyclesBackwardToLastCandidate()
    {
        // Shift-Tab opens the menu on the last candidate ("create").
        string? line = Run(Ch('c'), ShiftTab, Enter, Enter);
        Assert.Equal("create ", line);
    }

    [Fact]
    public void Tab_PastCommandWord_FillsArgDefault()
    {
        // "connect " (command word done, fresh arg) -> Tab fills the
        // <ip:127.0.0.1> default rather than opening a command menu.
        string? line = Run(
            Ch('c'), Ch('o'), Ch('n'), Ch('n'), Ch('e'), Ch('c'), Ch('t'), Ch(' '),
            Tab, Enter);
        Assert.Equal("connect 127.0.0.1", line);
    }

    [Fact]
    public void Tab_PastCommandWord_CompletesTypedArgPrefix()
    {
        // A partially-typed arg that prefixes the default is completed, not
        // clobbered.
        string? line = Run(
            Ch('c'), Ch('o'), Ch('n'), Ch('n'), Ch('e'), Ch('c'), Ch('t'), Ch(' '),
            Ch('1'), Ch('2'), Ch('7'),
            Tab, Enter);
        Assert.Equal("connect 127.0.0.1", line);
    }

    [Fact]
    public void Tab_PastCommandWord_NoDefault_LeavesBufferUntouched()
    {
        // create's first slot is <class> with no default -> Tab is inert; the
        // buffer submits exactly as typed.
        string? line = Run(
            Ch('c'), Ch('r'), Ch('e'), Ch('a'), Ch('t'), Ch('e'), Ch(' '),
            Tab, Enter);
        Assert.Equal("create ", line);
    }

    [Fact]
    public void RightArrow_InMenu_PicksHighlightedCandidate()
    {
        // "c" opens the menu (connect, create); Tab highlights connect.
        // Right-arrow picks it -- same as Enter in the menu -- committing
        // "connect " (trailing space, it takes an arg). Enter then submits.
        string? line = Run(Ch('c'), Tab, Right, Enter);
        Assert.Equal("connect ", line);
    }

    [Fact]
    public void RightArrow_InMenu_PicksAfterCycling()
    {
        // Tab past connect to create, then right-arrow picks create.
        string? line = Run(Ch('c'), Tab, Tab, Right, Enter);
        Assert.Equal("create ", line);
    }

    [Fact]
    public void RightArrow_AtEnd_AcceptsArgGhost()
    {
        // "connect " then right-arrow fills the <ip:127.0.0.1> default,
        // fish-style -- the same value Tab fills past the command word.
        string? line = Run(
            Ch('c'), Ch('o'), Ch('n'), Ch('n'), Ch('e'), Ch('c'), Ch('t'), Ch(' '),
            Right, Enter);
        Assert.Equal("connect 127.0.0.1", line);
    }

    [Fact]
    public void RightArrow_AtEnd_CompletesCommandWord()
    {
        // "conn" uniquely prefixes connect; right-arrow at end completes the
        // command word (and adds the trailing arg-space) without a menu.
        string? line = Run(Ch('c'), Ch('o'), Ch('n'), Ch('n'), Right, Enter);
        Assert.Equal("connect ", line);
    }

    [Fact]
    public void RightArrow_MidLine_MovesCursorNotAccept()
    {
        // "ab", Left (cursor between a and b), Right (back to end), then 'c'
        // appends -> "abc". Mid-line right-arrow is plain cursor motion, not
        // a suggestion-accept.
        string? line = Run(Ch('a'), Ch('b'), Left, Right, Ch('c'), Enter);
        Assert.Equal("abc", line);
    }

    [Fact]
    public void Backspace_EditsBuffer()
    {
        string? line = Run(Ch('h'), Ch('x'), Back, Ch('e'), Ch('l'), Ch('p'), Enter);
        Assert.Equal("help", line);
    }

    [Fact]
    public void Enter_OnPlainText_ReturnsBufferVerbatim()
    {
        string? line = Run(Ch('q'), Ch('u'), Ch('i'), Ch('t'), Enter);
        Assert.Equal("quit", line);
    }

    [Fact]
    public void ExhaustedKeys_NoEnter_ReturnsNull()
    {
        // No Enter in the stream -> source runs dry -> clean null (cancel/EOF).
        string? line = Run(Ch('h'), Ch('i'));
        Assert.Null(line);
    }
    // A StringReader is never Console.In, so the editor takes the plain
    // ReadLine fallback -- which is what the test harness and the
    // `just *-replay` recipes rely on.

    [Fact]
    public async Task ReadLine_NonTty_FallsBackToReadLine_AndWritesPrompt()
    {
        var editor = new LineEditor(() => Array.Empty<CommandSpec>());
        var input  = new StringReader("connect 1.2.3.4\n");
        var output = new StringWriter();

        string? line = await editor.ReadLineAsync("> ", input, output, CancellationToken.None);

        Assert.Equal("connect 1.2.3.4", line);
        Assert.Contains("> ", output.ToString());
    }

    [Fact]
    public async Task ReadLine_NonTty_Eof_ReturnsNull()
    {
        var editor = new LineEditor(() => Array.Empty<CommandSpec>());
        string? line = await editor.ReadLineAsync(
            "> ", new StringReader(""), new StringWriter(), CancellationToken.None);
        Assert.Null(line);
    }
}
