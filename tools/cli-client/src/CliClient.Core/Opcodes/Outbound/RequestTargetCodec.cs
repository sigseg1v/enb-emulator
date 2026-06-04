// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;

namespace N7.CliClient.Opcodes.Outbound;

/// <summary>
/// 0x0017 REQUEST_TARGET (client -&gt; server). The avatar selects an object as
/// its target -- the precondition for offensive abilities, which read the
/// current lock rather than a target field in their own packet. Wire layout is
/// <c>struct RequestTarget</c> (common/include/net7/PacketStructures.h:528),
/// 8 bytes:
/// <code>
///   int32_t GameID;     // requesting avatar id
///   int32_t TargetID;   // object to target, or -1 to clear
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// <b>Byte order.</b> Player::HandleRequestTarget
/// (server/src/PlayerConnection.cpp:3390) reads <c>request-&gt;TargetID</c> raw
/// (GetObjectFromID, no <c>ntohl</c>) and stores it via
/// <c>ShipIndex()-&gt;SetTargetGameID(request-&gt;TargetID)</c>. Every field is
/// LITTLE-endian on the wire.
/// </para>
/// <para>
/// This is the targeting half of "use a device on a mob": send REQUEST_TARGET
/// with the mob's game id to set the lock, then send 0x0058 SKILL_ABILITY (see
/// <see cref="SkillUseCodec"/>) -- the ability handler resolves the target from
/// <c>ShipIndex()-&gt;GetTargetGameID()</c>, not from its own payload.
/// </para>
/// <para>
/// Byte-pinned against the live reference server (216.219.87.147)
/// SkillTrainingHostileDevice2 capture dg #35:
/// <c>2A 99 03 40  ED 87 01 00</c> = GameID 0x4003992A LE, TargetID 0x000187ED
/// (100333) LE. The inbound half is
/// <see cref="N7.CliClient.Opcodes.Records.RequestTargetRecord"/>.
/// </para>
/// </remarks>
public sealed record RequestTargetMessage(int GameId, int TargetId)
{
    /// <summary>The TargetID value that clears the current lock.</summary>
    public const int NoTarget = -1;
}

/// <summary>Codec for opcode 0x0017 REQUEST_TARGET (client -&gt; server).</summary>
public sealed class RequestTargetCodec : IOpcodeCodec
{
    /// <summary>Fixed wire size of struct RequestTarget.</summary>
    public const int Size = 8;

    public OpcodeId Opcode => OpcodeId.Known.RequestTarget;

    public object DecodeInbound(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < Size)
        {
            throw new InvalidDataException(
                $"RequestTarget payload is {payload.Length} bytes, expected {Size}");
        }

        return new RequestTargetMessage(
            GameId:   BinaryPrimitives.ReadInt32LittleEndian(payload[0..4]),
            TargetId: BinaryPrimitives.ReadInt32LittleEndian(payload[4..8]));
    }

    public byte[] EncodeOutbound(object message)
    {
        if (message is not RequestTargetMessage req)
            throw new ArgumentException(
                $"expected RequestTargetMessage, got {message?.GetType().Name ?? "null"}",
                nameof(message));

        byte[] buf = new byte[Size];
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(0, 4), req.GameId);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(4, 4), req.TargetId);
        return buf;
    }
}
