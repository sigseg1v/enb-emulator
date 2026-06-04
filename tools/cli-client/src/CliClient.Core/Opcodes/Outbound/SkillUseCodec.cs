// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;

namespace N7.CliClient.Opcodes.Outbound;

/// <summary>
/// 0x0058 SKILL_ABILITY / SkillUse (client -&gt; server). The avatar activates a
/// trained ability or device-skill -- a buff on self, or an offensive skill on
/// the current target (the "use device on mob" action). Wire layout is
/// <c>struct SkillUse</c> (common/include/net7/PacketStructures.h:1004),
/// 12 bytes:
/// <code>
///   int32_t GameID;        // requesting avatar id
///   int32_t Action;        // client-supplied; the server IGNORES it
///   int32_t AbilityIndex;  // which ability slot to fire
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// <b>The target is NOT in this packet.</b> Player::HandleSkillAbility
/// (server/src/PlayerAbilitys.cpp:23) resolves the target from the avatar's
/// current lock -- <c>int TargetID = ShipIndex()-&gt;GetTargetGameID()</c> --
/// then calls <c>m_AbilityList[AbilityIndex]-&gt;Use(TargetID)</c>. So
/// "use device on mob" is two steps: lock the mob with the targeting opcode
/// first, then send this 0x0058 with the ability index. This codec carries only
/// the ability selection.
/// </para>
/// <para>
/// <b>Byte order.</b> The handler casts the struct directly with NO
/// <c>ntohl</c> -- every field is LITTLE-endian on the wire, like 0x005D /
/// 0x004E / 0x0057 and unlike 0x0027 INVENTORY_MOVE.
/// </para>
/// <para>
/// <b>Action is server-ignored.</b> HandleSkillAbility reads only GameID
/// (for the cast) and AbilityIndex; the Action field is never consumed. The
/// captured value is 0; we default to it to match the retail client wire shape.
/// </para>
/// <para>
/// Byte-pinned against the live reference server (216.219.87.147)
/// SkillTrainingHostileDevice2 capture dg #28:
/// <c>2A 99 03 40  00 00 00 00  2E 00 00 00</c> = GameID 0x4003992A LE,
/// Action 0 LE, AbilityIndex 46 LE.
/// </para>
/// </remarks>
public sealed record SkillUseMessage(int GameId, int Action, int AbilityIndex)
{
    /// <summary>The Action value the retail client sends. Server-ignored;
    /// captured value is 0.</summary>
    public const int DefaultAction = 0;

    /// <summary>Fire <paramref name="abilityIndex"/> with the captured default
    /// Action value (<see cref="DefaultAction"/>).</summary>
    public SkillUseMessage(int gameId, int abilityIndex) : this(gameId, DefaultAction, abilityIndex) { }
}

/// <summary>Codec for opcode 0x0058 SKILL_ABILITY (client -&gt; server).</summary>
public sealed class SkillUseCodec : IOpcodeCodec
{
    /// <summary>Fixed wire size of struct SkillUse.</summary>
    public const int Size = 12;

    public OpcodeId Opcode => OpcodeId.Known.SkillAbility;

    public object DecodeInbound(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < Size)
        {
            throw new InvalidDataException(
                $"SkillUse payload is {payload.Length} bytes, expected {Size}");
        }

        return new SkillUseMessage(
            GameId:       BinaryPrimitives.ReadInt32LittleEndian(payload[0..4]),
            Action:       BinaryPrimitives.ReadInt32LittleEndian(payload[4..8]),
            AbilityIndex: BinaryPrimitives.ReadInt32LittleEndian(payload[8..12]));
    }

    public byte[] EncodeOutbound(object message)
    {
        if (message is not SkillUseMessage use)
            throw new ArgumentException(
                $"expected SkillUseMessage, got {message?.GetType().Name ?? "null"}",
                nameof(message));

        byte[] buf = new byte[Size];
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(0, 4), use.GameId);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(4, 4), use.Action);
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(8, 4), use.AbilityIndex);
        return buf;
    }
}
