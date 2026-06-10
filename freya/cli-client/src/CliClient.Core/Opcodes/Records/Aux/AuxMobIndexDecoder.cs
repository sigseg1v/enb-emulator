// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace N7.CliClient.Opcodes.Records.Aux;

/// <summary>
/// Dedicated decoder for the two AuxMobIndex serialisations carried inside
/// opcode 0x001B: BuildCreatePacket (full NPC/creature spawn) and
/// BuildClickPacket (the trimmed body sent when a player clicks/targets one).
///
/// These do NOT follow the generic "present at bit N+4" AuxBase layout that
/// <see cref="AuxWalker"/> models, so they cannot be expressed as an
/// <see cref="AuxSchema"/>. AuxMobIndex hand-rolls a fixed 15-byte flag block
/// (server/src/AuxClasses/AuxMobIndex.cpp): each emitted flag byte is an
/// in-memory ExtendedFlags byte AND-masked with a per-byte constant, and every
/// conditional field is gated by testing a bit of the EMITTED block
/// (<c>buffer[N] &amp; mask</c>) rather than a contiguous bitmap. Absent nested
/// members (QuadrantDamage, the three Damage decals, Lego) are replaced by a
/// single 0x05 marker whose presence is itself gated on bits of buffer[19]
/// (create) / buffer[15] (click).
///
/// The embedded Shield (an AuxPercent) is decoded in the form the live
/// net-7.org server emits and the retail client accepts: a single flag byte
/// followed by EndTime(u32) + ChangePerTick(f32) + StartValue(f32) = 13 bytes.
/// Our own tada-o AuxPercent fork emits a different 2-flag-byte block; that
/// SERVER-side divergence is tracked separately (plans/11-phase-k-ingame.md) --
/// this decoder targets the captured wire bytes, which are the primary-source
/// ground truth (frame #273 "Craxel" et al. in the 2026-06-02 net7 captures).
///
/// Decode tries create first, then click, and keeps whichever consumes the
/// payload byte-exact with plausible strings. On no exact fit it reports -1 and
/// the caller falls through to the generic partial/divergence path.
/// </summary>
public sealed class AuxMobIndexDecoder
{
    private readonly byte[] _p;
    private bool _ok = true;
    public List<AuxAnno> Annos { get; } = new();
    public string Variant { get; private set; } = "";

    private long _strBytes, _strPrintable;
    public double StringPlausibility => _strBytes == 0 ? 1.0 : (double)_strPrintable / _strBytes;

    public AuxMobIndexDecoder(ReadOnlySpan<byte> payload) => _p = payload.ToArray();

    /// <summary>
    /// Decode as create, else click; on the first byte-exact consume set
    /// <see cref="Annos"/>/<see cref="Variant"/> and return the length consumed
    /// (== payload length). Returns -1 when neither flavour fits exactly.
    /// </summary>
    public int TryDecode()
    {
        int c = Run(WalkCreate);
        if (c == _p.Length) { Variant = "create"; return c; }

        int k = Run(WalkClick);
        if (k == _p.Length) { Variant = "click"; return k; }

        return -1;
    }

    private int Run(System.Func<int> walk)
    {
        Annos.Clear(); _ok = true; _strBytes = _strPrintable = 0;
        int o = walk();
        return _ok ? o : -1;
    }

    // ── shared header + 15-byte flag block ───────────────────────────────────
    private int Header()
    {
        uint gameId  = BinaryPrimitives.ReadUInt32LittleEndian(_p.AsSpan(0, 4));
        Annos.Add(new(0, 4, 0, "MobIndex.GameID", $"0x{gameId:X8}"));
        ushort bodyLen = BinaryPrimitives.ReadUInt16LittleEndian(_p.AsSpan(4, 2));
        string blNote = bodyLen == _p.Length - 6 ? "(payload-6)" : bodyLen == _p.Length ? "(payload)" : $"(payload={_p.Length})";
        Annos.Add(new(4, 2, 0, "BodyLen", $"{bodyLen}  {blNote}"));
        Annos.Add(new(6, 1, 0, "Version", _p[6].ToString(CultureInfo.InvariantCulture)));
        string flagsHex = Convert.ToHexString(_p.AsSpan(7, 15)).ToLowerInvariant();
        Annos.Add(new(7, 15, 0, "MobIndex.Flags", flagsHex));
        return 22;
    }

