// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Opcodes;
using N7.CliClient.Repl;
using Xunit;

namespace N7.CliClient.UnitTests.Repl;

public sealed class LogoffRequestTests
{
    // struct LogoffRequest (common/include/net7/PacketStructures.h:583):
    //   int32 PlayerID; int32 LogOutType;  -- both little-endian, 8 bytes.
    // The server's Player::HandleLogoffRequest (PlayerConnection.cpp:7722)
    // ignores both fields (the struct cast is commented out) and drops the
    // player immediately. We still reproduce the retail field convention:
    // PlayerID = the avatar GameID with the 0x40000000 PLAYER_TAG bit cleared,
    // LogOutType = 0 (matching the UndockCommand PlayerID convention pinned to
    // the live capture's seq=26 frame).
    [Fact]
    public void BuildLogoffRequest_ClearsPlayerTagAndZeroesLogoutType()
    {
        byte[] frame = SessionContext.BuildLogoffRequest(0x4003992a);

        Assert.Equal(
            new byte[] { 0x2a, 0x99, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00 },
            frame);
    }

    [Fact]
    public void BuildLogoffRequest_IsEightBytes()
    {
        byte[] frame = SessionContext.BuildLogoffRequest(0x40000006);
        Assert.Equal(8, frame.Length);
        Assert.Equal(0x06, frame[0]);   // PlayerID low byte (LE), tag bit cleared
        Assert.Equal(0x00, frame[4]);   // LogOutType = 0
    }

    [Fact]
    public void LogoffRequest_OpcodeIs0x00B9()
    {
        Assert.Equal(0x00B9, OpcodeId.Known.LogoffRequest.Value);
    }
}
