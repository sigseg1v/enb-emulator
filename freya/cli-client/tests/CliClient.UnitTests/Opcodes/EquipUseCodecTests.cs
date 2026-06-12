// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Outbound;
using Xunit;

namespace N7.CliClient.UnitTests.Opcodes;

/// <summary>
/// Byte-pins <see cref="EquipUseCodec"/> (0x005D EQUIP_USE, client -&gt; server)
/// against the live reference server captures. The 6-byte
/// EquipUse struct followed the 12-byte outer sector header on the
/// proxy&lt;-&gt;server leg; the outer header is added by the transport, so we pin
/// only the codec's payload.
///
/// Player GameID in both captures is 0x4003992A; EQUIP_USE carries it
/// LITTLE-endian (40 03 99 2A host order -&gt; 2A 99 03 40 on the wire), unlike
/// 0x0027 INVENTORY_MOVE which is big-endian on every field.
/// </summary>
public sealed class EquipUseCodecTests
{
    private const int PlayerGameId = 0x4003992A;

    [Fact]
    public void Opcode_Is_0x005D()
    {
        var codec = new EquipUseCodec();
        Assert.Equal(OpcodeId.Known.EquipUse, codec.Opcode);
        Assert.Equal(0x005D, codec.Opcode.Value);
    }

    [Fact]
    public void Encode_FireWeaponSlot3_MatchesCapture_KillLoot2_dg3()
    {
        // KillLoot2 dg #3 body: fire the weapon equipped in slot 3.
        //   2A 99 03 40 02 03
        var msg = new EquipUseMessage(PlayerGameId, InvNum: 2, InvSlot: 3);

        byte[] wire = new EquipUseCodec().EncodeOutbound(msg);

        Assert.Equal(new byte[]
        {
            0x2A, 0x99, 0x03, 0x40,   // GameID 0x4003992A (LE)
            0x02,                     // InvNum = 2
            0x03,                     // InvSlot = 3 (weapon)
        }, wire);
    }

    [Fact]
    public void Encode_ActivateDeviceSlot11_MatchesCapture_SkillTraining_dg44()
    {
        // SkillTrainingHostileDevice2 dg #44 body: activate the device in slot 11.
        //   2A 99 03 40 02 0B
        var msg = new EquipUseMessage(PlayerGameId, InvNum: 2, InvSlot: 11);

        byte[] wire = new EquipUseCodec().EncodeOutbound(msg);

        Assert.Equal(new byte[]
        {
            0x2A, 0x99, 0x03, 0x40,   // GameID 0x4003992A (LE)
            0x02,                     // InvNum = 2
            0x0B,                     // InvSlot = 11 (device)
        }, wire);
    }

    [Fact]
    public void DefaultInvNumCtor_UsesCapturedPageValue()
    {
        // The single-byte-slot ctor must default InvNum to the captured value (2).
        byte[] wire = new EquipUseCodec().EncodeOutbound(
            new EquipUseMessage(PlayerGameId, invSlot: 3));

        Assert.Equal(new byte[] { 0x2A, 0x99, 0x03, 0x40, 0x02, 0x03 }, wire);
    }

    [Fact]
    public void EncodeDecode_RoundTrips()
    {
        var codec = new EquipUseCodec();
        var msg = new EquipUseMessage(PlayerGameId, InvNum: 2, InvSlot: 7);
        var back = (EquipUseMessage)codec.DecodeInbound(codec.EncodeOutbound(msg));
        Assert.Equal(msg, back);
    }

    [Fact]
    public void Encode_ProducesExactly6Bytes()
    {
        byte[] wire = new EquipUseCodec().EncodeOutbound(new EquipUseMessage(1, 2, 3));
        Assert.Equal(EquipUseCodec.Size, wire.Length);
    }

    [Fact]
    public void Decode_TooShort_Throws()
    {
        var codec = new EquipUseCodec();
        Assert.Throws<InvalidDataException>(
            () => codec.DecodeInbound(new byte[EquipUseCodec.Size - 1]));
    }

    [Fact]
    public void Encode_WrongMessageType_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => new EquipUseCodec().EncodeOutbound("not an equip-use"));
    }
}
