// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x005C VERB_UPDATE. Wire (built via AddData in Player::UpdateVerbs):
///   int32 GameID (BE -- ntohl); int32 Count (BE);
///   Count x { int16 Attribute; int16 VerbID };   // "disabled/too-far" pass
///   int32 Count (BE);                             // repeated
///   Count x { int16 Attribute; int16 VerbID };   // "enabled" pass
/// Attribute: 0x00 ENABLE, 0x02 DIS_TOOFAR. GameID and BOTH Counts are
/// big-endian (ntohl); the int16 verb entries are little-endian.
/// Source: Player::UpdateVerbs (PlayerClass.cpp).
/// </summary>
public sealed class VerbUpdateRecord : PacketRecord
{
    public VerbUpdateRecord(ReadOnlySpan<byte> payload) : base(0x005C, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 8) { Flag(sb, $"VERB_UPDATE truncated -- {Payload.Length} bytes, expected >= 8"); return; }
        FHex(sb, 0, "GameID", ReadI32BE(Payload, 0), "(BE -- ntohl at emit)");
        int off = 4;
        for (int pass = 0; pass < 2 && off + 4 <= Payload.Length; pass++)
        {
            int count = ReadI32BE(Payload, off);
            FDec(sb, off, $"Count[{pass}]", count, "(BE)"); off += 4;
            for (int i = 0; i < count && off + 4 <= Payload.Length; i++)
            {
                short attr = ReadI16LE(Payload, off);
                short verb = ReadI16LE(Payload, off + 2);
                string an = attr == 0 ? "ENABLE" : attr == 2 ? "DIS_TOOFAR" : $"0x{(ushort)attr:X4}";
                F(sb, off, 4, $"  [{pass}.{i}] Attr={an}", $"Verb=0x{(ushort)verb:X4} ({(ushort)verb})");
                off += 4;
            }
        }
    }
}
