// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using System.Net;
using System.Text;
using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Inbound;
using Xunit;

namespace N7.CliClient.UnitTests.Opcodes;

/// <summary>
/// Offline unit tests for <see cref="GlobalAvatarListCodec"/>. The
/// integration suite proves the codec works against the live server's
/// actual output; these tests cover the decode logic in isolation
/// (without docker), so a typo in field offsets or byte order surfaces
/// on every PR rather than waiting for the cli-integration job to run.
/// </summary>
public sealed class GlobalAvatarListCodecTests
{
    [Fact]
    public void Opcode_Is_0x0070()
    {
        var codec = new GlobalAvatarListCodec();
        Assert.Equal(OpcodeId.Known.GlobalAvatarList, codec.Opcode);
        Assert.Equal(0x0070, codec.Opcode.Value);
    }

    [Fact]
    public void WireSize_MatchesStructSize()
    {
        // 5 × 374 (AvatarListItem) + 4 (num_galaxies) + 2 × 84 (Galaxy)
        Assert.Equal(2042, GlobalAvatarListCodec.WireSize);
    }

    [Fact]
    public void Decode_AllZeros_YieldsFiveEmptySlotsAndOneEmptyGalaxy()
    {
        byte[] payload = new byte[GlobalAvatarListCodec.WireSize];

        var result = (GlobalAvatarList)new GlobalAvatarListCodec()
            .DecodeInbound(payload);

        Assert.Equal(5, result.Avatars.Length);
        foreach (var slot in result.Avatars)
        {
            Assert.Equal(0, slot.Info.AvatarSlot);
            Assert.Equal(0, slot.Info.SectorId);
            Assert.Equal(string.Empty, slot.Info.Location);
            Assert.Equal(string.Empty, slot.Data.FirstName);
            Assert.Equal(string.Empty, slot.Data.LastName);
        }
        // num_galaxies is zero — both BE and LE interpretations are 0,
        // which fails the [1, MaxGalaxies] range check, so the codec
        // falls back to the LE reading. Either way we expect zero
        // decoded galaxies.
        Assert.Equal(0, result.NumGalaxies);
        Assert.Empty(result.Galaxies);
    }

    [Fact]
    public void Decode_AvatarInfo_ReadsAllFieldsBigEndian()
    {
        // Populate avatar[0].info with distinct big-endian values per field.
        byte[] payload = new byte[GlobalAvatarListCodec.WireSize];
        var info = payload.AsSpan(0, GlobalAvatarListCodec.AvatarInfoSize);
        BinaryPrimitives.WriteInt32BigEndian(info.Slice( 0, 4), 0x01020304); // AvatarSlot
        BinaryPrimitives.WriteInt32BigEndian(info.Slice( 4, 4), 1071);        // SectorId
        BinaryPrimitives.WriteInt32BigEndian(info.Slice( 8, 4), 1);           // GalaxyId
        BinaryPrimitives.WriteInt32BigEndian(info.Slice(12, 4), 5);           // Count
        BinaryPrimitives.WriteInt32BigEndian(info.Slice(16, 4), 0);           // AvatarIdMsb
        BinaryPrimitives.WriteInt32BigEndian(info.Slice(20, 4), 12345);       // AvatarIdLsb
        BinaryPrimitives.WriteInt32BigEndian(info.Slice(24, 4), 0);           // AccountIdMsb
        BinaryPrimitives.WriteInt32BigEndian(info.Slice(28, 4), 9000001);     // AccountIdLsb
        BinaryPrimitives.WriteInt32BigEndian(info.Slice(32, 4), 100);         // AdminLevel
        BinaryPrimitives.WriteInt32BigEndian(info.Slice(36, 4), 1);           // GmFlag
        BinaryPrimitives.WriteInt32BigEndian(info.Slice(40, 4), 50);          // CombatLevel
        BinaryPrimitives.WriteInt32BigEndian(info.Slice(44, 4), 75);          // ExploreLevel
        BinaryPrimitives.WriteInt32BigEndian(info.Slice(48, 4), 25);          // TradeLevel
        Encoding.ASCII.GetBytes("Saturn").CopyTo(info.Slice(52, 81));         // Location

        var result = (GlobalAvatarList)new GlobalAvatarListCodec()
            .DecodeInbound(payload);

        var decoded = result.Avatars[0].Info;
        Assert.Equal(0x01020304, decoded.AvatarSlot);
        Assert.Equal(1071, decoded.SectorId);
        Assert.Equal(1, decoded.GalaxyId);
        Assert.Equal(5, decoded.Count);
        Assert.Equal(12345, decoded.AvatarIdLsb);
        Assert.Equal(9000001, decoded.AccountIdLsb);
        Assert.Equal(100, decoded.AdminLevel);
        Assert.Equal(1, decoded.GmFlag);
        Assert.Equal(50, decoded.CombatLevel);
        Assert.Equal(75, decoded.ExploreLevel);
        Assert.Equal(25, decoded.TradeLevel);
        Assert.Equal("Saturn", decoded.Location);
    }

