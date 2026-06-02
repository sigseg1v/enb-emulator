// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace N7.CliClient.Opcodes.Records.Aux;

/// <summary>
/// Dedicated decoder for the two AuxHulkIndex serialisations carried inside
/// opcode 0x001B: the create body (server/src/AuxClasses/AuxHulkIndex.cpp,
/// <c>TwoBitFlags == true</c>) sent when a destroyed object's husk/corpse spawns,
/// and the diff body (<c>TwoBitFlags == false</c>) sent when that husk's contents
/// change. The id slot names the husk; for the 2026-06-02 captures every frame is
/// gid 0x0001887D ("Corpse of Crystalline Gamma").
///
/// Like AuxMobIndex this does NOT follow the generic "present at bit N+4" AuxBase
/// layout that <see cref="AuxWalker"/> models, so it cannot be an
/// <see cref="AuxSchema"/> candidate:
///
///  * Create writes a 3-byte ExtendedFlags block; diff writes a 2-byte Flags
///    block. Each nested member is gated on a bit of the EMITTED block, and an
///    ABSENT member in create mode is replaced by a single 0x05 marker gated on a
///    DIFFERENT (cross-byte) bit -- exactly the AuxMobIndex pattern.
///  * The two nested item containers are AuxInventory20 (equip, 20 slots) and
///    AuxInventory40 (cargo, 40 slots). In create they serialise extended
///    (per-slot present bit at i+4, deleted/0x05 bit at i+count+4, each item a
///    3-flag-byte AuxItem); in diff they serialise plain (per-slot bit at i+4,
///    each item a 2-flag-byte AuxItem). Flag-block sizes are the fixed
///    sizeof(ExtendedFlags)/sizeof(Flags) from the headers, NOT recomputed.
///
/// IMPORTANT -- bodyLen is NOT a frame delimiter here. AuxHulkIndex.cpp writes
/// <c>bodyLen = index - 6</c>, but the live net-7.org server's value is
/// (payload - 8) for the inventory-bearing create frame and (payload - 6) for the
/// others (confirmed against net7-live-2026-06-02-full-workflow.pcap frames
/// 1195/1198/1269: bodyLen 37/532/19 vs payload 43/540/25). The inner-bundle
/// length is the authoritative boundary; this decoder walks fields to the framed
/// payload end and never gates on bodyLen.
///
/// Decode tries create first, then diff, and keeps whichever consumes the payload
/// byte-exact with plausible strings. On no exact fit it reports -1 and the caller
/// falls through to the generic partial/divergence path.
/// </summary>
public sealed class AuxHulkIndexDecoder
{
    private readonly byte[] _p;
    private bool _ok = true;
    public List<AuxAnno> Annos { get; } = new();
    public string Variant { get; private set; } = "";

    private long _strBytes, _strPrintable;
    public double StringPlausibility => _strBytes == 0 ? 1.0 : (double)_strPrintable / _strBytes;

    public AuxHulkIndexDecoder(ReadOnlySpan<byte> payload) => _p = payload.ToArray();

    /// <summary>
    /// Decode as create, else diff; on the first byte-exact consume set
    /// <see cref="Annos"/>/<see cref="Variant"/> and return the length consumed
    /// (== payload length). Returns -1 when neither flavour fits exactly.
    /// </summary>
    public int TryDecode()
    {
        int c = Run(WalkCreate);
        if (c == _p.Length) { Variant = "create"; return c; }

        int k = Run(WalkDiff);
        if (k == _p.Length) { Variant = "diff"; return k; }

        return -1;
    }

    private int Run(System.Func<int> walk)
    {
        Annos.Clear(); _ok = true; _strBytes = _strPrintable = 0;
        int o = walk();
        return _ok ? o : -1;
    }

