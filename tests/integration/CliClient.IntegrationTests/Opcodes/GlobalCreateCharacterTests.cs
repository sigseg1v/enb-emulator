// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using System.Text;
using N7.CliClient.Auth;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Inbound;
using Xunit;

namespace N7.CliClient.IntegrationTests.Opcodes;

/// <summary>
/// 0x0072 GlobalCreateCharacter → 0x0070 GlobalAvatarList round-trip
/// against the live proxy on the global server port (3805). This is the
/// avatar-creation path the client invokes from the character-select
/// screen's "create new" flow.
///
/// <para>
/// Wire layout (verbatim 539-byte canonical Win32 GlobalCreateCharacter
/// struct — proxy and server both <c>(GlobalCreateCharacter *) msg</c>
/// the buffer directly with no reframing):
/// <code>
///   offset  size  field                  encoding
///   0       4     galaxy_id              int32 BE (server reads via ntohl)
///   4       4     character_slot         int32 BE (server reads via htonl
///                                        to swap back to host)
///   8       4     tutorial_status        int32 host order (unused server-side)
///   12      65    account_username[65]   NUL-padded ASCII; drives
///                                        GetAvatarID lookup
///   77      241   avatar (AvatarData)    packed; race/prof/gender/mood
///                                        at 46/50/54/58 within the
///                                        sub-struct (LE int32, host order)
///   318     194   ship_data (ShipData)   packed; race/prof/hull/wing/decal
///                                        at 0/4/8/12/16 (LE int32),
///                                        ship_name[26] at offset 20
///   512     27    unknown[27]            zero
/// </code>
/// </para>
///
/// <para>
/// This test is the failure detector for the Phase K ColorInfo wire-size
/// fix. Before that commit ColorInfo carried <c>long metal</c>, which on
/// Linux x86_64 sized the struct at 21B instead of the canonical 17B.
/// ColorInfo is embedded 8× in ShipData and ShipData is embedded in
/// GlobalCreateCharacter, so a 4-byte-per-ColorInfo bloat ballooned
/// ShipData to 226B and GlobalCreateCharacter to 571B — meaning the
/// Linux proxy would have stamped a 571-byte payload into the UDP_GLOBAL
/// frame and the server's <c>(GlobalCreateCharacter *) msg</c> cast
/// would have misaligned every field after the first ColorInfo. Today
/// the proxy and server both build against
/// <c>sizeof(GlobalCreateCharacter) == 539</c>, so any regression of
/// ColorInfo's <c>metal</c> field back to <c>long</c> would fail
/// CreateCharacter at sizeof-check time on the proxy side (the
/// proxy/server can no longer agree on the wire layout).
/// </para>
///
/// <para>
/// The test also exercises the Phase K <c>character_slot</c> byte-order
/// convention: the field is BE on the wire (matches AvatarInfo's
/// "All fields are in Big Endian format" comment for the avatar-list
/// equivalent), and <c>AccountManager::CreateCharacter</c> calls
/// <c>GetAvatarID(account_username, htonl(create-&gt;character_slot))</c>
/// to swap back to host order — confirming the BE convention.
/// </para>
///
/// <para>
/// Cleanup: the test deletes the created character via 0x0071 after
/// asserting it appears in the avatar list. Two reasons:
/// </para>
/// <list type="number">
///   <item>
///     <c>IsUsernameUnique</c> is a global SQL check across <i>all</i>
///     accounts, so leaking "Testavus" into the DB would make a
///     subsequent CreateCharacter in the same compose lifetime fail
///     with G_ERROR_NICKNAME_USED.
///   </item>
///   <item>
///     Documents the round-trip on both sides — the create succeeded
///     and the delete also still works against a real-populated slot
///     (the existing 0x0071 test only covers the empty-slot no-op path).
///   </item>
/// </list>
/// </summary>
[Collection(ServerCollection.Name)]
public sealed class GlobalCreateCharacterTests
{
    private readonly ServerFixture _server;
    private readonly ClientFixture _client;

