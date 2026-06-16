// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

// capture-extract: parse a kyp-snapshot capture text dump into a binary
// replay file consumable by the proxy's PROXY_S2C_REPLAY hook.
//
// Input format: the text-dump format under
//   archive/kyp-snapshot/capturedPackets/capture_*.rar
// Each packet is a header line
//   `Packet #N: M bytes, Server->Client  IP:PORT`
// followed by separator + annotated hex rows. Length+opcode rows look like
//   ` 10 00            Length = 16 bytes`
//   ` 34 00            Opcode 0x34 = Set_Client_Time`
// and payload rows are
//   ` DB D3 00 00 F8 3E 6F 25 F8 3E 6F 25               ....`>o%`>o%`
// (16 bytes per row, trailing ASCII). Pre-handshake frames (SYN1/ACK1/SYN2/
// ACK2/RC4-key-exchange) carry no opcode and are skipped: replay starts at
// the first opcode-bearing S->C frame on the target IP and ends after the
// stop opcode (default 0x0005 START -- sector handshake terminator).
//
// Output: a binary file the proxy can mmap-style load --
//   magic     "ENBREPLAY"    (9 bytes, ASCII)
//   version   1              (u8)
//   meta_len  N              (u32 LE)
//   meta      N bytes ASCII  ("key=value\n" lines)
//   frames    u32 LE
//   <repeat>  opcode u16 LE, payload_len u32 LE, payload bytes
//
// Stdout prints: per-opcode counts, metadata detected (avatar_id, names),
// and a sample SQL block sketching what to seed into Postgres so the live
// session's avatar/account match retail's bytes.

using System.Globalization;
using System.Text;

namespace N7.CaptureExtract;

internal static class Program
{
    private const string Magic = "ENBREPLAY";
    private const byte Version = 1;