    [Fact]
    public void Decode_AvatarData_ReadsNamesAndCoreIntsLittleEndian()
    {
        byte[] payload = new byte[GlobalAvatarListCodec.WireSize];
        int dataStart = GlobalAvatarListCodec.AvatarInfoSize;
        var data = payload.AsSpan(dataStart, GlobalAvatarListCodec.AvatarDataSize);

        Encoding.ASCII.GetBytes("Skylar").CopyTo(data.Slice(0, 20));
        Encoding.ASCII.GetBytes("Skywalker").CopyTo(data.Slice(20, 20));
        BinaryPrimitives.WriteInt32LittleEndian(data.Slice(40, 4), 7);  // AvatarType
        data[45] = 3;                                                    // AvatarVersion
        BinaryPrimitives.WriteInt32LittleEndian(data.Slice(48, 4), 2);  // Race
        BinaryPrimitives.WriteInt32LittleEndian(data.Slice(52, 4), 9);  // Profession
        BinaryPrimitives.WriteInt32LittleEndian(data.Slice(56, 4), 1);  // Gender
        BinaryPrimitives.WriteInt32LittleEndian(data.Slice(60, 4), 4);  // MoodType

        var result = (GlobalAvatarList)new GlobalAvatarListCodec()
            .DecodeInbound(payload);

        var decoded = result.Avatars[0].Data;
        Assert.Equal("Skylar", decoded.FirstName);
        Assert.Equal("Skywalker", decoded.LastName);
        Assert.Equal(7, decoded.AvatarType);
        Assert.Equal(3, decoded.AvatarVersion);
        Assert.Equal(2, decoded.Race);
        Assert.Equal(9, decoded.Profession);
        Assert.Equal(1, decoded.Gender);
        Assert.Equal(4, decoded.MoodType);
        Assert.Equal(241 - 64, decoded.RawAppearance.Length);
    }

    [Fact]
    public void Decode_NumGalaxies_HonoursBigEndianWhenInRange()
    {
        // Server writes `list->num_galaxies = ntohl(1)` — on a LE host
        // that's 0x01000000 in memory, which reads as big-endian 1.
        byte[] payload = new byte[GlobalAvatarListCodec.WireSize];
        int numGalaxiesOffset =
            GlobalAvatarListCodec.AvatarCount * GlobalAvatarListCodec.AvatarListItemSize;

        // 0x01000000 in big-endian == 1
        BinaryPrimitives.WriteInt32BigEndian(
            payload.AsSpan(numGalaxiesOffset, 4), 1);

        // Also fill galaxy[0] so the per-galaxy decode runs
        int galaxy0 = numGalaxiesOffset + 4;
        Encoding.ASCII.GetBytes("Earth & Beyond").CopyTo(
            payload.AsSpan(galaxy0, 64));
        BinaryPrimitives.WriteInt32LittleEndian(
            payload.AsSpan(galaxy0 + 64, 4), 1);          // GalaxyId
        // IP_Address: server writes `ntohl(inet_addr("127.0.0.1"))`.
        // `inet_addr` returns network-byte-order bytes 7F 00 00 01 (= the
        // uint32 0x0100007F on LE). `ntohl` swaps to host order = the
        // uint32 0x7F000001, which a LE host stores back to memory as
        // bytes 01 00 00 7F. So the wire bytes at offset 68 must be
        // 01 00 00 7F. WriteInt32LittleEndian(value=0x7F000001) writes
        // exactly those bytes (LE = least-significant byte first → 01,
        // 00, 00, 7F).
        BinaryPrimitives.WriteInt32LittleEndian(
            payload.AsSpan(galaxy0 + 68, 4), 0x7F000001);
        // port: 3801 (0x0ED9). Server writes `ntohs(3801)` = 0xD90E on LE.
        // Codec reads LE so it sees back 0x0ED9 = 3801.
        BinaryPrimitives.WriteUInt16LittleEndian(
            payload.AsSpan(galaxy0 + 72, 2), 3801);
        BinaryPrimitives.WriteInt32LittleEndian(
            payload.AsSpan(galaxy0 + 74, 4), 42);         // NumPlayers
        BinaryPrimitives.WriteInt32LittleEndian(
            payload.AsSpan(galaxy0 + 78, 4), 500);        // MaxPlayers

        var result = (GlobalAvatarList)new GlobalAvatarListCodec()
            .DecodeInbound(payload);

        Assert.Equal(1, result.NumGalaxies);
        Assert.Single(result.Galaxies);
        var galaxy = result.Galaxies[0];
        Assert.Equal("Earth & Beyond", galaxy.Name);
        Assert.Equal(1, galaxy.GalaxyId);
        Assert.Equal(IPAddress.Loopback, galaxy.IpAddress);
        Assert.Equal(3801, galaxy.Port);
        Assert.Equal(42, galaxy.NumPlayers);
        Assert.Equal(500, galaxy.MaxPlayers);
    }

    [Fact]
    public void Decode_ShortPayload_Throws()
    {
        var codec = new GlobalAvatarListCodec();
        Assert.Throws<InvalidDataException>(
            () => codec.DecodeInbound(new byte[GlobalAvatarListCodec.WireSize - 1]));
    }

    [Fact]
    public void EncodeOutbound_Throws_ServerToClientOnly()
    {
        var codec = new GlobalAvatarListCodec();
        Assert.Throws<NotSupportedException>(() => codec.EncodeOutbound(new object()));
    }
}
