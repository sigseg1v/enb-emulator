// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Buffers.Binary;
using System.Text;
using N7.CliClient.Logging;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// Shared base for the per-opcode record classes.
///
/// Field annotation system: every annotated helper (F, FHex, FDec, FFloat,
/// FStr, FBytes) marks the corresponding payload bytes as "known" and prefixes
/// the field line with its byte offset so readers can cross-reference with the
/// hex dump. After WriteFields runs, DumpToString() calls WriteGaps() which
/// lists any unknown byte ranges sequentially, then WriteHexTail() which
/// colours each byte: green background = decoded, orange background = not yet
/// decoded.
///
/// Legacy helpers (Field, FieldHex, FieldDec, FieldFloat, FieldString) remain
/// for GenericRecord and error-path output that genuinely has no offset info.
/// </summary>
public abstract class PacketRecord : IPacketRecord
{
    protected byte[] Payload { get; }
    private readonly bool[] _known;

    public ushort Opcode { get; }
    public int PayloadLength => Payload.Length;

    protected PacketRecord(ushort opcode, ReadOnlySpan<byte> payload)
    {
        Opcode  = opcode;
        Payload = payload.ToArray();
        _known  = new bool[Payload.Length];
    }

    // ── public API ──────────────────────────────────────────────────────────

    public string DumpToString()
    {
        var sb = new StringBuilder(512);
        WriteFields(sb);
        WriteGaps(sb);
        WriteHexTail(sb);
        return sb.ToString();
    }

    public string HexOnlyDump()
    {
        var sb = new StringBuilder(512);
        WriteHexTail(sb);
        return sb.ToString();
    }

    protected abstract void WriteFields(StringBuilder sb);

    // ── byte annotation ─────────────────────────────────────────────────────

    /// <summary>Mark <paramref name="len"/> bytes at <paramref name="off"/> as decoded.</summary>
    protected void Mark(int off, int len)
    {
        for (int i = 0; i < len && off + i < _known.Length; i++)
            _known[off + i] = true;
    }

    // ── annotated field emitters (F* family) ────────────────────────────────
    // Each marks the byte range AND shows [OOOO] offset prefix in the output.

    protected void F(StringBuilder sb, int off, int len, string name, string value, string? note = null)
    {
        Mark(off, len);
        sb.Append("  ")
          .Append(Color(AnsiPalette.Gray, $"[{off:X4}]"))
          .Append(' ')
          .Append(name.PadRight(18))
          .Append("= ")
          .Append(Color(AnsiPalette.Green, value));
        if (note is not null)
            sb.Append("  ").Append(Color(AnsiPalette.Gray, note));
        sb.AppendLine();
    }

    protected void FHex(StringBuilder sb, int off, string name, int value, string? note = null)
        => F(sb, off, 4, name, $"0x{value:X8}  ({value})", note);

    protected void FHex(StringBuilder sb, int off, string name, uint value, string? note = null)
        => F(sb, off, 4, name, $"0x{value:X8}  ({value})", note);

    protected void FHex(StringBuilder sb, int off, string name, ushort value, string? note = null)
        => F(sb, off, 2, name, $"0x{value:X4}  ({value})", note);

    protected void FHex(StringBuilder sb, int off, string name, byte value, string? note = null)
        => F(sb, off, 1, name, $"0x{value:X2}  ({value})", note);

    protected void FDec(StringBuilder sb, int off, string name, int value, string? note = null)
        => F(sb, off, 4, name, value.ToString(System.Globalization.CultureInfo.InvariantCulture), note);

    protected void FDec(StringBuilder sb, int off, string name, short value, string? note = null)
        => F(sb, off, 2, name, value.ToString(System.Globalization.CultureInfo.InvariantCulture), note);

    protected void FDec(StringBuilder sb, int off, string name, byte value, string? note = null)
        => F(sb, off, 1, name, value.ToString(System.Globalization.CultureInfo.InvariantCulture), note);

    protected void FFloat(StringBuilder sb, int off, string name, float value, string? note = null)
    {
        if (float.IsNaN(value))           Flag(sb, name + " is NaN");
        else if (float.IsInfinity(value)) Flag(sb, name + " is +-Infinity");
        F(sb, off, 4, name, value.ToString("0.0##", System.Globalization.CultureInfo.InvariantCulture), note);
    }

    protected void FStr(StringBuilder sb, int off, int len, string name, string value, bool required = false)
    {
        F(sb, off, len, name, Quote(value));
        if (required && string.IsNullOrEmpty(value))
            Flag(sb, $"{name} is EMPTY -- expected non-empty string");
    }

    /// <summary>Mark and label a group of bytes (e.g. Position, Orientation) as a unit.</summary>
    protected void FBytes(StringBuilder sb, int off, int len, string name, string display, string? note = null)
        => F(sb, off, len, name, display, note);

    // ── legacy non-annotating helpers (no offset; bytes stay "unknown" colour) ──

