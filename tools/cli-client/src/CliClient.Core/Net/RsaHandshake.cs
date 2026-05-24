// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

using System.Buffers.Binary;
using System.Security.Cryptography;

namespace N7.CliClient.Net;

/// <summary>
/// The client side of the Net-7 / Westwood RSA + RC4 handshake. Mirrors
/// <c>proxy/Connection.cpp::DoClientKeyExchange</c> byte-for-byte so the
/// CLI client looks identical to the real Win32 client on the wire.
/// </summary>
/// <remarks>
/// <para>
/// Wire dance (every TCP connection to a Net-7 server):
/// </para>
/// <list type="number">
///   <item>
///     Receive 74 bytes from the server. This is the server's RSA
///     pubkey + exponent in the format <c>GetModulus()</c> /
///     <c>GetPublicExponent()</c> emit (4-byte length, 0x00, 64-byte N,
///     4-byte length, e). We ignore the contents — the pubkey is fixed
///     and we already have it baked into <see cref="WestwoodRSA"/>.
///   </item>
///   <item>
///     Pick 8 random bytes — the RC4 session key.
///   </item>
///   <item>
///     Place those 8 bytes <b>reversed</b> at the END of a 64-byte
///     all-zero block (positions [63..56]).
///   </item>
///   <item>
///     RSA-encrypt the 64-byte block.
///   </item>
///   <item>
///     Send the 4-byte big-endian length (= 64) followed by the
///     64-byte ciphertext.
///   </item>
///   <item>
///     From here on, every byte we send or receive is RC4-encrypted
///     under the 8-byte session key (each direction gets its own
///     <see cref="WestwoodRC4"/> instance keyed off the same bytes).
///   </item>
/// </list>
/// <para>
/// The "reversed" placement at positions [63..56] is not a typo — it's
/// what the C++ client does (<c>DoClientKeyExchange</c>: <c>*dest-- = *src++</c>
/// starting at <c>key[WWRSA_BLOCK_SIZE - 1]</c>). Server-side
/// (<c>DoKeyExchange</c>) pulls the RC4 key back out of the decrypted
/// block from the same positions in the same reversed order.
/// </para>
/// </remarks>
public static class RsaHandshake
{
    /// <summary>Bytes the server sends as its "public key packet" — always 74 on EnB.</summary>
    public const int ServerPubkeyPacketSize = 74;

    /// <summary>
    /// Bytes the client sends back: 4-byte big-endian length + 64-byte
    /// RSA-encrypted block. Always 68 on EnB.
    /// </summary>
    public const int ClientKeyPacketSize = 4 + WestwoodRSA.BlockSize;

    /// <summary>
    /// Build the encrypted-RC4-key blob the client sends after receiving
    /// the server's pubkey. Returns the 68-byte buffer ready for the
    /// wire, plus the 8-byte plaintext RC4 session key the caller must
    /// feed into both <see cref="WestwoodRC4"/> instances (inbound +
    /// outbound).
    /// </summary>
    /// <param name="rng">RNG used for the 8-byte session key. Defaults
    /// to <see cref="RandomNumberGenerator.Fill(Span{byte})"/>.</param>
    public static (byte[] WireBytes, byte[] SessionKey) BuildClientKeyPacket(RandomNumberGenerator? rng = null)
    {
        byte[] sessionKey = new byte[WestwoodRC4.KeySize];
        if (rng is null)
            RandomNumberGenerator.Fill(sessionKey);
        else
            rng.GetBytes(sessionKey);

        return (BuildClientKeyPacketFromKey(sessionKey), sessionKey);
    }

    /// <summary>
    /// Same as <see cref="BuildClientKeyPacket"/> but takes a
    /// caller-supplied session key. Exposed so tests can use a fixed
    /// key for deterministic round-tripping.
    /// </summary>
    public static byte[] BuildClientKeyPacketFromKey(ReadOnlySpan<byte> sessionKey)
    {
        if (sessionKey.Length != WestwoodRC4.KeySize)
            throw new ArgumentException(
                $"session key must be {WestwoodRC4.KeySize} bytes, got {sessionKey.Length}",
                nameof(sessionKey));

        byte[] wire = new byte[ClientKeyPacketSize];

        // [0..4): big-endian length = 64 (the size of the RSA block we're about to send).
        BinaryPrimitives.WriteUInt32BigEndian(wire.AsSpan(0, 4), WestwoodRSA.BlockSize);

        // [4..68): 64-byte block — all zeros except positions [56..64)
        // which hold the RC4 key BYTE-REVERSED.
        Span<byte> block = wire.AsSpan(4, WestwoodRSA.BlockSize);
        // block is already zeroed by `new byte[]`.
        for (int i = 0; i < WestwoodRC4.KeySize; i++)
        {
            // dest[WWRSA_BLOCK_SIZE - 1 - i] = src[i]
            block[WestwoodRSA.BlockSize - 1 - i] = sessionKey[i];
        }

        // RSA-encrypt the 64-byte block in-place.
        WestwoodRSA.EncryptBlock(block, block);

        return wire;
    }

    /// <summary>
    /// Server-side counterpart for round-trip tests: given the 64-byte
    /// RSA-decrypted block the client sent, extract the 8-byte RC4
    /// session key. Production CLI client never calls this — the server
    /// does it.
    /// </summary>
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
