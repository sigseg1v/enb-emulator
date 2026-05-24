// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Outbound;
using Xunit;

namespace N7.CliClient.UnitTests.Opcodes;

public sealed class VersionRequestCodecTests
{
    [Fact]
    public void Opcode_Is_0x0000()
    {
        var codec = new VersionRequestCodec();
        Assert.Equal(OpcodeId.Known.VersionRequest, codec.Opcode);
        Assert.Equal(0x0000, codec.Opcode.Value);
    }

    [Fact]
    public void EncodeOutbound_Major42_Minor0_BigEndian()
    {
        var codec = new VersionRequestCodec();
        byte[] bytes = codec.EncodeOutbound(new VersionRequest(42, 0));
        Assert.Equal(8, bytes.Length);
        Assert.Equal(new byte[]
        {
            0x00, 0x00, 0x00, 0x2A,   // Major=42 BE
            0x00, 0x00, 0x00, 0x00,   // Minor=0 BE
        }, bytes);
    }

    [Fact]
    public void Decode_RoundTrips()
    {
        var codec = new VersionRequestCodec();
        var original = new VersionRequest(123, 456);
        var encoded = codec.EncodeOutbound(original);
        var decoded = (VersionRequest) codec.DecodeInbound(encoded);
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void Decode_ShortPayload_Throws()
    {
        var codec = new VersionRequestCodec();
        Assert.Throws<InvalidDataException>(
            () => codec.DecodeInbound(new byte[VersionRequestCodec.WireSize - 1]));
    }

    [Fact]
    public void EncodeOutbound_WrongType_Throws()
    {
        var codec = new VersionRequestCodec();
        Assert.Throws<ArgumentException>(() => codec.EncodeOutbound("not a request"));
    }
}
