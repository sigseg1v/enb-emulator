// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// C# port of common/include/net7/WestwoodRC4.h + proxy/WestwoodRC4.cpp,
// originally (C) 2005-2009 Net-7 Entertainment.

namespace N7.CliClient.Net;

/// <summary>
/// Westwood Studios' RC4 stream cipher implementation, as used by the
/// Earth &amp; Beyond client / server protocol. Standard RC4 (KSA + PRGA);
/// the only Westwood-specific bit is the choice of session-key size
/// (<see cref="KeySize"/> = 8 bytes) plus the convention of streaming
/// the cipher across every byte after the RSA handshake completes
/// (header included).
/// </summary>
/// <remarks>
/// <para>
/// Ported byte-for-byte from <c>proxy/WestwoodRC4.cpp</c>. Do NOT swap
/// in <c>System.Security.Cryptography</c>'s RC4 (there isn't one) or
/// any other "clean" implementation — the exact PRGA order, modulo
/// behaviour, and swap pattern is what makes us bit-compatible with
/// the real client. Deviating breaks the handshake silently.
/// </para>
/// <para>
/// Two independent <see cref="WestwoodRC4"/> instances are used per
/// connection: one for the inbound stream, one for the outbound stream.
/// Both are keyed off the same 8-byte session key the client picked
/// during <see cref="RSAHandshake"/>.
/// </para>
/// </remarks>
public sealed class WestwoodRC4
{
    /// <summary>EnB session-key size (mirrors RC4_KEY_SIZE in proxy/Connection.h).</summary>
    public const int KeySize = 8;

    private readonly byte[] _state = new byte[256];
    private byte _x;
    private byte _y;

    /// <summary>
    /// Key the cipher with <paramref name="keyData"/>. Resets the
    /// permutation state and the (x, y) PRGA cursor. Safe to call
    /// repeatedly to re-key.
    /// </summary>
    /// <param name="keyData">The session key bytes. EnB uses 8 bytes
    /// (<see cref="KeySize"/>), but the algorithm itself accepts any
    /// non-empty length.</param>
    public void PrepareKey(ReadOnlySpan<byte> keyData)
    {
        if (keyData.IsEmpty)
            throw new ArgumentException("key cannot be empty", nameof(keyData));

        for (int counter = 0; counter < 256; counter++)
            _state[counter] = (byte)counter;

        _x = 0;
        _y = 0;
        byte index1 = 0;
        byte index2 = 0;
        for (int counter = 0; counter < 256; counter++)
        {
            index2 = (byte)((keyData[index1] + _state[counter] + index2) & 0xFF);
            (_state[counter], _state[index2]) = (_state[index2], _state[counter]);
            index1 = (byte)((index1 + 1) % keyData.Length);
        }
    }

    /// <summary>
    /// XOR <paramref name="buffer"/> in-place with the RC4 keystream.
    /// Advancing the keystream is stateful — encrypting N bytes is
    /// equivalent to encrypting them in two halves, and decryption uses
    /// the same operation (RC4 is symmetric).
    /// </summary>
    public void Transform(Span<byte> buffer)
    {
        byte x = _x;
        byte y = _y;

        for (int counter = 0; counter < buffer.Length; counter++)
        {
            x = (byte)((x + 1) & 0xFF);
            y = (byte)((_state[x] + y) & 0xFF);
            (_state[x], _state[y]) = (_state[y], _state[x]);
            byte xorIndex = (byte)((_state[x] + _state[y]) & 0xFF);
            buffer[counter] ^= _state[xorIndex];
        }

        _x = x;
        _y = y;
    }
}