    private bool Enough(int len) => _ok && len >= 22 && _p.Length >= 22 && _p[6] == 1;

    // ── BuildCreatePacket ────────────────────────────────────────────────────
    private int WalkCreate()
    {
        if (!Enough(_p.Length)) { _ok = false; return 0; }
        int o = Header();
        int B(int i) => _p[i];

        if ((B(7) & 0x10) != 0) Str(ref o, "Name");
        if ((B(7) & 0x20) != 0) Str(ref o, "Owner");
        if ((B(7) & 0x40) != 0) Str(ref o, "Title");
        if ((B(7) & 0x80) != 0) Str(ref o, "Rank");

        if ((B(8) & 0x04) != 0) Shield(ref o, "Shield");
        if ((B(8) & 0x08) != 0) F32(ref o, "MaxShield");
        if ((B(8) & 0x10) != 0) F32(ref o, "HullPoints");
        if ((B(8) & 0x20) != 0) F32(ref o, "MaxHullPoints");

        if ((B(9) & 0x80) != 0) Bool(ref o, "IsCloaked");

        if ((B(10) & 0x01) != 0) Bool(ref o, "IsCountermeasureActive");
        if ((B(10) & 0x02) != 0) Bool(ref o, "IsIncapacitated");
        if ((B(10) & 0x04) != 0) Bool(ref o, "IsOrganic");
        if ((B(10) & 0x08) != 0) Bool(ref o, "IsInPVP");
        if ((B(10) & 0x20) != 0) Bool(ref o, "IsRescueBeaconActive");
        if ((B(10) & 0x40) != 0) U32(ref o, "CombatLevel");

        if ((B(11) & 0x10) != 0) U32(ref o, "GlobalWarpState");

        // QuadrantDamage / the three Damage decals / Lego: present-branch emits
        // the member, absent-branch emits a single 0x05 iff its buffer[19] bit.
        if      ((B(12) & 0x02) != 0) QuadrantDamage(ref o, "QuadrantDamage");
        else if ((B(19) & 0x08) != 0) Marker(ref o, "QuadrantDamage");
        if      ((B(12) & 0x04) != 0) Empty(o, "DamageSpot");
        else if ((B(19) & 0x10) != 0) Marker(ref o, "DamageSpot");
        if      ((B(12) & 0x08) != 0) Empty(o, "DamageLine");
        else if ((B(19) & 0x20) != 0) Marker(ref o, "DamageLine");
        if      ((B(12) & 0x10) != 0) Empty(o, "DamageBlotch");
        else if ((B(19) & 0x40) != 0) Marker(ref o, "DamageBlotch");
        if      ((B(12) & 0x20) != 0) Lego(ref o, "Lego");
        else if ((B(19) & 0x80) != 0) Marker(ref o, "Lego");

        if ((B(13) & 0x04) != 0) U32(ref o, "EngineThrustState");
        if ((B(13) & 0x08) != 0) U32(ref o, "EngineTrailType");

        return o;
    }

    // ── BuildClickPacket ─────────────────────────────────────────────────────
    private int WalkClick()
    {
        if (!Enough(_p.Length)) { _ok = false; return 0; }
        // First flag byte is the literal char(0x06) BuildClickPacket emits; a
        // create frame never has exactly that byte at [7] (its gates are the
        // 0x10/0x20/0x40/0x80 name bits), so this both labels and guards.
        if (_p[7] != 0x06) { _ok = false; return 0; }
        int o = Header();
        int B(int i) => _p[i];

        if      ((B(8) & 0x04) != 0) Shield(ref o, "Shield");
        else if ((B(15) & 0x10) != 0) Marker(ref o, "Shield");
        if ((B(8) & 0x08) != 0) F32(ref o, "MaxShield");
        if ((B(8) & 0x10) != 0) F32(ref o, "HullPoints");

        if ((B(14) & 0x02) != 0) Str(ref o, "InterruptAbilityName");
        if ((B(14) & 0x04) != 0) U32(ref o, "InterruptState");
        if ((B(14) & 0x08) != 0) U32(ref o, "InterruptActivationTime");
        if ((B(14) & 0x10) != 0) F32(ref o, "InterruptProgress");
        if ((B(14) & 0x20) != 0) Str(ref o, "FactionIdentifier");

        return o;
    }

