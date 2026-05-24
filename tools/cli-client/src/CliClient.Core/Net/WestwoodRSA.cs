// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// C# port of common/include/net7/WestwoodRSA.h + proxy/WestwoodRSA.cpp,
// originally (C) 2005-2009 Net-7 Entertainment.

using System.Globalization;
using System.Numerics;

namespace N7.CliClient.Net;

/// <summary>
/// Westwood Studios' raw RSA primitive as used by the Earth &amp; Beyond
/// handshake. NOT general-purpose RSA — this is RSA-as-block-cipher
/// over 64-byte blocks, with the textbook (no padding) <c>c = m^e mod N</c>
/// transform. Public key (e, N) is fixed and baked into the protocol;
/// the client must use this exact (e, N) or the server can't decrypt
/// the RC4 session key the client picks.
/// </summary>
/// <remarks>
/// <para>
/// Ported from <c>proxy/WestwoodRSA.cpp</c>. The C++ code uses OpenSSL
/// <c>BIGNUM</c>; we use <see cref="BigInteger"/> with
/// <see cref="BigInteger.ModPow"/>. The on-wire format is big-endian
/// 64-byte blocks (matching <c>BN_bn2bin</c> / <c>BN_bin2bn</c>), so the
/// byte-order conversions are explicit below.
/// </para>
/// <para>
/// Phase S only needs <see cref="EncryptBlock"/> — the client encrypts a
/// 64-byte block containing the RC4 session key and sends it to the
/// server. <see cref="DecryptBlock"/> (server-side) is included for
/// completeness and for round-trip tests, but the CLI client never
/// holds the private key in practice.
/// </para>
/// </remarks>
public sealed class WestwoodRSA
{
    /// <summary>EnB RSA block size in bytes (mirrors WWRSA_BLOCK_SIZE).</summary>
    public const int BlockSize = 64;

    /// <summary>Public exponent — fixed at 35 by the protocol.</summary>
    private static readonly BigInteger E = BigInteger.Parse("35", CultureInfo.InvariantCulture);

    /// <summary>
    /// Modulus N — fixed by the protocol (mirrors WWRSA_N in WestwoodRSA.h).
    /// </summary>
    private static readonly BigInteger N = BigInteger.Parse(
        "10385578014804950221065190195736491193847541479389728420426514083771326945639729736695791225573893793119489336012297845146104637691941242485732839277543427",
        CultureInfo.InvariantCulture);

    /// <summary>
    /// Private exponent d — included for round-trip / decrypt tests only.
    /// The real client never holds d; only the server does.
    /// </summary>
    private static readonly BigInteger D = BigInteger.Parse(
        "10088847214381951643320470475858305731166183151407164751271470824235003318621252307969752086088076499395823874814123350292603347408732347765156628342107995",
        CultureInfo.InvariantCulture);

    /// <summary>
    /// Encrypt a 64-byte block in-place: c = m^e mod N.
    /// Input and output are big-endian byte arrays of length
    /// <see cref="BlockSize"/>.
    /// </summary>
    public static void EncryptBlock(ReadOnlySpan<byte> plaintext, Span<byte> ciphertext)
    {
        if (plaintext.Length != BlockSize)
            throw new ArgumentException($"plaintext must be {BlockSize} bytes, got {plaintext.Length}", nameof(plaintext));
        if (ciphertext.Length < BlockSize)
            throw new ArgumentException($"ciphertext must hold {BlockSize} bytes, got {ciphertext.Length}", nameof(ciphertext));

        BigInteger m = FromBigEndian(plaintext);
        BigInteger c = BigInteger.ModPow(m, E, N);
        ToBigEndian(c, ciphertext[..BlockSize]);
    }

    /// <summary>
    /// Decrypt a 64-byte block in-place: m = c^d mod N. Server-side
    /// operation; exposed for round-trip tests.
    /// </summary>
    public static void DecryptBlock(ReadOnlySpan<byte> ciphertext, Span<byte> plaintext)
    {
        if (ciphertext.Length != BlockSize)
            throw new ArgumentException($"ciphertext must be {BlockSize} bytes, got {ciphertext.Length}", nameof(ciphertext));
        if (plaintext.Length < BlockSize)
            throw new ArgumentException($"plaintext must hold {BlockSize} bytes, got {plaintext.Length}", nameof(plaintext));

        BigInteger c = FromBigEndian(ciphertext);
        BigInteger m = BigInteger.ModPow(c, D, N);
        ToBigEndian(m, plaintext[..BlockSize]);
    }

    /// <summary>
    /// Read a big-endian unsigned integer from <paramref name="source"/>.
    /// Mirrors OpenSSL <c>BN_bin2bn</c>. We append a 0x00 byte to force
    /// <see cref="BigInteger"/>'s signed-byte parser to treat the value
    /// as positive, then reverse to little-endian (which is what the
    /// <see cref="BigInteger(ReadOnlySpan{byte})"/> constructor wants).
    /// </summary>
    private static BigInteger FromBigEndian(ReadOnlySpan<byte> source)
    {
        Span<byte> little = stackalloc byte[source.Length + 1];
        for (int i = 0; i < source.Length; i++)
            little[i] = source[source.Length - 1 - i];
        little[source.Length] = 0; // sign byte (positive)
        return new BigInteger(little);
    }

    /// <summary>
    /// Write <paramref name="value"/> as a fixed-width big-endian byte
    /// array into <paramref name="destination"/>. Mirrors OpenSSL
    /// <c>BN_bn2bin</c> followed by left-zero-padding to BlockSize.
    /// </summary>
    private static void ToBigEndian(BigInteger value, Span<byte> destination)
    {
        if (value.Sign < 0)
            throw new InvalidOperationException("RSA result was negative; modulus arithmetic is broken");

        // BigInteger.ToByteArray is little-endian with possible trailing
        // sign byte. Strip any trailing 0x00 sign bytes, then reverse
        // into destination, left-padding with zeros.
        byte[] little = value.ToByteArray();
        int meaningful = little.Length;
        while (meaningful > 0 && little[meaningful - 1] == 0)
            meaningful--;

        if (meaningful > destination.Length)
            throw new InvalidOperationException(
                $"RSA result is {meaningful} bytes, won't fit in {destination.Length}-byte buffer");

        destination.Clear();
        for (int i = 0; i < meaningful; i++)
            destination[destination.Length - 1 - i] = little[i];
    }
}
