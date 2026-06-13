namespace N7.CliClient.Net;

public sealed class WestwoodRC4
{
    public const int KeySize = 8;

    private readonly byte[] _state = new byte[256];
    private byte _x;
    private byte _y;

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
