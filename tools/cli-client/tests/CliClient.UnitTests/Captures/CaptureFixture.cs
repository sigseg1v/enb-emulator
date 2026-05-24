// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

using System.Globalization;
using System.Reflection;

namespace N7.CliClient.UnitTests.Captures;

/// <summary>
/// Loader for the hand-curated retail-capture fixture files under
/// <c>Captures/fixtures/</c>. Each fixture file holds one or more named
/// records ("[name]" section header) with key-value lines and a
/// <c>hex:</c> block of whitespace-separated bytes. See
/// <c>capture3-frames.txt</c> for the format.
/// </summary>
/// <remarks>
/// We deliberately keep the format dead-simple and text-based so the
/// fixtures diff cleanly, survive code review, and don't require an
/// extracted RAR to be present in CI. The original RARs stay in
/// <c>archive/kyp-snapshot/capturedPackets/</c> as ground truth.
/// </remarks>
public sealed class CaptureFixture
{
    public string Name { get; }
    public string Provenance { get; }
    public int Opcode { get; }
    public byte[] Payload { get; }

    private CaptureFixture(string name, string provenance, int opcode, byte[] payload)
    {
        Name = name;
        Provenance = provenance;
        Opcode = opcode;
        Payload = payload;
    }

    public static IReadOnlyDictionary<string, CaptureFixture> Load(string fixtureFileName)
    {
        string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        string path = Path.Combine(dir, "Captures", "fixtures", fixtureFileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Capture fixture not found at '{path}'. Did the .csproj copy step run?",
                path);
        }

        var result = new Dictionary<string, CaptureFixture>(StringComparer.Ordinal);

        string? name = null;
        string? provenance = null;
        int? opcode = null;
        int? declaredLen = null;
        var hex = new List<byte>();
        bool inHex = false;

        void Flush()
        {
            if (name is null) return;
            if (provenance is null || opcode is null || declaredLen is null)
                throw new InvalidDataException(
                    $"Capture fixture '{name}' is missing required key (provenance/opcode/payload-bytes).");
            if (hex.Count != declaredLen.Value)
                throw new InvalidDataException(
                    $"Capture fixture '{name}' declares {declaredLen} payload bytes but supplies {hex.Count}.");
            result[name] = new CaptureFixture(name, provenance, opcode.Value, hex.ToArray());

            name = null;
            provenance = null;
            opcode = null;
            declaredLen = null;
            hex = new List<byte>();
            inHex = false;
        }

        foreach (string rawLine in File.ReadAllLines(path))
        {
            string line = StripComment(rawLine).Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                Flush();
                name = line[1..^1].Trim();
                continue;
            }

            int colon = line.IndexOf(':');
            if (colon > 0 && !inHex)
            {
                string key = line[..colon].Trim();
                string value = line[(colon + 1)..].Trim();
                switch (key)
                {
                    case "provenance":
                        provenance = value;
                        break;
                    case "opcode":
                        opcode = ParseHexOrDecInt(value);
                        break;
                    case "payload-bytes":
                        declaredLen = int.Parse(value, CultureInfo.InvariantCulture);
                        break;
                    case "hex":
                        inHex = true;
                        if (value.Length > 0) AppendHex(value, hex);
                        break;
                    default:
                        throw new InvalidDataException(
                            $"Unknown fixture key '{key}' in record '{name}'");
                }
            }
            else if (inHex)
            {
                AppendHex(line, hex);
            }
            else
            {
                throw new InvalidDataException(
                    $"Unparseable line outside any record: '{rawLine}'");
            }
        }
        Flush();

        return result;
    }

    private static string StripComment(string line)
    {
        int hash = line.IndexOf('#');
        return hash < 0 ? line : line[..hash];
    }

    private static int ParseHexOrDecInt(string s)
    {
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.Parse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return int.Parse(s, CultureInfo.InvariantCulture);
    }

    private static void AppendHex(string tokens, List<byte> sink)
    {
        foreach (string tok in tokens.Split(
                     (char[]?)null,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            sink.Add(byte.Parse(tok, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }
    }
}
