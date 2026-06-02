// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;
using N7.CliClient.Logging;

namespace N7.CliClient.Repl;

/// <summary>
/// A zsh-style interactive line editor: context-aware command suggestions
/// in grey, Tab / Shift-Tab menu cycling, Right-arrow or Enter to pick, and
/// a grey argument placeholder once a command is chosen.
/// </summary>
/// <remarks>
/// <para>
/// Interactive only on a real terminal. When stdin or stdout is redirected
/// (the test harness, the <c>just *-replay</c> recipes, anything piping the
/// REPL) it falls back to a plain prompt + <see cref="TextReader.ReadLineAsync"/>
/// so non-TTY callers behave exactly as before.
/// </para>
/// <para>
/// Rendering is single-line: each change rewrites the prompt line in place
/// (CR + clear-line), draws the buffer plus a grey ghost tail, then parks
/// the cursor. Async output from the chat/dump drain can still interleave
/// while idle; the next keystroke redraws the line.
/// </para>
/// </remarks>
public sealed class LineEditor : ILineInput
{
    private readonly Func<IReadOnlyList<CommandSpec>> _specs;
    private readonly int _pollMs;
    private readonly IKeySource? _keys;

    public LineEditor(Func<IReadOnlyList<CommandSpec>> specs, int pollMs = 15)
        : this(specs, null, pollMs) { }

    // Test seam: inject a deterministic key source instead of the console.
    internal LineEditor(Func<IReadOnlyList<CommandSpec>> specs, IKeySource? keys, int pollMs = 15)
    {
        _specs = specs ?? throw new ArgumentNullException(nameof(specs));
        _keys = keys;
        _pollMs = pollMs;
    }

    /// <summary>True when a real interactive terminal is attached both ways.</summary>
    public static bool IsInteractive =>
        !Console.IsInputRedirected && !Console.IsOutputRedirected;

    public async Task<string?> ReadLineAsync(
        string prompt, TextReader input, TextWriter output, CancellationToken ct)
    {
        // Non-TTY (piped stdin/stdout, or a non-console reader) -> plain path.
        if (!IsInteractive || !ReferenceEquals(input, Console.In))
        {
            await output.WriteAsync(prompt).ConfigureAwait(false);
            await output.FlushAsync(ct).ConfigureAwait(false);
            return await input.ReadLineAsync(ct).ConfigureAwait(false);
        }

        // Raw key editing runs on a worker so the poll loop can observe ct.
        IKeySource keys = _keys ?? new ConsoleKeySource(_pollMs);
        return await Task.Run(() => RunInteractive(prompt, keys, output, ct), ct).ConfigureAwait(false);
    }

    internal string? RunInteractive(string prompt, IKeySource keys, TextWriter output, CancellationToken ct)
    {
        var buffer = new StringBuilder();
        int cursor = 0;
        bool inMenu = false;
        var menu = new List<string>();
        int menuIndex = 0;
        string preMenu = string.Empty;
        int promptLen = VisibleLength(prompt);   // exclude ANSI bytes from column math

        Render(prompt, promptLen, buffer, cursor, inMenu, menu, menuIndex, output);

        while (true)
        {
            ConsoleKeyInfo? next = keys.ReadKey(ct);
            if (next is null)
            {
                // Cancelled, EOF, or the console went away -- give up cleanly.
                output.WriteLine();
                output.Flush();
                return null;
            }
            ConsoleKeyInfo k = next.Value;

            switch (k.Key)
            {
                case ConsoleKey.Enter:
                    if (inMenu)
                    {
                        AcceptCommand(buffer, menu[menuIndex]);
                        cursor = buffer.Length;
                        inMenu = false;
                        break;
                    }
                    // Final clean draw (no ghost), then newline + return.
                    output.Write('\r');
                    output.Write("\x1b[2K");
                    output.Write(prompt);
                    output.Write(buffer.ToString());
                    output.WriteLine();
                    output.Flush();
                    return buffer.ToString();

                case ConsoleKey.Tab:
                {
                    bool back = (k.Modifiers & ConsoleModifiers.Shift) != 0;
                    HandleTab(buffer, ref cursor, ref inMenu, menu, ref menuIndex, ref preMenu, back);
                    break;
                }

                case ConsoleKey.Backspace:
                    inMenu = false;
                    if (cursor > 0) { buffer.Remove(cursor - 1, 1); cursor--; }
                    break;

                case ConsoleKey.Delete:
                    inMenu = false;
                    if (cursor < buffer.Length) buffer.Remove(cursor, 1);
                    break;

                case ConsoleKey.LeftArrow:
                    inMenu = false; if (cursor > 0) cursor--; break;
                case ConsoleKey.RightArrow:
                    if (inMenu)
                    {
                        // Right-arrow picks the highlighted menu candidate, the
                        // same as Enter in the menu: commit the command word (+
                        // a trailing space when it takes args) and drop out of
                        // the menu so the argument can follow.
                        AcceptCommand(buffer, menu[menuIndex]);
                        cursor = buffer.Length;
                        inMenu = false;
                        break;
                    }
                    // At the end of the line, right-arrow also accepts the
                    // inline grey suggestion (fish-style) -- the argument
                    // default or the rest of the command word, whatever Tab
                    // would fill. Mid-line it is plain cursor motion.
                    if (cursor == buffer.Length && TryAcceptInlineGhost(buffer, ref cursor))
                        break;
                    if (cursor < buffer.Length) cursor++;
                    break;
                case ConsoleKey.Home:
                    inMenu = false; cursor = 0; break;
                case ConsoleKey.End:
                    inMenu = false; cursor = buffer.Length; break;

                case ConsoleKey.Escape:
                    if (inMenu) { buffer.Clear(); buffer.Append(preMenu); inMenu = false; }
                    else buffer.Clear();
                    cursor = buffer.Length;
                    break;

                default:
                    if (!char.IsControl(k.KeyChar))
                    {
                        inMenu = false;
                        buffer.Insert(cursor, k.KeyChar);
                        cursor++;
                    }
                    break;
            }

            Render(prompt, promptLen, buffer, cursor, inMenu, menu, menuIndex, output);
        }
    }

