// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Inbound;
using Xunit;

namespace N7.CliClient.UnitTests.Opcodes;

public sealed class VersionResponseCodecTests
{
    [Fact]
    public void Opcode_Is_0x0001()
    {
        var codec = new VersionResponseCodec();
        Assert.Equal(OpcodeId.Known.VersionResponse, codec.Opcode);
        Assert.Equal(0x0001, codec.Opcode.Value);
    }

    [Theory]
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x00 }, 0, true,  false, false)]
    [InlineData(new byte[] { 0x01, 0x00, 0x00, 0x00 }, 1, false, true,  false)]
    [InlineData(new byte[] { 0x02, 0x00, 0x00, 0x00 }, 2, false, false, true)]
    public void Decode_ParsesStatus_LittleEndian(
        byte[] payload, int expectedStatus, bool upToDate, bool tooOld, bool newer)
    {
        var codec = new VersionResponseCodec();
        var result = (VersionResponse) codec.DecodeInbound(payload);

        Assert.Equal(expectedStatus, result.Status);
        Assert.Equal(upToDate, result.ClientUpToDate);
        Assert.Equal(tooOld,   result.ClientTooOld);
        Assert.Equal(newer,    result.ClientNewer);
    }

    [Fact]
    public void Decode_ShortPayload_Throws()
    {
        var codec = new VersionResponseCodec();
        Assert.Throws<InvalidDataException>(
            () => codec.DecodeInbound(new byte[VersionResponseCodec.WireSize - 1]));
    }

    [Fact]
    public void EncodeOutbound_Throws_ServerOnly()
    {
        var codec = new VersionResponseCodec();
        Assert.Throws<NotSupportedException>(() => codec.EncodeOutbound(new object()));
    }
}
