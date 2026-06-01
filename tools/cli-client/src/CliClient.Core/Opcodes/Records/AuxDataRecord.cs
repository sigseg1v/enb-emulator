// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;
using N7.CliClient.Opcodes.Records.Aux;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x001B AUX_DATA. The payload is the raw output of some AuxBase-derived
/// structure's BuildPacket(): [u32 GameID][u16 bodyLen=payload-6][u8 version=1]
/// [flag bitmap][conditional fields]. There is NO subtype tag on the wire --
/// the retail client disambiguates by which object the GameID names. We
/// discriminate by trying each candidate schema's flag-walk (AuxWalker) and
/// keeping the one that consumes the payload exactly.
///
/// Schemas live in AuxSchemas, ported field-for-field from
/// server/src/AuxClasses/*::BuildPacket. Frames whose producing class we have
/// not yet schematised fall back to the AddString scanner + gap reporter.
/// </summary>
public sealed class AuxDataRecord : PacketRecord
{
    public AuxDataRecord(ReadOnlySpan<byte> payload) : base(0x001B, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 7) { Flag(sb, $"AUX_DATA truncated -- {Payload.Length} bytes, expected >= 7"); return; }

        ushort bodyLen = ReadU16LE(Payload, 4);
        byte   version = Payload[6];
        // The body-length field is set as (index-6) by most Build* paths but as
        // (index) by some diff paths, so don't gate on it -- gate on version and
        // let exact-consumption validate the walk.
        bool   headerOk = version == 1 && Payload.Length >= 8;

        if (headerOk && TrySchemaWalk(sb)) return;

        if (headerOk && EmitBestPartial(sb)) return;