    private void HandleTab(
        StringBuilder buffer, ref int cursor, ref bool inMenu,
        List<string> menu, ref int menuIndex, ref string preMenu, bool back)
    {
        if (!inMenu)
        {
            string text = buffer.ToString();

            // Past the command word: fill the suggested argument value (e.g.
            // connect's <ip:127.0.0.1> default), not another command name.
            if (Completion.PastFirstToken(text))
            {
                string? filled = Completion.CompleteArgument(text, _specs());
                if (filled is not null)
                {
                    buffer.Clear();
                    buffer.Append(filled);
                    cursor = buffer.Length;
                }
                return;
            }

            var cands = Completion.FirstTokenCandidates(text, _specs());
            if (cands.Count == 0) return;

            preMenu = text;
            if (cands.Count == 1)
            {
                AcceptCommand(buffer, cands[0]);
                cursor = buffer.Length;
                return;
            }
            inMenu = true;
            menu.Clear();
            menu.AddRange(cands);
            menuIndex = back ? menu.Count - 1 : 0;
        }
        else
        {
            menuIndex = back
                ? (menuIndex - 1 + menu.Count) % menu.Count
                : (menuIndex + 1) % menu.Count;
        }

        // Show the highlighted candidate in the buffer (no trailing space
        // yet -- that would end the first token and stop Tab cycling).
        SetFirstToken(buffer, menu[menuIndex]);
        cursor = buffer.Length;
    }

    /// <summary>
    /// Accept the inline grey suggestion at the end of the line (the fish-style
    /// right-arrow): fill the argument placeholder default past the command
    /// word, or complete the command word from its best match. Returns false --
    /// so the caller falls back to plain cursor motion -- when there is nothing
    /// to accept (blank line, unknown word, or value already complete).
    /// </summary>
    private bool TryAcceptInlineGhost(StringBuilder buffer, ref int cursor)
    {
        string text = buffer.ToString();
        var specs = _specs();

        if (Completion.PastFirstToken(text))
        {
            string? filled = Completion.CompleteArgument(text, specs);
            if (filled is null) return false;
            buffer.Clear();
            buffer.Append(filled);
            cursor = buffer.Length;
            return true;
        }

        // Command word: only when a prefix has been typed -- a blank line's
        // ghost is the whole command list, not a single suggestion to accept.
        if (text.TrimStart().Length == 0) return false;
        var cands = Completion.FirstTokenCandidates(text, specs);
        if (cands.Count == 0) return false;
        AcceptCommand(buffer, cands[0]);
        cursor = buffer.Length;
        return true;
    }

    /// <summary>Replace the command word, preserving any args after it.</summary>
    private static void SetFirstToken(StringBuilder buffer, string name)
    {
        string text = buffer.ToString();
        int sp = text.IndexOf(' ');
        string rest = sp >= 0 ? text[sp..] : string.Empty;
        buffer.Clear();
        buffer.Append(name).Append(rest);
    }

