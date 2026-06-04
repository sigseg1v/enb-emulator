// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;

namespace N7.Tools.PcapInventory;

/// <summary>
/// Minimal little-endian pcapng reader that yields the UDP payload (the EnB
/// application datagram, starting with the 12-byte EnbUdpHeader) of every
/// captured UDP packet, tagged with its 4-tuple flow key.
///
/// Only what this tool needs: SectionHeaderBlock (0x0A0D0D0A),
/// InterfaceDescriptionBlock (type 1) and EnhancedPacketBlock (type 6), with
/// LINKTYPE_ETHERNET (1) framing -- which is what the proxy-side loopback
/// captures in proxy/local-debug/ use. IPv4/UDP only.
/// </summary>
public static class PcapNgReader
{
    public readonly record struct Datagram(string SrcIp, int SrcPort, string DstIp, int DstPort, byte[] Payload)
    {
        public string FlowKey => $"{SrcIp}:{SrcPort}->{DstIp}:{DstPort}";
    }

    public static IEnumerable<Datagram> Read(string path)
    {
        byte[] d = File.ReadAllBytes(path);
        if (d.Length < 12) yield break;

        // SHB byte-order magic at offset 8 must be 0x1A2B3C4D for little-endian.
        uint firstType = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(0, 4));
        if (firstType != 0x0A0D0D0A)
            throw new InvalidDataException("not a pcapng file (missing Section Header Block)");
        uint bom = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(8, 4));
        if (bom != 0x1A2B3C4D)
            throw new NotSupportedException("big-endian pcapng not supported by this tool");

        int off = 0;
        while (off + 12 <= d.Length)
        {
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(off, 4));
            uint total = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(off + 4, 4));
            if (total < 12 || off + (long)total > d.Length) yield break; // truncated/garbage

            if (type == 6) // Enhanced Packet Block
            {
                // body: interface_id@0, ts_hi@4, ts_lo@8, cap_len@12, orig_len@16, data@20
                int body = off + 8;
                if (body + 20 <= d.Length)
                {
                    int capLen = (int)BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(body + 12, 4));
                    int dataOff = body + 20;
                    if (capLen > 0 && dataOff + capLen <= d.Length)
                    {
                        var dg = TryParseEthernetUdp(d.AsSpan(dataOff, capLen));
                        if (dg is { } v) yield return v;
                    }
                }
            }

            off += (int)total; // total is already 4-byte aligned
        }
    }

    // Ethernet(14) -> IPv4(IHL*4) -> UDP(8) -> payload. Returns null if not IPv4/UDP.
    private static Datagram? TryParseEthernetUdp(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 14) return null;
        ushort ethType = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(12, 2));
        if (ethType != 0x0800) return null; // IPv4 only

        var ip = frame[14..];
        if (ip.Length < 20) return null;
        int ihl = (ip[0] & 0x0F) * 4;
        if (ihl < 20 || ip.Length < ihl + 8) return null;
        if (ip[9] != 17) return null; // UDP
        string src = $"{ip[12]}.{ip[13]}.{ip[14]}.{ip[15]}";
        string dst = $"{ip[16]}.{ip[17]}.{ip[18]}.{ip[19]}";

        var udp = ip[ihl..];
        int sport = BinaryPrimitives.ReadUInt16BigEndian(udp.Slice(0, 2));
        int dport = BinaryPrimitives.ReadUInt16BigEndian(udp.Slice(2, 2));
        int ulen = BinaryPrimitives.ReadUInt16BigEndian(udp.Slice(4, 2));
        int payLen = Math.Min(ulen - 8, udp.Length - 8);
        if (payLen <= 0) return null;
        return new Datagram(src, sport, dst, dport, udp.Slice(8, payLen).ToArray());
    }
}
