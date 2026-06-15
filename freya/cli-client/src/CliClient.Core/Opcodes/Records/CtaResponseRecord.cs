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
/// (PlayerConnection.cpp:7973, "*((int32_t*)&CTAResponse[4]) = myCTARequest->Action"),
/// so against our server this field carries the raw Action (e.g. 5) -- matching
/// neither the template's 0x0F nor retail's varying code.
///
/// CLIENT SEMANTICS (observed from the retail client's 0x00BD handler): the client
/// switches on (Field[4] - 13), i.e. it only recognises Field[4] in {13, 14, 15, 17}:
///   13 -> acknowledge / no-op (no visual change)
///   14 -> clear the Call-To-Arms highlight on the local player (beacon OFF)
///   15 -> set the Call-To-Arms highlight on the local player (beacon ON)
///   17 -> set the Call-To-Arms highlight on the avatar named by SourceID (Field[0])
/// ANY other value falls into the client's default arm: it logs an error and renders
/// NO effect. So when our server echoes the request Action (4..12, or 0) here, the
/// client REJECTS the response and the Call-To-Arms beacon never fires -- the feature
/// is broken against our server. This is a real correctness defect, but the exact
/// request(Action)->response(code) mapping is NOT yet pinned (the retail
/// formation/CTA request Action that produced 0x0F/0x0E in capture_3 has to be decoded
/// from the paired request #21493), so the server is intentionally NOT changed here --
/// fixing it blind risks emitting the wrong beacon state. This decoder labels Field[4]
/// with its recognised meaning so a capture round-trip can confirm the mapping. Source:
/// Player::HandleCTARequest CTAResponse template (PlayerConnection.cpp:7960). Pinned to
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
        int code = ReadI32LE(Payload, 4);
        FDec(sb, 4, "RequestType", code,
            $"(LE; {DescribeCode(code)}; our emitter writes the request Action here instead -- see Phase Z Z-1)");
        FDec(sb, 8, "Success", Payload[8],
            "(1 = accepted)");

        if (Payload.Length > 9) Flag(sb, $"CTA_RESPONSE has {Payload.Length - 9} trailing bytes");
    }

    /// <summary>
    /// The retail client recognises Field[4] only in {13, 14, 15, 17} (it switches on
    /// the value minus 13). Any other value is rejected client-side with no effect.
    /// </summary>
    private static string DescribeCode(int code) => code switch
    {
        13 => "acknowledge / no-op",
        14 => "beacon OFF (local player)",
        15 => "beacon ON (local player)",
        17 => "beacon ON (avatar = SourceID)",
        _  => "client rejects -- not in {13,14,15,17}",
    };
}