    /// <summary>
    /// Commit a chosen command: the word plus a trailing space when it takes
    /// arguments, so the placeholder ghost appears immediately.
    /// </summary>
    private void AcceptCommand(StringBuilder buffer, string name)
    {
        var spec = _specs().FirstOrDefault(
            s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        buffer.Clear();
        buffer.Append(name);
        if (spec?.Placeholder is not null) buffer.Append(' ');
    }

    private void Render(
        string prompt, int promptLen, StringBuilder buffer, int cursor,
        bool inMenu, List<string> menu, int menuIndex, TextWriter output)
    {
        string text = buffer.ToString();
        string ghost = inMenu
            ? $"  ({menuIndex + 1}/{menu.Count}  Tab/Shift-Tab cycle, ->/Enter pick)"
            : Completion.Ghost(text, _specs());

        var sb = new StringBuilder();
        sb.Append('\r').Append("\x1b[2K").Append(prompt).Append(text);

        // Ghost only renders when colour is on -- as plain text it would be
        // indistinguishable from real input.
        if (ghost.Length > 0 && AnsiPalette.Enabled)
        {
            int width = SafeWidth();
            int avail = Math.Max(0, width - 1 - promptLen - text.Length);
            string ghostFit = ghost.Length > avail ? ghost[..avail] : ghost;
            if (ghostFit.Length > 0)
                sb.Append(ColorGhost(text, ghostFit, inMenu));
        }

        output.Write(sb.ToString());

        // Park the real cursor at promptLen + cursor (past the grey ghost).
        output.Write('\r');
        int col = promptLen + cursor;
        if (col > 0) output.Write($"\x1b[{col}C");
        output.Flush();
    }

    /// <summary>
    /// Colour the grey ghost tail. On an empty buffer the ghost is the list of
    /// available commands; the first one is the flow-preferred next step (see
    /// <see cref="CommandSpec.Priority"/>), so it is highlighted and the rest
    /// dimmed. Everything else (inline command remainder, argument
    /// placeholder, menu hint) stays muted.
    /// </summary>
    private static string ColorGhost(string text, string ghostFit, bool inMenu)
    {
        if (!inMenu && text.TrimStart().Length == 0)
        {
            int brk = ghostFit.IndexOf("  ", StringComparison.Ordinal);
            string accent = AnsiPalette.BrightCyan + AnsiPalette.Bold;
            if (brk < 0) return AnsiPalette.Colorize(accent, ghostFit);
            return AnsiPalette.Colorize(accent, ghostFit[..brk])
                 + AnsiPalette.Colorize(AnsiPalette.Gray, ghostFit[brk..]);
        }
        return AnsiPalette.Colorize(AnsiPalette.Gray, ghostFit);
    }

    /// <summary>Visible width of a string, ignoring CSI (ESC[...) escapes.</summary>
    private static int VisibleLength(string s)
    {
        int len = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] == '\x1b')
            {
                i++;                                   // skip ESC
                if (i < s.Length && s[i] == '[')       // CSI: run to the final letter
                    while (i < s.Length && !char.IsLetter(s[i])) i++;
                continue;                               // i lands on the final byte; loop ++ skips it
            }
            len++;
        }
        return len;
    }

    private static int SafeWidth()
    {
        try { int w = Console.WindowWidth; return w > 0 ? w : 80; }
        catch { return 80; }
    }
}

/// <summary>
/// Source of keystrokes for <see cref="LineEditor"/>. The production path
/// polls the console; tests inject a deterministic queue so the Tab/cycle/
/// ghost editing logic is exercised without a real TTY.
/// </summary>
internal interface IKeySource
{
    /// <summary>
    /// Block until a key is available, then return it. Returns null when the
    /// token is cancelled, input ends, or the console is unavailable.
    /// </summary>
    ConsoleKeyInfo? ReadKey(CancellationToken ct);
}

/// <summary>Polls <see cref="Console"/> for keystrokes (the runtime source).</summary>
internal sealed class ConsoleKeySource : IKeySource
{
    private readonly int _pollMs;
    public ConsoleKeySource(int pollMs) => _pollMs = pollMs;

    public ConsoleKeyInfo? ReadKey(CancellationToken ct)
    {
        try
        {
            while (!Console.KeyAvailable)
            {
                if (ct.IsCancellationRequested) return null;
                Thread.Sleep(_pollMs);
            }
        }
        catch (Exception)
        {
            return null; // console went away (redirected mid-run)
        }
        return Console.ReadKey(intercept: true);
    }
}
