// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using System.Globalization;
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

        // AuxMobIndex create/click do not use the generic "present at bit N+4"
        // bitmap (they hand-roll a 15-byte masked flag block with cross-byte
        // gating), so they can't be a registry candidate -- try them only after
        // the generic walk declines, before reporting divergence.
        if (headerOk && TryMobIndex(sb)) return;

        // AuxHulkIndex (destroyed-object husk/corpse) also hand-rolls a
        // create/diff flag layout with nested AuxInventory20/40 item lists that
        // the generic walk cannot model -- try it after MobIndex, before
        // reporting divergence.
        if (headerOk && TryHulkIndex(sb)) return;

        if (headerOk && EmitBestPartial(sb)) return;

        // A version byte of 0 is never a top-level AuxBase build -- every
        // Build*Packet path writes char(1) at offset 6. Those frames are the
        // player-variable ability-state list emitted by SendProspectAUX
        // (server/src/PlayerConnection.cpp): a flat (abilityID, value) array.
        if (version == 0 && TryAbilityVarUpdate(sb)) return;

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
    /// version-byte-0 AUX_DATA: the player-variable ability-state update emitted
    /// by Player::SendProspectAUX (server/src/PlayerConnection.cpp). Unlike a
    /// top-level AuxBase build (which always writes char(1) at offset 6), these
    /// write a 0 there and carry a flat list of (abilityID, value) updates:
    ///   [u32 GameID][u16 bodyLen][u8 0][u32 count]
    ///   [count x (u32 abilityID, u32 value)][u64 trailing]
    /// GameID is 0 for a self/player update and the target object id otherwise.
    /// The value is opaque -- observed as a last-activation timestamp, as a flag
    /// (256 disables / 0 re-enables the cloak abilities 0x0C15 and 0x0CF5), and as
    /// a float -- so it is shown as raw hex with a float gloss when the bit pattern
    /// is a finite, in-range float. Returns false (falls through) unless the
    /// count/length relationship holds exactly, so the 12-entry-style variant with
    /// a wider leading entry is left to the honest fallback rather than mis-split.
    /// </summary>
    private bool TryAbilityVarUpdate(StringBuilder sb)
    {
        if (Payload.Length < 19) return false;            // 7 hdr + 4 count + 8 entry (>=1) ... min real frame is 27
        uint count = ReadU32LE(Payload, 7);
        if (count < 1 || count > 64) return false;
        if (7 + 4 + (long)count * 8 + 8 != Payload.Length) return false;   // exact structural fit

        uint gameId = ReadU32LE(Payload, 0);
        F(sb, 0, 0, "AuxType", "AbilityVarUpdate (player-var ability-id/value pairs)");
        FHex(sb, 0, "GameID", gameId, gameId == 0 ? "(self / player-var)" : "(target object)");
        FDec(sb, 4, "BodyLen", (short)ReadU16LE(Payload, 4));
        FDec(sb, 6, "Version", Payload[6], "(0 = not a top-level AuxIndex build)");
        FDec(sb, 7, "Count", (int)count);
        int off = 11;
        for (int i = 0; i < count; i++)
        {
            uint id  = ReadU32LE(Payload, off);
            uint val = ReadU32LE(Payload, off + 4);
            float f  = ReadF32LE(Payload, off + 4);
            string? note = float.IsFinite(f) && f != 0f
                           && System.Math.Abs(f) >= 1e-3f && System.Math.Abs(f) < 1e9f
                ? $"(f32 {f:0.###})" : null;
            FHex(sb, off,     $"[{i}] AbilityID", id);
            FHex(sb, off + 4, $"[{i}] Value",     val, note);
            off += 8;
        }
        var trail = Payload.AsSpan(off, 8);
        var th = new StringBuilder();
        for (int k = 0; k < 8; k++) { if (k > 0) th.Append(' '); th.Append(trail[k].ToString("X2")); }
        bool trailZero = true;
        for (int k = 0; k < 8; k++) if (trail[k] != 0) { trailZero = false; break; }
        FBytes(sb, off, 8, "Trailing", th.ToString(), trailZero ? null : "(non-zero -- unexpected)");
        return true;
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
    /// Decode the payload as an AuxMobIndex create/click body (the hand-rolled
    /// 15-byte-flag-block layout that the generic AuxWalker cannot model).
    /// Emits annotations and returns true only on a byte-exact, plausible-string
    /// consume; otherwise falls through so the generic divergence path runs.
    /// </summary>
    private bool TryMobIndex(StringBuilder sb)
    {
        var d = new AuxMobIndexDecoder(Payload);
        if (d.TryDecode() != Payload.Length) return false;
        if (d.StringPlausibility < 0.9) return false;

        F(sb, 0, 0, "AuxType", $"MobIndex ({d.Variant})");
        foreach (var a in d.Annos)
        {
            string indent = a.Depth > 0 ? new string(' ', a.Depth * 2) : "";
            if (a.Len > 0 || a.Value.StartsWith("("))
                F(sb, a.Off, a.Len, indent + a.Name, a.Value);
        }
        return true;
    }

    /// <summary>
    /// Decode the payload as an AuxHulkIndex create/diff body (the husk/corpse
    /// layout with nested AuxInventory20 equip + AuxInventory40 cargo item
    /// lists). Emits annotations and returns true only on a byte-exact,
    /// plausible-string consume; otherwise falls through to the divergence path.
    /// </summary>
    private bool TryHulkIndex(StringBuilder sb)
    {
        var d = new AuxHulkIndexDecoder(Payload);
        if (d.TryDecode() != Payload.Length) return false;
        if (d.StringPlausibility < 0.9) return false;

        F(sb, 0, 0, "AuxType", $"HulkIndex ({d.Variant})");
        foreach (var a in d.Annos)
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

    /// <summary>
    /// Object name + combat level pulled out of a 0x001B frame for the
    /// in-sector world model. Reuses the exact same best-fit schema
    /// selection as the dump path (<see cref="TrySchemaWalk"/>): try each
    /// gated candidate, keep the one that consumes the payload exactly with
    /// the highest string-plausibility. <see cref="GameId"/> is the aux's
    /// own id slot -- 0 for the self <c>PlayerIndex</c> (whose nested
    /// names/levels belong to no single sector object), the entity id for
    /// <c>ShipIndex</c>/<c>Harvestable</c>. Returns null when no schema fits.
    /// </summary>
    public readonly record struct AuxSummary(uint GameId, string? Name, uint? CombatLevel, float? MaxSpeed);

    public static AuxSummary? TryExtractSummary(ReadOnlySpan<byte> payload)
    {
        // Same header gate as WriteFields: version byte at 6 must be 1.
        if (payload.Length < 8 || payload[6] != 1) return null;
        uint gameId = BinaryPrimitives.ReadUInt32LittleEndian(payload[..4]);

        List<AuxAnno>? bestAnnos = null;
        double bestPlaus = -1;
        foreach (var c in AuxSchemaRegistry.Candidates)
        {
            if (!AuxSchemaRegistry.GateMatches(c.Gate, gameId)) continue;
            var w = new AuxWalker(payload, c.Extended);
            if (w.Walk(c.Schema) != payload.Length) continue;
            double plaus = w.StringPlausibility;
            if (plaus < 0.9) continue;
            bool better = bestAnnos is null
                || plaus > bestPlaus + 0.001
                || (System.Math.Abs(plaus - bestPlaus) <= 0.001 && w.Annos.Count > bestAnnos.Count);
            if (better) { bestAnnos = w.Annos; bestPlaus = plaus; }
        }
        // No registry candidate fit -- the entity may be an AuxMobIndex
        // create/click (NPC/creature), whose bespoke layout the generic walk
        // can't model. Its top-level Name + CombatLevel still belong in the
        // world model.
        if (bestAnnos is null)
        {
            var d = new AuxMobIndexDecoder(payload);
            if (d.TryDecode() == payload.Length && d.StringPlausibility >= 0.9)
                bestAnnos = d.Annos;
        }
        // ...or an AuxHulkIndex (destroyed-object husk): its top-level Name is
        // the corpse's name, which the world model shows for lootable wrecks.
        if (bestAnnos is null)
        {
            var h = new AuxHulkIndexDecoder(payload);
            if (h.TryDecode() == payload.Length && h.StringPlausibility >= 0.9)
                bestAnnos = h.Annos;
        }
        if (bestAnnos is null) return null;

        string? name = null;
        uint? level = null;
        float? maxSpeed = null;
        foreach (var a in bestAnnos)
        {
            // Only the top-level (depth-0) Name field is the object's own
            // name; nested Mission/Faction/GroupMember "Name" fields are
            // depth>0 and must not be mistaken for it.
            if (a.Depth == 0 && a.Name == "Name" && name is null)
                name = Unquote(a.Value);
            else if (a.Name == "CombatLevel" && level is null && TryLeadingUint(a.Value, out uint v))
                level = v;
            else if (a.Depth == 0 && a.Name == "MaxSpeed" && maxSpeed is null
                     && float.TryParse(a.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float ms))
                maxSpeed = ms;
        }
        return new AuxSummary(gameId, name, level, maxSpeed);
    }

    // AuxWalker renders strings as "\"value\"" and U32 as "{v}  (0x........)".
    private static string Unquote(string v)
        => v.Length >= 2 && v[0] == '"' && v[^1] == '"' ? v[1..^1] : v;

    private static bool TryLeadingUint(string v, out uint result)
    {
        int sp = v.IndexOf(' ');
        string head = sp < 0 ? v : v[..sp];
        return uint.TryParse(head, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }
}
