using System.Globalization;
using System.Numerics;

namespace N7.CliClient.Net;

public sealed class WestwoodRSA
{
    public const int BlockSize = 64;

    private static readonly BigInteger E = BigInteger.Parse("35", CultureInfo.InvariantCulture);

    private static readonly BigInteger N = BigInteger.Parse(
        "10385578014804950221065190195736491193847541479389728420426514083771326945639729736695791225573893793119489336012297845146104637691941242485732839277543427",
        CultureInfo.InvariantCulture);

    private static readonly BigInteger D = BigInteger.Parse(
        "10088847214381951643320470475858305731166183151407164751271470824235003318621252307969752086088076499395823874814123350292603347408732347765156628342107995",
        CultureInfo.InvariantCulture);

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

    private static BigInteger FromBigEndian(ReadOnlySpan<byte> source)
    {
        Span<byte> little = stackalloc byte[source.Length + 1];
        for (int i = 0; i < source.Length; i++)
            little[i] = source[source.Length - 1 - i];
        little[source.Length] = 0;
        return new BigInteger(little);
    }

    private static void ToBigEndian(BigInteger value, Span<byte> destination)
    {
        if (value.Sign < 0)
            throw new InvalidOperationException("RSA result was negative; modulus arithmetic is broken");

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
