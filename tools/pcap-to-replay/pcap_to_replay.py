#!/usr/bin/env python3
# SPDX-License-Identifier: CC-BY-NC-SA-3.0
# Part of the Earth & Beyond emulator preservation project.
#
# pcap_to_replay.py -- convert a pcap of server->proxy UDP traffic into
# the ENBREPLAY binary format that the CLI replay tool reads.
#
# The Net-7 server sends all sector S2C opcodes batched into 0x2016
# PACKET_SEQUENCE UDP frames (EnbUdpHeader + inner [size,opcode,data] tuples).
# Large messages are split across multiple 0x2016/0x201A frames.
#
# Input:  a pcap file (standard LE pcap, linktype 276/1/113)
# Output: an ENBREPLAY binary (same format as archive/replay/*.bin)
#
# Usage:
#   python3 tools/pcap-to-replay/pcap_to_replay.py \
#       --pcap proxy/local-debug/foo.pcap \
#       --out  /tmp/foo.bin \
#       --server SERVER_IP --client CLIENT_IP
#
# Then replay in the CLI:
#   printf 'replay /tmp/foo.bin\nquit\n' | \
#       dotnet run --project tools/cli-client/src/CliClient.App -- start
#
# Wire formats (proxy/UDPProxyToClient_linux.cpp + PacketStructures.h):
#
#   EnbUdpHeader (ATTRIB_PACKED, 12 bytes):
#     [size:int16 LE] [opcode:int16 LE] [player_id:int32 LE] [pkt_seq:int32 LE]
#
#   0x2016 PACKET_SEQUENCE outer payload (after 12-B header):
#     One or more inner bundles.  If the first bundle's declared length exceeds
#     HEADER_SIZE (1400 B, g_Packet_Opt_requested=true), the message is split
#     across multiple 0x2016/0x201A frames; subsequent frames append their
#     inner bytes to the reassembly buffer until split_remaining <= 0.
#
#   Inner bundle:
#     [length:uint16 LE] [opcode:uint16 LE] [payload:(length-4) bytes]
#     Every inner bundle is a game-protocol opcode and is emitted, including
#     the object-create / login opcodes that live above 0x0FFE
#     (0x2018 STATIC_OBJECT_CREATE, 0x2019 RESOURCE_OBJECT_CREATE,
#     0x2020 LOGIN_STAGE_S_C, 0x2025, 0x2011 ...). Only the UDP-transport
#     framing opcodes (0x2016 PACKET_SEQUENCE, 0x201A PACKET_C_SEQUENCE)
#     and the 0x0000 padding sentinel are skipped -- those are never a
#     game payload and only appear here as reassembly artifacts.
#
#   ENBREPLAY frame format:
#     [opcode:uint16 LE] [payload_len:uint32 LE] [payload bytes]

import struct, argparse
from pathlib import Path
from collections import defaultdict

MAGIC            = b"ENBREPLAY"
VERSION          = 1
ENB_UDP_HDR_SIZE = 12   # sizeof(EnbUdpHeader) ATTRIB_PACKED: 2+2+4+4
HEADER_SIZE      = 1400 # g_Packet_Opt_requested=true (max inner bytes per chunk)


def parse_pcap(data: bytes):
    magic, = struct.unpack_from('<I', data, 0)
    assert magic == 0xA1B2C3D4, f"not LE pcap magic (got 0x{magic:08X})"
    snaplen, linktype = struct.unpack_from('<II', data, 16)
    pos, recs = 24, []
    while pos + 16 <= len(data):
        ts_sec, ts_usec, incl, orig = struct.unpack_from('<IIII', data, pos)
        pos += 16
        if incl > 200_000 or pos + incl > len(data):
            break
        recs.append((ts_sec, ts_usec, data[pos:pos+incl]))
        pos += incl
    return linktype, recs


def extract_udp(pkt: bytes, linktype: int):
    """Strip link/IP/UDP headers. Return (src_ip, dst_ip, sp, dp, udp_payload) or None."""
    if   linktype == 276: proto_eth = struct.unpack_from('>H', pkt, 0)[0];  ip_start = 20
    elif linktype ==   1: proto_eth = struct.unpack_from('>H', pkt, 12)[0]; ip_start = 14
    elif linktype == 113: proto_eth = struct.unpack_from('>H', pkt, 14)[0]; ip_start = 16
    else: return None
    if proto_eth != 0x0800 or len(pkt) < ip_start + 20: return None
    ip  = pkt[ip_start:]
    ihl = (ip[0] & 0xF) * 4
    if ip[9] != 17: return None   # UDP only
    src_ip = '.'.join(str(b) for b in ip[12:16])
    dst_ip = '.'.join(str(b) for b in ip[16:20])
    udp = ip[ihl:]
    if len(udp) < 8: return None
    sp, dp = struct.unpack_from('>HH', udp, 0)
    return src_ip, dst_ip, sp, dp, udp[8:]


