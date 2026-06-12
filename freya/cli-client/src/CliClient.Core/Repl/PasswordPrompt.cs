// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using N7.CliClient.Logging;

namespace N7.CliClient.Repl;

/// <summary>
/// Reads a password for <c>login</c> when it was not supplied on the command
/// line. A test seam so the login flow can be driven without a real console.
/// </summary>
public interface IPasswordPrompt
{
    /// <summary>
    /// Prompt for and read a password. On an interactive terminal the
    /// keystrokes are echoed as asterisks; when stdin is redirected (piped),
    /// the next line is read plainly (a pipe cannot be masked). Returns null on
    /// EOF (no password available).
    /// </summary>
    Task<string?> ReadPasswordAsync(string label, TextWriter output, CancellationToken ct);
}

/// <summary>
/// Console-backed <see cref="IPasswordPrompt"/>. Masks input with asterisks on
/// a TTY; falls back to reading a plain line from <see cref="Console.In"/> when
/// stdin is redirected -- which is the same stream the REPL reads, so a piped
/// <c>login &lt;user&gt;</c> followed by the password on the next line works.
/// </summary>
public sealed class ConsolePasswordPrompt : IPasswordPrompt
{
    public async Task<string?> ReadPasswordAsync(
        string label, TextWriter output, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(output);

        // Piped / redirected stdin: cannot mask, just take the next line. This
        // is what keeps `printf 'login alice\nsecret\n' | cli` working.
        if (Console.IsInputRedirected)
        {
            await output.WriteAsync(AnsiPalette.Muted(label)).ConfigureAwait(false);
            await output.FlushAsync(ct).ConfigureAwait(false);
            string? line = await Console.In.ReadLineAsync(ct).ConfigureAwait(false);
            // Echo a newline-terminated mask so the transcript shows the field
            // was consumed without leaking the length-bearing characters.
            await output.WriteLineAsync(line is null ? string.Empty : new string('*', line.Length))
                .ConfigureAwait(false);
            return line;
        }

        // Interactive TTY: read keys, echo '*', honour backspace.
        await output.WriteAsync(AnsiPalette.Muted(label)).ConfigureAwait(false);
        await output.FlushAsync(ct).ConfigureAwait(false);
        return await Task.Run(() => ReadMasked(output), ct).ConfigureAwait(false);
    }

    private static string ReadMasked(TextWriter output)
    {
        var sb = new System.Text.StringBuilder();
        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                output.WriteLine();
                break;
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (sb.Length > 0)
                {
                    sb.Length--;
                    output.Write("\b \b");   // erase the last asterisk
                    output.Flush();
                }
                continue;
            }
            // Ignore control keys that carry no character (arrows, F-keys, ...).
            if (char.IsControl(key.KeyChar)) continue;
            sb.Append(key.KeyChar);
            output.Write('*');
            output.Flush();
        }
        return sb.ToString();
    }
}
