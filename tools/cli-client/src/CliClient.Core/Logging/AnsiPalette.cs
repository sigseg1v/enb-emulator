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

    // 8-colour-safe codes. Bright variants stay readable on both dark and
    // light terminal themes; we deliberately avoid 256-colour codes.
    public const string Reset      = "[0m";
    public const string Bold       = "[1m";
    public const string Dim        = "[2m";

    public const string Cyan       = "[36m";    // opcode header
    public const string BrightCyan = "[96m";
    public const string Magenta    = "[35m";    // compare-mismatch
    public const string Yellow     = "[33m";    // suspicious value (zero, sentinel)
    public const string BrightYellow = "[93m";    // outbound packet header in dump-on live tail
    public const string Red        = "[31m";    // hard error flag
    public const string BrightRed  = "[91m";
    public const string Green      = "[32m";    // values, ok-compare
    public const string Blue       = "[34m";    // ASCII gutter
    public const string Gray       = "[90m";    // hex offsets

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
