// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Outbound;
using Xunit;

namespace N7.CliClient.UnitTests.Opcodes;

/// <summary>
/// Byte-pins <see cref="InventoryMoveCodec"/> (0x0027 INVENTORY_MOVE,
/// client -> server) against the live reference server
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
    public void Encode_JettisonCargoSlot32_MatchesCapture_ProspectRun_dg35()
    {
        // ProspectRun dg #35 body: cargo slot 32 -> space (jettison), Num=1.
        // GameID for this capture is 0x4000C95F.
        //   40 00 C9 5F 00 00 00 01 00 00 00 20 00 00 00 0B FF FF FF FF 00 00 00 01
        var msg = new InventoryMoveMessage(
            0x4000C95F, InventoryContainer.Cargo, 0x20,
            InventoryContainer.Space, -1, 1);

        byte[] wire = new InventoryMoveCodec().EncodeOutbound(msg);

        Assert.Equal(new byte[]
        {
            0x40, 0x00, 0xC9, 0x5F,   // GameID (BE)
            0x00, 0x00, 0x00, 0x01,   // FromInv = 1 (cargo)
            0x00, 0x00, 0x00, 0x20,   // FromSlot = 32
            0x00, 0x00, 0x00, 0x0B,   // ToInv = 11 (space / jettison)
            0xFF, 0xFF, 0xFF, 0xFF,   // ToSlot = -1 (first free)
            0x00, 0x00, 0x00, 0x01,   // Num = 1
        }, wire);
    }

    [Fact]
    public void Encode_LootHuskSlot0_MatchesCapture_KillLoot2_dg65()
    {
        // KillLoot2 dg #65 body: loot the targeted husk's slot 0.
        // The real client sends FromInv=6 (loot), ToInv=0, ToSlot=-1, Num=-1.
        //   40 03 99 2A 00 00 00 06 00 00 00 00 00 00 00 00 FF FF FF FF FF FF FF FF
        var msg = new InventoryMoveMessage(
            PlayerGameId, (int)InventoryContainer.Loot, 0,
            0, -1, -1);

        byte[] wire = new InventoryMoveCodec().EncodeOutbound(msg);

        Assert.Equal(new byte[]
        {
            0x40, 0x03, 0x99, 0x2A,   // GameID (BE)
            0x00, 0x00, 0x00, 0x06,   // FromInv = 6 (loot)
            0x00, 0x00, 0x00, 0x00,   // FromSlot = 0 (husk loot slot)
            0x00, 0x00, 0x00, 0x00,   // ToInv = 0
            0xFF, 0xFF, 0xFF, 0xFF,   // ToSlot = -1
            0xFF, 0xFF, 0xFF, 0xFF,   // Num = -1 (loot all)
        }, wire);
    }

    [Fact]
    public void Encode_StackSplit_CargoToEmptyCargoSlot_PartialNum()
    {
        // Stack split: move Num (< full stack) from cargo slot 5 to empty cargo
        // slot 9. Server CheckStack (server/src/PlayerInventory.cpp:435) splits
        // when the destination is empty and MoveNum < Source.StackCount. No
        // capture frame in the local set carries Num>1 (the captured operator
        // never split a stack), so this pins the encoding by construction --
        // the struct is identical to the captured frames, only the Num and
        // slot fields vary.
        var msg = new InventoryMoveMessage(
            PlayerGameId, InventoryContainer.Cargo, 5,
            InventoryContainer.Cargo, 9, 3);

        byte[] wire = new InventoryMoveCodec().EncodeOutbound(msg);

        Assert.Equal(new byte[]
        {
            0x40, 0x03, 0x99, 0x2A,
            0x00, 0x00, 0x00, 0x01,   // FromInv = 1 (cargo)
            0x00, 0x00, 0x00, 0x05,   // FromSlot = 5
            0x00, 0x00, 0x00, 0x01,   // ToInv = 1 (cargo)
            0x00, 0x00, 0x00, 0x09,   // ToSlot = 9
            0x00, 0x00, 0x00, 0x03,   // Num = 3 (partial -> split)
        }, wire);
    }

    [Fact]
    public void Encode_StackCombine_CargoOntoMatchingStack_FullNum()
    {
        // Stack combine: move the whole stack from cargo slot 5 onto a matching
        // stack in cargo slot 9. Server CheckStack merges (capped at MaxStack)
        // when source and destination share ItemTemplateID/Quality/BuilderName.
        // Same struct, distinguished only by the destination already holding the
        // same item -- a server-state condition, not a wire-format one.
        var msg = new InventoryMoveMessage(
            PlayerGameId, InventoryContainer.Cargo, 5,
            InventoryContainer.Cargo, 9, 10);

        byte[] wire = new InventoryMoveCodec().EncodeOutbound(msg);

        Assert.Equal(new byte[]
        {
            0x40, 0x03, 0x99, 0x2A,
            0x00, 0x00, 0x00, 0x01,
            0x00, 0x00, 0x00, 0x05,
            0x00, 0x00, 0x00, 0x01,
            0x00, 0x00, 0x00, 0x09,
            0x00, 0x00, 0x00, 0x0A,   // Num = 10 (full stack -> combine)
        }, wire);
    }

    [Fact]
    public void Encode_EquipWeapon_CargoSlot30ToEquipSlot3_MatchesCapture_VendorInvEco_dg31()
    {
        // VendorInvEco dg #31 body: equip the weapon in cargo slot 30 into
        // equip slot 3 (cargo -> equip, Num=1).
        //   40 03 99 2A 00 00 00 01 00 00 00 1E 00 00 00 02 00 00 00 03 00 00 00 01
        var msg = new InventoryMoveMessage(
            PlayerGameId, InventoryContainer.Cargo, 0x1E,
            InventoryContainer.Equip, 3, 1);

        byte[] wire = new InventoryMoveCodec().EncodeOutbound(msg);

        Assert.Equal(new byte[]
        {
            0x40, 0x03, 0x99, 0x2A,
            0x00, 0x00, 0x00, 0x01,   // FromInv = 1 (cargo)
            0x00, 0x00, 0x00, 0x1E,   // FromSlot = 30
            0x00, 0x00, 0x00, 0x02,   // ToInv = 2 (equip)
            0x00, 0x00, 0x00, 0x03,   // ToSlot = 3
            0x00, 0x00, 0x00, 0x01,   // Num = 1
        }, wire);
    }

    [Fact]
    public void Encode_LoadAmmo_CargoSlot29ToEquipSlot3_Num126_MatchesCapture_VendorInvEco_dg34()
    {
        // VendorInvEco dg #34 body: load 126 rounds of ammo from cargo slot 29
        // into the weapon equipped in equip slot 3 (cargo -> equip, Num=0x7E).
        //   40 03 99 2A 00 00 00 01 00 00 00 1D 00 00 00 02 00 00 00 03 00 00 00 7E
        var msg = new InventoryMoveMessage(
            PlayerGameId, InventoryContainer.Cargo, 0x1D,
            InventoryContainer.Equip, 3, 0x7E);

        byte[] wire = new InventoryMoveCodec().EncodeOutbound(msg);

        Assert.Equal(new byte[]
        {
            0x40, 0x03, 0x99, 0x2A,
            0x00, 0x00, 0x00, 0x01,   // FromInv = 1 (cargo)
            0x00, 0x00, 0x00, 0x1D,   // FromSlot = 29
            0x00, 0x00, 0x00, 0x02,   // ToInv = 2 (equip)
            0x00, 0x00, 0x00, 0x03,   // ToSlot = 3
            0x00, 0x00, 0x00, 0x7E,   // Num = 126 (ammo count)
        }, wire);
    }

    [Fact]
    public void Encode_UnloadAmmo_EquipSlot3ToCargoSlot29_Num126_MatchesCapture_VendorInvEco_dg36()
    {
        // VendorInvEco dg #36 body: unload the 126 ammo back from equip slot 3
        // into cargo slot 29 (equip -> cargo, Num=0x7E).
        //   40 03 99 2A 00 00 00 02 00 00 00 03 00 00 00 01 00 00 00 1D 00 00 00 7E
        var msg = new InventoryMoveMessage(
            PlayerGameId, InventoryContainer.Equip, 3,
            InventoryContainer.Cargo, 0x1D, 0x7E);

        byte[] wire = new InventoryMoveCodec().EncodeOutbound(msg);

        Assert.Equal(new byte[]
        {
            0x40, 0x03, 0x99, 0x2A,
            0x00, 0x00, 0x00, 0x02,   // FromInv = 2 (equip)
            0x00, 0x00, 0x00, 0x03,   // FromSlot = 3
            0x00, 0x00, 0x00, 0x01,   // ToInv = 1 (cargo)
            0x00, 0x00, 0x00, 0x1D,   // ToSlot = 29
            0x00, 0x00, 0x00, 0x7E,   // Num = 126
        }, wire);
    }

    [Fact]
    public void Encode_UnequipWeapon_EquipSlot3ToCargoSlot30_MatchesCapture_VendorInvEco_dg41()
    {
        // VendorInvEco dg #41 body: unequip the weapon from equip slot 3 back
        // into cargo slot 30 (equip -> cargo, Num=1).
        //   40 03 99 2A 00 00 00 02 00 00 00 03 00 00 00 01 00 00 00 1E 00 00 00 01
        var msg = new InventoryMoveMessage(
            PlayerGameId, InventoryContainer.Equip, 3,
            InventoryContainer.Cargo, 0x1E, 1);

        byte[] wire = new InventoryMoveCodec().EncodeOutbound(msg);

        Assert.Equal(new byte[]
        {
            0x40, 0x03, 0x99, 0x2A,
            0x00, 0x00, 0x00, 0x02,   // FromInv = 2 (equip)
            0x00, 0x00, 0x00, 0x03,   // FromSlot = 3
            0x00, 0x00, 0x00, 0x01,   // ToInv = 1 (cargo)
            0x00, 0x00, 0x00, 0x1E,   // ToSlot = 30
            0x00, 0x00, 0x00, 0x01,   // Num = 1
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
