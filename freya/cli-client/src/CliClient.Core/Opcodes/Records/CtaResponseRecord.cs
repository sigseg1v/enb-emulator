// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

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
/// DIVERGENCE NOTE (see plans/26-phase-z-emitter-fidelity.md, Z-1): Field[4] is NOT
/// a constant. Across capture_3 it VARIES -- 0x0F (=15) in #21495 and 0x0E (=14) in a
/// later 0x0BD frame -- so it is a server-assigned response/type code, not the
/// client's Action. Our server's emitter hardcodes 0x0F in its CTAResponse[] template
/// (citing this very frame) then OVERWRITES Field[4] with the request's Action
/// (PlayerConnection.cpp:7746, "*((int32_t*)&CTAResponse[4]) = myCTARequest->Action"),
/// so against our server this field carries the raw Action (e.g. 5) -- matching
/// neither the template's 0x0F nor retail's varying code. The retail frame is the
/// source of truth, so this decoder renders the per-frame value and labels Field[4]
/// "RequestType". The correct general value is unknown (the CTA response-type domain
/// has no enum in our headers), so the server is intentionally NOT changed -- resolving
/// it needs that enum plus more paired 0xBC->0xBD frames. Source: Player::
/// HandleCTARequest CTAResponse template (PlayerConnection.cpp:7733). Pinned to
/// capture_3.rar #21495.
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
            "(LE; retail server-assigned code, varies 0x0E/0x0F; our emitter writes the request Action here instead -- see Phase Z Z-1)");
        FDec(sb, 8, "Success", Payload[8],
            "(1 = accepted)");

        if (Payload.Length > 9) Flag(sb, $"CTA_RESPONSE has {Payload.Length - 9} trailing bytes");
    }
}
