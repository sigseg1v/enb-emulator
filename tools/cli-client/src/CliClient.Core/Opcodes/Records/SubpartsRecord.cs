// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x00B4 SUBPARTS. Wire shape (variable, built via AddData/AddDataS):
///   int32 GameID     (BE -- emitter applies ntohl)
///   int32 NumSubParts (BE -- emitter applies ntohl)
///   Then NumSubParts pairs of:
///     NUL-terminated string (bone path, e.g. "~01", "~02/~03_01")
///     int32 asset ID        (BE -- emitter applies ntohl)
/// Race-dependent: Jenquai ships emit 4 subparts; other races 4 (5-6 for
/// upgraded Terran traders). Source: PlayerClass.cpp SendSubparts().
/// </summary>
public sealed class SubpartsRecord : PacketRecord
{
    public SubpartsRecord(ReadOnlySpan<byte> payload) : base(0x00B4, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 8)
        {
            Flag(sb, $"SUBPARTS truncated -- {Payload.Length} bytes, expected >= 8");
            return;
        }
        int gameId    = ReadI32BE(Payload, 0);  // ntohl at emit
        int numParts  = ReadI32BE(Payload, 4);  // ntohl at emit

        FieldHex(sb, "GameID",      gameId, "(ntohl on wire -- decoded as BE)");
        FieldDec(sb, "NumSubParts", numParts);

        int off = 8;
        for (int i = 0; i < numParts && off < Payload.Length; i++)
        {
            // NUL-terminated bone path string
            int nul = Array.IndexOf(Payload, (byte)0, off);
            if (nul < 0 || nul >= Payload.Length) break;
            string bone = System.Text.Encoding.ASCII.GetString(Payload, off, nul - off);
            off = nul + 1;

            if (off + 4 > Payload.Length) break;
            int assetId = ReadI32BE(Payload, off);  // ntohl at emit
            off += 4;

            Field(sb, $"  [{i}] Bone",    Quote(bone));
            Field(sb, $"  [{i}] AssetID", $"0x{assetId:X8}  ({assetId})  (BE)");
        }
    }
}
