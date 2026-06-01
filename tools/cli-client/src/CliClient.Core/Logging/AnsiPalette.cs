// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

namespace N7.CliClient.Logging;

/// <summary>
/// ANSI colour palette for the REPL `dump` view. Centralised here so every
/// per-opcode <see cref="N7.CliClient.Opcodes.Records.IPacketRecord"/>
/// renders with the same look and a single switch can turn colour off
/// for piped output.
/// </summary>
/// <remarks>
/// Auto-disables when stdout is redirected or when the env var
/// <c>NO_COLOR</c> is set (per https://no-color.org/). Callers can also
/// flip <see cref="Enabled"/> at runtime from a CLI flag.
/// </remarks>
public static class AnsiPalette
{
    public static bool Enabled { get; set; } = InitialEnabled();

    // 8-colour-safe foreground codes.
    public const string Reset        = "\x1b[0m";
    public const string Bold         = "\x1b[1m";
    public const string Dim          = "\x1b[2m";

    public const string Cyan         = "\x1b[36m";   // opcode header
    public const string BrightCyan   = "\x1b[96m";
    public const string Magenta      = "\x1b[35m";   // compare-mismatch
    public const string Yellow       = "\x1b[33m";   // suspicious value (zero, sentinel)
    public const string BrightYellow = "\x1b[93m";   // outbound packet header in dump-on live tail
    public const string Red          = "\x1b[31m";   // hard error flag
    public const string BrightRed    = "\x1b[91m";
    public const string Green        = "\x1b[32m";   // values, ok-compare
    public const string Blue         = "\x1b[34m";   // ASCII gutter
    public const string Gray         = "\x1b[90m";   // hex offsets

    // Per-byte background colours for the annotated hex view.
    //   KnownBg   = black fg + bright-green bg  (bytes covered by a decoded field)
    //   UnknownBg = black fg + 256-colour orange bg (bytes not yet decoded)
    public const string KnownBg   = "\x1b[30;102m";
    public const string UnknownBg = "\x1b[30;48;5;214m";

    private static bool InitialEnabled()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR")))
            return false;
        try
        {
            if (Console.IsOutputRedirected) return false;
        }
        catch
        {
            // Console may be unavailable (e.g. inside a test host); be safe.
            return false;
        }
        return true;
    }

    public static string Colorize(string code, string text) =>
        Enabled ? string.Concat(code, text, Reset) : text;
}
