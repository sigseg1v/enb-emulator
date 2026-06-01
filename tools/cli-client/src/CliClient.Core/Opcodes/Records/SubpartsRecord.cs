// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x00B4 SUBPARTS. Wire (variable, built via AddData/AddDataS):
///   int32 GameID (BE -- emitter applies ntohl);
///   int32 NumSubParts (BE);
///   NumSubParts pairs: NUL-terminated bone path + int32 asset ID (BE).
/// Source: PlayerClass.cpp SendSubparts().
/// </summary>
public sealed class SubpartsRecord : PacketRecord
{
    public SubpartsRecord(ReadOnlySpan<byte> payload) : base(0x00B4, payload) { }
    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 8) { Flag(sb, $"SUBPARTS truncated -- {'{'}Payload.Length{'}'} bytes, expected >= 8"); return; }
        int gameId   = ReadI32BE(Payload, 0);
        int numParts = ReadI32BE(Payload, 4);
        FHex(sb, 0, "GameID",      gameId, "(BE -- ntohl at emit)");
        FDec(sb, 4, "NumSubParts", numParts);
        int off = 8;
        for (int i = 0; i < numParts && off < Payload.Length; i++)
        {
            int nul = System.Array.IndexOf(Payload, (byte)0, off);
            if (nul < 0 || nul >= Payload.Length) break;
            string bone = System.Text.Encoding.ASCII.GetString(Payload, off, nul - off);
            int strLen  = nul - off + 1; // include NUL
            FStr(sb, off, strLen, $"  [{i}] Bone",    bone);
            off = nul + 1;
            if (off + 4 > Payload.Length) break;
            int assetId = ReadI32BE(Payload, off);
            FHex(sb, off, $"  [{i}] AssetID", assetId);
            off += 4;
        }
    }
}
