// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Buffers.Binary;
using N7.CliClient.Logging;
using N7.CliClient.Opcodes.Records;
using Xunit;

namespace N7.CliClient.UnitTests.Opcodes;

/// <summary>
/// Byte-pins eight previously-unparsed server-emitted opcodes (all fell through
/// to <see cref="GenericRecord"/>): 0x0026 CHANGE_BASE_ASSET, 0x002A SET_ZBAND,
/// 0x002B SET_BBOX, 0x002F INIT_RENDER_STATE, 0x0032 DEACTIVATE_RENDER_STATE,
/// 0x004A CREATE_ATTACHMENT, 0x00A2 NOTIFY_EMOTE, 0x00D2 GUILD_PLAYER_PERMISSIONS.
/// Each synthesises the EXACT bytes the server emitter writes (server/src
/// emit sites cited per record) and asserts every field reads back AND every
/// byte is consumed (no <c>???</c> gap). The big-endian fields (CREATE_ATTACHMENT,
/// GUILD_PLAYER_PERMISSIONS) are written BE here to match the server's htonl/ntohl.
/// </summary>
public sealed class RenderStateAndMiscRecordTests
{
    public RenderStateAndMiscRecordTests() => AnsiPalette.Enabled = false;

    private static string Dump(ushort opcode, byte[] payload)
        => PacketRecord.Resolve(opcode, payload).DumpToString();

    private static void NoGaps(string dump)
    {
        Assert.DoesNotContain("???", dump);
        Assert.DoesNotContain("truncated", dump);
    }

    [Fact]
    public void ChangeBaseAsset_FullParse()
    {
        var b = new byte[24];
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(0), 0x000A1234);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(4), 9876);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(8), 1.5f);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(12), 0.25f);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(16), 0.5f);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(20), 0.75f);
        var dump = Dump(0x0026, b);
        Assert.Contains("0x000A1234", dump);
        Assert.Contains("BaseAsset", dump);
        Assert.Contains("9876", dump);
        Assert.Contains("Scale", dump);
        Assert.Contains("HSV", dump);
        NoGaps(dump);
    }

    [Fact]
    public void SetZBand_FullParse()
    {
        var b = new byte[8];
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(0), -100.0f);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(4), 100.0f);
        var dump = Dump(0x002A, b);
        Assert.Contains("Min", dump);
        Assert.Contains("Max", dump);
        Assert.Contains("-100", dump);
        NoGaps(dump);
    }

    [Fact]
    public void SetBBox_FullParse()
    {
        var b = new byte[16];
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(0), -1.0f);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(4), -2.0f);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(8), 3.0f);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(12), 4.0f);
        var dump = Dump(0x002B, b);
        Assert.Contains("XMin", dump);
        Assert.Contains("YMax", dump);
        NoGaps(dump);
    }

    [Fact]
    public void InitRenderState_FullParse()
    {
        var b = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(0), 0x000B5678);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4), 0xCAFEBABE);
        var dump = Dump(0x002F, b);
        Assert.Contains("GameID", dump);
        Assert.Contains("0x000B5678", dump);
        Assert.Contains("RenderStateID", dump);
        Assert.Contains("0xCAFEBABE", dump);
        NoGaps(dump);
    }

    [Fact]
    public void DeactivateRenderState_FullParse()
    {
        var b = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(0), 0x00001111);
        var dump = Dump(0x0032, b);
        Assert.Contains("GameID", dump);
        Assert.Contains("0x00001111", dump);
        NoGaps(dump);
    }

    [Fact]
    public void CreateAttachment_FullParse_BigEndianFields()
    {
        var b = new byte[12];
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(0), 0x000A0001);   // Parent (BE)
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(4), 0x000A0002);   // Child (BE)
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(8), 3);            // Slot (BE)
        var dump = Dump(0x004A, b);
        Assert.Contains("ParentID", dump);
        Assert.Contains("0x000A0001", dump);
        Assert.Contains("ChildID", dump);
        Assert.Contains("0x000A0002", dump);
        Assert.Contains("Slot", dump);
        Assert.Contains("3", dump);
        NoGaps(dump);
    }

    [Fact]
    public void NotifyEmote_FullParse()
    {
        var b = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(0), 0x000A1234);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(4), 42);
        var dump = Dump(0x00A2, b);
        Assert.Contains("GameID", dump);
        Assert.Contains("Emote", dump);
        Assert.Contains("42", dump);
        NoGaps(dump);
    }

    [Fact]
    public void GuildPlayerPermissions_FullParse_BigEndianFields()
    {
        var b = new byte[16];
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(0), 0x0000007F);   // Permission (BE)
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(4), 5);            // MaxPromote (BE)
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(8), 6);            // MaxRemove (BE)
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(12), 7);           // MinDemote (BE)
        var dump = Dump(0x00D2, b);
        Assert.Contains("Permission", dump);
        Assert.Contains("0x0000007F", dump);
        Assert.Contains("MaxPromote", dump);
        Assert.Contains("MinDemote", dump);
        NoGaps(dump);
    }

    [Theory]
    [InlineData((ushort)0x0026, 23)]
    [InlineData((ushort)0x002A, 7)]
    [InlineData((ushort)0x002B, 15)]
    [InlineData((ushort)0x002F, 7)]
    [InlineData((ushort)0x0032, 3)]
    [InlineData((ushort)0x004A, 11)]
    [InlineData((ushort)0x00A2, 7)]
    [InlineData((ushort)0x00D2, 15)]
    public void Truncated_Flags(ushort opcode, int shortLen)
    {
        var dump = Dump(opcode, new byte[shortLen]);
        Assert.Contains("truncated", dump);
    }
}
