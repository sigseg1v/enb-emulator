// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

namespace N7.CliClient.Session;

/// <summary>
/// Where the client is in the global → master → sector handoff chain.
/// Each transition closes the current TCP connection and opens a new
/// one (every TCP connection does its own RSA + RC4 handshake from
/// scratch — there's no session resumption at the transport layer).
/// </summary>
public enum SessionStage
{
    /// <summary>Not yet connected to anything.</summary>
    Disconnected = 0,

    /// <summary>TLS-authenticated against Net7SSL; we hold a 20-byte ticket.</summary>
    Authenticated = 1,

    /// <summary>TCP-connected to the global server, RSA+RC4 handshake complete.</summary>
    Global = 2,

    /// <summary>Redirected to the master server, RSA+RC4 handshake complete.</summary>
    Master = 3,

    /// <summary>Redirected to a specific sector server, RSA+RC4 handshake complete.</summary>
    Sector = 4,
}
