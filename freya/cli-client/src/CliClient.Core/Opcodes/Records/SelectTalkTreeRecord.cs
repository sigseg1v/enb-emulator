// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// SELECT_TALK_TREE (0x0055, client->server). The client sends this when the
/// player picks a menu option in an NPC conversation: which NPC, and which
/// branch. Wire (struct SelectTalkTree, 5 bytes, LITTLE-ENDIAN):
/// int32 PlayerID; u8 Selection. Both fields are little-endian raw reads --
/// Player::HandleSelectTalkTree casts data straight to SelectTalkTree* and reads
/// packet->PlayerID / packet->Selection with no ntohl. PlayerID is the targeted
/// NPC/object id: the handler only ever uses its low 24 bits
/// (packet->PlayerID & 0x00FFFFFF), the top byte being a type marker, so a small
/// station NPC shows up as e.g. 0x0000141E. Selection is the menu branch index,
/// with two reserved values the handler special-cases: 0 = "more"/back (resolved
/// against m_MoreDestination, and used to end an action response), and 255 =
/// resume the normal conversation tree after a mission debrief.
/// </summary>
public sealed class SelectTalkTreeRecord : PacketRecord
{
    public SelectTalkTreeRecord(ReadOnlySpan<byte> payload) : base(0x0055, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 5) { Flag(sb, $"SELECT_TALK_TREE truncated -- {Payload.Length} bytes, expected 5"); return; }

        FHex(sb, 0, "PlayerID", ReadI32LE(Payload, 0), "(LE; low 24 bits = NPC/object id)");
        byte sel = Payload[4];
        string note = sel switch
        {
            0   => "(0 = more/back)",
            255 => "(255 = resume tree after debrief)",
            _   => "(menu branch index)",
        };
        FDec(sb, 4, "Selection", sel, note);
    }
}
