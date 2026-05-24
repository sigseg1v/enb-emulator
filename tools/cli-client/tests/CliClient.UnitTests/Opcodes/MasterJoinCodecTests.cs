// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Outbound;
using Xunit;

namespace N7.CliClient.UnitTests.Opcodes;

public sealed class MasterJoinCodecTests
{
    [Fact]
    public void Opcode_Is_0x0035()
    {
        var codec = new MasterJoinCodec();
        Assert.Equal(OpcodeId.Known.MasterJoin, codec.Opcode);
        Assert.Equal(0x0035, codec.Opcode.Value);
    }

    [Fact]
    public void WireSize_Is64Bytes()
    {
        // Mirrors the C++ struct MasterJoin (PacketStructures.h:262-282).
        // The comment explicitly notes: "11 * int32_t + 20-byte ticket =
        // 64 bytes on every platform."
        Assert.Equal(64, MasterJoinCodec.WireSize);
        Assert.Equal(20, MasterJoinCodec.TicketLength);
    }

    [Fact]
    public void Encode_PutsTicketAtOffset44()
    {
        var req = new MasterJoinRequest(
            Unknown1: 0, Unknown2: 0, Unknown3: 0,
            AvatarIdMsb: 0, AvatarIdLsb: 0,
            ToSectorId: 0, FromSectorId: 0,
            PlayerLevel: 0,
            Unknown8: 0, Unknown9: 0, Unknown10: 0,
            Ticket: "ABCDEFGHIJ1234567890");

        byte[] wire = new MasterJoinCodec().EncodeOutbound(req);

        Assert.Equal(64, wire.Length);
        for (int i = 0; i < 44; i++)
            Assert.Equal(0, wire[i]);
        for (int i = 0; i < 20; i++)
            Assert.Equal((byte)"ABCDEFGHIJ1234567890"[i], wire[44 + i]);
    }

    [Fact]
    public void Encode_WritesToSectorIdAsBigEndian()
    {
        // Server reads with ntohl(), so wire MUST be big-endian.
        // ToSectorID is at offset 20 in the C++ struct.
        var req = new MasterJoinRequest(
            Unknown1: 0, Unknown2: 0, Unknown3: 0,
            AvatarIdMsb: 0, AvatarIdLsb: 0,
            ToSectorId: 0x12345678,
            FromSectorId: 0,
            PlayerLevel: 0,
            Unknown8: 0, Unknown9: 0, Unknown10: 0,
            Ticket: "T");

        byte[] wire = new MasterJoinCodec().EncodeOutbound(req);

        Assert.Equal(0x12, wire[20]);
        Assert.Equal(0x34, wire[21]);
        Assert.Equal(0x56, wire[22]);
        Assert.Equal(0x78, wire[23]);
    }

    [Fact]
    public void Encode_AvatarIdMsbAndLsbAtCorrectOffsets()
    {
        // avatar_id_msb at offset 12, lsb at offset 16
        // (per the C++ comment about the wire format).
        var req = new MasterJoinRequest(
            Unknown1: 0, Unknown2: 0, Unknown3: 0,
            AvatarIdMsb: unchecked((int)0xAABBCCDD),
            AvatarIdLsb: unchecked((int)0xEEFF0011),
            ToSectorId: 0, FromSectorId: 0,
            PlayerLevel: 0,
            Unknown8: 0, Unknown9: 0, Unknown10: 0,
            Ticket: "T");

        byte[] wire = new MasterJoinCodec().EncodeOutbound(req);

        Assert.Equal(0xAA, wire[12]);
        Assert.Equal(0xBB, wire[13]);
        Assert.Equal(0xCC, wire[14]);
        Assert.Equal(0xDD, wire[15]);
        Assert.Equal(0xEE, wire[16]);
        Assert.Equal(0xFF, wire[17]);
        Assert.Equal(0x00, wire[18]);
        Assert.Equal(0x11, wire[19]);
    }

    [Fact]
    public void Encode_ZeroPadsShortTicket()
    {
        var req = new MasterJoinRequest(
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            Ticket: "ABC");

        byte[] wire = new MasterJoinCodec().EncodeOutbound(req);

        Assert.Equal((byte)'A', wire[44]);
        Assert.Equal((byte)'B', wire[45]);
        Assert.Equal((byte)'C', wire[46]);
        for (int i = 47; i < 64; i++)
            Assert.Equal(0, wire[i]);
    }

    [Fact]
    public void Encode_TicketTooLong_Throws()
    {
        var req = new MasterJoinRequest(
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            Ticket: new string('X', MasterJoinCodec.TicketLength + 1));

        Assert.Throws<ArgumentException>(
            () => new MasterJoinCodec().EncodeOutbound(req));
    }

    [Fact]
    public void Encode_Decode_RoundTrips()
    {
        var original = new MasterJoinRequest(
            Unknown1: 1, Unknown2: 2, Unknown3: 3,
            AvatarIdMsb: 4, AvatarIdLsb: 5,
            ToSectorId: 100, FromSectorId: 99,
            PlayerLevel: 50,
            Unknown8: 8, Unknown9: 9, Unknown10: 10,
            Ticket: "ABCDEFGHIJ1234567890");

        var codec = new MasterJoinCodec();
        byte[] wire = codec.EncodeOutbound(original);
        var decoded = (MasterJoinRequest)codec.DecodeInbound(wire);

        Assert.Equal(original, decoded);
    }

    [Fact]
    public void Decode_TrimsNullTerminator()
    {
        var codec = new MasterJoinCodec();
        byte[] wire = codec.EncodeOutbound(new MasterJoinRequest(
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, Ticket: "SHORT"));

        var decoded = (MasterJoinRequest)codec.DecodeInbound(wire);
        Assert.Equal("SHORT", decoded.Ticket);
    }

    [Fact]
    public void Decode_ShortPayload_Throws()
    {
        var codec = new MasterJoinCodec();
        Assert.Throws<InvalidDataException>(
            () => codec.DecodeInbound(new byte[MasterJoinCodec.WireSize - 1]));
    }

    [Fact]
    public void Encode_WrongMessageType_Throws()
    {
        var codec = new MasterJoinCodec();
        Assert.Throws<ArgumentException>(() => codec.EncodeOutbound("not a request"));
    }
}
