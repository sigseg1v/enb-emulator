// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace N7.CliClient.Opcodes.Inbound;

/// <summary>
/// 0x0070 GLOBAL_AVATAR_LIST — server's reply to 0x006D GlobalConnect.
/// Returns the player's five avatar slots plus the galaxy list the
/// avatar-select screen renders.
/// </summary>
/// <remarks>
/// Wire layout matches <c>struct GlobalAvatarList</c> in
/// <c>common/include/net7/PacketStructures.h</c> after the Phase K
/// int32_t migration. The codec assumes a fixed <c>num_galaxies = 1</c>
/// (matches what AccountManager::BuildAvatarList currently emits and
/// what the struct's 2-galaxy upper bound allows). When the day comes
/// to support multiple galaxies the struct layout is already a fixed-
/// size 2-element array — the codec just needs to honour
/// <see cref="NumGalaxies"/>.
/// </remarks>
public sealed record GlobalAvatarList(
    AvatarSlot[] Avatars,
    int NumGalaxies,
    Galaxy[] Galaxies);

/// <summary>One of the five fixed avatar slots in the global avatar list.</summary>
public sealed record AvatarSlot(AvatarInfo Info, AvatarData Data);

/// <summary>
/// The <c>info</c> half of an avatar slot — sector/galaxy/level
/// metadata. All numeric fields are big-endian on the wire
/// (server passes them through ntohl before storing). The codec
/// converts to host order so consumers can read them directly.
/// </summary>
public sealed record AvatarInfo(
    int AvatarSlot,
    int SectorId,
    int GalaxyId,
    int Count,
    int AvatarIdMsb,
    int AvatarIdLsb,
    int AccountIdMsb,
    int AccountIdLsb,
    int AdminLevel,
    int GmFlag,
    int CombatLevel,
    int ExploreLevel,
    int TradeLevel,
    string Location);

/// <summary>
/// The <c>data</c> half of an avatar slot — appearance + race/class.
/// We decode the fields the avatar-select UI actually needs and keep
/// the appearance blob as a raw payload (no consumer for it yet).
/// </summary>
public sealed record AvatarData(
    string FirstName,
    string LastName,
    int AvatarType,
    byte AvatarVersion,
    int Race,
    int Profession,
    int Gender,
    int MoodType,
    byte[] RawAppearance);

/// <summary>
/// One entry in the GlobalAvatarList's galaxy table.
/// </summary>
/// <remarks>
/// IP address is big-endian on the wire (server passes through
/// ntohl(inet_addr(...))) — codec converts to <see cref="IPAddress"/>.
/// Port is big-endian (server passes through ntohs(MASTER_SERVER_PORT)).
/// NumPlayers / MaxPlayers are HOST order (no ntohl wrapper at the
/// build site — see <c>server/src/AccountManager.cpp:BuildAvatarList</c>).
/// </remarks>
public sealed record Galaxy(
    string Name,
    int GalaxyId,
    IPAddress IpAddress,
    ushort Port,
    int NumPlayers,
    int MaxPlayers,
    short Unknown2);

/// <summary>
/// Codec for opcode 0x0070 GLOBAL_AVATAR_LIST.
/// </summary>
/// <remarks>
/// <para>
/// Server-to-client only. Total wire size:
/// <c>5 × 374 (AvatarListItem) + 4 (num_galaxies) + 2 × 84 (Galaxy) = 2042 bytes</c>.
/// </para>
/// <para>
/// Wire layout (per the migrated <c>PacketStructures.h</c>):
/// </para>
/// <code>
///   offset  size  field
///   0       1870  avatar[5]            — 5 × AvatarListItem (374B each)
///                                          info:  133B  AvatarInfo
///                                          data:  241B  AvatarData
///   1870    4     num_galaxies         — int32, host order
///   1874    168   galaxy[2]            — 2 × Galaxy (84B each)
///   total: 2042 bytes
/// </code>
/// </remarks>
public sealed class GlobalAvatarListCodec : IOpcodeCodec
{
    public const int AvatarInfoSize = 133;
    public const int AvatarDataSize = 241;
    public const int AvatarListItemSize = AvatarInfoSize + AvatarDataSize; // 374
    public const int AvatarCount = 5;
    public const int MaxGalaxies = 2;
    public const int GalaxySize = 84;
    public const int WireSize =
        AvatarCount * AvatarListItemSize + 4 + MaxGalaxies * GalaxySize; // 2042

