// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// CTA_RESPONSE (0x00BD, server-&gt;client) -- the server's reply to a CTA_REQUEST
/// (0x00BC). Wire (9 bytes, all LITTLE-ENDIAN): int32 SourceID, int32 RequestType,
/// char Success. The emitter is the CTAResponse[] byte template in
/// Player::HandleCTARequest: {int32 GameID, int32 RequestType, byte Success},
/// written with raw int32 stores (no byte flip, host-order LE). Field[0] echoes the
/// request's SourceID, which both confirms the request/response pairing and pins the
/// byte order.
///
/// Field[4] (RequestType) is the echoed GroupAction selector from the request: the
/// server reflects back the request's Action value with Success=1 to acknowledge the
/// formation/group action. The live retail server echoes the request Action here
/// (values 4..12 observed) with Success=1, and the live retail client accepts those
/// values and runs the formation session. Our server's Player::HandleCTARequest
/// matches that retail behaviour byte-for-byte, so echoing the request Action in this
/// field is CORRECT, not a divergence.
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
            $"(LE; echoed GroupAction selector; {DescribeAction(code)})");
        FDec(sb, 8, "Success", Payload[8],
            "(1 = ok)");

        if (Payload.Length > 9) Flag(sb, $"CTA_RESPONSE has {Payload.Length - 9} trailing bytes");
    }

    /// <summary>
    /// Field[4] is the echoed GroupAction selector the request asked for. The server
    /// reflects it back with Success=1; the retail client accepts and runs it.
    /// </summary>
    internal static string DescribeAction(int code) => code switch
    {
        4  => "Slot Back",
        5  => "Block",
        6  => "Pipe",
        7  => "Form Up",
        8  => "Leave Formation",
        9  => "Break Formation",
        12 => "Request Target",
        _  => "GroupAction selector",
    };
}
