// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

using System.Buffers.Binary;
using System.Text;

namespace N7.CliClient.Opcodes.Outbound;

/// <summary>
/// Channel selector for <see cref="ClientChatMessage.Type"/>. Mirrors the
/// switch in <c>server/src/PlayerConnection.cpp:4515</c> (Player::HandleClientChat).
/// </summary>
public enum ChatChannel : byte
{
    /// <summary>Direct message to the current target.</summary>
    Target = 0,
    /// <summary>Send to all group members.</summary>
    Group = 1,
    /// <summary>Send to all guild members.</summary>
    Guild = 2,
    /// <summary>Local-area broadcast (limited radius).</summary>
    Local = 3,
    /// <summary>Sector-wide broadcast.</summary>
    Broadcast = 4,
}

/// <summary>
/// 0x0033 CLIENT_CHAT — chat / slash-command upload from client to server.
/// </summary>
/// <remarks>
/// Mirrors <c>struct ClientChat</c> in
/// <c>common/include/net7/PacketStructures.h:572</c>. Wire layout matches
/// the Win32 client's packed (long=4 bytes, little-endian) emission:
/// <list type="bullet">
///   <item>offset 0..3 : int32 LE GameID (the speaker's avatar id)</item>
///   <item>offset 4    : byte Type (see <see cref="ChatChannel"/>)</item>
///   <item>offset 5..6 : int16 LE Size = string-byte-length + 1 (NUL)</item>
///   <item>offset 7..N : ASCII string + NUL terminator</item>
/// </list>
/// The server treats a string whose first byte is '/' as a slash command —
/// the channel is ignored in that case. See
/// <c>server/src/PlayerConnection.cpp:4535 HandleSlashCommands</c>.
/// </remarks>
public sealed record ClientChatMessage(
    int GameId,
    ChatChannel Type,
    string Message);

/// <summary>
/// Codec for opcode 0x0033 CLIENT_CHAT (client → server).
/// </summary>
public sealed class ClientChatCodec : IOpcodeCodec
{
    /// <summary>Header size before the string: 4 (GameId) + 1 (Type) + 2 (Size).</summary>
    public const int FixedHeaderSize = 7;

    public OpcodeId Opcode => OpcodeId.Known.ClientChat;

    public object DecodeInbound(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < FixedHeaderSize + 1)
        {
            throw new InvalidDataException(
                $"ClientChat payload is {payload.Length} bytes, expected at least {FixedHeaderSize + 1}");
        }

        int gameId = BinaryPrimitives.ReadInt32LittleEndian(payload[0..4]);
        byte typeByte = payload[4];
        short size = BinaryPrimitives.ReadInt16LittleEndian(payload[5..7]);

        if (size < 1)
        {
            throw new InvalidDataException(
                $"ClientChat size field is {size}, must be at least 1 (NUL terminator)");
        }

        if (FixedHeaderSize + size > payload.Length)
        {
            throw new InvalidDataException(
                $"ClientChat size says {size} bytes after header but only {payload.Length - FixedHeaderSize} remain");
        }

        // size includes the trailing NUL — strip it for the C# string.
        ReadOnlySpan<byte> stringBytes = payload.Slice(FixedHeaderSize, size - 1);
        string message = Encoding.ASCII.GetString(stringBytes);

        return new ClientChatMessage(gameId, (ChatChannel)typeByte, message);
    }

    public byte[] EncodeOutbound(object message)
    {
        if (message is not ClientChatMessage chat)
            throw new ArgumentException(
                $"expected ClientChatMessage, got {message?.GetType().Name ?? "null"}",
                nameof(message));

        ArgumentNullException.ThrowIfNull(chat.Message);

        // The server reads chat->String as a NUL-terminated C string and
        // its slash-command branch indexes chat->String[0] unconditionally.
        // Treat an empty message as invalid here rather than letting the
        // server dereference past the end.
        if (chat.Message.Length == 0)
        {
            throw new ArgumentException(
                "ClientChat message must be non-empty", nameof(message));
        }

        int stringByteLength = Encoding.ASCII.GetByteCount(chat.Message);
        // Size on the wire is bytes-including-NUL; must fit in int16.
        int sizeWithNul = stringByteLength + 1;
        if (sizeWithNul > short.MaxValue)
        {
            throw new ArgumentException(
                $"ClientChat message is {stringByteLength} bytes, exceeds int16 size field",
                nameof(message));
        }

        byte[] buf = new byte[FixedHeaderSize + sizeWithNul];
        Span<byte> span = buf;

        BinaryPrimitives.WriteInt32LittleEndian(span[0..4], chat.GameId);
        span[4] = (byte)chat.Type;
        BinaryPrimitives.WriteInt16LittleEndian(span[5..7], (short)sizeWithNul);

        int written = Encoding.ASCII.GetBytes(chat.Message.AsSpan(), span[FixedHeaderSize..]);
        if (written != stringByteLength)
        {
            throw new InvalidOperationException(
                $"ASCII encode wrote {written} bytes, expected {stringByteLength}");
        }
        span[FixedHeaderSize + stringByteLength] = 0;

        return buf;
    }
}
