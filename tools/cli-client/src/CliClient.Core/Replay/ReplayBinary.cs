// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Text;

namespace N7.CliClient.Replay;

/// <summary>
/// Reader for the ENBREPLAY binary format produced by
/// <c>tools/capture-extract/</c>. The same format is consumed by the
/// proxy's PROXY_S2C_REPLAY substitution layer; the CLI re-uses it here
/// to compare live-server emissions against the recorded retail stream
/// frame-by-frame.
/// </summary>
/// <remarks>
/// On-disk layout (little-endian):
/// <code>
/// magic       "ENBREPLAY" (9 bytes ASCII)
/// version     u8 = 1
/// meta_len    u32
/// meta        meta_len bytes ASCII "key=value\n" lines
/// frame_count u32
/// frames      [opcode:u16, payload_len:u32, payload bytes] * frame_count
/// </code>
/// </remarks>
public sealed record ReplayFrame(ushort Opcode, byte[] Payload);

public sealed class ReplayFile
{
    private const string ExpectedMagic = "ENBREPLAY";

    public IReadOnlyDictionary<string, string> Metadata { get; }
    public IReadOnlyList<ReplayFrame> Frames { get; }

    private ReplayFile(IReadOnlyDictionary<string, string> meta, IReadOnlyList<ReplayFrame> frames)
    {
        Metadata = meta;
        Frames = frames;
    }

    public static ReplayFile Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!File.Exists(path))
            throw new FileNotFoundException($"replay file not found: {path}", path);

        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs, Encoding.ASCII, leaveOpen: false);

        var magicBytes = br.ReadBytes(ExpectedMagic.Length);
        string magic = Encoding.ASCII.GetString(magicBytes);
        if (magic != ExpectedMagic)
            throw new InvalidDataException(
                $"not an ENBREPLAY file (magic={Quote(magic)}, expected={Quote(ExpectedMagic)})");

        byte version = br.ReadByte();
        if (version != 1)
            throw new InvalidDataException(
                $"unsupported replay version {version}; only v1 is recognised");

        uint metaLen = br.ReadUInt32();
        if (metaLen > 1 << 20)
            throw new InvalidDataException($"meta_len={metaLen} looks unreasonable; refusing to read");
        byte[] metaBytes = br.ReadBytes((int)metaLen);
        var meta = ParseMeta(Encoding.ASCII.GetString(metaBytes));

        uint frameCount = br.ReadUInt32();
        if (frameCount > 1 << 20)
            throw new InvalidDataException($"frame_count={frameCount} looks unreasonable; refusing to read");

        var frames = new List<ReplayFrame>((int)frameCount);
        for (uint i = 0; i < frameCount; i++)
        {
            ushort opcode = br.ReadUInt16();
            uint payloadLen = br.ReadUInt32();
            if (payloadLen > 1 << 20)
                throw new InvalidDataException(
                    $"frame {i}: payload_len={payloadLen} looks unreasonable");
            byte[] payload = br.ReadBytes((int)payloadLen);
            if (payload.Length != payloadLen)
                throw new EndOfStreamException(
                    $"frame {i}: truncated payload (wanted {payloadLen}, got {payload.Length})");
            frames.Add(new ReplayFrame(opcode, payload));
        }

        return new ReplayFile(meta, frames);
    }

    private static Dictionary<string, string> ParseMeta(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rawLine in text.Split('\n'))
        {
            string line = rawLine.Trim('\r', ' ', '\t');
            if (string.IsNullOrEmpty(line)) continue;
            int eq = line.IndexOf('=');
            if (eq < 0) continue;
            result[line[..eq]] = line[(eq + 1)..];
        }
        return result;
    }

    private static string Quote(string s) => "\"" + s + "\"";
}
