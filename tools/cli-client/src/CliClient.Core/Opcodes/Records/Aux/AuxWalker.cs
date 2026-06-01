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
/// Walks a schema against a payload, producing per-field annotations and a
/// byte-exact consumption count. Bit rule (AuxBase.cpp CheckAuxBit): a field
/// with flagNum N is present iff bit (N+4) is set in the flag bitmap, i.e.
/// byte (N+4)/8, mask 1 &lt;&lt; ((N+4)%8).
/// </summary>
public sealed class AuxWalker
{
    private readonly byte[] _p;
    public List<AuxAnno> Annos { get; } = new();

    public AuxWalker(ReadOnlySpan<byte> payload) { _p = payload.ToArray(); }

    /// <summary>
    /// Walk <paramref name="schema"/> from offset 0. Returns the number of bytes
    /// consumed, or -1 on any structural failure (overrun, bad string length).
    /// </summary>
    public int Walk(AuxSchema schema)
    {
        Annos.Clear();
        int off = 0;
        return WalkStruct(schema, ref off, 0) ? off : -1;
    }

    private static bool Bit(ReadOnlySpan<byte> flags, int flagNum)
    {
        int idx = (flagNum + 4) / 8, bit = (flagNum + 4) % 8;
        return idx < flags.Length && (flags[idx] & (1 << bit)) != 0;
    }

    private bool WalkStruct(AuxSchema s, ref int off, int depth, string? labelOverride = null)
    {
        if (s.HasHeader)
        {
            if (off + 7 > _p.Length) return false;
            uint gameId = BinaryPrimitives.ReadUInt32LittleEndian(_p.AsSpan(off, 4));
            Annos.Add(new(off, 4, depth, $"{s.Name}.GameID", $"0x{gameId:X8}")); off += 4;
            ushort bodyLen = BinaryPrimitives.ReadUInt16LittleEndian(_p.AsSpan(off, 2));
            Annos.Add(new(off, 2, depth, "BodyLen", $"{bodyLen}  (= payload-6: {_p.Length - 6})")); off += 2;
            byte ver = _p[off];
            Annos.Add(new(off, 1, depth, "Version", ver.ToString(CultureInfo.InvariantCulture))); off += 1;
        }

        if (off + s.FlagBytes > _p.Length) return false;
        string flagsHex = Convert.ToHexString(_p.AsSpan(off, s.FlagBytes)).ToLowerInvariant();
        Annos.Add(new(off, s.FlagBytes, depth, (labelOverride ?? s.Name) + ".Flags", flagsHex));
        var flags = _p.AsSpan(off, s.FlagBytes);
        off += s.FlagBytes;

        if (s.IsContainer)
        {
            for (int i = 0; i < s.ContainerCount; i++)
            {
                if (!Bit(flags, i)) continue;
                if (!WalkStruct(s.ContainerElement!, ref off, depth + 1, $"[{i}]")) return false;
            }
            return true;
        }

        foreach (var f in s.Fields)
        {
            if (!Bit(flags, f.FlagNum)) continue;
            if (!ConsumeField(f, ref off, depth)) return false;
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
            case AuxKind.Str:
                if (off + 2 > _p.Length) return false;
                { int len = BinaryPrimitives.ReadUInt16LittleEndian(_p.AsSpan(off, 2));
                  if (off + 2 + len > _p.Length) return false;
                  string sv = Encoding.ASCII.GetString(_p, off + 2, len);
                  Annos.Add(new(off, 2 + len, depth, f.Name, "\"" + sv + "\"")); off += 2 + len; }
                return true;
            case AuxKind.Nested:
                return WalkStruct(f.Schema!, ref off, depth + 1, f.Name);
            default:
                return false;
        }
    }
}
