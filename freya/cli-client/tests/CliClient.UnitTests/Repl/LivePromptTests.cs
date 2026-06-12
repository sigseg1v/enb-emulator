// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Collections.Generic;
using System.Threading;
using N7.CliClient.Repl;
using Xunit;

namespace N7.CliClient.UnitTests.Repl;

/// <summary>
/// Pins the contract that keeps async chat echo from clobbering the prompt:
/// <see cref="LivePrompt"/> writes the message on its own line (erasing the
/// prompt first) and replays the editor's published redraw beneath it. Only
/// active while the interactive editor is rendering a line; otherwise the
/// caller is told to write plainly.
/// </summary>
public sealed class LivePromptTests
{
    private const string Erase = "\r\x1b[2K";   // CR + clear-whole-line

    [Fact]
    public void TryWriteLineAbove_BeforeActivate_ReturnsFalseAndWritesNothing()
    {
        var sw = new StringWriter();
        var lp = new LivePrompt();
        lp.Emit(sw, "IGNORED");  // publishing without an active line is a no-op contract-wise
        // (Emit itself writes -- that models the editor's own render -- so clear it.)
        sw.GetStringBuilder().Clear();

        // Without Activate there is no live prompt to protect.
        Assert.False(new LivePrompt().TryWriteLineAbove("hi"));
    }

    [Fact]
    public void TryWriteLineAbove_ActiveButNothingRenderedYet_ReturnsFalse()
    {
        var sw = new StringWriter();
        var lp = new LivePrompt();
        lp.Activate(sw);
        // Active, but the editor has not emitted a render -> nothing to redraw.
        Assert.False(lp.TryWriteLineAbove("hi"));
        Assert.Equal(string.Empty, sw.ToString());
    }

    [Fact]
    public void TryWriteLineAbove_AfterRender_ErasesPrintsRedraws()
    {
        var sw = new StringWriter();
        var lp = new LivePrompt();
        lp.Activate(sw);
        lp.Emit(sw, "REDRAW");        // the editor draws its prompt line
        Assert.Equal("REDRAW", sw.ToString());

        sw.GetStringBuilder().Clear();
        Assert.True(lp.TryWriteLineAbove("hello"));

        // erase the prompt line, print the message + newline, replay the redraw.
        Assert.Equal(Erase + "hello\n" + "REDRAW", sw.ToString());
    }

    [Fact]
    public void TryWriteLineAbove_MessageIsOnItsOwnLine_NotFusedToPrompt()
    {
        var sw = new StringWriter();
        var lp = new LivePrompt();
        lp.Activate(sw);
        lp.Emit(sw, Erase + "> co");   // a realistic prompt-line redraw
        sw.GetStringBuilder().Clear();

        Assert.True(lp.TryWriteLineAbove("<-- [sector] bob: hi"));
        string outp = sw.ToString();

        int msg = outp.IndexOf("<-- [sector] bob: hi", System.StringComparison.Ordinal);
        Assert.True(msg > 0);
        // the message is immediately preceded by a clear-line (prompt erased)...
        Assert.EndsWith(Erase, outp[..msg]);
        // ...and immediately followed by a newline (it owns its line).
        Assert.Equal('\n', outp[msg + "<-- [sector] bob: hi".Length]);
        // ...and the prompt is redrawn after that newline.
        Assert.EndsWith(Erase + "> co", outp);
    }

    [Fact]
    public void TryWriteLineAbove_AfterDeactivate_ReturnsFalse()
    {
        var sw = new StringWriter();
        var lp = new LivePrompt();
        lp.Activate(sw);
        lp.Emit(sw, "REDRAW");
        lp.Deactivate();
        sw.GetStringBuilder().Clear();

        Assert.False(lp.TryWriteLineAbove("hi"));
        Assert.Equal(string.Empty, sw.ToString());
    }

    [Fact]
    public void Reactivate_AfterStaleRedraw_DoesNotReplayOldLine()
    {
        var sw = new StringWriter();
        var lp = new LivePrompt();
        lp.Activate(sw);
        lp.Emit(sw, "OLD");
        lp.Deactivate();

        // A fresh line begins; until it renders, there is nothing to protect,
        // so the stale "OLD" redraw must not leak.
        lp.Activate(sw);
        sw.GetStringBuilder().Clear();
        Assert.False(lp.TryWriteLineAbove("hi"));
        Assert.Equal(string.Empty, sw.ToString());
    }

    // ── end-to-end through the real editor render path ──

    private static readonly IReadOnlyList<CommandSpec> Specs = new[]
    {
        new CommandSpec("connect", true, "<ip:127.0.0.1>"),
        new CommandSpec("create",  true, "<class> <firstname>"),
    };

    private static ConsoleKeyInfo Ch(char c)
        => new(c, (ConsoleKey)char.ToUpperInvariant(c), false, false, false);
    private static readonly ConsoleKeyInfo Enter = new('\r', ConsoleKey.Enter, false, false, false);

    /// <summary>
    /// Key source that fires <paramref name="onIdle"/> the first time the editor
    /// blocks for a key -- i.e. with the prompt rendered and the cursor parked,
    /// the exact window an async chat write must survive.
    /// </summary>
    private sealed class InterleavingKeys : IKeySource
    {
        private readonly Queue<ConsoleKeyInfo> _q;
        private readonly Action _onIdle;
        private bool _fired;
        public InterleavingKeys(IEnumerable<ConsoleKeyInfo> keys, Action onIdle)
        { _q = new Queue<ConsoleKeyInfo>(keys); _onIdle = onIdle; }
        public ConsoleKeyInfo? ReadKey(CancellationToken ct)
        {
            if (!_fired) { _fired = true; _onIdle(); }
            return _q.Count > 0 ? _q.Dequeue() : (ConsoleKeyInfo?)null;
        }
    }

    [Fact]
    public void Editor_AsyncWriteWhileBlocked_LandsAboveTheRenderedPrompt()
    {
        var sw = new StringWriter();
        var lp = new LivePrompt();
        var editor = new LineEditor(() => Specs, lp);

        bool wroteAbove = false;
        // The editor renders "> co" then blocks; we inject a chat line there.
        var keys = new InterleavingKeys(
            new[] { Enter },
            () => wroteAbove = lp.TryWriteLineAbove("<-- [sector] bob: hi"));

        // Pre-type "co" so the rendered line has content to restore.
        string? line = editor.RunInteractive("> ",
            new InterleavingKeysWithPrefix(keys, Ch('c'), Ch('o')),
            sw, CancellationToken.None);

        Assert.Equal("co", line);
        Assert.True(wroteAbove, "async write should have been coordinated, not refused");
        string outp = sw.ToString();
        // The chat line printed, erased-line first, on its own line.
        Assert.Contains(Erase + "<-- [sector] bob: hi\n", outp);
        // And the editor cleanly Deactivated by the time it returned: a write
        // now takes the plain path.
        Assert.False(lp.TryWriteLineAbove("after"));
    }

    /// <summary>Feeds prefix keys first, then delegates to the inner source.</summary>
    private sealed class InterleavingKeysWithPrefix : IKeySource
    {
        private readonly Queue<ConsoleKeyInfo> _prefix;
        private readonly IKeySource _inner;
        public InterleavingKeysWithPrefix(IKeySource inner, params ConsoleKeyInfo[] prefix)
        { _inner = inner; _prefix = new Queue<ConsoleKeyInfo>(prefix); }
        public ConsoleKeyInfo? ReadKey(CancellationToken ct)
            => _prefix.Count > 0 ? _prefix.Dequeue() : _inner.ReadKey(ct);
    }
}
