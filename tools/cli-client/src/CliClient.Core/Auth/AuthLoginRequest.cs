// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

namespace N7.CliClient.Auth;

/// <summary>
/// Inputs for an EnB authentication request. Mirrors the four query
/// parameters the real Win32 client appends to <c>/AuthLogin</c>:
/// <c>username</c>, <c>password</c>, <c>serviceID</c>, <c>version</c>.
/// </summary>
/// <remarks>
/// <para>
/// Defaults for <see cref="ServiceId"/> and <see cref="Version"/> match
/// the values the retail client used and the Net-7 server expects.
/// Override if you have a packet capture proving the server accepted
/// different values for a specific build.
/// </para>
/// <para>
/// Credentials are passed plaintext over TLS (TLSv1.3), exactly as the
/// retail client did. Do not pre-hash; the server validates against its
/// own salted password store.
/// </para>
/// </remarks>
public sealed record AuthLoginRequest(
    string Username,
    string Password,
    string ServiceId = "EA-ENB",
    string Version = "2.5");
