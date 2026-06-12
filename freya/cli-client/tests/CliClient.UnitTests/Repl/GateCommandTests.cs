// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Buffers.Binary;
using N7.CliClient.Opcodes;
using N7.CliClient.Repl;
using N7.CliClient.Repl.Commands;
using Xunit;

namespace N7.CliClient.UnitTests.Repl;

public sealed class GateCommandTests
{
    // struct ActionPacket (common/include/net7/PacketStructures.h:546): all
    // int32 LE -- GameID@0, Action@4, Target@8, OptionalVar@12 -> 16 bytes.
    [Fact]
    public void BuildActionFrame_LaysOutFourLittleEndianInt32s()
    {
        byte[] frame = GateCommand.BuildActionFrame(0x4003992a, 18, 0x00000215, 0);

        Assert.Equal(16, frame.Length);
        Assert.Equal(0x4003992a, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(0)));
        Assert.Equal(18,         BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(4)));
        Assert.Equal(0x00000215, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(8)));
        Assert.Equal(0,          BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(12)));
    }

    [Fact]
    public void BuildActionFrame_GateButtonBytes()
    {
        // Action=18 (gate button), Target=533 (a Luna sector gate gid 0x215).
        byte[] frame = GateCommand.BuildActionFrame(0x4003992a, 18, 533, 0);
        Assert.Equal(
            new byte[]
            {
                0x2a, 0x99, 0x03, 0x40, // GameID 0x4003992a
                0x12, 0x00, 0x00, 0x00, // Action 18
                0x15, 0x02, 0x00, 0x00, // Target 533
                0x00, 0x00, 0x00, 0x00, // OptionalVar 0
            },
            frame);
    }

    [Fact]
    public void BuildActionFrame_FinishGateBytes()
    {
        // Action=19 (finish gate sequence) -- triggers SectorServerHandoff.
        byte[] frame = GateCommand.BuildActionFrame(0x40000006, 19, 533, 0);
        Assert.Equal(19, BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(4)));
    }

    // Byte-pins against the live reference server
    // SingleGateJump capture, proxy<->server UDP cleartext leg (same leg the
    // CLI/integration harness talks on). The 16-byte ActionPacket payload
    // followed the 12-byte outer sector header; we pin only the payload here.
    private const int CaptureGameId = 0x4003992A;
    private const int CaptureGateTarget = 0x0001870B; // the stargate's gid

    [Fact]
    public void BuildActionFrame_MatchesCapture_SingleGateJump_dg2_GateButton()
    {
        // SingleGateJump dg #2 body (0x002C ACTION, Action=18 gate button):
        //   2A 99 03 40  12 00 00 00  0B 87 01 00  00 00 00 00
        byte[] frame = GateCommand.BuildActionFrame(CaptureGameId, 18, CaptureGateTarget, 0);
        Assert.Equal(
            new byte[]
            {
                0x2A, 0x99, 0x03, 0x40, // GameID 0x4003992A
                0x12, 0x00, 0x00, 0x00, // Action 18 (gate button)
                0x0B, 0x87, 0x01, 0x00, // Target 0x0001870B (stargate gid)
                0x00, 0x00, 0x00, 0x00, // OptionalVar 0
            },
            frame);
    }

    [Fact]
    public void BuildActionFrame_MatchesCapture_SingleGateJump_dg63_FinishGate()
    {
        // SingleGateJump dg #63 body (0x002C ACTION, Action=19 finish gate):
        //   2A 99 03 40  13 00 00 00  0B 87 01 00  00 00 00 00
        byte[] frame = GateCommand.BuildActionFrame(CaptureGameId, 19, CaptureGateTarget, 0);
        Assert.Equal(
            new byte[]
            {
                0x2A, 0x99, 0x03, 0x40, // GameID 0x4003992A
                0x13, 0x00, 0x00, 0x00, // Action 19 (finish gate sequence)
                0x0B, 0x87, 0x01, 0x00, // Target 0x0001870B (same stargate gid)
                0x00, 0x00, 0x00, 0x00, // OptionalVar 0
            },
            frame);
    }

    [Fact]
    public async Task Execute_NotInSector_ReturnsError()
    {
        var ctx = new SessionContext(new OpcodeRegistry());
        var cmd = new GateCommand(ctx);
        var output = new StringWriter();

        int rc = await cmd.ExecuteAsync(new[] { "533" }, output, CancellationToken.None);

        Assert.Equal(1, rc);
        Assert.Contains("not in a sector", output.ToString());
    }

    [Fact]
    public void Name_IsGate()
    {
        var cmd = new GateCommand(new SessionContext(new OpcodeRegistry()));
        Assert.Equal("gate", cmd.Name);
    }
}
