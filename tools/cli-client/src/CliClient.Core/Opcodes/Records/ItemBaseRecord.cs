// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using System.Text;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// 0x0025 ITEM_BASE. Full decode of ItemBase::BuildItemBasePacket() wire format.
///
/// Wire layout (all from ItemBase.cpp BuildItemBasePacket()):
///   [0-3]   ItemTemplateID  BE int32  (AddDataFlip4)
///   [4-7]   Category        LE int32  (AddData&lt;int&gt;)
///   [8-11]  SubCategory     LE int32
///   [12-15] ItemType        LE int32
///   [16]    ItemFieldCount  1B char   (AddData&lt;char&gt;)
///
///   For each of ItemFieldCount item fields:
///     FieldID   LE int32 (AddData&lt;int&gt;, value 0-37)
///     Value:
///       FieldID in {0x01,0x0B,0x0D,0x1B} -> AddDataLSN: LE int16(strlen+1) + chars + NUL
///       FieldID in {0x00,0x09,0x0A,0x14,0x15,0x17,0x19,0x1A,0x22,0x24,0x25} -> LE float32
///       else -> LE int32
///
///   ActivatableEffects.Count  BE int32  (AddDataFlip4)
///   For each activatable effect:
///     Name        AddDataLS: LE int16(len) + chars, no NUL
///     Description AddDataLS
///     Tooltip     AddDataLS
///     Filler      LE int32 = 0
///     DescVarCount BE int32  (AddDataFlip4)
///     For each DescVar: 4B BE float (float bits stored as long, AddDataFlip4)
///     Flag1       LE int32
///     Flag2       LE int32
///   If Count &gt; 0: RechargeTime(LE int32) + 0(4B) + EffectRange(LE int32) + 0(4B)
///
///   EquipableEffects.Count  BE int32  (same layout as activatable)
///   For each equippable effect: [identical structure]
///   If Count &gt; 0: 4 * LE int32(0) = 16 bytes filler
///
///   GameBaseAsset  BE uint16  (AddDataFlip2)
///   IconBaseAsset  BE uint16
///   TechLevel      BE uint16
///   Cost           BE int32   (AddDataFlip4)
///   MaxStack       BE int32
///   UseEffect      BE int32
///   Flags          LE int32   (AddData&lt;int&gt;)
///   Name           AddDataLS
///   Description    AddDataLS
///   Manufacturer   AddDataLS
///
/// Verified byte-by-byte against capture_1-sector-s2c.bin frames 1-77.
/// </summary>
public sealed class ItemBaseRecord : PacketRecord
{
    public ItemBaseRecord(ReadOnlySpan<byte> payload) : base(0x0025, payload) { }

