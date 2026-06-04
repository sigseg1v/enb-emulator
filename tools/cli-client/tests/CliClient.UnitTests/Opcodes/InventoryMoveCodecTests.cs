// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Outbound;
using Xunit;

namespace N7.CliClient.UnitTests.Opcodes;

/// <summary>
/// Byte-pins <see cref="InventoryMoveCodec"/> (0x0027 INVENTORY_MOVE,
/// client -> server) against the live reference server (216.219.87.147)
/// capture VendorInvEco-...-072833.pcapng. Each pinned body is the 24-byte
/// InvMove struct that followed the 12-byte outer sector header on the
/// proxy<->server leg; the outer header (length/opcode/gameid/seq) is added
/// by the transport, not the codec, so we pin only the codec's payload.
///
/// Player GameID throughout this capture is 0x4003992A.
/// </summary>
public sealed class InventoryMoveCodecTests
{
    private const int PlayerGameId = 0x4003992A;

    [Fact]
    public void Opcode_Is_0x0027()
    {
        var codec = new InventoryMoveCodec();
        Assert.Equal(OpcodeId.Known.InventoryMove, codec.Opcode);
        Assert.Equal(0x0027, codec.Opcode.Value);
    }

    [Fact]
    public void Encode_SellCargoSlot28_MatchesCapture_dg20()
    {
        // VendorInvEco dg #20 body: cargo slot 28 -> vendor (sell), first free.
        //   40 03 99 2A 00 00 00 01 00 00 00 1C 00 00 00 04 FF FF FF FF 00 00 00 01
        var msg = new InventoryMoveMessage(
            PlayerGameId, InventoryContainer.Cargo, 0x1C,
            InventoryContainer.Vendor, -1, 1);

        byte[] wire = new InventoryMoveCodec().EncodeOutbound(msg);

        Assert.Equal(new byte[]
        {
            0x40, 0x03, 0x99, 0x2A,   // GameID  (BE)
            0x00, 0x00, 0x00, 0x01,   // FromInv = 1 (cargo)
            0x00, 0x00, 0x00, 0x1C,   // FromSlot = 28
            0x00, 0x00, 0x00, 0x04,   // ToInv = 4 (vendor / sell)
            0xFF, 0xFF, 0xFF, 0xFF,   // ToSlot = -1 (first free)
            0x00, 0x00, 0x00, 0x01,   // Num = 1
        }, wire);
    }

    [Fact]
    public void Encode_BuyIntoCargoSlot31_MatchesCapture_dg12()
    {
        // VendorInvEco dg #12 body: vendor stock slot 0 -> cargo slot 31 (buy).
        //   40 03 99 2A 00 00 00 04 00 00 00 00 00 00 00 01 00 00 00 1F 00 00 00 01
        var msg = new InventoryMoveMessage(
            PlayerGameId, InventoryContainer.Vendor, 0,
            InventoryContainer.Cargo, 0x1F, 1);

        byte[] wire = new InventoryMoveCodec().EncodeOutbound(msg);

        Assert.Equal(new byte[]
        {
            0x40, 0x03, 0x99, 0x2A,
            0x00, 0x00, 0x00, 0x04,   // FromInv = 4 (vendor stock)
            0x00, 0x00, 0x00, 0x00,   // FromSlot = 0
            0x00, 0x00, 0x00, 0x01,   // ToInv = 1 (cargo)
            0x00, 0x00, 0x00, 0x1F,   // ToSlot = 31
            0x00, 0x00, 0x00, 0x01,   // Num = 1
        }, wire);
    }

    [Fact]
    public void Encode_CargoRearrange24to28_MatchesCapture_dg25()
    {
        // VendorInvEco dg #25 body: cargo slot 24 -> cargo slot 28.
        //   40 03 99 2A 00 00 00 01 00 00 00 18 00 00 00 01 00 00 00 1C 00 00 00 01
        var msg = new InventoryMoveMessage(
            PlayerGameId, InventoryContainer.Cargo, 0x18,
            InventoryContainer.Cargo, 0x1C, 1);

        byte[] wire = new InventoryMoveCodec().EncodeOutbound(msg);

        Assert.Equal(new byte[]
        {
            0x40, 0x03, 0x99, 0x2A,
            0x00, 0x00, 0x00, 0x01,
            0x00, 0x00, 0x00, 0x18,   // FromSlot = 24
            0x00, 0x00, 0x00, 0x01,
            0x00, 0x00, 0x00, 0x1C,   // ToSlot = 28
            0x00, 0x00, 0x00, 0x01,
        }, wire);
    }

    [Fact]
    public void EncodeDecode_RoundTrips()
    {
        var codec = new InventoryMoveCodec();
        var msg = new InventoryMoveMessage(
            PlayerGameId, InventoryContainer.Cargo, 5,
            InventoryContainer.Vault, -1, 7);
        var back = (InventoryMoveMessage)codec.DecodeInbound(codec.EncodeOutbound(msg));
        Assert.Equal(msg, back);
    }

    [Fact]
    public void Encode_ProducesExactly24Bytes()
    {
        byte[] wire = new InventoryMoveCodec().EncodeOutbound(
            new InventoryMoveMessage(1, 1, 0, 1, 1, 1));
        Assert.Equal(InventoryMoveCodec.Size, wire.Length);
    }

    [Fact]
    public void Decode_TooShort_Throws()
    {
        var codec = new InventoryMoveCodec();
        Assert.Throws<InvalidDataException>(
            () => codec.DecodeInbound(new byte[InventoryMoveCodec.Size - 1]));
    }

    [Fact]
    public void Encode_WrongMessageType_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new InventoryMoveCodec().EncodeOutbound("not a move"));
    }
}