    // ── embedded AuxPercent (captured net-7.org wire form) ───────────────────
    // 1 flag byte + EndTime(u32) + ChangePerTick(f32) + StartValue(f32) = 13B.
    // Every Shield observed in the 2026-06-02 captures has flag 0x03 with all
    // three values present; the values are decoded unconditionally to match.
    private void Shield(ref int o, string name)
    {
        if (!Bounds(o, 13)) return;
        Annos.Add(new(o, 1, 1, name + ".Flags", $"0x{_p[o]:X2}"));
        U32At(o + 1, 1, name + ".EndTime");
        F32At(o + 5, 1, name + ".ChangePerTick");
        F32At(o + 9, 1, name + ".StartValue");
        o += 13;
    }

    // AuxQuadrantDamage extended: 2 flag bytes + up to 4 floats gated on f0.
    private void QuadrantDamage(ref int o, string name)
    {
        if (!Bounds(o, 2)) return;
        byte f0 = _p[o];
        Annos.Add(new(o, 2, 1, name + ".Flags", Convert.ToHexString(_p.AsSpan(o, 2)).ToLowerInvariant()));
        o += 2;
        foreach (var (m, lbl) in new[] { (0x10, "FrontQuad"), (0x20, "RearQuad"), (0x40, "LeftQuad"), (0x80, "RightQuad") })
            if ((f0 & m) != 0) F32(ref o, $"{name}.{lbl}", 1);
    }

    // AuxLego extended: 1 flag + Scale(0x10 f32) + Attachments(0x20, unmodelled)
    // + a 0x05 marker (0x80). Attachments has never appeared in a MobIndex
    // create frame in the captures; if one does, fail rather than guess.
    private void Lego(ref int o, string name)
    {
        if (!Bounds(o, 1)) return;
        byte f0 = _p[o];
        Annos.Add(new(o, 1, 1, name + ".Flags", $"0x{f0:X2}"));
        o += 1;
        if ((f0 & 0x10) != 0) F32(ref o, name + ".Scale", 1);
        if ((f0 & 0x20) != 0) { _ok = false; return; }   // Attachments: unmodelled
        if ((f0 & 0x80) != 0) Marker(ref o, name);
    }

    // ── primitive readers ────────────────────────────────────────────────────
    private bool Bounds(int o, int n)
    {
        if (o + n > _p.Length) { _ok = false; return false; }
        return true;
    }

    private void Str(ref int o, string name, int depth = 0)
    {
        if (!Bounds(o, 2)) return;
        int len = BinaryPrimitives.ReadUInt16LittleEndian(_p.AsSpan(o, 2));
        if (!Bounds(o + 2, len)) return;
        for (int k = 0; k < len; k++) { byte b = _p[o + 2 + k]; _strBytes++; if (b >= 0x20 && b < 0x7F) _strPrintable++; }
        string sv = Encoding.ASCII.GetString(_p, o + 2, len);
        Annos.Add(new(o, 2 + len, depth, name, "\"" + sv + "\""));
        o += 2 + len;
    }

    private void Bool(ref int o, string name, int depth = 0)
    {
        if (!Bounds(o, 1)) return;
        Annos.Add(new(o, 1, depth, name, _p[o] != 0 ? "true" : "false"));
        o += 1;
    }

    private void U32(ref int o, string name, int depth = 0) { U32At(o, depth, name); o += _ok ? 4 : 0; }
    private void U32At(int o, int depth, string name)
    {
        if (!Bounds(o, 4)) return;
        uint v = BinaryPrimitives.ReadUInt32LittleEndian(_p.AsSpan(o, 4));
        Annos.Add(new(o, 4, depth, name, $"{v}  (0x{v:X8})"));
    }

    private void F32(ref int o, string name, int depth = 0) { F32At(o, depth, name); o += _ok ? 4 : 0; }
    private void F32At(int o, int depth, string name)
    {
        if (!Bounds(o, 4)) return;
        float v = BinaryPrimitives.ReadSingleLittleEndian(_p.AsSpan(o, 4));
        Annos.Add(new(o, 4, depth, name, v.ToString("0.0##", CultureInfo.InvariantCulture)));
    }

    // present-but-zero-byte nested member (AuxDamage::BuildExtendedPacket emits
    // nothing); consumes no payload.
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