    protected override void WriteFields(StringBuilder sb)
    {
        if (Payload.Length < 17)
        {
            Flag(sb, $"ITEM_BASE truncated -- {Payload.Length} bytes, expected >= 17");
            return;
        }

        int  templateId  = ReadItemI32BE(Payload, 0);
        int  category    = ReadI32LE(Payload, 4);
        int  subCategory = ReadI32LE(Payload, 8);
        int  itemType    = ReadI32LE(Payload, 12);
        byte fieldCount  = Payload[16];

        FHex(sb, 0,  "ItemTemplateID", templateId, "(BE)");
        FDec(sb, 4,  "Category",       category,    CategoryName(category));
        FDec(sb, 8,  "SubCategory",    subCategory);
        FDec(sb, 12, "ItemType",       itemType);
        FDec(sb, 16, "FieldCount",     fieldCount);

        int off = 17;

        // -- Item fields --
        for (int i = 0; i < fieldCount && off + 4 <= Payload.Length; i++)
        {
            int fieldId = ReadI32LE(Payload, off);
            string? fieldName = FieldName(fieldId);
            FDec(sb, off, $"  Field[{i}].ID", fieldId, fieldName);
            off += 4;

            if (IsStringField(fieldId))
            {
                if (off + 2 > Payload.Length) { Flag(sb, $"truncated before Field[{i}] string len"); return; }
                short strLenWithNul = ReadI16LE(Payload, off);
                int   strLen        = Math.Max(0, strLenWithNul - 1);
                if (off + 2 + strLenWithNul > Payload.Length) { Flag(sb, $"truncated inside Field[{i}] string"); return; }
                string sv = ReadNulString(Payload.AsSpan(off + 2, strLen));
                FStr(sb, off, 2 + strLenWithNul, $"  Field[{i}].Value", sv);
                off += 2 + strLenWithNul;
            }
            else if (IsFloatField(fieldId))
            {
                if (off + 4 > Payload.Length) { Flag(sb, $"truncated before Field[{i}] float"); return; }
                float fv = ReadF32LE(Payload, off);
                FFloat(sb, off, $"  Field[{i}].Value", fv);
                off += 4;
            }
            else
            {
                if (off + 4 > Payload.Length) { Flag(sb, $"truncated before Field[{i}] int"); return; }
                int iv = ReadI32LE(Payload, off);
                FDec(sb, off, $"  Field[{i}].Value", iv);
                off += 4;
            }
        }

        // -- Activatable effects --
        if (off + 4 > Payload.Length) { Flag(sb, "truncated before ActivatableEffects.Count"); return; }
        int actCount = ReadItemI32BE(Payload, off);
        FDec(sb, off, "ActEffects.Count", actCount, "(BE)");
        off += 4;

        for (int i = 0; i < actCount; i++)
        {
            off = ReadEffect(sb, off, $"ActEffect[{i}]");
            if (off < 0) return;
        }
        if (actCount > 0)
        {
            if (off + 16 > Payload.Length) { Flag(sb, "truncated before activatable filler"); return; }
            FDec(sb,   off,    "ActEffects.RechargeTime", ReadI32LE(Payload, off));
            FDec(sb,   off+4,  "ActEffects.Filler",       ReadI32LE(Payload, off+4));
            FDec(sb,   off+8,  "ActEffects.EffectRange",  ReadI32LE(Payload, off+8));
            FDec(sb,   off+12, "ActEffects.Filler2",      ReadI32LE(Payload, off+12));
            off += 16;
        }

        // -- Equippable effects --
        if (off + 4 > Payload.Length) { Flag(sb, "truncated before EquipableEffects.Count"); return; }
        int eqCount = ReadItemI32BE(Payload, off);
        FDec(sb, off, "EqEffects.Count", eqCount, "(BE)");
        off += 4;

        for (int i = 0; i < eqCount; i++)
        {
            off = ReadEffect(sb, off, $"EqEffect[{i}]");
            if (off < 0) return;
        }
        if (eqCount > 0)
        {
            if (off + 16 > Payload.Length) { Flag(sb, "truncated before equippable filler"); return; }
            FDec(sb, off,    "EqEffects.Filler[0]", ReadI32LE(Payload, off));
            FDec(sb, off+4,  "EqEffects.Filler[1]", ReadI32LE(Payload, off+4));
            FDec(sb, off+8,  "EqEffects.Filler[2]", ReadI32LE(Payload, off+8));
            FDec(sb, off+12, "EqEffects.Filler[3]", ReadI32LE(Payload, off+12));
            off += 16;
        }

        // -- Fixed tail --
        if (off + 22 > Payload.Length) { Flag(sb, $"truncated before fixed tail (need 22B, have {Payload.Length - off}B)"); return; }
        ushort gameBase = ReadU16BE(Payload, off);
        ushort iconBase = ReadU16BE(Payload, off + 2);
        ushort techLvl  = ReadU16BE(Payload, off + 4);
        int    cost     = ReadItemI32BE(Payload, off + 6);
        int    maxStack = ReadItemI32BE(Payload, off + 10);
        int    useEff   = ReadItemI32BE(Payload, off + 14);
        int    flags    = ReadI32LE(Payload, off + 18);

        FHex(sb, off,      "GameBaseAsset", gameBase,  "(BE)");
        FHex(sb, off+2,    "IconBaseAsset", iconBase,  "(BE)");
        FDec(sb, off+4,    "TechLevel",     techLvl,   "(BE)");
        FDec(sb, off+6,    "Cost",          cost,       "(BE)");
        FDec(sb, off+10,   "MaxStack",      maxStack,   "(BE)");
        FHex(sb, off+14,   "UseEffect",     useEff,     "(BE)");
        FHex(sb, off+18,   "Flags",         flags,      FlagsText(flags));
        off += 22;

        // -- Trailing strings --
        off = ReadAddDataLS(sb, off, "Name",         required: true);
        if (off < 0) return;
        off = ReadAddDataLS(sb, off, "Description",  required: false);
        if (off < 0) return;
        ReadAddDataLS(sb, off, "Manufacturer", required: false);
    }

