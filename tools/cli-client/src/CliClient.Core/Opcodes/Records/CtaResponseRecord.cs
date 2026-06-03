// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// CTA_RESPONSE (0x00BD, server-&gt;client) -- the server's reply to a CTA_REQUEST
/// (0x00BC). Wire (9 bytes, all LITTLE-ENDIAN): int32 SourceID, int32 RequestType,
/// char Success. The emitter is the CTAResponse[] byte template in
/// Player::HandleCTARequest (PlayerConnection.cpp:7733): {int32 GameID, int32
/// RequestType=0x0F, byte Success=0x01}, written with raw int32 stores (no byte
/// flip, host-order LE). Field[0] echoes the request's SourceID -- in the captured
/// pair it is 1473672, identical to the CTA_REQUEST SourceID (capture_3.rar request
/// #21493 / response #21495), which both confirms the pairing and pins the byte
/// order.
///
/// DIVERGENCE NOTE: the retail capture shows Field[4] = 0x0F (the template's
/// "RequestType" constant) and Success = 0x01. Our server's emitter, however,
/// overwrites Field[4] with the request's Action (PlayerConnection.cpp:7746,
/// "*((int32_t*)&CTAResponse[4]) = myCTARequest->Action"), so against our server
/// this field would carry the Action value (e.g. 5) instead of 0x0F. The retail
/// frame is the source of truth, so this decoder labels Field[4] "RequestType" and
/// renders the retail value; the emitter mismatch is left to a separate server-side
/// review (one captured frame is not enough to prove the correct general value, so
/// the server is intentionally not changed here). Source: Player::HandleCTARequest
/// CTAResponse template (PlayerConnection.cpp:7733). Pinned to capture_3.rar #21495.
/// </summary>
public sealed class CtaResponseRecord : PacketRecord
{
    public CtaResponseRecord(ReadOnlySpan<byte> payload) : base(0x00BD, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 9) { Flag(sb, $"CTA_RESPONSE truncated -- {Payload.Length} bytes, expected 9"); return; }

        FHex(sb, 0, "SourceID", ReadI32LE(Payload, 0),
            "(LE; echoes the CTA_REQUEST SourceID)");
        FDec(sb, 4, "RequestType", ReadI32LE(Payload, 4),
            "(LE; retail 0x0F; our emitter writes the request Action here instead -- divergence)");
        FDec(sb, 8, "Success", Payload[8],
            "(1 = accepted)");

        if (Payload.Length > 9) Flag(sb, $"CTA_RESPONSE has {Payload.Length - 9} trailing bytes");
    }
}
