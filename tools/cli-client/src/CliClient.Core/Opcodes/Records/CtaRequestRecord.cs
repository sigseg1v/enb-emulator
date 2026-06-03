// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// CTA_REQUEST (0x00BC, client-&gt;server) -- a "call to action" group request: one
/// group member proposes a group action (the Action selector) targeting another
/// object. Wire (struct CTARequest, 12 bytes): int32 SourceID, int32 TargetID,
/// int32 Action. ALL LITTLE-ENDIAN. Player::HandleCTARequest
/// (PlayerConnection.cpp:7723) reads SourceID, TargetID and Action directly off
/// the struct and passes them straight to PlayerManager::GroupAction -- NO ntohl
/// anywhere -- so all three int32s are host-order little-endian. In the captured
/// frame SourceID is a small sane avatar/group id (1473672) little-endian, which
/// big-endian would turn into a multi-hundred-million-byte-swapped value; the same
/// SourceID is echoed back by the server's CTA_RESPONSE (0x00BD) for the matching
/// request, which pins the byte order. Source: struct CTARequest
/// (PacketStructures.h:974), Player::HandleCTARequest (PlayerConnection.cpp:7723).
/// Pinned to capture_3.rar.
/// </summary>
public sealed class CtaRequestRecord : PacketRecord
{
    public CtaRequestRecord(ReadOnlySpan<byte> payload) : base(0x00BC, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 12) { Flag(sb, $"CTA_REQUEST truncated -- {Payload.Length} bytes, expected 12"); return; }

        FHex(sb, 0, "SourceID", ReadI32LE(Payload, 0),
            "(LE; group-action source avatar/group id -- HandleCTARequest reads direct, no ntohl)");
        FHex(sb, 4, "TargetID", ReadI32LE(Payload, 4),
            "(LE; group-action target object -- 0 = whole group / no single target)");
        FDec(sb, 8, "Action", ReadI32LE(Payload, 8),
            "(LE; GroupAction selector)");

        if (Payload.Length > 12) Flag(sb, $"CTA_REQUEST has {Payload.Length - 12} trailing bytes");
    }
}
