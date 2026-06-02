// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

namespace N7.CliClient.Repl;

/// <summary>
/// Coordinates asynchronous "write a line above the prompt" output -- the chat
/// echo fired from the background sector drain thread -- with the
/// <see cref="LineEditor"/>'s in-place prompt rendering, so an async line never
/// lands in the middle of the line the user is typing on.
/// </summary>
/// <remarks>
/// <para>
/// The problem: the editor draws its prompt + buffer + grey ghost on a single
/// line and parks the cursor there, then blocks polling for the next key. If a
/// chat frame arrives in that window and is written straight to the console, it
/// is appended to (and visually fused with) the prompt line -- exactly the
/// "the chat msg overwrites / prepends my prompt completion" report.
/// </para>
/// <para>
/// The fix: the editor publishes, on every render, the exact escape sequence
/// that redraws its current prompt line (<see cref="Emit"/>). An async writer
/// calls <see cref="TryWriteLineAbove"/>, which -- holding the same lock the
/// editor renders under -- erases the prompt line, prints the message on its
/// own line, then replays the published redraw so the prompt reappears
/// untouched beneath the new output. When no interactive prompt is active
/// (piped stdout, between lines, command output in flight) it returns false and
/// the caller writes the line plainly.
/// </para>
/// <para>
/// One instance is shared for the life of the REPL: the editor (re)activates it
/// per line, the session's chat echo writes through it.
/// </para>
/// </remarks>
public sealed class LivePrompt
{
    private readonly object _gate = new();
    private TextWriter? _out;
    private string? _redraw;   // full "\r\x1b[2K<prompt><buf><ghost>" + cursor-park
    private bool _active;

    /// <summary>The editor has begun rendering a line on <paramref name="output"/>.</summary>
    internal void Activate(TextWriter output)
    {
        lock (_gate)
        {
            _out = output;
            _redraw = null;
            _active = true;
        }
    }

    /// <summary>The editor finished the current line (Enter / EOF / cancel).</summary>
    internal void Deactivate()
    {
        lock (_gate)
        {
            _active = false;
            _redraw = null;
        }
    }

    /// <summary>
    /// Atomically write the editor's current prompt-line sequence and remember
    /// it, so a concurrent <see cref="TryWriteLineAbove"/> can restore it.
    /// Called by the editor's render on every keystroke.
    /// </summary>
    /// <param name="output">The editor's output writer.</param>
    /// <param name="redrawSequence">
    /// The complete sequence that draws the current prompt line and parks the
    /// cursor: <c>\r</c> + erase-line + prompt + buffer + grey ghost, then the
    /// cursor-park (<c>\r</c> + forward-cursor).
    /// </param>
    internal void Emit(TextWriter output, string redrawSequence)
    {
        lock (_gate)
        {
            _out = output;
            _redraw = redrawSequence;
            output.Write(redrawSequence);
            output.Flush();
        }
    }

    /// <summary>
    /// If an interactive prompt is currently on screen, erase it, print
    /// <paramref name="text"/> on its own line, and redraw the prompt beneath.
    /// Returns <c>false</c> when no prompt is active -- the caller should then
    /// write the line plainly (non-TTY, between lines, or during a command).
    /// </summary>
    public bool TryWriteLineAbove(string text)
    {
        lock (_gate)
        {
            if (!_active || _out is null || _redraw is null) return false;
            _out.Write("\r\x1b[2K");   // erase the prompt line the cursor sits on
            _out.Write(text);
            _out.Write('\n');
            _out.Write(_redraw);       // redraw prompt + buffer + ghost, re-park cursor
            _out.Flush();
            return true;
        }
    }
}
