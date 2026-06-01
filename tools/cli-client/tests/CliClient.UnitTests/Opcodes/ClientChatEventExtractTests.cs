// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Opcodes.Records;
using Xunit;

namespace N7.CliClient.UnitTests.Opcodes;

public sealed class ClientChatEventExtractTests
{
    // Exact 0x00A5 payload from a live ALL_STATUS-channel chat line:
    //   Type=3, Unknown=0, LastName="Derp [ADMIN]" (x2), OtherPlayer="",
    //   Channel="Beta", Message="test", Unknown6Len=0, Trailing=0.
    private static readonly byte[] BetaTestPayload =
    {
        0x03, 0x00, 0x00, 0x00,                         // Type = 3
        0x00, 0x00, 0x00, 0x00,                         // Unknown = 0
        0x0C, 0x00, (byte)'D',(byte)'e',(byte)'r',(byte)'p',(byte)' ',
                    (byte)'[',(byte)'A',(byte)'D',(byte)'M',(byte)'I',(byte)'N',(byte)']',
        0x0C, 0x00, (byte)'D',(byte)'e',(byte)'r',(byte)'p',(byte)' ',
                    (byte)'[',(byte)'A',(byte)'D',(byte)'M',(byte)'I',(byte)'N',(byte)']',
        0x00, 0x00,                                     // OtherPlayer = ""
        0x04, 0x00, (byte)'B',(byte)'e',(byte)'t',(byte)'a',
        0x04, 0x00, (byte)'t',(byte)'e',(byte)'s',(byte)'t',
        0x00, 0x00,                                     // Unknown6Len
        0x00, 0x00, 0x00, 0x00,                         // Trailing
    };

    [Fact]
    public void TryExtract_ParsesSenderChannelMessage()
    {
        var ev = ClientChatEventRecord.TryExtract(BetaTestPayload);

        Assert.NotNull(ev);
        Assert.Equal(3, ev!.Value.Type);
        Assert.Equal("Derp [ADMIN]", ev.Value.Sender);
        Assert.Equal("Beta", ev.Value.Channel);
        Assert.Equal("test", ev.Value.Message);
        Assert.Equal("", ev.Value.OtherPlayer);
    }

    [Fact]
    public void TryExtract_TooShort_ReturnsNull()
    {
        Assert.Null(ClientChatEventRecord.TryExtract(new byte[] { 0x01, 0x00 }));
    }

    [Fact]
    public void TryExtract_OverrunningLengthPrefix_ReturnsNull()
    {
        // Type + Unknown header, then a LastName length prefix claiming 50
        // bytes but with none following.
        byte[] bad =
        {
            0x03, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
            0x32, 0x00,             // len = 50, but no bytes follow
        };
        Assert.Null(ClientChatEventRecord.TryExtract(bad));
    }
}
