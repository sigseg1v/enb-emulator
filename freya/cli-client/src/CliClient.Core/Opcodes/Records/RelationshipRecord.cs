// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0089 RELATIONSHIP. Wire (struct Relationship, 9 bytes):
///   int32 ObjectID (BE -- emitter applies ntohl); int32 Reaction; uint8 IsAttacking.
/// </summary>
public sealed class RelationshipRecord : PacketRecord
{
    public RelationshipRecord(ReadOnlySpan<byte> payload) : base(0x0089, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 9) { Flag(sb, $"RELATIONSHIP truncated -- {Payload.Length} bytes, expected 9"); return; }
        int  objectId    = ReadI32BE(Payload, 0);
        int  reaction    = ReadI32LE(Payload, 4);
        byte isAttacking = Payload[8];
        FHex(sb, 0, "ObjectID",    objectId, "(BE -- ntohl at emit)");
        // Reaction values per the server enum (PacketStructures.h
        // RELATIONSHIP_*): 0=ATTACK, 1=SHUN, 2=FRIENDLY, 3=ADORATION. Confirmed
        // against an upstream net7 sector capture (Ishuan): the only values seen
        // on the wire were 0/1/2/3 -- never 4 or 5.
        FDec(sb, 4, "Reaction",    reaction,
            reaction == 0 ? "(ATTACK)"    :
            reaction == 1 ? "(SHUN)"      :
            reaction == 2 ? "(FRIENDLY)"  :
            reaction == 3 ? "(ADORATION)" : null);
        FDec(sb, 8, "IsAttacking", isAttacking, isAttacking != 0 ? "(true)" : "(false)");
    }
}
