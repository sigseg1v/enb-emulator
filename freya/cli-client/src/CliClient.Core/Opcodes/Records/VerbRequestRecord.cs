// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x005A VERB_REQUEST. Wire (struct VerbRequest, 12 bytes) -- MIXED endianness:
///   int32 SubjectID (BE); int32 ObjectID (BE); int32 Action (LE).
/// Client->server "perform verb Action by SubjectID on ObjectID". The server reads
/// the two ids through ntohl (so they are big-endian on the wire) but compares
/// Action raw host-order (pkt->Action == 1 on x86 == little-endian) in
/// Player::HandleVerbRequest. Same id-BE / payload-LE split documented on 0x005C
/// VERB_UPDATE. Confirmed against capture_3.rar Packet 1479: SubjectID/ObjectID
/// read BE equal the SAME player's 0x0017 REQUEST_TARGET ids read LE
/// (8708585 / 2617), and Action reads 1 LE -- exactly the UpdateVerbs(true) trigger.
/// </summary>
public sealed class VerbRequestRecord : PacketRecord
{
    public VerbRequestRecord(ReadOnlySpan<byte> payload) : base(0x005A, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 12) { Flag(sb, $"VERB_REQUEST truncated -- {Payload.Length} bytes, expected 12"); return; }
        FHex(sb, 0, "SubjectID", ReadI32BE(Payload, 0), "(BE -- ntohl at parse)");
        FHex(sb, 4, "ObjectID",  ReadI32BE(Payload, 4), "(BE -- ntohl at parse)");
        FDec(sb, 8, "Action",    ReadI32LE(Payload, 8), "(LE -- raw host read)");
    }
}
