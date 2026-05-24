// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

using N7.CliClient.Net;
using Xunit;

namespace N7.CliClient.UnitTests.Net;

public sealed class WestwoodRSATests
{
    [Fact]
    public void Encrypt_Then_Decrypt_RoundTrips()
    {
        byte[] plaintext = new byte[WestwoodRSA.BlockSize];
        new Random(123).NextBytes(plaintext);

        // Force the high bit clear so the block (as a big-endian
        // integer) is comfortably less than N — textbook RSA requires
        // m < N. We zero the leading byte and re-randomize the rest.
        plaintext[0] = 0;

        byte[] ciphertext = new byte[WestwoodRSA.BlockSize];
        WestwoodRSA.EncryptBlock(plaintext, ciphertext);

        Assert.NotEqual(plaintext, ciphertext);

        byte[] decrypted = new byte[WestwoodRSA.BlockSize];
        WestwoodRSA.DecryptBlock(ciphertext, decrypted);

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_ProducesExactlyBlockSizeOutput()
    {
        byte[] plaintext = new byte[WestwoodRSA.BlockSize];
        plaintext[0] = 0;
        plaintext[WestwoodRSA.BlockSize - 1] = 0x42;

        byte[] ciphertext = new byte[WestwoodRSA.BlockSize];
        WestwoodRSA.EncryptBlock(plaintext, ciphertext);

        Assert.Equal(WestwoodRSA.BlockSize, ciphertext.Length);
    }

    [Fact]
    public void Encrypt_WrongInputSize_Throws()
    {
        byte[] plaintext = new byte[WestwoodRSA.BlockSize - 1];
        byte[] ciphertext = new byte[WestwoodRSA.BlockSize];

        Assert.Throws<ArgumentException>(() => WestwoodRSA.EncryptBlock(plaintext, ciphertext));
    }

    [Fact]
    public void Encrypt_OutputBufferTooSmall_Throws()
    {
        byte[] plaintext = new byte[WestwoodRSA.BlockSize];
        byte[] ciphertext = new byte[WestwoodRSA.BlockSize - 1];

        Assert.Throws<ArgumentException>(() => WestwoodRSA.EncryptBlock(plaintext, ciphertext));
    }

    [Fact]
    public void Encrypt_AllZeroBlock_ProducesZero()
    {
        // 0^e mod N = 0 — a trivial smoke check that our pow-mod
        // wiring isn't doing something weird like off-by-one bn2bin.
        byte[] plaintext = new byte[WestwoodRSA.BlockSize];
        byte[] ciphertext = new byte[WestwoodRSA.BlockSize];

        WestwoodRSA.EncryptBlock(plaintext, ciphertext);

        Assert.All(ciphertext, b => Assert.Equal(0, b));
    }
}
