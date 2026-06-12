// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Buffers.Binary;

namespace N7.CliClient.Opcodes.Outbound;

/// <summary>
/// 0x004E STARBASE_REQUEST (client -&gt; server). The avatar interacts with a
/// starbase fixture: exiting the station, opening an NPC talk tree, the vendor
/// terminal, or a job terminal. Wire layout is <c>struct StarbaseRequest</c>
/// (common/include/net7/PacketStructures.h:812), 9 bytes:
/// <code>
///   int32_t PlayerID;     // requesting avatar id
///   int32_t StarbaseID;   // target fixture/NPC id (0 for actions that need none)
///   char    Action;       // what the avatar is doing
/// </code>
/// The server's Player::HandleStarbaseRequest
/// (server/src/PlayerConnection.cpp:9861) casts the struct directly with NO
/// <c>ntohl</c> -- it reads <c>pkt-&gt;PlayerID</c>, <c>pkt-&gt;StarbaseID</c>,
/// <c>pkt-&gt;Action</c> as host-order ints. So both ints are LITTLE-endian on
/// the wire, like 0x005D EQUIP_USE and unlike 0x0027 INVENTORY_MOVE.
/// </summary>
/// <remarks>
/// <para>
/// <b>Action values</b> (from the handler's <c>switch(pkt-&gt;Action)</c>):
/// 1 = exit the station (LaunchIntoSpace); 4 = talk to NPC (open talk tree /
/// missions, latches <c>m_StarbaseTargetID</c>); 6 = open a job terminal;
/// 7 = pull up a job description.
/// </para>
/// <para>
/// <b>PlayerID is the masked avatar id, not the session GameID.</b> In the
/// VendorInvEco capture the outer-header session is 0x4003992A but the
/// StarbaseRequest.PlayerID field is 0x0003992A -- the top type nibble
/// (0x40000000) is cleared. Callers pass the value they want on the wire
/// explicitly; this codec does not derive it.
/// </para>
/// <para>
/// Byte-pinned against the live reference server VendorInvEco
/// capture dg #4: <c>2A 99 03 00  C4 01 00 00  04</c> = PlayerID 0x0003992A LE,
/// StarbaseID 452 LE, Action 4 (talk to NPC).
/// </para>
/// </remarks>
public sealed record StarbaseRequestMessage(int PlayerId, int StarbaseId, byte Action)
{
    /// <summary>Exit the station into space.</summary>
    public const byte ActionExitStation = 1;
    /// <summary>Talk to the NPC identified by <see cref="StarbaseId"/>.</summary>
    public const byte ActionTalkToNpc = 4;
    /// <summary>Open the job terminal identified by <see cref="StarbaseId"/>.</summary>
    public const byte ActionJobTerminal = 6;
    /// <summary>Pull up a job description on the job terminal.</summary>
    public const byte ActionJobDescription = 7;
}

/// <summary>Codec for opcode 0x004E STARBASE_REQUEST (client -&gt; server).</summary>
public sealed class StarbaseRequestCodec : IOpcodeCodec
{
    /// <summary>Fixed wire size of struct StarbaseRequest.</summary>
    public const int Size = 9;

    public OpcodeId Opcode => OpcodeId.Known.StarbaseRequest;

    public object DecodeInbound(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < Size)
        {
            throw new InvalidDataException(
                $"StarbaseRequest payload is {payload.Length} bytes, expected {Size}");
        }

        return new StarbaseRequestMessage(
            PlayerId:   BinaryPrimitives.ReadInt32LittleEndian(payload[0..4]),
            StarbaseId: BinaryPrimitives.ReadInt32LittleEndian(payload[4..8]),
            Action:     payload[8]);
    }

    public byte[] EncodeOutbound(object message)
    {
        if (message is not StarbaseRequestMessage req)
            throw new ArgumentException(
                $"expected StarbaseRequestMessage, got {message?.GetType().Name ?? "null"}",
                nameof(message));

        byte[] buf = new byte[Size];
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(0, 4), req.PlayerId);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(4, 4), req.StarbaseId);
        buf[8] = req.Action;
        return buf;
    }
}