    public OpcodeId Opcode => OpcodeId.Known.GlobalAvatarList;

    public object DecodeInbound(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < WireSize)
        {
            throw new InvalidDataException(
                $"GlobalAvatarList payload is {payload.Length} bytes, expected at least {WireSize}");
        }

        var avatars = new AvatarSlot[AvatarCount];
        int offset = 0;
        for (int i = 0; i < AvatarCount; i++)
        {
            var info = DecodeAvatarInfo(payload.Slice(offset, AvatarInfoSize));
            offset += AvatarInfoSize;
            var data = DecodeAvatarData(payload.Slice(offset, AvatarDataSize));
            offset += AvatarDataSize;
            avatars[i] = new AvatarSlot(info, data);
        }

        // num_galaxies is HOST order at the build site
        // (server/src/AccountManager.cpp:1256 does `ntohl(1)` which on
        // a little-endian host writes 0x01000000 — see field comment).
        // We read both encodings for forward-compat: prefer the
        // big-endian interpretation when it lands in [1, MaxGalaxies],
        // otherwise fall back to little-endian.
        int numGalaxiesBe = BinaryPrimitives.ReadInt32BigEndian(payload.Slice(offset, 4));
        int numGalaxiesLe = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(offset, 4));
        offset += 4;

        int numGalaxies = numGalaxiesBe is >= 1 and <= MaxGalaxies
            ? numGalaxiesBe
            : numGalaxiesLe;

        var galaxies = new Galaxy[Math.Min(numGalaxies, MaxGalaxies)];
        for (int i = 0; i < galaxies.Length; i++)
        {
            galaxies[i] = DecodeGalaxy(payload.Slice(offset, GalaxySize));
            offset += GalaxySize;
        }

        return new GlobalAvatarList(avatars, numGalaxies, galaxies);
    }

    public byte[] EncodeOutbound(object message)
        => throw new NotSupportedException(
            "GlobalAvatarList is server-to-client only; the client never originates it");

    private static AvatarInfo DecodeAvatarInfo(ReadOnlySpan<byte> info)
    {
        // All 13 ints are big-endian on the wire (server's ReadDatabase
        // stores them via ntohl); decode back to host order for the
        // caller's convenience. No helper closure — ReadOnlySpan<byte>
        // is a ref struct and can't be captured by a local function.
        return new AvatarInfo(
            AvatarSlot:   BinaryPrimitives.ReadInt32BigEndian(info.Slice( 0, 4)),
            SectorId:     BinaryPrimitives.ReadInt32BigEndian(info.Slice( 4, 4)),
            GalaxyId:     BinaryPrimitives.ReadInt32BigEndian(info.Slice( 8, 4)),
            Count:        BinaryPrimitives.ReadInt32BigEndian(info.Slice(12, 4)),
            AvatarIdMsb:  BinaryPrimitives.ReadInt32BigEndian(info.Slice(16, 4)),
            AvatarIdLsb:  BinaryPrimitives.ReadInt32BigEndian(info.Slice(20, 4)),
            AccountIdMsb: BinaryPrimitives.ReadInt32BigEndian(info.Slice(24, 4)),
            AccountIdLsb: BinaryPrimitives.ReadInt32BigEndian(info.Slice(28, 4)),
            AdminLevel:   BinaryPrimitives.ReadInt32BigEndian(info.Slice(32, 4)),
            GmFlag:       BinaryPrimitives.ReadInt32BigEndian(info.Slice(36, 4)),
            CombatLevel:  BinaryPrimitives.ReadInt32BigEndian(info.Slice(40, 4)),
            ExploreLevel: BinaryPrimitives.ReadInt32BigEndian(info.Slice(44, 4)),
            TradeLevel:   BinaryPrimitives.ReadInt32BigEndian(info.Slice(48, 4)),
            Location:     ReadCString(info.Slice(52, 81)));
    }

    private static AvatarData DecodeAvatarData(ReadOnlySpan<byte> data)
    {
        // AvatarData wire layout (post-int32_t migration, 241 bytes):
        //   0..19   first_name[20]   (NUL-padded ASCII)
        //   20..39  last_name[20]    (NUL-padded ASCII)
        //   40..43  avatar_type      int32 LE (host order — sourced from
        //                            DB without ntohl wrapper)
        //   44      filler1
        //   45      avatar_version   byte
        //   46..47  (padding)
        //   48..51  race
        //   52..55  profession
        //   56..59  gender
        //   60..63  mood_type
        //   64..240 appearance blob (chars + floats + 4 metals)
        string firstName = ReadCString(data.Slice(0, 20));
        string lastName = ReadCString(data.Slice(20, 20));
        int avatarType = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(40, 4));
        byte avatarVersion = data[45];
        int race = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(48, 4));
        int profession = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(52, 4));
        int gender = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(56, 4));
        int moodType = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(60, 4));

        return new AvatarData(
            FirstName:     firstName,
            LastName:      lastName,
            AvatarType:    avatarType,
            AvatarVersion: avatarVersion,
            Race:          race,
            Profession:    profession,
            Gender:        gender,
            MoodType:      moodType,
            RawAppearance: data.Slice(64).ToArray());
    }

    private static Galaxy DecodeGalaxy(ReadOnlySpan<byte> g)
    {
        // Galaxy wire layout (post-int32_t migration, 84 bytes):
        //   0..63   Name[64]         (NUL-padded ASCII)
        //   64..67  GalaxyID         int32, host order (no ntohl)
        //   68..71  IP_Address       int32, network order (ntohl-wrapped at build)
        //   72..73  port             uint16, network order (ntohs-wrapped at build)
        //   74..77  NumPlayers       int32, host order
        //   78..81  MaxPlayers       int32, host order
        //   82..83  unknown2         int16, host order
        string name = ReadCString(g.Slice(0, 64));
        int galaxyId = BinaryPrimitives.ReadInt32LittleEndian(g.Slice(64, 4));

        // IP_Address: server does `ntohl(inet_addr(LOCAL_IP))`. inet_addr
        // returns a uint32_t in network order; ntohl on LE-host swaps it
        // back to host order, so what we see on the wire is the in-host
        // representation of the four address bytes — which is the SAME
        // bytes inet_addr stored in memory pre-swap. To recover the
        // dotted-quad we just write the int back out big-endian.
        int ipNetOrderRaw = BinaryPrimitives.ReadInt32LittleEndian(g.Slice(68, 4));
        Span<byte> ipBytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(ipBytes, ipNetOrderRaw);
        var ipAddress = new IPAddress(ipBytes.ToArray());

        // Port: server does `ntohs(MASTER_SERVER_PORT)` — same logic as
        // IP_Address. Read as little-endian to undo the ntohs swap.
        ushort port = BinaryPrimitives.ReadUInt16LittleEndian(g.Slice(72, 2));

        int numPlayers = BinaryPrimitives.ReadInt32LittleEndian(g.Slice(74, 4));
        int maxPlayers = BinaryPrimitives.ReadInt32LittleEndian(g.Slice(78, 4));
        short unknown2 = BinaryPrimitives.ReadInt16LittleEndian(g.Slice(82, 2));

        return new Galaxy(
            Name:        name,
            GalaxyId:    galaxyId,
            IpAddress:   ipAddress,
            Port:        port,
            NumPlayers:  numPlayers,
            MaxPlayers:  maxPlayers,
            Unknown2:    unknown2);
    }

    private static string ReadCString(ReadOnlySpan<byte> bytes)
    {
        int nul = bytes.IndexOf((byte)0);
        if (nul < 0) nul = bytes.Length;
        return Encoding.ASCII.GetString(bytes.Slice(0, nul));
    }
}
