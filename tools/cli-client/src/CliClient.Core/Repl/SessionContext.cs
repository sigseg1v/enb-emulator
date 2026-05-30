// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Inbound;
using N7.CliClient.Session;

namespace N7.CliClient.Repl;

/// <summary>
/// Mutable session state shared between REPL commands. One instance is
/// constructed when the REPL starts and the commands pass it around to
/// keep `connect` -> `login` -> `list` -> `create` -> `enter` coherent.
///
/// <para>
/// Holds at most one long-lived global <see cref="EncryptedTcpConnection"/>
/// (the post-login global channel) and at most one in-sector
/// <see cref="EncryptedTcpConnection"/> after `enter` succeeds.
/// </para>
/// </summary>
public sealed class SessionContext : IAsyncDisposable
{
    // Hosts default to localhost; ports default to the dev-stack values
    // from docker-compose.yml. `connect` overrides Host and AuthPort.
    public string Host { get; set; } = "127.0.0.1";

    public int AuthPort { get; set; } = 4443;
    public int GlobalPort { get; set; } = 3805;
    public int MasterPort { get; set; } = 3801;
    public int SectorPort { get; set; } = 3500;

    /// <summary>Accept self-signed TLS certs (true for the docker dev stack).</summary>
    public bool AcceptUntrustedTls { get; set; } = true;

    public OpcodeRegistry Registry { get; }

    public string? Username { get; set; }

    /// <summary>20-byte auth ticket from /AuthLogin. Null until <c>login</c>.</summary>
    public string? Ticket { get; set; }

    /// <summary>Last decoded GlobalAvatarList from the active global channel.</summary>
    public GlobalAvatarList? AvatarList { get; set; }

    /// <summary>Long-lived global connection after <c>login</c>. Null otherwise.</summary>
    public EncryptedTcpConnection? Global { get; set; }

    /// <summary>Long-lived sector connection after <c>enter</c>. Null otherwise.</summary>
    public EncryptedTcpConnection? Sector { get; set; }

    /// <summary>PLAYER_TAG-bit-set avatar id allocated by GlobalTicketRequest.</summary>
    public int? GameId { get; set; }

    /// <summary>Slot of the avatar currently active in-sector.</summary>
    public int? ActiveSlot { get; set; }

    /// <summary>Sector id the active avatar entered.</summary>
    public int? ActiveSectorId { get; set; }

    public SessionContext(OpcodeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        Registry = registry;
    }

    public async ValueTask DisposeAsync()
    {
        if (Sector is not null) { await Sector.DisposeAsync(); Sector = null; }
        if (Global is not null) { await Global.DisposeAsync(); Global = null; }
    }
}
