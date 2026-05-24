// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

using N7.CliClient.Net;
using Xunit;

namespace N7.CliClient.UnitTests.Net;

public sealed class WestwoodRC4Tests
{
    /// <summary>
    /// RFC 6229 RC4 test vector: key = "Key" (3 bytes "Key" in ASCII =
    /// 0x4B 0x65 0x79), plaintext = "Plaintext" (9 bytes ASCII),
    /// ciphertext = BBF316E8 D940AF0A D3 (from any standard RC4 reference).
    /// This verifies our PRGA matches the textbook algorithm — if it
    /// fails, our port has a bug independent of any EnB specifics.
    /// </summary>
    [Fact]
    public void Rc4_KnownAnswer_KeyEqualsKey_PlaintextEqualsPlaintext()
    {
        byte[] key = "Key"u8.ToArray();
        byte[] plaintext = "Plaintext"u8.ToArray();
        byte[] expectedCiphertext = new byte[]
        {
            0xBB, 0xF3, 0x16, 0xE8, 0xD9, 0x40, 0xAF, 0x0A, 0xD3
        };

        var rc4 = new WestwoodRC4();
        rc4.PrepareKey(key);
        byte[] buffer = (byte[])plaintext.Clone();
        rc4.Transform(buffer);

        Assert.Equal(expectedCiphertext, buffer);
    }

    [Fact]
    public void Rc4_IsSymmetric_DecryptIsSameAsEncrypt()
    {
        byte[] key = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        byte[] original = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE, 0xBA, 0xBE };

        var enc = new WestwoodRC4();
        enc.PrepareKey(key);
        byte[] ciphertext = (byte[])original.Clone();
        enc.Transform(ciphertext);

        Assert.NotEqual(original, ciphertext);

        var dec = new WestwoodRC4();
        dec.PrepareKey(key);
        dec.Transform(ciphertext);

        Assert.Equal(original, ciphertext);
    }

    [Fact]
    public void Rc4_StreamingMatchesSingleShot()
    {
        byte[] key = new byte[] { 9, 8, 7, 6, 5, 4, 3, 2 };
        byte[] payload = new byte[64];
        new Random(42).NextBytes(payload);

        var single = new WestwoodRC4();
        single.PrepareKey(key);
        byte[] expected = (byte[])payload.Clone();
        single.Transform(expected);

        var streamed = new WestwoodRC4();
        streamed.PrepareKey(key);
        byte[] actual = (byte[])payload.Clone();
        streamed.Transform(actual.AsSpan(0, 17));
        streamed.Transform(actual.AsSpan(17, 30));
        streamed.Transform(actual.AsSpan(47, 17));

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Rc4_PrepareKey_EmptyKey_Throws()
    {
        var rc4 = new WestwoodRC4();
        Assert.Throws<ArgumentException>(() => rc4.PrepareKey(ReadOnlySpan<byte>.Empty));
    }
}
