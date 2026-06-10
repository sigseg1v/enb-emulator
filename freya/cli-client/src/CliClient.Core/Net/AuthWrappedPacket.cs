// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System;

namespace N7.CliClient.Net;

/// <summary>
/// The inner C-&gt;S datagram exactly as it travels today: the bytes the
/// plaintext CLI already builds and sends -- an <c>EnbUdpHeader</c> followed by
/// the opcode payload. Nothing about these bytes changes when DTLS is on; the
/// per-packet auth token rides OUTSIDE them as a separate wrapper (see
/// <see cref="AuthWrappedPacket"/>), so the existing CLI/integration tests that
/// pin inner bytes never have to change.
/// </summary>
/// <remarks>
/// This is the C# mirror of "the payload the server's recv edge hands to
/// DispatchDatagram after stripping the wrapper" -- see
/// server/src/UDPConnection.cpp and the wrapper struct in
/// common/include/net7/PacketStructures.h (<c>EnbUdpAuthWrapper</c>).
/// </remarks>
public readonly struct UnwrappedPacket
{
    /// <summary>The inner datagram bytes ([EnbUdpHeader][payload]).</summary>
    public byte[] Bytes { get; }

    public UnwrappedPacket(byte[] bytes)
    {
        Bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
    }

    public int Length => Bytes.Length;
}

/// <summary>
/// A C-&gt;S datagram with its per-packet auth token, the container the server
/// actually receives over the DTLS leg. On the wire it is the 17-byte
/// <c>EnbUdpAuthWrapper</c> (1 version byte + 16 token bytes) immediately
/// followed by the <see cref="UnwrappedPacket"/> bytes. The token is the
/// 16-byte binary form of the account's CSPRNG login-ticket suffix.
/// </summary>
/// <remarks>
/// Wire layout MUST stay byte-identical to <c>struct EnbUdpAuthWrapper</c> in
/// common/include/net7/PacketStructures.h. This C# model exists to keep the
/// three places that touch the format (server, proxy, CLI) in sync and to
/// byte-pin the layout in CliClient.UnitTests, even though the plaintext CLI
/// never emits a wrapper itself. The proxy prepends it (UDPClient_linux.cpp
/// UDP_Send) and the server strips it (UDPConnection.cpp); the token is only
/// enforced on gameplay datagrams (header player_id != 0).
/// </remarks>
public readonly struct AuthWrappedPacket
{
    /// <summary>Wrapper version byte. Mirrors NET7_UDP_AUTH_WRAPPER_VERSION.</summary>
    public const byte WrapperVersion = 0x01;

    /// <summary>Token length in bytes. Mirrors NET7_UDP_AUTH_TOKEN_LEN.</summary>
    public const int TokenLength = 16;

    /// <summary>Total wrapper prefix size: 1 version byte + 16 token bytes.</summary>
    public const int WrapperSize = 1 + TokenLength;

    /// <summary>The wrapper version byte as received/sent.</summary>
    public byte Version { get; }

    /// <summary>
    /// The 16-byte auth token. All-zero is the pre-auth form the proxy sends
    /// before it has learned the ticket; the server skips the token check on
    /// pre-auth datagrams (player_id == 0) anyway.
    /// </summary>
    public byte[] AuthToken { get; }

    /// <summary>The inner datagram the token authenticates.</summary>
    public UnwrappedPacket Inner { get; }

    public AuthWrappedPacket(byte[] authToken, UnwrappedPacket inner)
        : this(WrapperVersion, authToken, inner)
    {
    }

    public AuthWrappedPacket(byte version, byte[] authToken, UnwrappedPacket inner)
    {
        ArgumentNullException.ThrowIfNull(authToken);
        if (authToken.Length != TokenLength)
            throw new ArgumentException(
                $"auth token must be exactly {TokenLength} bytes", nameof(authToken));
        Version = version;
        AuthToken = authToken;
        Inner = inner;
    }

    /// <summary>
    /// Serialize to the on-wire form: [version][16 token bytes][inner bytes].
    /// </summary>
    public byte[] ToWire()
    {
        byte[] wire = new byte[WrapperSize + Inner.Length];
        wire[0] = Version;
        Buffer.BlockCopy(AuthToken, 0, wire, 1, TokenLength);
        Buffer.BlockCopy(Inner.Bytes, 0, wire, WrapperSize, Inner.Length);
        return wire;
    }

    /// <summary>
    /// Strip a wrapper from the front of a received datagram. Returns false if
    /// the buffer is too short to hold the 17-byte wrapper. (Version is parsed,
    /// not validated, so a caller can pin the exact byte and decide policy --
    /// the server rejects any version != <see cref="WrapperVersion"/>.)
    /// </summary>
    public static bool TryParse(ReadOnlySpan<byte> wire, out AuthWrappedPacket packet)
    {
        packet = default;
        if (wire.Length < WrapperSize)
            return false;

        byte version = wire[0];
        byte[] token = wire.Slice(1, TokenLength).ToArray();
        byte[] inner = wire.Slice(WrapperSize).ToArray();
        packet = new AuthWrappedPacket(version, token, new UnwrappedPacket(inner));
        return true;
    }
}
