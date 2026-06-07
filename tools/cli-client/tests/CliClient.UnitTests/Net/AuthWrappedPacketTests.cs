// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System;
using System.Linq;
using N7.CliClient.Net;
using Xunit;

namespace N7.CliClient.UnitTests.Net;

/// <summary>
/// Byte-pins the per-packet C-&gt;S auth wrapper (Phase AH, AH-8/AH-9/AH-10).
/// The wire layout here MUST stay byte-identical to <c>struct
/// EnbUdpAuthWrapper</c> in common/include/net7/PacketStructures.h: a 17-byte
/// prefix (1 version byte + 16 token bytes) in front of the inner datagram.
/// These tests are the CLI half of the "three places in sync" rule -- they
/// lock the format the proxy prepends (proxy/UDPClient_linux.cpp) and the
/// server strips (server/src/UDPConnection.cpp). A drift in any of the three
/// breaks this build.
/// </summary>
public sealed class AuthWrappedPacketTests
{
    [Fact]
    public void WrapperConstants_MatchCHeader()
    {
        // Mirrors NET7_UDP_AUTH_WRAPPER_VERSION / NET7_UDP_AUTH_TOKEN_LEN and
        // sizeof(EnbUdpAuthWrapper) == 17 in PacketStructures.h.
        Assert.Equal(0x01, AuthWrappedPacket.WrapperVersion);
        Assert.Equal(16, AuthWrappedPacket.TokenLength);
        Assert.Equal(17, AuthWrappedPacket.WrapperSize);
    }

    [Fact]
    public void ToWire_PinsExactByteLayout()
    {
        byte[] token = Enumerable.Range(0, 16).Select(i => (byte)(0xA0 + i)).ToArray();
        byte[] innerBytes = { 0xDE, 0xAD, 0xBE, 0xEF };
        var packet = new AuthWrappedPacket(token, new UnwrappedPacket(innerBytes));

        byte[] wire = packet.ToWire();

        // Exactly 17 + inner.
        Assert.Equal(17 + innerBytes.Length, wire.Length);
        // Byte 0 is the version.
        Assert.Equal(0x01, wire[0]);
        // Bytes 1..16 are the token, verbatim and in order.
        Assert.Equal(token, wire.Skip(1).Take(16).ToArray());
        // Bytes 17.. are the inner datagram, byte-for-byte unchanged.
        Assert.Equal(innerBytes, wire.Skip(17).ToArray());
    }

    [Fact]
    public void WrapThenUnwrap_RoundTrips_InnerBytesIdentical()
    {
        byte[] token = Enumerable.Repeat((byte)0x5A, 16).ToArray();
        byte[] innerBytes = { 0x10, 0x00, 0x06, 0x00, 0x01, 0x00, 0x00, 0x00 };
        var original = new AuthWrappedPacket(token, new UnwrappedPacket(innerBytes));

        byte[] wire = original.ToWire();
        Assert.True(AuthWrappedPacket.TryParse(wire, out var parsed));

        Assert.Equal(original.Version, parsed.Version);
        Assert.Equal(original.AuthToken, parsed.AuthToken);
        // The whole point of the wrapper model: the inner bytes survive a
        // wrap/unwrap untouched, so nothing the CLI already pins changes.
        Assert.Equal(innerBytes, parsed.Inner.Bytes);
    }

    [Fact]
    public void ZeroToken_IsTheValidPreAuthForm()
    {
        // Before the proxy learns the ticket it sends an all-zero token; the
        // server skips the token check on pre-auth datagrams (player_id == 0).
        byte[] zero = new byte[16];
        byte[] innerBytes = { 0x01, 0x02 };
        var packet = new AuthWrappedPacket(zero, new UnwrappedPacket(innerBytes));

        byte[] wire = packet.ToWire();

        Assert.Equal(0x01, wire[0]);
        Assert.All(wire.Skip(1).Take(16), b => Assert.Equal(0, b));
    }

    [Fact]
    public void TryParse_TooShortForWrapper_ReturnsFalse()
    {
        // 16 bytes -- one short of the 17-byte wrapper.
        byte[] tooShort = new byte[16];
        Assert.False(AuthWrappedPacket.TryParse(tooShort, out _));
    }

    [Fact]
    public void Ctor_RejectsWrongTokenLength()
    {
        Assert.Throws<ArgumentException>(
            () => new AuthWrappedPacket(new byte[15], new UnwrappedPacket(new byte[] { 0x00 })));
    }
}