    private static int Main(string[] args)
    {
        string? input = null;
        string? output = null;
        string ip = "159.153.232.46";
        ushort stopOpcode = 0x0005;
        bool printFrames = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--input": input = args[++i]; break;
                case "--output": output = args[++i]; break;
                case "--ip": ip = args[++i]; break;
                case "--stop-at": stopOpcode = ParseHexU16(args[++i]); break;
                case "--print-frames": printFrames = true; break;
                case "-h": case "--help": PrintUsage(); return 0;
                default:
                    Console.Error.WriteLine($"unknown arg: {args[i]}");
                    PrintUsage();
                    return 2;
            }
        }

        if (input is null || output is null)
        {
            PrintUsage();
            return 2;
        }

        if (!File.Exists(input))
        {
            Console.Error.WriteLine($"input not found: {input}");
            return 2;
        }

        var frames = Parse(input, ip, stopOpcode);
        if (frames.Count == 0)
        {
            Console.Error.WriteLine($"no frames matched ip={ip} (stop=0x{stopOpcode:X4})");
            return 1;
        }

        var meta = ExtractMetadata(frames, ip, stopOpcode, input);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        WriteBinary(output, meta, frames);

        PrintReport(output, frames, meta, printFrames);
        return 0;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine(
            "usage: capture-extract --input <capture.txt> --output <out.bin> " +
            "[--ip 159.153.232.46] [--stop-at 0x05] [--print-frames]");
    }

    private static ushort ParseHexU16(string s)
    {
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        return ushort.Parse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    // ---------- parsing ----------

    private sealed record Frame(int PacketNumber, ushort Opcode, byte[] Payload, string Port);

    private static List<Frame> Parse(string path, string targetIp, ushort stopOpcode)
    {
        var frames = new List<Frame>();
        var ipMatch = " " + targetIp + ":";
        Frame? cur = null;
        var curPayload = new List<byte>();
        bool inMatchingPacket = false;
        int packetNum = 0;
        string port = "";
        bool stop = false;

        // Process each line; the file is ISO-8859 with CRLF -- byte-clean read.
        foreach (var rawLine in File.ReadLines(path, Encoding.Latin1))
        {
            if (stop) break;
            var line = rawLine.TrimEnd('\r');

            if (line.StartsWith("Packet #", StringComparison.Ordinal))
            {
                FlushCurrent(frames, ref cur, curPayload);
                inMatchingPacket = false;

                // Format: "Packet #NNN: BBB bytes, DIR  IP:PORT"
                if (!line.Contains("Server->Client", StringComparison.Ordinal)) continue;
                int ipIdx = line.IndexOf(ipMatch, StringComparison.Ordinal);
                if (ipIdx < 0) continue;

                int hashIdx = line.IndexOf('#');
                int colonIdx = line.IndexOf(':', hashIdx);
                packetNum = int.Parse(
                    line.AsSpan(hashIdx + 1, colonIdx - hashIdx - 1),
                    CultureInfo.InvariantCulture);

                int portStart = ipIdx + ipMatch.Length;
                int portEnd = portStart;
                while (portEnd < line.Length && char.IsDigit(line[portEnd])) portEnd++;
                port = line.Substring(portStart, portEnd - portStart);

                inMatchingPacket = true;
                continue;
            }

            if (!inMatchingPacket) continue;

            // Length row: " HH HH            Length = NN bytes"
            // Opcode row: " HH HH            Opcode 0xNN = Name"
            // Payload row: hex bytes (no trailing annotation).
            int opcodeIdx = line.IndexOf("Opcode 0x", StringComparison.Ordinal);
            if (opcodeIdx >= 0)
            {
                // Flush any prior frame in this same multi-opcode packet.
                FlushCurrent(frames, ref cur, curPayload);

                int hexStart = opcodeIdx + "Opcode 0x".Length;
                int hexEnd = hexStart;
                while (hexEnd < line.Length && IsHexChar(line[hexEnd])) hexEnd++;
                ushort opcode = ushort.Parse(
                    line.AsSpan(hexStart, hexEnd - hexStart),
                    NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                cur = new Frame(packetNum, opcode, Array.Empty<byte>(), port);
                curPayload.Clear();
                continue;
            }

            if (line.Contains("Length = ", StringComparison.Ordinal))
            {
                // Length row is structural; skip (the next opcode row starts cur).
                continue;
            }

            if (cur is null) continue;

            // Payload row: leading space then hex pairs. Stop at the first
            // non-hex sequence (the ASCII annotation column).
            int byteCount = ReadHexBytesInto(line, curPayload);
            if (byteCount == 0)
            {
                // Either separator (`---`) or end-of-packet blank. Flush.
                FlushCurrent(frames, ref cur, curPayload);
                if (frames.Count > 0 && frames[^1].Opcode == stopOpcode) stop = true;
            }
        }

        FlushCurrent(frames, ref cur, curPayload);
        return frames;
    }

    private static void FlushCurrent(List<Frame> frames, ref Frame? cur, List<byte> payload)
    {
        if (cur is null) return;
        var done = cur with { Payload = payload.ToArray() };
        frames.Add(done);
        cur = null;
        payload.Clear();
    }

    private static bool IsHexChar(char c) =>
        (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');

    // Read hex byte pairs from a payload-row line. The row is hex-pair
    // columns left-justified with ASCII annotation on the right. We walk
    // forward consuming "HH " groups (uppercase or lowercase) until the
    // pattern breaks, which marks the boundary between hex and ASCII.
    private static int ReadHexBytesInto(string line, List<byte> sink)
    {
        int i = 0;
        // skip leading whitespace
        while (i < line.Length && line[i] == ' ') i++;
        if (i >= line.Length) return 0;

        int before = sink.Count;
        while (i + 1 < line.Length && IsHexChar(line[i]) && IsHexChar(line[i + 1]))
        {
            // Require the pair to be followed by a space or end-of-line --
            // otherwise it might be ASCII text that happens to look hex (e.g.
            // "ee" in "queen"). The dump format always pads with a space.
            if (i + 2 < line.Length && line[i + 2] != ' ') break;
            byte b = byte.Parse(line.AsSpan(i, 2), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture);
            sink.Add(b);
            i += 2;
            while (i < line.Length && line[i] == ' ') i++;
        }
        return sink.Count - before;
    }

    // ---------- metadata extraction ----------

    private sealed class Metadata
    {
        public uint? AvatarId;       // little-endian 4-byte avatar/player id
        public string? AvatarName;   // null-terminated ASCII string
        public string? ShipName;     // from 0xB2 NameDecal payload
        public ushort? StationId;    // from 0x004F Starbase_Set, if present
        public ushort? SectorId;     // 45151 for Friendship 7 (capture_1)
        public string? CaptureSource;
        public string? TargetIp;
        public ushort StopOpcode;
        public int FrameCount;
    }

    private static Metadata ExtractMetadata(
        List<Frame> frames, string ip, ushort stopOpcode, string sourcePath)
    {
        var meta = new Metadata
        {
            CaptureSource = Path.GetFileName(sourcePath),
            TargetIp = ip,
            StopOpcode = stopOpcode,
            FrameCount = frames.Count,
            SectorId = 45151, // capture_1 hardcoded -- override later if needed
        };

        foreach (var f in frames)
        {
            switch (f.Opcode)
            {
                // 0x37 Client_Avatar = 4-byte avatar id, LE.
                case 0x37 when meta.AvatarId is null && f.Payload.Length >= 4:
                    meta.AvatarId = BitConverter.ToUInt32(f.Payload, 0);
                    break;

                // 0x61 AvatarDescription = [avatar_id u32][first_name null-term ASCII]...
                case 0x61 when meta.AvatarName is null && f.Payload.Length > 4:
                    meta.AvatarName = ReadCString(f.Payload, 4, 32);
                    break;

                // 0xB2 NameDecal = [avatar_id u32][ship_name null-term ASCII]...
                case 0xB2 when meta.ShipName is null && f.Payload.Length > 4:
                    meta.ShipName = ReadCString(f.Payload, 4, 32);
                    break;

                // 0x4F Starbase_Set probably carries the station id near front;
                // leave the offset-0 read as a best-effort guess. User can
                // verify against the capture.
                case 0x4F when meta.StationId is null && f.Payload.Length >= 2:
                    meta.StationId = BitConverter.ToUInt16(f.Payload, 0);
                    break;
            }
        }

        return meta;
    }

    private static string ReadCString(byte[] buf, int offset, int maxLen)
    {
        int end = offset;
        int limit = Math.Min(buf.Length, offset + maxLen);
        while (end < limit && buf[end] != 0) end++;
        return Encoding.Latin1.GetString(buf, offset, end - offset);
    }

    // ---------- binary writer ----------

    private static void WriteBinary(string path, Metadata meta, List<Frame> frames)
    {
        var metaSb = new StringBuilder();
        if (meta.CaptureSource is not null) metaSb.AppendLine($"capture={meta.CaptureSource}");
        if (meta.TargetIp is not null) metaSb.AppendLine($"target_ip={meta.TargetIp}");
        metaSb.AppendLine($"stop_opcode=0x{meta.StopOpcode:X4}");
        metaSb.AppendLine($"frame_count={meta.FrameCount}");
        if (meta.AvatarId is uint a) metaSb.AppendLine($"avatar_id=0x{a:X8}");
        if (meta.AvatarName is not null) metaSb.AppendLine($"avatar_name={meta.AvatarName}");
        if (meta.ShipName is not null) metaSb.AppendLine($"ship_name={meta.ShipName}");
        if (meta.StationId is ushort st) metaSb.AppendLine($"station_id=0x{st:X4}");
        if (meta.SectorId is ushort sec) metaSb.AppendLine($"sector_id={sec}");
        var metaBytes = Encoding.ASCII.GetBytes(metaSb.ToString());

        using var fs = File.Create(path);
        fs.Write(Encoding.ASCII.GetBytes(Magic));
        fs.WriteByte(Version);
        WriteU32LE(fs, (uint)metaBytes.Length);
        fs.Write(metaBytes);
        WriteU32LE(fs, (uint)frames.Count);
        foreach (var f in frames)
        {
            WriteU16LE(fs, f.Opcode);
            WriteU32LE(fs, (uint)f.Payload.Length);
            fs.Write(f.Payload);
        }
    }

    private static void WriteU16LE(Stream s, ushort v)
    {
        s.WriteByte((byte)(v & 0xFF));
        s.WriteByte((byte)((v >> 8) & 0xFF));
    }

    private static void WriteU32LE(Stream s, uint v)
    {
        s.WriteByte((byte)(v & 0xFF));
        s.WriteByte((byte)((v >> 8) & 0xFF));
        s.WriteByte((byte)((v >> 16) & 0xFF));
        s.WriteByte((byte)((v >> 24) & 0xFF));
    }

    // ---------- report ----------

    private static void PrintReport(string outPath, List<Frame> frames, Metadata meta, bool printFrames)
    {
        Console.Out.WriteLine($"# capture-extract output: {outPath}");
        Console.Out.WriteLine();
        Console.Out.WriteLine("## Frame summary");
        Console.Out.WriteLine($"  frames    = {frames.Count}");
        Console.Out.WriteLine($"  stop_op   = 0x{meta.StopOpcode:X4}");
        Console.Out.WriteLine($"  ip filter = {meta.TargetIp}");
        Console.Out.WriteLine();

        Console.Out.WriteLine("## Opcode histogram (count desc, opcode asc)");
        var hist = frames.GroupBy(f => f.Opcode)
            .Select(g => (op: g.Key, n: g.Count()))
            .OrderByDescending(x => x.n).ThenBy(x => x.op);
        foreach (var (op, n) in hist)
            Console.Out.WriteLine($"  {n,4}x 0x{op:X4}");
        Console.Out.WriteLine();

        Console.Out.WriteLine("## Retail identity detected (proxy rewrites these to your live session)");
        Console.Out.WriteLine($"  avatar_id   = {FmtU32(meta.AvatarId)}");
        Console.Out.WriteLine($"  avatar_name = {meta.AvatarName ?? "(not found)"}");
        Console.Out.WriteLine($"  ship_name   = {meta.ShipName ?? "(not found)"} (ship_id aliases avatar_id in this protocol)");
        Console.Out.WriteLine($"  station_id  = {FmtU16(meta.StationId)} (best-effort, verify against capture)");
        Console.Out.WriteLine($"  sector_id   = {meta.SectorId} (hardcoded from capture_1 = Friendship 7)");
        Console.Out.WriteLine();

        Console.Out.WriteLine("## How to use");
        Console.Out.WriteLine("# Phase K Wave 325: the proxy rewrites the retail avatar_id above to");
        Console.Out.WriteLine("# whatever avatar_id your live session is assigned, so no DB seeding is");
        Console.Out.WriteLine("# required. Just run `just packet-replay {0}` and log in with any", meta.CaptureSource is null ? "capture_1" : meta.CaptureSource.Replace(".rar", "").Replace(".txt", ""));
        Console.Out.WriteLine("# account that has at least one character. Enter any sector -- the");
        Console.Out.WriteLine("# moment our server emits a sector-handshake opcode whose retail payload");
        Console.Out.WriteLine("# starts with the retail avatar_id, the proxy learns your live id");
        Console.Out.WriteLine("# (look for `replay: LEARN live_avatar_id=...` in the proxy log) and");
        Console.Out.WriteLine("# rewrites every subsequent substitution.");
        Console.Out.WriteLine("#");
        Console.Out.WriteLine("# If you already know your live avatar_id (e.g. queried via psql),");
        Console.Out.WriteLine("# you can skip the lazy learn with:");
        Console.Out.WriteLine("#   PROXY_LIVE_AVATAR_ID=0xNNNNNNNN just packet-replay capture_1");
        Console.Out.WriteLine("#");
        Console.Out.WriteLine("# What is NOT rewritten: other-player avatar IDs in retail's payloads");
        Console.Out.WriteLine("# (e.g. nearby players in Friendship 7). The client may still try to");
        Console.Out.WriteLine("# render them; if it crashes there, that's a separate problem from the");
        Console.Out.WriteLine("# transport / your-avatar question this tool is meant to answer.");
        Console.Out.WriteLine();

        if (printFrames)
        {
            Console.Out.WriteLine("## Frame list (packet#, opcode, payload bytes)");
            int idx = 0;
            foreach (var f in frames)
            {
                Console.Out.WriteLine(
                    $"  [{idx,3}] pkt#{f.PacketNumber,5} port={f.Port,-4} 0x{f.Opcode:X4} payload={f.Payload.Length,5}B");
                idx++;
            }
        }
    }

    private static string FmtU32(uint? v) =>
        v is uint x ? $"0x{x:X8} ({x})" : "(not found)";
    private static string FmtU16(ushort? v) =>
        v is ushort x ? $"0x{x:X4} ({x})" : "(not found)";
}