    // Returns new off, or -1 on truncation.
    private int ReadEffect(StringBuilder sb, int off, string prefix)
    {
        off = ReadAddDataLS(sb, off, $"{prefix}.Name");
        if (off < 0) return -1;
        off = ReadAddDataLS(sb, off, $"{prefix}.Description");
        if (off < 0) return -1;
        off = ReadAddDataLS(sb, off, $"{prefix}.Tooltip");
        if (off < 0) return -1;

        if (off + 4 > Payload.Length) { Flag(new StringBuilder(), $"truncated before {prefix}.Filler"); return -1; }
        FDec(sb, off, $"{prefix}.Filler", ReadI32LE(Payload, off));
        off += 4;

        if (off + 4 > Payload.Length) { Flag(new StringBuilder(), $"truncated before {prefix}.DescVarCount"); return -1; }
        int descVarCount = ReadItemI32BE(Payload, off);
        FDec(sb, off, $"{prefix}.DescVarCount", descVarCount, "(BE)");
        off += 4;

        for (int j = 0; j < descVarCount; j++)
        {
            if (off + 4 > Payload.Length) { Flag(new StringBuilder(), $"truncated before {prefix}.DescVar[{j}]"); return -1; }
            // DescVar is a float stored as long bits via AddDataFlip4 -- wire bytes are BE float bits.
            float fv = BinaryPrimitives.ReadSingleBigEndian(Payload.AsSpan(off, 4));
            FFloat(sb, off, $"{prefix}.DescVar[{j}]", fv);
            off += 4;
        }

        if (off + 8 > Payload.Length) { Flag(new StringBuilder(), $"truncated before {prefix}.Flag1/2"); return -1; }
        FHex(sb, off,   $"{prefix}.Flag1", ReadI32LE(Payload, off));
        FHex(sb, off+4, $"{prefix}.Flag2", ReadI32LE(Payload, off+4));
        off += 8;
        return off;
    }

    // AddDataLS decoder: LE int16 len + bytes. Returns new off or -1.
    //
    // The retail EA server's AddDataLS wrote `len` = count of STANDARD PRINTABLE ASCII
    // chars (0x20-0x7E), then memcpy'd ALL bytes including any embedded non-printable
    // formatting codes. Non-printable bytes (< 0x20 control chars like NUL, or >= 0x7F
    // extended/DEL) are consumed from the wire stream but do NOT count toward `len`.
    //
    // Examples from the retail capture:
    //   "Defle\xCEct Energy (Activated)" -- len=26 (26 printable), wire=27B (CE skipped)
    //   "Incre\x00ase Shield Capacity (Equip)" -- len=32 (32 printable), wire=33B (NUL skipped)
    //
    // Our server uses strlen (total byte count). For pure-ASCII DB strings this is identical.
    // Any item DB string with embedded format bytes would produce a divergence -- fix those
    // item DB entries rather than changing the server's AddDataLS.
    private int ReadAddDataLS(StringBuilder sb, int off, string name, bool required = false)
    {
        if (off + 2 > Payload.Length)
        {
            Flag(sb, $"truncated before {name} length");
            return -1;
        }
        short len = ReadI16LE(Payload, off);
        if (len < 0)
        {
            Flag(sb, $"negative length for {name} (len={len})");
            return -1;
        }

        // Read until `len` standard-printable chars consumed.
        // Bytes outside 0x20-0x7E (control chars, DEL, extended ASCII) are
        // formatting codes: consume from the wire but don't count toward len.
        int dataOff   = off + 2;
        int charsLeft = len;
        int byteIdx   = 0;
        var chars     = new System.Text.StringBuilder(len + 4);
        while (charsLeft > 0 && dataOff + byteIdx < Payload.Length)
        {
            byte b = Payload[dataOff + byteIdx];
            byteIdx++;
            if (b >= 0x20 && b < 0x7F)
            {
                chars.Append((char)b);
                charsLeft--;
            }
            // else: format/control byte -- consume, not counted toward len
        }
        if (charsLeft > 0)
        {
            Flag(sb, $"truncated inside {name} (got {len - charsLeft}/{len} chars, {byteIdx} bytes consumed)");
            return -1;
        }

        string s = chars.ToString();
        FStr(sb, off, 2 + byteIdx, name, s, required);
        return off + 2 + byteIdx;
    }