    // ── shared header (gid + bodyLen + version) ──────────────────────────────
    private int Header(int flagBytes)
    {
        uint gameId = BinaryPrimitives.ReadUInt32LittleEndian(_p.AsSpan(0, 4));
        Annos.Add(new(0, 4, 0, "HulkIndex.GameID", $"0x{gameId:X8}"));
        ushort bodyLen = BinaryPrimitives.ReadUInt16LittleEndian(_p.AsSpan(4, 2));
        string blNote = bodyLen == _p.Length - 6 ? "(payload-6)"
            : bodyLen == _p.Length - 8 ? "(payload-8, inventory-bearing)"
            : bodyLen == _p.Length ? "(payload)" : $"(payload={_p.Length})";
        Annos.Add(new(4, 2, 0, "BodyLen", $"{bodyLen}  {blNote}"));
        Annos.Add(new(6, 1, 0, "Version", _p[6].ToString(CultureInfo.InvariantCulture)));
        string flagsHex = Convert.ToHexString(_p.AsSpan(7, flagBytes)).ToLowerInvariant();
        Annos.Add(new(7, flagBytes, 0, "HulkIndex.Flags", flagsHex));
        return 7 + flagBytes;
    }

    // ── BuildPacket(TwoBitFlags = true): 3 ExtendedFlags bytes ───────────────
    private int WalkCreate()
    {
        if (_p.Length < 10 || _p[6] != 1) { _ok = false; return 0; }
        int o = Header(3);
        byte f0 = _p[7], f1 = _p[8], f2 = _p[9];

        // Flags[0] & 0x02 is the required "this member is present" sentinel; a
        // body without it returns false in the server, so it can't be a hulk.
        if ((f0 & 0x02) == 0) { _ok = false; return 0; }

        if ((f0 & 0x10) != 0) Str(ref o, "Name");
        if ((f0 & 0x20) != 0) Str(ref o, "Owner");   // no absent-marker for Owner

        // QuadrantDamage / the three Damage decals: present-branch emits the
        // member (decals emit 0 bytes -- AuxDamage::Build*Packet are no-ops),
        // absent-branch emits a single 0x05 gated on the next flag byte's bit.
        if      ((f0 & 0x40) != 0) QuadrantDamageExt(ref o, "QuadrantDamage");
        else if ((f1 & 0x40) != 0) Marker(ref o, "QuadrantDamage");
        if      ((f0 & 0x80) != 0) Empty(o, "DamageSpot");
        else if ((f1 & 0x80) != 0) Marker(ref o, "DamageSpot");
        if      ((f1 & 0x01) != 0) Empty(o, "DamageLine");
        else if ((f2 & 0x01) != 0) Marker(ref o, "DamageLine");
        if      ((f1 & 0x02) != 0) Empty(o, "DamageBlotch");
        else if ((f2 & 0x02) != 0) Marker(ref o, "DamageBlotch");

        // EquipInv (AuxInventory20): ExtendedFlags[6], 20 slots.
        if      ((f1 & 0x04) != 0) InventoryExt(ref o, "EquipInv", 20, 6);
        else if ((f2 & 0x04) != 0) Marker(ref o, "EquipInv");
        // CargoInv (AuxInventory40): ExtendedFlags[11], 40 slots.
        if      ((f1 & 0x08) != 0) InventoryExt(ref o, "CargoInv", 40, 11);
        else if ((f2 & 0x08) != 0) Marker(ref o, "CargoInv");

        return o;
    }

    // ── BuildPacket(TwoBitFlags = false): 2 Flags bytes, no markers ──────────
    private int WalkDiff()
    {
        if (_p.Length < 9 || _p[6] != 1) { _ok = false; return 0; }
        int o = Header(2);
        byte f0 = _p[7], f1 = _p[8];

        if ((f0 & 0x02) == 0) { _ok = false; return 0; }

        if ((f0 & 0x10) != 0) Str(ref o, "Name");
        if ((f0 & 0x20) != 0) Str(ref o, "Owner");

        // QuadrantDamage non-extended has never appeared in a hulk diff in the
        // captures; fail rather than guess its plain-flag wire form.
        if ((f0 & 0x40) != 0) { _ok = false; return o; }
        if ((f0 & 0x80) != 0) Empty(o, "DamageSpot");
        if ((f1 & 0x01) != 0) Empty(o, "DamageLine");
        if ((f1 & 0x02) != 0) Empty(o, "DamageBlotch");

        // Plain inventories: Flags[3] (equip) / Flags[6] (cargo), 2-flag-byte items.
        if ((f1 & 0x04) != 0) InventoryPlain(ref o, "EquipInv", 20, 3);
        if ((f1 & 0x08) != 0) InventoryPlain(ref o, "CargoInv", 40, 6);

        return o;
    }

