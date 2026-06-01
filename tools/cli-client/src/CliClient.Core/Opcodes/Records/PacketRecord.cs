// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using System.Text;
using N7.CliClient.Logging;

namespace N7.CliClient.Opcodes.Records;

/// <summary>
/// Shared base for the per-opcode record classes. Provides:
/// <list type="bullet">
///   <item>the opcode + payload length plumbing</item>
///   <item>colour helpers tied to <see cref="AnsiPalette"/></item>
///   <item>field formatters (<see cref="Field"/>, <see cref="Flag"/>,
///         <see cref="FlagSuspicious"/>, etc.)</item>
///   <item>a generic 16-bytes-per-row hex+ASCII gutter dump</item>
///   <item>an ASCII-string scanner used by records that carry variable
///         names/descriptions past a fixed-size header</item>
/// </list>
/// </summary>
public abstract class PacketRecord : IPacketRecord
{
    protected byte[] Payload { get; }

    public ushort Opcode { get; }
    public int PayloadLength => Payload.Length;

    protected PacketRecord(ushort opcode, ReadOnlySpan<byte> payload)
    {
        Opcode = opcode;
        Payload = payload.ToArray();
    }

    /// <inheritdoc/>
    public string DumpToString()
    {
        var sb = new StringBuilder(512);
        WriteFields(sb);
        WriteHexTail(sb);
        return sb.ToString();
    }

    /// <summary>
    /// Hex+ASCII gutter only, no structured fields. Used by the live
    /// REPL tail for opcodes we don't have a structured decoder for --
    /// printing field heuristics for unknown opcodes is more noise than
    /// signal, so we just show every byte and let the operator spot the
    /// nonsense.
    /// </summary>
    public string HexOnlyDump()
    {
        var sb = new StringBuilder(512);
        WriteHexTail(sb);
        return sb.ToString();
    }

    /// <summary>
    /// Per-opcode field decoder -- subclasses override and append decoded
    /// fields with <see cref="Field"/> / <see cref="FlagSuspicious"/> / etc.
    /// </summary>
    protected abstract void WriteFields(StringBuilder sb);

    // ---------------------------- formatting helpers ----------------------------

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

    // ---------------------------- payload-scan helpers ----------------------------

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

    private static bool IsPrintable(byte b) => b >= 0x20 && b < 0x7F;

    protected static string Quote(string s) =>
        "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    protected static string Color(string code, string text) =>
        AnsiPalette.Colorize(code, text);

    // ---------------------------- hex tail ----------------------------

    private void WriteHexTail(StringBuilder sb)
    {
        const int Stride = 16;
        ReadOnlySpan<byte> p = Payload;
        int rows = (p.Length + Stride - 1) / Stride;
        for (int row = 0; row < rows; row++)
        {
            int off = row * Stride;
            int n = Math.Min(Stride, p.Length - off);

            sb.Append("  ")
              .Append(Color(AnsiPalette.Gray, $"{off:X4}  "));

            for (int i = 0; i < Stride; i++)
            {
                if (i < n) sb.Append($"{p[off + i]:X2} ");
                else       sb.Append("   ");
                if (i == 7) sb.Append(' ');
            }
            sb.Append(' ')
              .Append(Color(AnsiPalette.Blue, AsciiGutter(p.Slice(off, n))))
              .AppendLine();
        }
    }

    private static string AsciiGutter(ReadOnlySpan<byte> bytes)
    {
        var chars = new char[bytes.Length];
        for (int i = 0; i < bytes.Length; i++)
            chars[i] = bytes[i] >= 0x20 && bytes[i] < 0x7F ? (char)bytes[i] : '.';
        return new string(chars);
    }

    // ---------------------------- public factory ----------------------------

    /// <summary>
    /// Resolve an opcode + payload to the matching record type. Always
    /// returns a non-null record -- unrecognised opcodes get the
    /// <see cref="GenericRecord"/> fallback which still produces a
    /// useful hex+ASCII dump and heuristic field hints.
    /// </summary>
    public static IPacketRecord Resolve(ushort opcode, ReadOnlySpan<byte> payload) =>
        PacketRecordRegistry.Resolve(opcode, payload);
}