    protected void Field(StringBuilder sb, string name, string value, string? note = null)
    {
        sb.Append("  ")
          .Append(name.PadRight(20))
          .Append("= ")
          .Append(Color(AnsiPalette.Green, value));
        if (note is not null)
            sb.Append("  ").Append(Color(AnsiPalette.Gray, note));
        sb.AppendLine();
    }

    protected void FieldHex(StringBuilder sb, string name, int value, string? note = null)
        => Field(sb, name, $"0x{value:X8}  ({value})", note);

    protected void FieldHex(StringBuilder sb, string name, uint value, string? note = null)
        => Field(sb, name, $"0x{value:X8}  ({value})", note);

    protected void FieldHex(StringBuilder sb, string name, ushort value, string? note = null)
        => Field(sb, name, $"0x{value:X4}  ({value})", note);

    protected void FieldDec(StringBuilder sb, string name, int value, string? note = null)
        => Field(sb, name, value.ToString(System.Globalization.CultureInfo.InvariantCulture), note);

    protected void FieldFloat(StringBuilder sb, string name, float value)
    {
        if (float.IsNaN(value))           Flag(sb, name + " is NaN");
        else if (float.IsInfinity(value)) Flag(sb, name + " is +-Infinity");
        Field(sb, name, value.ToString("0.0##", System.Globalization.CultureInfo.InvariantCulture));
    }

    protected void FieldString(StringBuilder sb, string name, string value, bool requiredNonEmpty = false)
    {
        Field(sb, name, Quote(value));
        if (requiredNonEmpty && string.IsNullOrEmpty(value))
            Flag(sb, $"{name} is EMPTY -- expected non-empty string");
    }

    protected void Flag(StringBuilder sb, string message)
    {
        sb.Append("  ")
          .Append(Color(AnsiPalette.BrightRed + AnsiPalette.Bold, "[!] " + message))
          .AppendLine();
    }

    protected void FlagSuspicious(StringBuilder sb, string field, int v)
    {
        if (v == 0)                 sb.Append("  ").AppendLine(Color(AnsiPalette.Yellow, $"[!] {field} == 0 (likely uninitialised)"));
        else if (v == -1)           sb.Append("  ").AppendLine(Color(AnsiPalette.Yellow, $"[!] {field} == -1 (sentinel)"));
        else if (v == int.MinValue) sb.Append("  ").AppendLine(Color(AnsiPalette.Yellow, $"[!] {field} == INT32_MIN"));
    }

    // ── payload-scan helpers ─────────────────────────────────────────────────

    protected static int ReadI32LE(ReadOnlySpan<byte> p, int off) =>
        BinaryPrimitives.ReadInt32LittleEndian(p.Slice(off, 4));

    protected static uint ReadU32LE(ReadOnlySpan<byte> p, int off) =>
        BinaryPrimitives.ReadUInt32LittleEndian(p.Slice(off, 4));

    protected static int ReadI32BE(ReadOnlySpan<byte> p, int off) =>
        BinaryPrimitives.ReadInt32BigEndian(p.Slice(off, 4));

    protected static short ReadI16LE(ReadOnlySpan<byte> p, int off) =>
        BinaryPrimitives.ReadInt16LittleEndian(p.Slice(off, 2));

    protected static ushort ReadU16LE(ReadOnlySpan<byte> p, int off) =>
        BinaryPrimitives.ReadUInt16LittleEndian(p.Slice(off, 2));

    protected static float ReadF32LE(ReadOnlySpan<byte> p, int off) =>
        BinaryPrimitives.ReadSingleLittleEndian(p.Slice(off, 4));

    protected static string ReadNulString(ReadOnlySpan<byte> span)
    {
        int nul = span.IndexOf((byte)0);
        if (nul < 0) nul = span.Length;
        return Encoding.ASCII.GetString(span[..nul]);
    }

    /// <summary>
    /// Reads one server-side AddDataLS string at <paramref name="off"/>: a
    /// uint16 little-endian length followed by that many raw bytes (NO NUL
    /// terminator -- see server/src/PacketMethods.h AddDataLS). Marks the
    /// length + body as decoded, advances <paramref name="off"/>, and emits the
    /// field. Returns false (with a Flag) if either part runs past the payload.
    /// </summary>
    protected bool TryReadAddDataLS(StringBuilder sb, ref int off, string name)
    {
        if (off + 2 > Payload.Length)
        {
            Flag(sb, $"{name}: truncated -- offset {off}, only {Payload.Length - off} bytes remain (need 2 for length)");
            return false;
        }
        ushort len = ReadU16LE(Payload, off);
        Mark(off, 2);
        off += 2;
        if (off + len > Payload.Length)
        {
            Flag(sb, $"{name}: truncated -- need {len} bytes of string data at offset {off}, only {Payload.Length - off} remain");
            return false;
        }
        string value = Encoding.Latin1.GetString(Payload, off, len);
        FStr(sb, off, len, name, value);
        off += len;
        return true;
    }