        // ── fallback: outer header + AddString scan (legacy behaviour) ────────
        int gameId = ReadI32LE(Payload, 0);
        FHex(sb, 0, "GameID", gameId);
        bool isAuxOuter = version == 1 && (bodyLen == 0 || bodyLen == (ushort)(Payload.Length - 6));
        if (isAuxOuter)
        {
            FDec(sb, 4, "BodyLen", (short)bodyLen, bodyLen == 0 ? "(not filled -- AuxPlayerIndex pattern)" : null);
            FDec(sb, 6, "Version", version);
            foreach (var (soff, value) in ExtractAddStrings(Payload.AsSpan(7), minLen: 2))
            {
                int absOff = soff + 7;
                FStr(sb, absOff, 2 + value.Length, $"str@{absOff:X4}", value);
            }
            return;
        }
        if (Payload.Length >= 10)
        {
            ushort inlineLen  = ReadU16LE(Payload, 4);
            ushort inlineType = ReadU16LE(Payload, 6);
            if (inlineType == 0x1201)
            {
                short strLen = ReadI16LE(Payload, 8);
                FDec(sb, 4, "InlineLen", (short)inlineLen);
                F(sb,   6, 2, "SubType",   "0x1201  (resource name)");
                if (strLen > 0 && Payload.Length >= 10 + strLen)
                {
                    string name = ReadNulString(Payload.AsSpan(10, strLen));
                    FStr(sb, 10, strLen, "Name", name, required: true);
                    return;
                }
            }
            if (inlineType == 0x0116 || inlineType == 0x1603 || inlineType == 0x03E0)
            {
                F(sb, 6, 2, "SubType", $"0x{inlineType:X4}  (husk/mob name)");
                foreach (var (soff, value) in ExtractAddStrings(Payload.AsSpan(8), minLen: 2)) { int absOff = soff + 8; FStr(sb, absOff, 2 + value.Length, $"str@{absOff:X4}", value); }
                return;
            }
        }
        foreach (var (soff, value) in ExtractAddStrings(Payload.AsSpan(4), minLen: 2)) { int absOff = soff + 4; FStr(sb, absOff, 2 + value.Length, $"str@{absOff:X4}", value); }
    }

    /// <summary>
    /// Try every candidate schema/flavour; keep the one that consumes the payload
    /// exactly. Among exact fits, prefer the highest string-plausibility (a wrong
    /// schema tends to read random bytes as bogus strings), then most fields.
    /// </summary>
    private bool TrySchemaWalk(StringBuilder sb)
    {
        uint gameId = ReadU32LE(Payload, 0);
        string? bestName = null; bool bestExt = false;
        List<AuxAnno>? bestAnnos = null; double bestPlaus = -1;
        foreach (var c in AuxSchemaRegistry.Candidates)
        {
            if (!AuxSchemaRegistry.GateMatches(c.Gate, gameId)) continue;
            var w = new AuxWalker(Payload, c.Extended);
            if (w.Walk(c.Schema) != Payload.Length) continue;
            double plaus = w.StringPlausibility;
            if (plaus < 0.9) continue;
            bool better = bestAnnos is null
                || plaus > bestPlaus + 0.001
                || (System.Math.Abs(plaus - bestPlaus) <= 0.001 && w.Annos.Count > bestAnnos.Count);
            if (better)
            {
                bestName = c.Schema.Name; bestExt = c.Extended;
                bestAnnos = w.Annos; bestPlaus = plaus;
            }
        }
        if (bestName is null || bestAnnos is null) return false;

        F(sb, 0, 0, "AuxType", bestName + (bestExt ? " (extended)" : ""));
        foreach (var a in bestAnnos)
        {
            string indent = a.Depth > 0 ? new string(' ', a.Depth * 2) : "";
            if (a.Len > 0 || a.Value.StartsWith("("))
                F(sb, a.Off, a.Len, indent + a.Name, a.Value);
        }
        return true;
    }

    /// <summary>
    /// No candidate consumed the payload exactly. Emit the furthest-reaching
    /// candidate's partial annotations plus a divergence marker, so the
    /// remaining bytes show as a gap exactly where the schema drifts. Falls
    /// through (returns false) when even the best walk barely started.
    /// </summary>
    private bool EmitBestPartial(StringBuilder sb)
    {
        uint gameId = ReadU32LE(Payload, 0);
        AuxWalker? best = null; AuxSchemaRegistry.Candidate bestC = default;
        foreach (var c in AuxSchemaRegistry.Candidates)
        {
            if (!AuxSchemaRegistry.GateMatches(c.Gate, gameId)) continue;
            var w = new AuxWalker(Payload, c.Extended);
            w.Walk(c.Schema);
            if (best is null || w.FailOffset > best.FailOffset) { best = w; bestC = c; }
        }
        if (best is null || best.FailOffset < 12) return false;  // nothing useful

        F(sb, 0, 0, "AuxType", $"{bestC.Schema.Name}{(bestC.Extended ? " (extended)" : "")} -- PARTIAL");
        foreach (var a in best.Annos)
        {
            string indent = a.Depth > 0 ? new string(' ', a.Depth * 2) : "";
            if (a.Len > 0 || a.Value.StartsWith("(")) F(sb, a.Off, a.Len, indent + a.Name, a.Value);
        }
        Flag(sb, $"schema diverged at 0x{best.FailOffset:X4} in {best.FailWhere} -- remaining bytes undecoded");
        return true;
    }

    private static List<(int offset, string value)> ExtractAddStrings(ReadOnlySpan<byte> span, int minLen)
    {
        var result = new List<(int, string)>();
        int i = 0;
        while (i + 2 <= span.Length)
        {
            int len = span[i] | (span[i + 1] << 8);
            if (len >= minLen && len <= 256 && i + 2 + len <= span.Length)
            {
                bool ok = true;
                for (int k = 0; k < len; k++) { byte b = span[i + 2 + k]; if (b < 0x20 || b >= 0x7F) { ok = false; break; } }
                if (ok) { result.Add((i, System.Text.Encoding.ASCII.GetString(span.Slice(i + 2, len)))); i += 2 + len; continue; }
            }
            i++;
        }
        return result;
    }
}
