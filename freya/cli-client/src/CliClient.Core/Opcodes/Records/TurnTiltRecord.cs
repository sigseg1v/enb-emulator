// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0012 TURN (yaw) / 0x0013 TILT (pitch). Identical 8-byte wire layout, LE,
/// no byte-swap:
///   int32 GameID; float Intensity.
/// Client->server steering: Intensity is the signed turn/tilt rate (retail frames
/// carry exactly -1.0 / +1.0 at full deflection). The server reads Turning->
/// Intensity raw host-order in Player::HandleTurn / Player::HandleTilt (PacketTurn
/// {int32 GameID; float Intensity}, no ntohl), so the wire is LE.
/// </summary>
public sealed class TurnTiltRecord : PacketRecord
{
    private readonly ushort _opcode;

    public TurnTiltRecord(ReadOnlySpan<byte> payload, ushort opcode) : base(opcode, payload)
    {
        _opcode = opcode;
    }

    protected override void WriteFields(StringBuilder sb)
    {
        bool tilt = _opcode == 0x0013;
        string label = tilt ? "TILT (pitch)" : "TURN (yaw)";
        if (Payload.Length < 8) { Flag(sb, $"{label} truncated -- {Payload.Length} bytes, expected 8"); return; }
        FHex(sb, 0, "GameID", ReadI32LE(Payload, 0));
        // The opcode is the ONLY thing that says which axis this rate steers --
        // the 8-byte struct is byte-identical for 0x12/0x13, so annotate it.
        FFloat(sb, 4, "Intensity", ReadF32LE(Payload, 4), tilt ? "(TILT -- pitch rate)" : "(TURN -- yaw rate)");
    }
}
