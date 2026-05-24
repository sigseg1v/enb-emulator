// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

using System.Globalization;
using System.Text;

namespace N7.CliClient.IntegrationTests.Verification;

/// <summary>
/// Loader for the textual hex fixtures under <c>Fixtures/Captures/</c>.
/// Each fixture file is freeform: lines starting with <c>#</c> are
/// comments and ignored; everything else is interpreted as a stream of
/// hex bytes with arbitrary whitespace between them.
/// </summary>
/// <remarks>
/// Format chosen so the fixture files are reviewable in PR diffs and
/// can carry an inline citation to the capture frame they came from —
/// per the server-integrity rules, every captured byte stream used as
/// a test reference must cite its primary source.
/// </remarks>
public static class HexFixture
{
    /// <summary>
    /// Load a hex fixture by relative path under <c>Fixtures/Captures/</c>.
    /// </summary>
    /// <param name="relative">Path relative to the fixture directory,
    /// e.g. <c>"masterjoin_packet220.hex"</c>.</param>
    /// <returns>The decoded byte array.</returns>
    public static byte[] Load(string relative)
    {
        var path = Path.Combine(
            AppContext.BaseDirectory, "Fixtures", "Captures", relative);
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Hex fixture not found at '{path}'. " +
                "Check the csproj <None Include='Fixtures/**'> entry.",
                path);

        return Parse(File.ReadAllText(path));
    }

    /// <summary>
    /// Parse raw hex text. Comments (<c>#</c> to end of line) and
    /// whitespace are skipped. Throws on any non-hex non-whitespace
    /// non-comment character so silently-wrong fixtures fail loudly.
    /// </summary>
    public static byte[] Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var hex = new StringBuilder(text.Length);
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine;
            int hash = line.IndexOf('#');
            if (hash >= 0) line = line[..hash];

            foreach (char c in line)
            {
                if (char.IsWhiteSpace(c)) continue;
                if (!IsHexDigit(c))
                    throw new FormatException(
                        $"Unexpected character '{c}' in hex fixture (expected hex or whitespace or # comment).");
                hex.Append(c);
            }
        }

        if ((hex.Length & 1) != 0)
            throw new FormatException(
                $"Hex fixture has odd nibble count ({hex.Length}); each byte needs 2 hex chars.");

        byte[] bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            bytes[i] = byte.Parse(
                hex.ToString(i * 2, 2),
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture);
        }
        return bytes;
    }

    private static bool IsHexDigit(char c)
        => (c >= '0' && c <= '9')
        || (c >= 'a' && c <= 'f')
        || (c >= 'A' && c <= 'F');
}
