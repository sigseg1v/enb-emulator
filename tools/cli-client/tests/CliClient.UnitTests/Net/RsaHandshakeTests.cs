// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

using System.Buffers.Binary;
using N7.CliClient.Net;
using Xunit;

namespace N7.CliClient.UnitTests.Net;

public sealed class RsaHandshakeTests
{
    [Fact]
    public void ClientKeyPacket_HasExpectedSize()
    {
        Assert.Equal(68, RsaHandshake.ClientKeyPacketSize);
        Assert.Equal(74, RsaHandshake.ServerPubkeyPacketSize);
    }

    [Fact]
    public void ClientKeyPacket_StartsWithBigEndianLength64()
    {
        byte[] key = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

        byte[] wire = RsaHandshake.BuildClientKeyPacketFromKey(key);

        Assert.Equal(68, wire.Length);
        uint length = BinaryPrimitives.ReadUInt32BigEndian(wire.AsSpan(0, 4));
        Assert.Equal(64u, length);
    }

    /// <summary>
    /// Full client-side handshake → server-side extraction round trip.
    /// Client builds the wire bytes; we then play the server's role
    /// (RSA-decrypt the 64-byte block, pull the RC4 key out of the
    /// reversed positions) and verify we get the same 8 bytes back.
    /// This is the proof that our handshake is wire-compatible with
    /// the C++ server's <c>DoKeyExchange</c>.
    /// </summary>
    [Fact]
    public void Client_BuildsPacket_Server_RecoversSameKey()
    {
        byte[] sessionKey = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD, 0x11, 0x22, 0x33, 0x44 };

        byte[] wire = RsaHandshake.BuildClientKeyPacketFromKey(sessionKey);

        // Skip the 4-byte length prefix.
        ReadOnlySpan<byte> encryptedBlock = wire.AsSpan(4, WestwoodRSA.BlockSize);

        byte[] decryptedBlock = new byte[WestwoodRSA.BlockSize];
        WestwoodRSA.DecryptBlock(encryptedBlock, decryptedBlock);

        byte[] recovered = RsaHandshake.ExtractSessionKeyFromDecryptedBlock(decryptedBlock);

        Assert.Equal(sessionKey, recovered);
    }

    [Fact]
    public void Client_RandomKey_RoundTripsThroughServer()
    {
        (byte[] wire, byte[] sessionKey) = RsaHandshake.BuildClientKeyPacket();

        Assert.Equal(68, wire.Length);
        Assert.Equal(8, sessionKey.Length);

        byte[] decryptedBlock = new byte[WestwoodRSA.BlockSize];
        WestwoodRSA.DecryptBlock(wire.AsSpan(4, WestwoodRSA.BlockSize), decryptedBlock);

        byte[] recovered = RsaHandshake.ExtractSessionKeyFromDecryptedBlock(decryptedBlock);

        Assert.Equal(sessionKey, recovered);
    }

    [Fact]
    public void ClientKeyPacket_ZeroExcludingKeyPositions()
    {
        // The C++ client zeroes the whole 64-byte buffer, then writes
        // only positions [63..56]. Decrypting should yield exactly that
        // shape (60 leading zeros, then 8 reversed key bytes).
        byte[] sessionKey = new byte[] { 0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80 };
        byte[] wire = RsaHandshake.BuildClientKeyPacketFromKey(sessionKey);

        byte[] decryptedBlock = new byte[WestwoodRSA.BlockSize];
        WestwoodRSA.DecryptBlock(wire.AsSpan(4, WestwoodRSA.BlockSize), decryptedBlock);

        for (int i = 0; i < WestwoodRSA.BlockSize - WestwoodRC4.KeySize; i++)
            Assert.Equal(0, decryptedBlock[i]);

        // positions [56..63] hold sessionKey REVERSED.
        for (int i = 0; i < WestwoodRC4.KeySize; i++)
            Assert.Equal(sessionKey[i], decryptedBlock[WestwoodRSA.BlockSize - 1 - i]);
    }

    [Fact]
    public void BuildClientKeyPacketFromKey_WrongKeyLength_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => RsaHandshake.BuildClientKeyPacketFromKey(new byte[7]));
        Assert.Throws<ArgumentException>(
            () => RsaHandshake.BuildClientKeyPacketFromKey(new byte[9]));
    }
}
