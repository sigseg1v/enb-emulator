// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

namespace N7.CliClient.Workflows;

/// <summary>
/// Inputs to <see cref="ConnectAndLogin"/>. Defaults match the
/// dev-stack docker-compose layout (Net7SSL on localhost:443, proxy
/// on localhost:3500).
/// </summary>
public sealed class ConnectAndLoginOptions
{
    /// <summary>Login server hostname (Net7SSL).</summary>
    public string LoginHost { get; init; } = "127.0.0.1";

    /// <summary>Login server TLS port.</summary>
    public int LoginPort { get; init; } = 443;

    /// <summary>Global game server (proxy) hostname.</summary>
    public string GlobalHost { get; init; } = "127.0.0.1";

    /// <summary>Global game server (proxy) TCP port.</summary>
    public int GlobalPort { get; init; } = 3500;

    /// <summary>Account username for /AuthLogin.</summary>
    public required string Username { get; init; }

    /// <summary>Account password for /AuthLogin.</summary>
    public required string Password { get; init; }

    /// <summary>
    /// How long to sit on the global connection after handshake, draining
    /// inbound packets. The plan calls for 5s as the smoke-test target.
    /// </summary>
    public TimeSpan IdleDuration { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Whether to accept untrusted TLS certs on the login server. Dev
    /// stack uses a self-signed cert so this defaults to true; prod
    /// should override to false.
    /// </summary>
    public bool AcceptUntrustedLoginCertificate { get; init; } = true;
}
