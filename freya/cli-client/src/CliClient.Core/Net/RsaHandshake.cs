using System.Buffers.Binary;
using System.Security.Cryptography;

namespace N7.CliClient.Net;

public static class RsaHandshake
{
    public const int ServerPubkeyPacketSize = 74;

    public const int ClientKeyPacketSize = 4 + WestwoodRSA.BlockSize;

    public static (byte[] WireBytes, byte[] SessionKey) BuildClientKeyPacket(RandomNumberGenerator? rng = null)
    {
        byte[] sessionKey = new byte[WestwoodRC4.KeySize];
        if (rng is null)
            RandomNumberGenerator.Fill(sessionKey);
        else
            rng.GetBytes(sessionKey);

        return (BuildClientKeyPacketFromKey(sessionKey), sessionKey);
    }

    public static byte[] BuildClientKeyPacketFromKey(ReadOnlySpan<byte> sessionKey)
    {
        if (sessionKey.Length != WestwoodRC4.KeySize)
            throw new ArgumentException(
                $"session key must be {WestwoodRC4.KeySize} bytes, got {sessionKey.Length}",
                nameof(sessionKey));

        byte[] wire = new byte[ClientKeyPacketSize];

        BinaryPrimitives.WriteUInt32BigEndian(wire.AsSpan(0, 4), WestwoodRSA.BlockSize);

        Span<byte> block = wire.AsSpan(4, WestwoodRSA.BlockSize);
        for (int i = 0; i < WestwoodRC4.KeySize; i++)
        {
            block[WestwoodRSA.BlockSize - 1 - i] = sessionKey[i];
        }

        WestwoodRSA.EncryptBlock(block, block);

        return wire;
    }

    public static byte[] ExtractSessionKeyFromDecryptedBlock(ReadOnlySpan<byte> decryptedBlock)
    {
        if (decryptedBlock.Length != WestwoodRSA.BlockSize)
            throw new ArgumentException(
                $"block must be {WestwoodRSA.BlockSize} bytes, got {decryptedBlock.Length}",
                nameof(decryptedBlock));

        byte[] sessionKey = new byte[WestwoodRC4.KeySize];
        for (int i = 0; i < WestwoodRC4.KeySize; i++)
            sessionKey[i] = decryptedBlock[WestwoodRSA.BlockSize - 1 - i];

        return sessionKey;
    }
}