    // ── nested inventories ───────────────────────────────────────────────────
    // Extended: per-slot present bit at (i+4), deleted/0x05 bit at (i+count+4).
    private void InventoryExt(ref int o, string name, int count, int flagLen)
    {
        if (!Bounds(o, flagLen)) return;
        ReadOnlySpan<byte> flags = _p.AsSpan(o, flagLen);
        int flagOff = o;
        int present = 0, deleted = 0;
        o += flagLen;
        for (int i = 0; i < count; i++)
        {
            if (Bit(flags, i)) { present++; ItemExt(ref o, $"{name}[{i}]"); }
            else if (Bit(flags, count + i)) { deleted++; Marker(ref o, $"{name}[{i}]"); }
            if (!_ok) return;
        }
        Annos.Add(new(flagOff, flagLen, 1, name,
            $"{Convert.ToHexString(flags).ToLowerInvariant()}  ({count} slots: {present} present, {deleted} deleted)"));
    }

    // Plain: per-slot present bit at (i+4); no deleted markers, 2-flag-byte items.
    private void InventoryPlain(ref int o, string name, int count, int flagLen)
    {
        if (!Bounds(o, flagLen)) return;
        ReadOnlySpan<byte> flags = _p.AsSpan(o, flagLen);
        int flagOff = o;
        int present = 0;
        o += flagLen;
        for (int i = 0; i < count; i++)
        {
            if (Bit(flags, i)) { present++; ItemPlain(ref o, $"{name}[{i}]"); }
            if (!_ok) return;
        }
        Annos.Add(new(flagOff, flagLen, 1, name,
            $"{Convert.ToHexString(flags).ToLowerInvariant()}  ({count} slots: {present} present)"));
    }

    // CheckAuxBit: bit (n+4) of the flag block (AuxBase::CheckAuxBit).
    private static bool Bit(ReadOnlySpan<byte> flags, int n)
    {
        int idx = (n + 4) / 8, bit = (n + 4) % 8;
        return idx < flags.Length && (flags[idx] & (1 << bit)) != 0;
    }

    // ── AuxItem (server/src/AuxClasses/AuxItem.cpp) ──────────────────────────
    // Extended = 3 flag bytes, plain = 2; field gates are identical (the plain
    // path reads ExtendedFlags-equivalent bits off Flags[0]/Flags[1]).
    private void ItemExt(ref int o, string name) => Item(ref o, name, 3);
    private void ItemPlain(ref int o, string name) => Item(ref o, name, 2);

    private void Item(ref int o, string name, int flagLen)
    {
        if (!Bounds(o, flagLen)) return;
        byte g0 = _p[o], g1 = _p[o + 1];
        int start = o;
        o += flagLen;

        int? itemId = null;
        bool anyExtra = false;
        var fields = new List<AuxAnno>();

        if ((g0 & 0x10) != 0) { itemId = S32At(o); fields.Add(new(o, 4, 2, name + ".ItemTemplateID", $"{itemId}")); o += 4; }
        if ((g0 & 0x20) != 0) { anyExtra = true; U32(ref o, name + ".StackCount", fields); }
        if ((g0 & 0x40) != 0) { anyExtra = true; U64(ref o, name + ".Price", fields); }
        if ((g0 & 0x80) != 0) { anyExtra = true; F32(ref o, name + ".AveCost", fields); }
        if ((g1 & 0x01) != 0) { anyExtra = true; F32(ref o, name + ".Structure", fields); }
        if ((g1 & 0x02) != 0) { anyExtra = true; F32(ref o, name + ".Quality", fields); }
        if ((g1 & 0x04) != 0) { anyExtra = true; Str(ref o, name + ".InstanceInfo", fields, 2); }
        if ((g1 & 0x08) != 0) { anyExtra = true; Str(ref o, name + ".ActivatedEEII", fields, 2); }
        if ((g1 & 0x10) != 0) { anyExtra = true; Str(ref o, name + ".EquipEEII", fields, 2); }
        if ((g1 & 0x20) != 0) { anyExtra = true; Str(ref o, name + ".BuilderName", fields, 2); }
        if (!_ok) return;

        // An invisible/empty slot (only ItemTemplateID, == -1 or -2) is the bulk
        // of a corpse inventory -- collapse it to one line; show real items in full.
        if (!anyExtra && itemId is -1 or -2)
            Annos.Add(new(start, o - start, 1, name, $"(empty, id={itemId})"));
        else
        {
            Annos.Add(new(start, flagLen, 1, name,
                Convert.ToHexString(_p.AsSpan(start, flagLen)).ToLowerInvariant()));
            Annos.AddRange(fields);
        }
    }

