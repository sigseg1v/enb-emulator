// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace N7.CliClient.Opcodes.Records.Aux;

/// <summary>One decoded field, ready to be emitted by the record's annotator.</summary>
public readonly record struct AuxAnno(int Off, int Len, int Depth, string Name, string Value);

/// <summary>
/// Schema-driven decoder for AuxBase-serialised structures (opcode 0x001B).
/// Walks a schema (plain BuildPacket or extended BuildExtendedPacket form)
/// against a payload, producing per-field annotations and a byte-exact
/// consumption count. Bit rule (AuxBase.cpp CheckAuxBit): a field with flagNum
/// N is present iff bit (N+4) is set in the flag bitmap; in extended form its
/// "deleted" bit is (N+4+FlagCount) and an absent-deleted nested field is a
/// single 0x05 byte.
/// </summary>
public sealed class AuxWalker
{
    private readonly byte[] _p;
    private readonly bool   _extended;
    public List<AuxAnno> Annos { get; } = new();

    // string-plausibility tracking, to reject coincidental wrong-schema fits
    private long _strBytes;
    private long _strPrintable;
    public double StringPlausibility => _strBytes == 0 ? 1.0 : (double)_strPrintable / _strBytes;

    /// <summary>Offset reached when a walk failed (for diagnostics).</summary>
    public int FailOffset { get; private set; }
    public string FailWhere { get; private set; } = "";

    public AuxWalker(ReadOnlySpan<byte> payload, bool extended)
    {
        _p = payload.ToArray(); _extended = extended;
    }

    /// <summary>
    /// Walk <paramref name="schema"/> from offset 0. Returns bytes consumed, or
    /// -1 on any structural failure (overrun, bad string length, bad marker).
    /// </summary>
    public int Walk(AuxSchema schema)
    {
        Annos.Clear(); _strBytes = _strPrintable = 0;
        int off = 0;
        return WalkStruct(schema, ref off, 0) ? off : -1;
    }

    private static bool Bit(ReadOnlySpan<byte> flags, int pos)
    {
        int idx = pos / 8, bit = pos % 8;
        return idx < flags.Length && (flags[idx] & (1 << bit)) != 0;
    }

    private bool Fail(int off, string where) { FailOffset = off; FailWhere = where; return false; }

    private bool WalkStruct(AuxSchema s, ref int off, int depth, string? labelOverride = null)
    {
        if (s.HasHeader)
        {
            if (off + 7 > _p.Length) return Fail(off, $"{s.Name}.header");
            uint gameId = BinaryPrimitives.ReadUInt32LittleEndian(_p.AsSpan(off, 4));
            Annos.Add(new(off, 4, depth, $"{s.Name}.GameID", $"0x{gameId:X8}")); off += 4;
            ushort bodyLen = BinaryPrimitives.ReadUInt16LittleEndian(_p.AsSpan(off, 2));
            string blNote = bodyLen == _p.Length - 6 ? "(payload-6)" : bodyLen == _p.Length ? "(payload)" : $"(payload={_p.Length})";
            Annos.Add(new(off, 2, depth, "BodyLen", $"{bodyLen}  {blNote}")); off += 2;
            byte ver = _p[off];
            Annos.Add(new(off, 1, depth, "Version", ver.ToString(CultureInfo.InvariantCulture))); off += 1;
        }

        int flagBytes = _extended ? s.ExtFlagBytes : s.PlainFlagBytes;
        if (off + flagBytes > _p.Length) return Fail(off, $"{s.Name}.flags");
        string flagsHex = Convert.ToHexString(_p.AsSpan(off, flagBytes)).ToLowerInvariant();
        Annos.Add(new(off, flagBytes, depth, (labelOverride ?? s.Name) + ".Flags", flagsHex));
        var flags = _p.AsSpan(off, flagBytes);
        off += flagBytes;

        int count = s.IsContainer ? s.ContainerCount : s.Fields.Count;
        for (int i = 0; i < count; i++)
        {
            AuxField? field = s.IsContainer ? null : s.Fields[i];
            int flagNum = s.IsContainer ? i : field!.FlagNum;
            bool isNested = s.IsContainer || field!.Kind is AuxKind.Nested or AuxKind.Empty;
            // Extended-suppressed nested field: full-serialise branch is commented
            // out server-side, so its present-bit is ignored in extended mode.
            bool suppressed = _extended && !s.IsContainer && field!.ExtSuppressed;
            bool present = !suppressed && Bit(flags, flagNum + 4);

            if (present)
            {
                if (s.IsContainer)
                {
                    if (!WalkStruct(s.ContainerElement!, ref off, depth + 1, $"[{i}]")) return false;
                }
                else if (!ConsumeField(field!, ref off, depth)) { if (FailWhere == "") Fail(off, field!.Name); return false; }
            }
            else if (_extended && isNested && off < _p.Length && _p[off] == 0x05)
            {
                // Absent nested field/slot: BuildExtendedPacket emits a single
                // 0x05 deletion marker for it. The per-field "deleted" flag bit
                // is hand-coded in the server and not consistently derivable, so
                // detect the marker by its unambiguous 0x05 sentinel instead.
                string nm = s.IsContainer ? $"[{i}]" : field!.Name;
                Annos.Add(new(off, 1, depth, nm, "(deleted: 0x05)")); off += 1;
            }
        }
        return true;
    }

