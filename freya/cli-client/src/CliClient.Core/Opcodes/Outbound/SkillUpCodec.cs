// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;

namespace N7.CliClient.Opcodes.Outbound;

/// <summary>
/// 0x0057 SKILL_UP / SkillAction (client -&gt; server). The avatar spends a skill
/// point to raise one skill by a level (the "train" button in the skill UI).
/// The server-side struct is <c>struct SkillAction</c>
/// (common/include/net7/PacketStructures.h:997), declared 10 bytes packed:
/// <code>
///   int32_t GameID;       // requesting avatar id
///   int     SkillPoints;  // client-supplied; the server IGNORES it
///   short   SkillID;      // which skill to raise
/// </code>
/// but the retail client sends a <b>12-byte</b> frame -- it serializes
/// <c>SkillID</c> as a 4-byte int (<c>37 00 00 00</c> in the capture), so the
/// server's <c>short SkillID</c> read of the low 2 bytes yields the right value
/// on little-endian and the high 2 bytes are ignored. We emit the full 12-byte
/// retail shape.
/// </summary>
/// <remarks>
/// <para>
/// <b>Byte order.</b> The handler Player::HandleSkillAction
/// (server/src/PlayerSkills.cpp:97) casts the struct directly with NO
/// <c>ntohl</c> -- every field is LITTLE-endian on the wire, like 0x005D /
/// 0x004E and unlike 0x0027 INVENTORY_MOVE.
/// </para>
/// <para>
/// <b>SkillPoints is server-ignored.</b> HandleSkillAction recomputes the
/// available points from <c>m_PlayerIndex.RPGInfo.GetSkillPoints()</c> and never
/// reads <c>Action-&gt;SkillPoints</c>; the cost comes from the skill's
/// availability table. The captured value is 1; we default to it to match the
/// retail client wire shape, but the field has no server effect.
/// </para>
/// <para>
/// Byte-pinned against the live reference server
/// SkillTrainingHostileDevice2 capture dg #18:
/// <c>2A 99 03 40  01 00 00 00  37 00 00 00</c> = GameID 0x4003992A LE,
/// SkillPoints 1 LE, SkillID 55 LE.
/// </para>
/// </remarks>
public sealed record SkillUpMessage(int GameId, int SkillPoints, int SkillId)
{
    /// <summary>The skill-points value the retail client sends. Server-ignored;
    /// captured value is 1.</summary>
    public const int DefaultSkillPoints = 1;

    /// <summary>Raise <paramref name="skillId"/> with the captured default
    /// skill-points value (<see cref="DefaultSkillPoints"/>).</summary>
    public SkillUpMessage(int gameId, int skillId) : this(gameId, DefaultSkillPoints, skillId) { }
}

/// <summary>Codec for opcode 0x0057 SKILL_UP (client -&gt; server).</summary>
public sealed class SkillUpCodec : IOpcodeCodec
{
    /// <summary>Fixed wire size of the retail SkillAction frame (SkillID widened
    /// to a 4-byte int on the wire).</summary>
    public const int Size = 12;

    public OpcodeId Opcode => OpcodeId.Known.SkillUp;

    public object DecodeInbound(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < Size)
        {
            throw new InvalidDataException(
                $"SkillUp payload is {payload.Length} bytes, expected {Size}");
        }

        return new SkillUpMessage(
            GameId:      BinaryPrimitives.ReadInt32LittleEndian(payload[0..4]),
            SkillPoints: BinaryPrimitives.ReadInt32LittleEndian(payload[4..8]),
            SkillId:     BinaryPrimitives.ReadInt32LittleEndian(payload[8..12]));
    }

    public byte[] EncodeOutbound(object message)
    {
        if (message is not SkillUpMessage up)
            throw new ArgumentException(
                $"expected SkillUpMessage, got {message?.GetType().Name ?? "null"}",
                nameof(message));

        byte[] buf = new byte[Size];
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(0, 4), up.GameId);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(4, 4), up.SkillPoints);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(8, 4), up.SkillId);
        return buf;
    }
}
