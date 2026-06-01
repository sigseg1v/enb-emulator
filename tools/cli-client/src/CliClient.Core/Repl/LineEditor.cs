// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;
using N7.CliClient.Logging;

namespace N7.CliClient.Repl;

/// <summary>
/// A zsh-style interactive line editor: context-aware command suggestions
/// in grey, Tab / Shift-Tab menu cycling, Enter to pick, and a grey
/// argument placeholder once a command is chosen.
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

    public LineEditor(Func<IReadOnlyList<CommandSpec>> specs, int pollMs = 15)
    {
        _specs = specs ?? throw new ArgumentNullException(nameof(specs));
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
        return await Task.Run(() => RunInteractive(prompt, output, ct), ct).ConfigureAwait(false);
    }

    private string? RunInteractive(string prompt, TextWriter output, CancellationToken ct)
    {
        var buffer = new StringBuilder();
        int cursor = 0;
        bool inMenu = false;
        var menu = new List<string>();
        int menuIndex = 0;
        string preMenu = string.Empty;
        int promptLen = prompt.Length;

        Render(prompt, promptLen, buffer, cursor, inMenu, menu, menuIndex, output);

        while (true)
        {
            try
            {
                while (!Console.KeyAvailable)
                {
                    if (ct.IsCancellationRequested) { output.WriteLine(); return null; }
                    Thread.Sleep(_pollMs);
                }
            }
            catch (Exception)
            {
                // Console went away (redirected mid-run) -- give up cleanly.
                return null;
            }

            ConsoleKeyInfo k = Console.ReadKey(intercept: true);

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
                    inMenu = false; if (cursor < buffer.Length) cursor++; break;
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
            var cands = Completion.FirstTokenCandidates(buffer.ToString(), _specs());
            if (cands.Count == 0) return;

            preMenu = buffer.ToString();
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
            ? $"  ({menuIndex + 1}/{menu.Count}  Tab/Shift-Tab cycle, Enter pick)"
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
                sb.Append(AnsiPalette.Colorize(AnsiPalette.Gray, ghostFit));
        }

        output.Write(sb.ToString());

        // Park the real cursor at promptLen + cursor (past the grey ghost).
        output.Write('\r');
        int col = promptLen + cursor;
        if (col > 0) output.Write($"\x1b[{col}C");
        output.Flush();
    }

    private static int SafeWidth()
    {
        try { int w = Console.WindowWidth; return w > 0 ? w : 80; }
        catch { return 80; }
    }
}