    private static ushort ReadU16BE(ReadOnlySpan<byte> p, int off) =>
        BinaryPrimitives.ReadUInt16BigEndian(p.Slice(off, 2));

    private static int ReadItemI32BE(ReadOnlySpan<byte> p, int off) =>
        BinaryPrimitives.ReadInt32BigEndian(p.Slice(off, 4));

    // ---- lookup tables ----

    private static readonly HashSet<int> StringFields = new() { 0x01, 0x0B, 0x0D, 0x1B };
    private static readonly HashSet<int> FloatFields  = new() { 0x00, 0x09, 0x0A, 0x14, 0x15, 0x17, 0x19, 0x1A, 0x22, 0x24, 0x25 };

    private static bool IsStringField(int id) => StringFields.Contains(id);
    private static bool IsFloatField(int id)  => FloatFields.Contains(id);

    private static string? FieldName(int id) => id switch
    {
        0x00 => "????",
        0x01 => "Ammo Parent",
        0x02 => "Autofire",
        0x03 => "Profession Restriction",
        0x04 => "Combat Level Req",
        0x05 => "Weapon Damage",
        0x06 => "Weapon Damage Type",
        0x07 => "Effect Range",
        0x08 => "Effect Radius",
        0x09 => "Energy Usage",
        0x0A => "Energy Drain",
        0x0B => "Requires Level",
        0x0C => "Explore Level Req",
        0x0D => "Item Type (Subdesc)",
        0x0E => "Overall Level Req",
        0x0F => "Missile Maneuverability",
        0x10 => "Reactor Capacity",
        0x11 => "Lore Requirements",
        0x12 => "Race Requirement",
        0x13 => "Weapon Range",
        0x14 => "Reactor Recharge Rate",
        0x15 => "Weapon Reload",
        0x16 => "Rounds Per Shot",
        0x17 => "Shield Usage",
        0x18 => "Shield Capacity",
        0x19 => "Shield Drain",
        0x1A => "Shield Recharge Rate",
        0x1B => "String for 0x1C",
        0x1C => "Level Req (for 0x1B)",
        0x1D => "Engine Signature",
        0x1E => "????",
        0x1F => "Engine Speed",
        0x20 => "Trade Level Req",
        0x21 => "Engine Warp Speed",
        0x22 => "Engine Freewarp Drain",
        0x23 => "Terminal Override Flags",
        0x24 => "Terminal Override Skill+",
        0x25 => "Terminal Override Crit+",
        _    => null
    };

    private static string? CategoryName(int cat) => cat switch
    {
        10  => "(Weapon)",
        11  => "(Device)",
        12  => "(CoreItem)",
        13  => "(Consumable)",
        50  => "(ElectronicItem)",
        51  => "(ReactorComponent)",
        52  => "(FabricatedItem)",
        53  => "(WeaponComponent)",
        54  => "(AmmoComponent)",
        100 => "(Ammo)",
        110 => "(RewardItem)",
        _   => null
    };

    private static string? FlagsText(int flags)
    {
        if (flags == 0) return null;
        var parts = new List<string>();
        if ((flags & 1)   != 0) parts.Add("NO_TRADE");
        if ((flags & 2)   != 0) parts.Add("TEMPORARY");
        if ((flags & 4)   != 0) parts.Add("UNIQUE");
        if ((flags & 8)   != 0) parts.Add("NO_STORE");
        if ((flags & 16)  != 0) parts.Add("NO_DESTROY");
        if ((flags & 128) != 0) parts.Add("NO_MANUFACTURE");
        return "(" + string.Join(" | ", parts) + ")";
    }
}
