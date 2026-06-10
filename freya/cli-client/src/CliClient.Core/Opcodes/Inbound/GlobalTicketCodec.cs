// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using System.Text;

namespace N7.CliClient.Opcodes.Inbound;

/// <summary>
/// 0x006F GLOBAL_TICKET — server's reply to 0x006E
/// GLOBAL_TICKET_REQUEST. Issued by the proxy after the global UDP
/// plane resolved (or failed to resolve) the avatar slot to a real
/// avatar_id. Carries the ticket the client will hand to the master
/// server to enter a sector.
/// </summary>
/// <remarks>
/// <para>
/// Wire layout matches <c>struct GlobalTicket</c> in
/// <c>common/include/net7/PacketStructures.h</c> after the Phase K
/// int32_t migration — 4 byte response_code + 64 byte embedded
/// MasterJoin = 68 bytes total.
/// </para>
/// <para>
/// The proxy's <c>Connection::SendGlobalTicket</c>
/// (<c>proxy/ClientToServer_linux_stubs.cpp:273</c>) writes the
/// payload by hand rather than serialising the struct, so the
/// canonical field map is the AddData/AddDataFlip4 sequence in that
/// function, not the C struct declaration. Decoded layout:
/// <code>
///   offset  size  field         encoding
///   0       4     response_code be32  (0 on success; level/error code on failure — 1000/1002)
///   4..19   16    (zero)
///   20      4     avatar_id     be32  (0x40000000 on failure)
///   24      4     sector_id     be32
///   28..31  4     (zero)
///   32      4     level         host  (AddData, no byte swap)
///   36..47  12    (zero)
///   48..63  16    ticket "MY_Avatar_Ticket\0..."
///   64..67  4     (zero, struct tail padding)
/// </code>
/// </para>
/// <para>
/// On the failure path (avatar_id=0x40000000, response_code=1002) the
/// server's HandleGlobalTicketRequest returns silently and the proxy
/// emits this packet from the WaitForResponse timeout fallback. On the
/// happy path the server emits a 0x2005 AVATARLOGIN_CONFIRM back to
/// the proxy and the proxy populates response_code=0 with the real
/// avatar_id/sector_id.
/// </para>
/// </remarks>
public sealed record GlobalTicket(
    int ResponseCode,
    int AvatarId,
    int SectorId,
    int Level,
    string TicketString);

/// <summary>
/// Codec for opcode 0x006F GLOBAL_TICKET. Server-to-client only.
/// </summary>
public sealed class GlobalTicketCodec : IOpcodeCodec
{
    /// <summary>
    /// Fixed wire size: <c>sizeof(GlobalTicket)</c> on Win32 after the
    /// Phase K int32_t migration. 4 byte response_code + 64 byte
    /// embedded MasterJoin = 68 bytes.
    /// </summary>
    public const int WireSize = 68;

    /// <summary>
    /// Failure response code used by the proxy when SendAvatarLogin
    /// returns -1 (server's GetAvatarID failed to resolve the slot).
    /// Match for <c>SendGlobalTicket(0x40000000, 0, 1002, false)</c> in
    /// <c>proxy/ClientToServer_linux_stubs.cpp</c>.
    /// </summary>
    public const int GalaxyFullResponseCode = 1002;

    /// <summary>
    /// Failure response code used by Win32 retail server when the
    /// avatar-selection task rejects the user (not authorised).
    /// </summary>
    public const int UserNotAuthorisedResponseCode = 1000;

    /// <summary>Sentinel avatar_id written by the proxy on the failure path.</summary>
    public const int FailureAvatarIdSentinel = 0x40000000;

    public OpcodeId Opcode => OpcodeId.Known.GlobalTicket;

    public object DecodeInbound(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < WireSize)
        {
            throw new InvalidDataException(
                $"GlobalTicket payload is {payload.Length} bytes, expected at least {WireSize}");
        }

        int responseCode = BinaryPrimitives.ReadInt32BigEndian(payload.Slice(0, 4));
        int avatarId = BinaryPrimitives.ReadInt32BigEndian(payload.Slice(20, 4));
        int sectorId = BinaryPrimitives.ReadInt32BigEndian(payload.Slice(24, 4));
        int level = BinaryPrimitives.ReadInt32LittleEndian(payload.Slice(32, 4));

        int nul = payload.Slice(48, 16).IndexOf((byte)0);
        if (nul < 0) nul = 16;
        string ticketString = Encoding.ASCII.GetString(payload.Slice(48, nul));

        return new GlobalTicket(responseCode, avatarId, sectorId, level, ticketString);
    }

    public byte[] EncodeOutbound(object message)
        => throw new NotSupportedException(
            "GlobalTicket is server-to-client only; the client never originates it");
}
