// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

namespace N7.CliClient.Auth;

/// <summary>
/// Result of an <c>/AuthLogin</c> request. The server returns one of
/// two text-body shapes (no JSON, no XML — this is 2003-era Westwood):
/// <code>
///   Valid=TRUE\r\nTicket=&lt;ticket&gt;\r\n
///   Valid=False\r\n
/// </code>
/// </summary>
/// <param name="Valid">
/// True iff the server returned <c>Valid=TRUE</c>. Note: the failure
/// response uses <c>Valid=False</c> (mixed case) — both halves are
/// quoted from <c>login-server/Net7SSL/LinuxAuth.cpp:382-408</c>.
/// </param>
/// <param name="Ticket">
/// The session ticket the server issued; empty when
/// <see cref="Valid"/> is false. Carry this opaque blob through every
/// subsequent connection in the global → master → sector chain.
/// </param>
/// <param name="RawBody">
/// The full response body the server returned. Kept for diagnostics
/// and so the packet log can record exactly what we saw.
/// </param>
public sealed record AuthLoginResponse(
    bool Valid,
    string Ticket,
    string RawBody)
{
    /// <summary>
    /// Parse the text body of an <c>/AuthLogin</c> response. The body
    /// is line-oriented; values are <c>Key=Value\r\n</c>. We accept
    /// <c>\r\n</c> or <c>\n</c> line endings — the C++ server emits
    /// <c>\r\n</c> but be lenient on the parsing side.
    /// </summary>
    public static AuthLoginResponse Parse(string body)
    {
        ArgumentNullException.ThrowIfNull(body);

        bool valid = false;
        string ticket = string.Empty;

        foreach (string rawLine in body.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            int eq = line.IndexOf('=');
            if (eq <= 0)
                continue;

            string key = line[..eq];
            string value = line[(eq + 1)..];

            if (key.Equals("Valid", StringComparison.Ordinal))
            {
                // Server emits "TRUE" on success and "False" on failure
                // — match the success token explicitly so a typo doesn't
                // silently authenticate.
                valid = value.Equals("TRUE", StringComparison.Ordinal);
            }
            else if (key.Equals("Ticket", StringComparison.Ordinal))
            {
                ticket = value;
            }
        }

        return new AuthLoginResponse(valid, ticket, body);
    }
}
