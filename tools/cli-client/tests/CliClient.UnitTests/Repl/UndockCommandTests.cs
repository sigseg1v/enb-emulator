// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Opcodes;
using N7.CliClient.Repl;
using N7.CliClient.Repl.Commands;
using Xunit;

namespace N7.CliClient.UnitTests.Repl;

public sealed class UndockCommandTests
{
    // Byte-pin against the live cleartext proxy<->server capture
    // proxy/local-debug/net7-live-2026-06-01-login-undock-dock-logout.pcap,
    // the seq=15 client->server 0x009F frame to udp/3636. After the 12-byte
    // EnbUdpHeader the payload is exactly:
    //   2a 99 03 40   00 00 00 00   ff ff ff ff
    //   (AvatarID LE) (room word 1) (room word 2 = -1)
    [Fact]
    public void BuildUndockFrame_MatchesRetailCaptureSeq15()
    {
        // capture AvatarID was 0x4003992a (sector-local GameID).
        byte[] frame = UndockCommand.BuildUndockFrame(0x4003992a);

        Assert.Equal(
            new byte[] { 0x2a, 0x99, 0x03, 0x40, 0x00, 0x00, 0x00, 0x00, 0xff, 0xff, 0xff, 0xff },
            frame);
    }

    [Fact]
    public void BuildUndockFrame_TrailingWordIsMinusOne_LittleEndian()
    {
        // The destination-room word (-1, "into space") is the last 4 bytes,
        // little-endian -- the field the server assigns to m_Room. The capture
        // proves this byte position, NOT the field NAMES in PacketStructures.h.
        byte[] frame = UndockCommand.BuildUndockFrame(0x40000006);
        Assert.Equal(0x06, frame[0]); // GameID low byte (LE)
        Assert.Equal(0, frame[4]);    // room word 1 = 0
        Assert.Equal(0xff, frame[8]);
        Assert.Equal(0xff, frame[11]); // room word 2 = -1
    }

    [Fact]
    public async Task Execute_NotInSector_ReturnsError()
    {
        var ctx = new SessionContext(new OpcodeRegistry());
        var cmd = new UndockCommand(ctx);
        var output = new StringWriter();

        int rc = await cmd.ExecuteAsync(System.Array.Empty<string>(), output, CancellationToken.None);

        Assert.Equal(1, rc);
        Assert.Contains("not in a sector", output.ToString());
    }

    [Fact]
    public void Name_IsUndock()
    {
        var cmd = new UndockCommand(new SessionContext(new OpcodeRegistry()));
        Assert.Equal("undock", cmd.Name);
    }
}
