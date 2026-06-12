// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using N7.CliClient.Opcodes;
using N7.CliClient.Repl;
using N7.CliClient.Repl.Commands;
using Xunit;

namespace N7.CliClient.UnitTests.Repl;

public sealed class UndockCommandTests
{
    // Byte-pin against the live cleartext proxy<->server capture
    // proxy/local-debug/net7-live-2026-06-01-login-undock-dock-logout.pcap,
    // the seq=26 client->server 0x004E STARBASE_REQUEST frame to udp/3636.
    // After the sector framing the 9-byte payload is exactly:
    //   2a 99 03 00   53 4e 00 00   01
    //   (PlayerID LE)  (StarbaseID)  (Action = 1 = exit station)
    // PlayerID 0x0003992a is the avatar GameID (0x4003992a) with the
    // 0x40000000 PLAYER_TAG bit cleared; StarbaseID 0x00004e53; Action 1.
    [Fact]
    public void BuildUndockFrame_MatchesRetailCaptureSeq26()
    {
        byte[] frame = UndockCommand.BuildUndockFrame(0x0003992a, 0x00004e53);

        Assert.Equal(
            new byte[] { 0x2a, 0x99, 0x03, 0x00, 0x53, 0x4e, 0x00, 0x00, 0x01 },
            frame);
    }

    [Fact]
    public void BuildUndockFrame_ActionByteIsExitStation()
    {
        // The trailing byte is Action; 1 = "exit station" -> LaunchIntoSpace.
        // This is the only field the server reads on the launch path.
        byte[] frame = UndockCommand.BuildUndockFrame(0x40000006, 0);
        Assert.Equal(9, frame.Length);
        Assert.Equal(0x06, frame[0]);  // PlayerID low byte (LE)
        Assert.Equal(0x00, frame[4]);  // StarbaseID = 0
        Assert.Equal(0x01, frame[8]);  // Action = 1
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