    public GlobalCreateCharacterTests(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    [Fact]
    public async Task ValidAvatar_AppearsInRefreshedAvatarList_AndCleanlyDeletes()
    {
        var account = TestAccounts.New(_server);

        const string CharacterFirstName = "Testavus";  // 8 chars, has 'e','a','u' vowels, no 3-repeating
        const string ShipName = "TestShip";

        // 60s budget: TLS login + RC4+RSA handshake + GlobalConnect
        // round-trip + Create (proxy↔server UDP) + drain + Delete + drain.
        // Multiple UDP round-trips so we give it more than the
        // delete-only test's 40s.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var login = await _client.AuthLogin.LoginAsync(
            new AuthLoginRequest(account.Username, account.Password),
            cts.Token);
        Assert.True(login.Valid, $"login: {login.RawBody.TrimEnd()}");
        Assert.False(string.IsNullOrEmpty(login.Ticket));

        await using var conn = await EncryptedTcpConnection.ConnectAsync(
            _server.GlobalHost, _server.GlobalPort, cts.Token);

        // ---- Step 1: GlobalConnect (drives the proxy into ticket-consumed state) ----
        byte[] ticketBytes = Encoding.ASCII.GetBytes(login.Ticket!);
        byte[] connectPayload = new byte[4 + ticketBytes.Length + 1];
        BinaryPrimitives.WriteUInt32BigEndian(connectPayload.AsSpan(0, 4), (uint)ticketBytes.Length);
        ticketBytes.CopyTo(connectPayload, 4);
        connectPayload[^1] = 0;

        await conn.SendAsync(
            Packet.ForOpcode(OpcodeId.Known.GlobalConnect.Value, connectPayload),
            cts.Token);

        // Drain to the initial (empty) GlobalAvatarList. Establishes the
        // baseline — five empty slots, exactly one galaxy.
        var initial = await DrainUntilOpcode(conn, OpcodeId.Known.GlobalAvatarList.Value, cts.Token);
        var initialDecoded = (GlobalAvatarList)new GlobalAvatarListCodec()
            .DecodeInbound(initial.Payload.Span);
        Assert.All(initialDecoded.Avatars, slot => Assert.Equal(string.Empty, slot.Data.FirstName));

        // ---- Step 2: GlobalCreateCharacter (slot 0) ----
        byte[] createPayload = BuildCreateCharacterPayload(
            galaxyId:            1,
            characterSlot:       0,
            accountUsername:     account.Username,
            firstName:           CharacterFirstName,
            race:                0,  // Terran
            profession:          0,  // Warrior
            gender:              0,
            shipName:            ShipName);

        // Sanity-check the local size matches canonical Win32 wire size
        // — if the C# builder ever drifts to 571 (the pre-ColorInfo-fix
        // size) or anything else, the server's struct cast misaligns
        // silently. Fail loudly here instead.
        Assert.Equal(539, createPayload.Length);

        await conn.SendAsync(
            Packet.ForOpcode(OpcodeId.Known.GlobalCreateCharacter.Value, createPayload),
            cts.Token);

        // ---- Step 3: drain to the GlobalAvatarList showing the new character ----
        var afterCreate = await DrainUntilOpcode(conn, OpcodeId.Known.GlobalAvatarList.Value, cts.Token);
        Assert.Equal(GlobalAvatarListCodec.WireSize, afterCreate.Payload.Length);

        var afterCreateDecoded = (GlobalAvatarList)new GlobalAvatarListCodec()
            .DecodeInbound(afterCreate.Payload.Span);

        // Slot 0 should now carry our character. Race/profession verify
        // the inner AvatarData round-tripped correctly — these read at
        // the post-fix codec offsets (46/50) which match the packed
        // struct's actual layout.
        var slot0 = afterCreateDecoded.Avatars[0];
        Assert.Equal(CharacterFirstName, slot0.Data.FirstName);
        Assert.Equal(0, slot0.Data.Race);
        Assert.Equal(0, slot0.Data.Profession);
        // sector_id is BE on the wire. StartSector[0*3+0] = 10151 (Terran
        // Warrior, Luna). Server's BuildAvatarList runs ntohl on it before
        // packing, so the codec's BE read recovers 10151.
        Assert.Equal(10151, slot0.Info.SectorId);
        // account_id_lsb encodes the seeded account ID (9_000_003). Server
        // stores via ntohl, codec reads BE.
        Assert.Equal(account.Id, slot0.Info.AccountIdLsb);

        // Slots 1-4 should still be empty.
        for (int i = 1; i < 5; i++)
        {
            Assert.Equal(string.Empty, afterCreateDecoded.Avatars[i].Data.FirstName);
        }

        // ---- Step 4: DELETE slot 0 to clean up ----
        byte[] deletePayload = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(deletePayload, 0);

        await conn.SendAsync(
            Packet.ForOpcode(OpcodeId.Known.GlobalDeleteCharacter.Value, deletePayload),
            cts.Token);

        var afterDelete = await DrainUntilOpcode(conn, OpcodeId.Known.GlobalAvatarList.Value, cts.Token);
        var afterDeleteDecoded = (GlobalAvatarList)new GlobalAvatarListCodec()
            .DecodeInbound(afterDelete.Payload.Span);

        // All slots back to empty. The character was created and torn
        // down via the live wire — no leftover state for the next test.
        Assert.All(afterDeleteDecoded.Avatars,
            slot => Assert.Equal(string.Empty, slot.Data.FirstName));
    }

