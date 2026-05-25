// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using System.Text;
using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Inbound;
using Xunit;

namespace N7.CliClient.UnitTests.Opcodes;

/// <summary>
/// Offline unit tests for <see cref="GlobalTicketCodec"/>. The
/// integration suite proves the codec works against the live proxy's
/// actual output; these tests cover the decode logic in isolation
/// (no docker), so a typo in field offsets or byte order surfaces on
/// every PR rather than waiting for the cli-integration job to run.
/// </summary>
public sealed class GlobalTicketCodecTests
{
    [Fact]
    public void Opcode_Is_0x006F()
    {
        var codec = new GlobalTicketCodec();
        Assert.Equal(OpcodeId.Known.GlobalTicket, codec.Opcode);
        Assert.Equal(0x006F, codec.Opcode.Value);
    }

    [Fact]
    public void WireSize_Is68_PostInt32Migration()
    {
        // 4 byte response_code + 64 byte embedded MasterJoin (11 *
        // int32_t + 20 byte ticket). Was 72 bytes on Linux pre-Phase K
        // when `long` was 8 bytes.
        Assert.Equal(68, GlobalTicketCodec.WireSize);
    }

    [Fact]
    public void Decode_AllZeros_YieldsZeroFieldsAndEmptyString()
    {
        byte[] payload = new byte[GlobalTicketCodec.WireSize];

        var result = (GlobalTicket)new GlobalTicketCodec()
            .DecodeInbound(payload);

        Assert.Equal(0, result.ResponseCode);
        Assert.Equal(0, result.AvatarId);
        Assert.Equal(0, result.SectorId);
        Assert.Equal(0, result.Level);
        Assert.Equal(string.Empty, result.TicketString);
    }

    [Fact]
    public void Decode_HappyPathLayout_ReadsAllFields()
    {
        // Reproduce proxy SendGlobalTicket(player_id, sector_id, level,
        // issue=true) — see proxy/ClientToServer_linux_stubs.cpp:273.
        // issue=true → response_code=0 BE at offset 0.
        // index=20 → avatar_id BE at 20, sector_id BE at 24.
        // index=32 → level host-order at 32 (AddData, no byte swap).
        // index=48 → "MY_Avatar_Ticket" at 48.
        byte[] payload = new byte[GlobalTicketCodec.WireSize];

        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(0, 4), 0);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(20, 4), 0x42AE5906);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(24, 4), 1071);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(32, 4), 5);
        Encoding.ASCII.GetBytes("MY_Avatar_Ticket").CopyTo(payload.AsSpan(48, 16));

        var result = (GlobalTicket)new GlobalTicketCodec()
            .DecodeInbound(payload);

        Assert.Equal(0, result.ResponseCode);
        Assert.Equal(0x42AE5906, result.AvatarId);
        Assert.Equal(1071, result.SectorId);
        Assert.Equal(5, result.Level);
        Assert.Equal("MY_Avatar_Ticket", result.TicketString);
    }

    [Fact]
    public void Decode_FailurePathLayout_ReadsGalaxyFullSentinels()
    {
        // SendGlobalTicket(0x40000000, 0, 1002, issue=false) — proxy's
        // failure-path call when SendAvatarLogin returned -1.
        // issue=false → response_code = level (1002) BE at offset 0.
        byte[] payload = new byte[GlobalTicketCodec.WireSize];

        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(0, 4),
            GlobalTicketCodec.GalaxyFullResponseCode);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(20, 4),
            GlobalTicketCodec.FailureAvatarIdSentinel);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(24, 4), 0);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(32, 4),
            GlobalTicketCodec.GalaxyFullResponseCode);
        Encoding.ASCII.GetBytes("MY_Avatar_Ticket").CopyTo(payload.AsSpan(48, 16));

        var result = (GlobalTicket)new GlobalTicketCodec()
            .DecodeInbound(payload);

        Assert.Equal(GlobalTicketCodec.GalaxyFullResponseCode, result.ResponseCode);
        Assert.Equal(GlobalTicketCodec.FailureAvatarIdSentinel, result.AvatarId);
        Assert.Equal(0, result.SectorId);
        Assert.Equal(GlobalTicketCodec.GalaxyFullResponseCode, result.Level);
        Assert.Equal("MY_Avatar_Ticket", result.TicketString);
    }

    [Fact]
    public void Decode_TicketStringStopsAtFirstNul()
    {
        byte[] payload = new byte[GlobalTicketCodec.WireSize];

        // Stuff bytes past a NUL terminator — the codec must stop at
        // the NUL, not slurp the trailing garbage.
        Encoding.ASCII.GetBytes("ABC\0DEF").CopyTo(payload.AsSpan(48, 7));

        var result = (GlobalTicket)new GlobalTicketCodec()
            .DecodeInbound(payload);

        Assert.Equal("ABC", result.TicketString);
    }

    [Fact]
    public void Decode_ShortPayload_Throws()
    {
        var codec = new GlobalTicketCodec();
        Assert.Throws<InvalidDataException>(
            () => codec.DecodeInbound(new byte[GlobalTicketCodec.WireSize - 1]));
    }

    [Fact]
    public void EncodeOutbound_Throws_ServerToClientOnly()
    {
        var codec = new GlobalTicketCodec();
        Assert.Throws<NotSupportedException>(() => codec.EncodeOutbound(new object()));
    }
}
