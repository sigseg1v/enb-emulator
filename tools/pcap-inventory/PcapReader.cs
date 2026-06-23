// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;

namespace N7.Tools.PcapInventory;

/// <summary>
/// Classic libpcap (.pcap) reader, sibling to <see cref="PcapNgReader"/>. The
/// proxy-side loopback captures in proxy/local-debug/ are pcapng over
/// LINKTYPE_ETHERNET, but the unattended explore-sector survey captures the host
/// WINE proxy with `tcpdump -i any`, which writes a classic .pcap with
/// LINKTYPE_LINUX_SLL2 (276). This reader yields the same <see
/// cref="PcapNgReader.Datagram"/> so the inventory decode path is identical.
///
/// Supported link types (link-header length to skip before the IPv4 header):
///   1   LINKTYPE_ETHERNET   -> 14 (+ EtherType check)
///   113 LINKTYPE_LINUX_SLL  -> 16
///   276 LINKTYPE_LINUX_SLL2 -> 20
/// IPv4/UDP only, little- and big-endian magic, microsecond and nanosecond ts.
/// </summary>
public static class PcapReader
{
    public static bool IsClassicPcap(string path)
    {
        using var fs = File.OpenRead(path);
        Span<byte> m = stackalloc byte[4];
        if (fs.Read(m) != 4) return false;
        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(m);
        return magic is 0xA1B2C3D4 or 0xD4C3B2A1 or 0xA1B23C4D or 0x4D3CB2A1;
    }

    public static IEnumerable<PcapNgReader.Datagram> Read(string path)
    {
        byte[] d = File.ReadAllBytes(path);
        if (d.Length < 24) yield break;

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(0, 4));
        // 0xA1B2C3D4 (us) / 0xA1B23C4D (ns) are little-endian on disk; their
        // byte-swapped forms mean a big-endian writer.
        bool le = magic is 0xA1B2C3D4 or 0xA1B23C4D;
        bool be = magic is 0xD4C3B2A1 or 0x4D3CB2A1;
        if (!le && !be)
            throw new InvalidDataException("not a classic pcap file (bad magic)");

        uint U32(ReadOnlySpan<byte> s) =>
            le ? BinaryPrimitives.ReadUInt32LittleEndian(s) : BinaryPrimitives.ReadUInt32BigEndian(s);

        uint linkType = U32(d.AsSpan(20, 4)) & 0xFFFF;
        int linkLen = linkType switch
        {
            1 => 14,    // Ethernet
            113 => 16,  // Linux SLL
            276 => 20,  // Linux SLL2
            101 => 0,   // raw IP
            12 or 14 => 0, // raw IPv4 variants
            _ => -1,
        };
        if (linkLen < 0)
            throw new NotSupportedException($"unsupported pcap linktype {linkType}");

        int off = 24; // after the global header
        while (off + 16 <= d.Length)
        {
            int inclLen = (int)U32(d.AsSpan(off + 8, 4));
            int dataOff = off + 16;
            if (inclLen <= 0 || dataOff + inclLen > d.Length) yield break; // truncated
            var frame = d.AsSpan(dataOff, inclLen);

            var dg = TryParse(frame, (int)linkType, linkLen);
            if (dg is { } v) yield return v;

            off = dataOff + inclLen;
        }
    }

    // Skip the link header, then parse IPv4 -> UDP -> payload. For Ethernet the
    // EtherType is checked; SLL/SLL2/raw start the IPv4 header at a fixed offset.
    private static PcapNgReader.Datagram? TryParse(ReadOnlySpan<byte> frame, int linkType, int linkLen)
    {
        if (frame.Length < linkLen + 20) return null;
        if (linkType == 1)
        {
            ushort ethType = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(12, 2));
            if (ethType != 0x0800) return null; // IPv4 only
        }
        var ip = frame[linkLen..];
        if (ip.Length < 20) return null;
        if ((ip[0] >> 4) != 4) return null;      // IPv4
        int ihl = (ip[0] & 0x0F) * 4;
        if (ihl < 20 || ip.Length < ihl + 8) return null;
        if (ip[9] != 17) return null;            // UDP
        string src = $"{ip[12]}.{ip[13]}.{ip[14]}.{ip[15]}";
        string dst = $"{ip[16]}.{ip[17]}.{ip[18]}.{ip[19]}";

        var udp = ip[ihl..];
        int sport = BinaryPrimitives.ReadUInt16BigEndian(udp.Slice(0, 2));
        int dport = BinaryPrimitives.ReadUInt16BigEndian(udp.Slice(2, 2));
        int ulen = BinaryPrimitives.ReadUInt16BigEndian(udp.Slice(4, 2));
        int payLen = Math.Min(ulen - 8, udp.Length - 8);
        if (payLen <= 0) return null;
        return new PcapNgReader.Datagram(src, sport, dst, dport, udp.Slice(8, payLen).ToArray());
    }
}