    private bool ConsumeField(AuxField f, ref int off, int depth)
    {
        switch (f.Kind)
        {
            case AuxKind.U8:
                if (off + 1 > _p.Length) return false;
                Annos.Add(new(off, 1, depth, f.Name, $"{_p[off]}  (0x{_p[off]:X2})")); off += 1; return true;
            case AuxKind.Bool:
                if (off + 1 > _p.Length) return false;
                Annos.Add(new(off, 1, depth, f.Name, _p[off] != 0 ? "true" : "false")); off += 1; return true;
            case AuxKind.U16:
                if (off + 2 > _p.Length) return false;
                { ushort v = BinaryPrimitives.ReadUInt16LittleEndian(_p.AsSpan(off, 2));
                  Annos.Add(new(off, 2, depth, f.Name, $"{v}  (0x{v:X4})")); } off += 2; return true;
            case AuxKind.U32:
                if (off + 4 > _p.Length) return false;
                { uint v = BinaryPrimitives.ReadUInt32LittleEndian(_p.AsSpan(off, 4));
                  Annos.Add(new(off, 4, depth, f.Name, $"{v}  (0x{v:X8})")); } off += 4; return true;
            case AuxKind.S32:
                if (off + 4 > _p.Length) return false;
                { int v = BinaryPrimitives.ReadInt32LittleEndian(_p.AsSpan(off, 4));
                  Annos.Add(new(off, 4, depth, f.Name, v.ToString(CultureInfo.InvariantCulture))); } off += 4; return true;
            case AuxKind.U64:
                if (off + 8 > _p.Length) return false;
                { ulong v = BinaryPrimitives.ReadUInt64LittleEndian(_p.AsSpan(off, 8));
                  Annos.Add(new(off, 8, depth, f.Name, $"{v}")); } off += 8; return true;
            case AuxKind.F32:
                if (off + 4 > _p.Length) return false;
                { float v = BinaryPrimitives.ReadSingleLittleEndian(_p.AsSpan(off, 4));
                  Annos.Add(new(off, 4, depth, f.Name, v.ToString("0.0##", CultureInfo.InvariantCulture))); } off += 4; return true;
            case AuxKind.F64:
                if (off + 8 > _p.Length) return false;
                { double v = BinaryPrimitives.ReadDoubleLittleEndian(_p.AsSpan(off, 8));
                  Annos.Add(new(off, 8, depth, f.Name, v.ToString("0.0##", CultureInfo.InvariantCulture))); } off += 8; return true;
            case AuxKind.Avail4:
                if (off + 16 > _p.Length) return false;
                { var parts = new string[4];
                  for (int k = 0; k < 4; k++) parts[k] = $"0x{BinaryPrimitives.ReadUInt32LittleEndian(_p.AsSpan(off + 4 * k, 4)):X8}";
                  Annos.Add(new(off, 16, depth, f.Name, "[" + string.Join(", ", parts) + "]")); } off += 16; return true;
            case AuxKind.Float3:
                if (off + 12 > _p.Length) return false;
                { var parts = new string[3];
                  for (int k = 0; k < 3; k++) parts[k] = BinaryPrimitives.ReadSingleLittleEndian(_p.AsSpan(off + 4 * k, 4)).ToString("0.0##", CultureInfo.InvariantCulture);
                  Annos.Add(new(off, 12, depth, f.Name, "(" + string.Join(", ", parts) + ")")); } off += 12; return true;
            case AuxKind.Mount3:
                if (off + 12 > _p.Length) return false;
                { uint m = BinaryPrimitives.ReadUInt32LittleEndian(_p.AsSpan(off, 4));
                  int a = BinaryPrimitives.ReadInt32LittleEndian(_p.AsSpan(off + 4, 4));
                  int b = BinaryPrimitives.ReadInt32LittleEndian(_p.AsSpan(off + 8, 4));
                  Annos.Add(new(off, 12, depth, f.Name, $"mount=0x{m:X8} ({a}, {b})")); } off += 12; return true;
            case AuxKind.Str:
                if (off + 2 > _p.Length) return false;
                { int len = BinaryPrimitives.ReadUInt16LittleEndian(_p.AsSpan(off, 2));
                  if (off + 2 + len > _p.Length) return false;
                  for (int k = 0; k < len; k++) { byte b = _p[off + 2 + k]; _strBytes++; if (b >= 0x20 && b < 0x7F) _strPrintable++; }
                  string sv = Encoding.ASCII.GetString(_p, off + 2, len);
                  Annos.Add(new(off, 2 + len, depth, f.Name, "\"" + sv + "\"")); off += 2 + len; }
                return true;
            case AuxKind.Nested:
                return WalkStruct(f.Schema!, ref off, depth + 1, f.Name);
            case AuxKind.Empty:
                Annos.Add(new(off, 0, depth, f.Name, "(present, 0 bytes)")); return true;
            default:
                return false;
        }
    }
}