def decode_inner_bundles(inner: bytes):
    """Yield (opcode, payload) for every game-protocol inner bundle in a reassembled payload."""
    idx = 0
    while idx + 4 <= len(inner):
        length = struct.unpack_from('<H', inner, idx)[0]
        opcode = struct.unpack_from('<H', inner, idx + 2)[0]
        if length < 4 or idx + length > len(inner):
            break
        payload = inner[idx + 4 : idx + length]
        # Skip only UDP-transport framing / padding -- never a game payload.
        if opcode not in (0x0000, 0x2016, 0x201A):
            yield opcode, payload
        idx += length


def pcap_to_frames(pcap_path: str, server_ip: str, client_ip: str):
    """Return list of (opcode, payload) from all sector S2C EnB opcodes in the pcap."""
    raw            = Path(pcap_path).read_bytes()
    linktype, recs = parse_pcap(raw)
    frames         = []

    # Group S2C UDP packets by (src_port, dst_port) flow preserving timestamp order
    flows = defaultdict(list)
    for _, _, pkt in recs:
        r = extract_udp(pkt, linktype)
        if r and r[0] == server_ip and r[1] == client_ip:
            flows[(r[2], r[3])].append(r[4])

    for (sp, dp), pkts in flows.items():
        split_buf    = bytearray()
        split_remain = 0

        for udp in pkts:
            if len(udp) < ENB_UDP_HDR_SIZE:
                continue
            size, opcode_hdr, player_id, pkt_seq = struct.unpack_from('<hhII', udp, 0)
            inner = bytes(udp[ENB_UDP_HDR_SIZE:])

            if opcode_hdr == 0x2016:           # PACKET_SEQUENCE
                if split_remain > 0:
                    # Continuation chunk arriving as a 0x2016 (not 0x201A)
                    split_buf    += inner
                    split_remain -= len(inner)
                    if split_remain <= 0:
                        frames += list(decode_inner_bundles(bytes(split_buf)))
                        split_buf = bytearray(); split_remain = 0
                elif len(inner) >= 2:
                    first_len = struct.unpack_from('<H', inner, 0)[0]
                    if first_len > HEADER_SIZE and first_len < 20_000:
                        # Split-packet start: first chunk's inner bytes are the beginning
                        split_buf    = bytearray(inner)
                        split_remain = first_len - len(inner)
                    else:
                        frames += list(decode_inner_bundles(inner))

            elif opcode_hdr == 0x201A:         # PACKET_C_SEQUENCE (continuation)
                split_buf    += inner
                split_remain -= len(inner)
                if split_remain <= 0:
                    frames += list(decode_inner_bundles(bytes(split_buf)))
                    split_buf = bytearray(); split_remain = 0

            # Any other outer opcode (auth/launcher): skip -- not sector data

    return frames


def write_enbreplay(frames, out_path: str, meta: str = ''):
    meta_bytes = meta.encode('ascii')
    with open(out_path, 'wb') as f:
        f.write(MAGIC)
        f.write(bytes([VERSION]))
        f.write(struct.pack('<I', len(meta_bytes)))
        f.write(meta_bytes)
        f.write(struct.pack('<I', len(frames)))
        for opcode, payload in frames:
            f.write(struct.pack('<HI', opcode, len(payload)))
            f.write(payload)


def main():
    ap = argparse.ArgumentParser(
        description='Convert a server->proxy UDP pcap to ENBREPLAY format for the CLI replay tool')
    ap.add_argument('--pcap',   required=True, help='input .pcap file')
    ap.add_argument('--out',    required=True, help='output .bin file')
    ap.add_argument('--server', default='',
                    help='server (sender) IP address -- pass the reference server IP')
    ap.add_argument('--client', default='',
                    help='client/proxy (receiver) IP address')
    ap.add_argument('--verbose', '-v', action='store_true')
    args = ap.parse_args()

    frames = pcap_to_frames(args.pcap, args.server, args.client)
    meta   = (f'source={Path(args.pcap).name}\n'
              f'server={args.server}\nclient={args.client}\n')
    write_enbreplay(frames, args.out, meta)

    print(f"Wrote {len(frames)} frames to {args.out}")
    if args.verbose:
        from collections import Counter
        for op_hex, cnt in sorted(Counter(f'0x{op:04X}' for op, _ in frames).items()):
            print(f"  {op_hex}: {cnt}x")


if __name__ == '__main__':
    main()