    /// <summary>
    /// Reads one server-side AddDataSN string at <paramref name="off"/>: raw
    /// bytes up to and including a NUL terminator (see PacketMethods.h
    /// AddDataSN). Marks the string + NUL as decoded, advances
    /// <paramref name="off"/> past the NUL, and emits the field. Returns false
    /// (with a Flag) if no NUL is found before the payload ends.
    /// </summary>
    protected bool TryReadCString(StringBuilder sb, ref int off, string name)
    {
        if (off >= Payload.Length)
        {
            Flag(sb, $"{name}: truncated -- offset {off} past end of {Payload.Length}-byte payload");
            return false;
        }
        int nul = Array.IndexOf(Payload, (byte)0, off);
        if (nul < 0)
        {
            Flag(sb, $"{name}: unterminated -- no NUL from offset {off} to end of payload");
            return false;
        }
        int strLen = nul - off;
        string value = Encoding.Latin1.GetString(Payload, off, strLen);
        FStr(sb, off, strLen + 1, name, value);   // include the NUL in the marked span
        off = nul + 1;
        return true;
    }

    protected static (int offset, string? value) FindFirstAsciiString(
        ReadOnlySpan<byte> p, int minLen)
    {
        int i = 0;
        while (i < p.Length)
        {
            if (IsPrintable(p[i]))
            {
                int j = i;
                while (j < p.Length && IsPrintable(p[j])) j++;
                if (j - i >= minLen)
                    return (i, Encoding.ASCII.GetString(p[i..j]));
                i = j + 1;
            }
            else i++;
        }
        return (-1, null);
    }

    protected static List<(int offset, string value)> ExtractAsciiStrings(
        ReadOnlySpan<byte> p, int minLen)
    {
        var result = new List<(int, string)>();
        int i = 0;
        while (i < p.Length)
        {
            if (IsPrintable(p[i]))
            {
                int j = i;
                while (j < p.Length && IsPrintable(p[j])) j++;
                if (j - i >= minLen)
                    result.Add((i, Encoding.ASCII.GetString(p[i..j])));
                i = j + 1;
            }
            else i++;
        }
        return result;
    }

    // Overload accepting byte[] for GenericRecord
    protected static List<(int offset, string value)> ExtractAsciiStrings(
        byte[] p, int minLen) => ExtractAsciiStrings(p.AsSpan(), minLen);

    private static bool IsPrintable(byte b) => b >= 0x20 && b < 0x7F;

    protected static string Quote(string s) =>
        "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    protected static string Color(string code, string text) =>
        AnsiPalette.Colorize(code, text);

    // ── gap reporter ─────────────────────────────────────────────────────────

    private void WriteGaps(StringBuilder sb)
    {
        int i = 0;
        while (i < _known.Length)
        {
            if (!_known[i])
            {
                int start = i;
                while (i < _known.Length && !_known[i]) i++;
                int gapLen = i - start;
                // Show up to 16 bytes of the gap inline
                int showLen = Math.Min(gapLen, 16);
                var hexBytes = string.Join(" ",
                    Payload.Skip(start).Take(showLen).Select(b => b.ToString("X2")));
                string more = gapLen > showLen ? $"  +{gapLen - showLen} more" : "";
                sb.Append("  ")
                  .Append(Color(AnsiPalette.Gray, $"[{start:X4}]"))
                  .Append(' ')
                  .Append("???".PadRight(18))
                  .Append("  ")
                  .Append(Color(AnsiPalette.Yellow, $"({gapLen}B)  {hexBytes}{more}"))
                  .AppendLine();
            }
            else i++;
        }
    }

    // ── hex + ASCII gutter dump (per-byte background coloured) ───────────────

    private void WriteHexTail(StringBuilder sb)
    {
        const int Stride = 16;
        ReadOnlySpan<byte> p = Payload;
        int rows = (p.Length + Stride - 1) / Stride;
        for (int row = 0; row < rows; row++)
        {
            int off = row * Stride;
            int n   = Math.Min(Stride, p.Length - off);

            sb.Append("  ").Append(Color(AnsiPalette.Gray, $"{off:X4}  "));

            for (int i = 0; i < Stride; i++)
            {
                if (i < n)
                {
                    int abs  = off + i;
                    bool k   = abs < _known.Length && _known[abs];
                    string bg = k ? AnsiPalette.KnownBg : AnsiPalette.UnknownBg;
                    sb.Append(Color(bg, $"{p[abs]:X2}")).Append(' ');
                }
                else
                {
                    sb.Append("   ");
                }
                if (i == 7) sb.Append(' ');
            }

            sb.Append(' ');

            for (int i = 0; i < n; i++)
            {
                int abs  = off + i;
                bool k   = abs < _known.Length && _known[abs];
                string bg = k ? AnsiPalette.KnownBg : AnsiPalette.UnknownBg;
                char c   = p[abs] >= 0x20 && p[abs] < 0x7F ? (char)p[abs] : '.';
                sb.Append(Color(bg, c.ToString()));
            }

            sb.AppendLine();
        }
    }

    // ── public factory ───────────────────────────────────────────────────────

    public static IPacketRecord Resolve(ushort opcode, ReadOnlySpan<byte> payload) =>
        PacketRecordRegistry.Resolve(opcode, payload);
}
