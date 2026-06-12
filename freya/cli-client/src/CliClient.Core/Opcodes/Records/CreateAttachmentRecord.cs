// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x004A CREATE_ATTACHMENT. Wire (struct CreateAttachment, PacketStructures.h:438):
///   int32 Parent_ID; int32 Child_ID; int32 Slot. = 12 bytes.
/// Emitter PlayerConnection.cpp:1155 emits ALL THREE via ntohl() -> BIG-ENDIAN.
/// </summary>
public sealed class CreateAttachmentRecord : PacketRecord
{
    public CreateAttachmentRecord(ReadOnlySpan<byte> payload) : base(0x004A, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 12) { Flag(sb, $"CREATE_ATTACHMENT truncated -- {Payload.Length} bytes, expected 12"); return; }
        FHex(sb, 0, "ParentID", ReadI32BE(Payload, 0), "(BE -- ntohl at emit)");
        FHex(sb, 4, "ChildID",  ReadI32BE(Payload, 4), "(BE -- ntohl at emit)");
        FDec(sb, 8, "Slot",     ReadI32BE(Payload, 8), "(BE -- ntohl at emit)");
    }
}