    /// <summary>
    /// Build the 539-byte GlobalCreateCharacter wire payload. Field
    /// offsets and endianness verified against the live packed
    /// <c>common/include/net7/PacketStructures.h</c> struct on x86_64
    /// via <c>offsetof()</c>; see the class-level docstring for the
    /// full layout table.
    /// </summary>
    private static byte[] BuildCreateCharacterPayload(
        int galaxyId,
        int characterSlot,
        string accountUsername,
        string firstName,
        int race,
        int profession,
        int gender,
        string shipName)
    {
        byte[] payload = new byte[539];

        // Top-level header
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(0, 4), galaxyId);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(4, 4), characterSlot);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(8, 4), 0);  // tutorial_status

        // account_username[65] — NUL-padded ASCII. AccountManager's
        // GetAccountID does an exact lookup on this, so it must match
        // the seeded account name byte-for-byte.
        var usernameBytes = Encoding.ASCII.GetBytes(accountUsername);
        if (usernameBytes.Length >= 65)
            throw new ArgumentException(
                $"account_username '{accountUsername}' is {usernameBytes.Length}B but the " +
                "wire field is 65B (must NUL-terminate)");
        usernameBytes.CopyTo(payload.AsSpan(12, 65));

        // AvatarData (offset 77, size 241). Offsets relative to AvatarData:
        //   0..19   first_name[20]
        //   20..39  last_name[20]
        //   40..43  avatar_type
        //   44      filler1
        //   45      avatar_version
        //   46..49  race
        //   50..53  profession
        //   54..57  gender
        //   58..61  mood_type
        //   62..240 personality + appearance blob (zero-filled — the
        //           server stores it verbatim; the create-character UI
        //           normally fills it but the server doesn't validate
        //           the appearance fields so zeros are accepted).
        const int AvatarOffset = 77;
        var firstNameBytes = Encoding.ASCII.GetBytes(firstName);
        if (firstNameBytes.Length >= 20)
            throw new ArgumentException(
                $"first_name '{firstName}' is {firstNameBytes.Length}B but the wire field is 20B");
        firstNameBytes.CopyTo(payload.AsSpan(AvatarOffset + 0, 20));
        // last_name left as zero (server doesn't validate)
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(AvatarOffset + 40, 4), 0);  // avatar_type
        payload[AvatarOffset + 44] = 0;  // filler1
        payload[AvatarOffset + 45] = 0;  // avatar_version
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(AvatarOffset + 46, 4), race);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(AvatarOffset + 50, 4), profession);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(AvatarOffset + 54, 4), gender);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(AvatarOffset + 58, 4), 0);  // mood_type

        // ShipData (offset 318, size 194). Offsets relative to ShipData:
        //   0   race          int32 LE
        //   4   profession    int32 LE
        //   8   hull          int32 LE
        //   12  wing          int32 LE
        //   16  decal         int32 LE
        //   20  ship_name[26] ASCII (forbidden-words check is on the
        //                     non-NUL bytes — "TestShip" is in neither
        //                     RestrictedWords nor RestrictedShips)
        //   46  ship_name_color[3] floats (unused server-side, zero-filled)
        //   58  8 × ColorInfo (17B each), zero-filled
        const int ShipOffset = 318;
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(ShipOffset +  0, 4), race);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(ShipOffset +  4, 4), profession);
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(ShipOffset +  8, 4), 0);  // hull (BaseHullAsset[race*3+hull] must be in-range)
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(ShipOffset + 12, 4), 0);  // wing
        BinaryPrimitives.WriteInt32LittleEndian(payload.AsSpan(ShipOffset + 16, 4), 0);  // decal
        var shipNameBytes = Encoding.ASCII.GetBytes(shipName);
        if (shipNameBytes.Length >= 26)
            throw new ArgumentException(
                $"ship_name '{shipName}' is {shipNameBytes.Length}B but the wire field is 26B");
        shipNameBytes.CopyTo(payload.AsSpan(ShipOffset + 20, 26));
        // ship_name_color + 8 ColorInfos left zero.

        // unknown[27] at 512 left zero.
        return payload;
    }

    /// <summary>
    /// Drain the global channel until we see <paramref name="targetOpcode"/>.
    /// Surfaces 0x0075 GLOBAL_ERROR loudly so a server-side rejection
    /// (e.g. G_ERROR_NICKNAME_USED, G_ERROR_RESTRICTED_LIST) fails the
    /// test with a meaningful code rather than timing out silently.
    /// </summary>
    private static async Task<Packet> DrainUntilOpcode(
        EncryptedTcpConnection conn,
        ushort targetOpcode,
        CancellationToken ct)
    {
        while (true)
        {
            var p = await conn.ReceiveAsync(ct);
            Assert.NotNull(p);

            if (p!.Header.Opcode == targetOpcode)
                return p;

            if (p.Header.Opcode == OpcodeId.Known.GlobalError.Value)
            {
                // Wire layout (proxy ClientToServer_linux_stubs.cpp::GlobalError):
                //   [u32 msg_len][be32 (G_ERROR_code + 7)][char msg[msg_len]]
                // Subtract 7 to recover the original G_ERROR_* code.
                var span = p.Payload.Span;
                int errCode = -1;
                if (span.Length >= 8)
                    errCode = BinaryPrimitives.ReadInt32BigEndian(span.Slice(4, 4)) - 7;
                throw new Xunit.Sdk.XunitException(
                    $"server returned GlobalError code={errCode}; expected opcode 0x{targetOpcode:X4}");
            }
        }
    }
}
