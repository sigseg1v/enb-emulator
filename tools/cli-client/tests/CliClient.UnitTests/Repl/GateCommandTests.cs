// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

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
