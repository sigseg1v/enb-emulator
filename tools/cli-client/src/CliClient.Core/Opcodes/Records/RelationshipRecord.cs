// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0089 RELATIONSHIP. Wire shape (struct Relationship, 9 bytes):
///   int32 ObjectID; int32 Reaction; uint8 IsAttacking.
/// NOTE: the server emitter applies ntohl(ObjectID) before placing it in
/// the struct, so the wire bytes for ObjectID are big-endian. Decoded here
/// as BE to recover the original game ID.
/// </summary>
public sealed class RelationshipRecord : PacketRecord
{
    public RelationshipRecord(ReadOnlySpan<byte> payload) : base(0x0089, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 9)
        {
            Flag(sb, $"RELATIONSHIP truncated -- {Payload.Length} bytes, expected 9");
            return;
        }
        int objectId    = ReadI32BE(Payload, 0);  // ntohl applied at emit -- wire is BE
        int reaction    = ReadI32LE(Payload, 4);
        byte isAttacking = Payload[8];

        FieldHex(sb, "ObjectID",    objectId, "(ntohl on wire -- decoded as BE)");
        FieldDec(sb, "Reaction",    reaction,
            reaction == 5 ? "(FRIENDLY)" :
            reaction == 4 ? "(NEUTRAL)" :
            reaction == 3 ? "(HOSTILE)" :
            reaction == 0 ? "(UNFRIENDLY)" : null);
        FieldDec(sb, "IsAttacking", isAttacking, isAttacking != 0 ? "(true)" : "(false)");
    }
}
