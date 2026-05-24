// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Outbound;
using Xunit;

namespace N7.CliClient.UnitTests.Opcodes;

public sealed class ClientChatCodecTests
{
    [Fact]
    public void Opcode_Is_0x0033()
    {
        var codec = new ClientChatCodec();
        Assert.Equal(OpcodeId.Known.ClientChat, codec.Opcode);
        Assert.Equal(0x0033, codec.Opcode.Value);
    }

    [Fact]
    public void Encode_LayoutMatches_PacketStructures_h_572()
    {
        // ClientChat wire layout (Win32 client, packed, LE):
        //   [0..3]  int32 LE   GameID
        //   [4]     byte       Type
        //   [5..6]  int16 LE   Size = strlen(string)+1
        //   [7..]   string + NUL
        var msg = new ClientChatMessage(
            GameId: 0x11223344,
            Type: ChatChannel.Local,
            Message: "hi");

        byte[] wire = new ClientChatCodec().EncodeOutbound(msg);

        // Header: 4 + 1 + 2 = 7 bytes, then "hi\0" = 3 bytes → 10 total.
        Assert.Equal(10, wire.Length);

        Assert.Equal(0x44, wire[0]);
        Assert.Equal(0x33, wire[1]);
        Assert.Equal(0x22, wire[2]);
        Assert.Equal(0x11, wire[3]);

        Assert.Equal((byte)ChatChannel.Local, wire[4]);

        Assert.Equal(3, wire[5]);   // Size LSB
        Assert.Equal(0, wire[6]);   // Size MSB

        Assert.Equal((byte)'h', wire[7]);
        Assert.Equal((byte)'i', wire[8]);
        Assert.Equal(0, wire[9]);   // NUL terminator
    }

    [Fact]
    public void Encode_AllChannelValues_RoundTrip()
    {
        // The server's switch in PlayerConnection.cpp:4515 dispatches on
        // chat->Type for values 0..4. Make sure every ChatChannel value
        // survives encode → decode unchanged.
        foreach (ChatChannel ch in Enum.GetValues<ChatChannel>())
        {
            var msg = new ClientChatMessage(GameId: 42, Type: ch, Message: "x");
            var codec = new ClientChatCodec();
            var decoded = (ClientChatMessage)codec.DecodeInbound(codec.EncodeOutbound(msg));
            Assert.Equal(msg, decoded);
        }
    }

    [Fact]
    public void Encode_SlashCommand_PreservesLeadingSlash()
    {
        // Server treats String[0] == '/' as HandleSlashCommands. The
        // codec must not strip / re-escape the leading slash.
        var msg = new ClientChatMessage(99, ChatChannel.Broadcast, "/whereami");
        byte[] wire = new ClientChatCodec().EncodeOutbound(msg);
        Assert.Equal((byte)'/', wire[7]);
        Assert.Equal((byte)'w', wire[8]);
    }

    [Fact]
    public void Encode_EmptyMessage_Throws()
    {
        // Server indexes chat->String[0] unconditionally before checking
        // the slash branch. We refuse to send an empty string upstream
        // rather than potentially trip a server-side OOB read.
        var msg = new ClientChatMessage(1, ChatChannel.Target, "");
        Assert.Throws<ArgumentException>(
            () => new ClientChatCodec().EncodeOutbound(msg));
    }

    [Fact]
    public void Encode_TooLongMessage_Throws()
    {
        // Size field is int16. Max payload string is 32766 bytes (so that
        // size = length+1 ≤ short.MaxValue).
        var huge = new string('A', short.MaxValue);
        var msg = new ClientChatMessage(1, ChatChannel.Target, huge);
        Assert.Throws<ArgumentException>(
            () => new ClientChatCodec().EncodeOutbound(msg));
    }

    [Fact]
    public void Encode_MaxLengthMessage_Encodes()
    {
        // length = short.MaxValue - 1 → size = short.MaxValue: still fits.
        var maxOk = new string('A', short.MaxValue - 1);
        var msg = new ClientChatMessage(1, ChatChannel.Target, maxOk);
        byte[] wire = new ClientChatCodec().EncodeOutbound(msg);
        Assert.Equal(ClientChatCodec.FixedHeaderSize + short.MaxValue, wire.Length);
    }

    [Fact]
    public void Decode_TooShort_Throws()
    {
        // Anything under FixedHeaderSize+1 (room for at least a NUL) is
        // malformed. The real client never produces this.
        var codec = new ClientChatCodec();
        Assert.Throws<InvalidDataException>(
            () => codec.DecodeInbound(new byte[ClientChatCodec.FixedHeaderSize]));
    }

    [Fact]
    public void Decode_SizeFieldZero_Throws()
    {
        var codec = new ClientChatCodec();
        // Header with Size=0 (impossible — server always includes NUL).
        byte[] wire = new byte[ClientChatCodec.FixedHeaderSize + 1];
        // size at offset 5..6 already 0; payload byte after is 0.
        Assert.Throws<InvalidDataException>(() => codec.DecodeInbound(wire));
    }

    [Fact]
    public void Decode_SizeExceedsPayload_Throws()
    {
        var codec = new ClientChatCodec();
        byte[] wire = new byte[ClientChatCodec.FixedHeaderSize + 1];
        // Lie: claim 50 bytes follow when only 1 does.
        wire[5] = 50;
        Assert.Throws<InvalidDataException>(() => codec.DecodeInbound(wire));
    }

    [Fact]
    public void Encode_WrongMessageType_Throws()
    {
        var codec = new ClientChatCodec();
        Assert.Throws<ArgumentException>(() => codec.EncodeOutbound("not a chat"));
    }

    [Fact]
    public void Encode_NullMessageContent_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ClientChatCodec().EncodeOutbound(
                new ClientChatMessage(1, ChatChannel.Target, null!)));
    }
}