    // AuxQuadrantDamage extended: flag block (2 bytes) + up to 4 ints gated on f0.
    // Same class/wire form AuxMobIndexDecoder models; absent in every captured
    // hulk frame, kept so a future hull-damaged husk still decodes.
    private void QuadrantDamageExt(ref int o, string name)
    {
        if (!Bounds(o, 2)) return;
        byte f0 = _p[o];
        Annos.Add(new(o, 2, 1, name + ".Flags", Convert.ToHexString(_p.AsSpan(o, 2)).ToLowerInvariant()));
        o += 2;
        foreach (var (m, lbl) in new[] { (0x10, "Slot1"), (0x20, "Slot2"), (0x40, "Slot3"), (0x80, "Slot4") })
            if ((f0 & m) != 0) U32(ref o, $"{name}.{lbl}", null, 2);
    }

    // ── primitive readers ────────────────────────────────────────────────────
    private bool Bounds(int o, int n)
    {
        if (o + n > _p.Length) { _ok = false; return false; }
        return true;
    }

    private void Str(ref int o, string name, List<AuxAnno>? sink = null, int depth = 0)
    {
        if (!Bounds(o, 2)) return;
        int len = BinaryPrimitives.ReadUInt16LittleEndian(_p.AsSpan(o, 2));
        if (!Bounds(o + 2, len)) return;
        for (int k = 0; k < len; k++) { byte b = _p[o + 2 + k]; _strBytes++; if (b >= 0x20 && b < 0x7F) _strPrintable++; }
        string sv = Encoding.ASCII.GetString(_p, o + 2, len);
        (sink ?? Annos).Add(new(o, 2 + len, depth, name, "\"" + sv + "\""));
        o += 2 + len;
    }

    private void U32(ref int o, string name, List<AuxAnno>? sink = null, int depth = 0)
    {
        if (!Bounds(o, 4)) return;
        uint v = BinaryPrimitives.ReadUInt32LittleEndian(_p.AsSpan(o, 4));
        (sink ?? Annos).Add(new(o, 4, depth, name, $"{v}  (0x{v:X8})"));
        o += 4;
    }

    private void U64(ref int o, string name, List<AuxAnno>? sink = null, int depth = 0)
    {
        if (!Bounds(o, 8)) return;
        ulong v = BinaryPrimitives.ReadUInt64LittleEndian(_p.AsSpan(o, 8));
        (sink ?? Annos).Add(new(o, 8, depth, name, $"{v}"));
        o += 8;
    }

    private void F32(ref int o, string name, List<AuxAnno>? sink = null, int depth = 0)
    {
        if (!Bounds(o, 4)) return;
        float v = BinaryPrimitives.ReadSingleLittleEndian(_p.AsSpan(o, 4));
        (sink ?? Annos).Add(new(o, 4, depth, name, v.ToString("0.0##", CultureInfo.InvariantCulture)));
        o += 4;
    }

    private int S32At(int o) => BinaryPrimitives.ReadInt32LittleEndian(_p.AsSpan(o, 4));

    // present-but-zero-byte nested member (AuxDamage::Build*Packet emit nothing).
    private void Empty(int o, string name) => Annos.Add(new(o, 0, 1, name, "(present, 0 bytes)"));

    // absent nested member: a single 0x05 deletion marker.
    private void Marker(ref int o, string name)
    {
        if (!Bounds(o, 1)) return;
        if (_p[o] != 0x05) { _ok = false; return; }
        Annos.Add(new(o, 1, 1, name, "(deleted: 0x05)"));
        o += 1;
    }
}
