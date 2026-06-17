#!/usr/bin/env python3
"""
Decrypt the encrypted client<->proxy TCP leg of a FreyaProxy / net7proxy capture.

The client<->proxy game leg is Westwood RSA + RC4 encrypted, so a raw capture of
it (e.g. taken with proxy/scripts/Start-WindowsEnbProxyCapture.ps1 against a
retail net7proxy) is opaque. But the Westwood RSA keypair is fixed and committed
(proxy/WestwoodCrypto/WestwoodRSA.cs), so the per-session RC4 key can be
recovered straight out of the captured handshake and the whole leg decrypted
offline -- giving the exact ordered opcode stream the client actually decoded.

This is the offline counterpart to the live PROXY_S2C_HEXDUMP=1 dump (which logs
our own proxy's cleartext client-facing frames). Use this to get the same view
from a retail net7proxy capture, so the two legs can be compared apples-to-apples.

Handshake (mirrors Connection::DoKeyExchange):
  proxy->client : 74-byte RSA public-key packet (cleartext), then the RC4 stream
  client->proxy : [4-byte big-endian length=64][64-byte RSA-encrypted block],
                  then the RC4 stream. If the length field is 65 with a leading
                  zero byte, skip that byte.
  RC4 key       : the last 8 bytes of the RSA-decrypted block, REVERSED.

Usage:
  decrypt-client-leg.py CAPTURE.pcapng [--proxy-port N] [--max-ops K]

If --proxy-port is omitted, the script auto-detects it: the proxy game port is
the TCP port that appears on the most distinct connections AND carries a valid
74-byte pubkey + client key handshake.
"""
import sys
import struct
import argparse
from collections import defaultdict, Counter

# Westwood RSA (proxy/WestwoodCrypto/WestwoodRSA.cs) -- fixed keypair.
E = 35
N = 10385578014804950221065190195736491193847541479389728420426514083771326945639729736695791225573893793119489336012297845146104637691941242485732839277543427
D = 10088847214381951643320470475858305731166183151407164751271470824235003318621252307969752086088076499395823874814123350292603347408732347765156628342107995
BLOCK = 64          # WestwoodRSA.BlockSize
RC4_KEYLEN = 8      # RC4_KEY_SIZE
PUBKEY = 74         # ServerPubkeyPacketSize


def read_pcapng(path):
    """Minimal pcapng/pcap reader -> list of (linktype, frame_bytes)."""
    with open(path, 'rb') as f:
        data = f.read()
    # classic pcap?
    if len(data) >= 4 and data[:4] in (b'\xd4\xc3\xb2\xa1', b'\xa1\xb2\xc3\xd4',
                                       b'\x4d\x3c\xb2\xa1', b'\xa1\xb2\x3c\x4d'):
        return _read_pcap(data)
    off = 0
    end = '<'
    linktype = None
    pkts = []
    while off + 8 <= len(data):
        btype, blen = struct.unpack_from(end + 'II', data, off)
        if btype == 0x0A0D0D0A:
            bom = struct.unpack_from('<I', data, off + 8)[0]
            end = '<' if bom == 0x1A2B3C4D else '>'
            btype, blen = struct.unpack_from(end + 'II', data, off)
        if blen < 12 or off + blen > len(data):
            break
        body = data[off + 8:off + blen - 4]
        if btype == 1:          # Interface Description Block
            linktype = struct.unpack_from(end + 'H', body, 0)[0]
        elif btype == 6:        # Enhanced Packet Block
            caplen = struct.unpack_from(end + 'I', body, 12)[0]
            pkts.append((linktype, body[20:20 + caplen]))
        elif btype == 3:        # Simple Packet Block
            origlen = struct.unpack_from(end + 'I', body, 0)[0]
            pkts.append((linktype, body[4:4 + origlen]))
        off += blen
    return pkts


def _read_pcap(data):
    end = '<' if data[:4] in (b'\xd4\xc3\xb2\xa1', b'\x4d\x3c\xb2\xa1') else '>'
    linktype = struct.unpack_from(end + 'I', data, 20)[0]
    off = 24
    pkts = []
    while off + 16 <= len(data):
        _, _, caplen, _ = struct.unpack_from(end + 'IIII', data, off)
        off += 16
        if off + caplen > len(data):
            break
        pkts.append((linktype, data[off:off + caplen]))
        off += caplen
    return pkts


def parse_eth_ip_tcp(lt, frame):
    if len(frame) >= 14 and frame[12:14] == b'\x08\x00' and (frame[14] >> 4) == 4:
        ipoff = 14
    elif lt == 1:
        ipoff = 14
    elif lt == 276:
        ipoff = 20
    elif lt == 0:
        ipoff = 4
    else:
        ipoff = 14
    if ipoff + 20 > len(frame) or frame[ipoff] >> 4 != 4:
        return None
    ihl = (frame[ipoff] & 0xf) * 4
    if frame[ipoff + 9] != 6:        # not TCP
        return None
    src = '.'.join(str(b) for b in frame[ipoff + 12:ipoff + 16])
    dst = '.'.join(str(b) for b in frame[ipoff + 16:ipoff + 20])
    o = ipoff + ihl
    sport, dport = struct.unpack_from('>HH', frame, o)
    seq = struct.unpack_from('>I', frame, o + 4)[0]
    doff = (frame[o + 12] >> 4) * 4
    return (src, sport, dst, dport, seq, frame[o + doff:])


def reassemble_tcp(pk):
    streams = defaultdict(dict)     # (src,sp,dst,dp) -> {seq: payload}
    for lt, fr in pk:
        r = parse_eth_ip_tcp(lt, fr)
        if not r:
            continue
        src, sp, dst, dp, seq, pl = r
        if not pl:
            continue
        streams[(src, sp, dst, dp)].setdefault(seq, pl)
    out = {}
    for k, segs in streams.items():
        out[k] = b''.join(segs[s] for s in sorted(segs))
    return out


def rsa_decrypt(block):
    return pow(int.from_bytes(block, 'big'), D, N).to_bytes(BLOCK, 'big')


def session_key_from_block(dec):
    return bytes(dec[BLOCK - 1 - i] for i in range(RC4_KEYLEN))


class RC4:
    def __init__(self, key):
        s = list(range(256))
        j = 0
        for i in range(256):
            j = (key[i % len(key)] + s[i] + j) & 0xff
            s[i], s[j] = s[j], s[i]
        self.s = s
        self.x = 0
        self.y = 0

    def xor(self, buf):
        s, x, y = self.s, self.x, self.y
        out = bytearray(len(buf))
        for k, b in enumerate(buf):
            x = (x + 1) & 0xff
            y = (s[x] + y) & 0xff
            s[x], s[y] = s[y], s[x]
            out[k] = b ^ s[(s[x] + s[y]) & 0xff]
        self.x, self.y = x, y
        return bytes(out)


def walk(stream):
    """Walk packed [len:u16][op:u16][payload] sub-packets."""
    res = []
    i = 0
    while i + 4 <= len(stream):
        ln, op = struct.unpack_from('<HH', stream, i)
        if ln < 4 or i + ln > len(stream):
            break
        res.append((op, stream[i + 4:i + ln]))
        i += ln
    return res


def find_conns(streams, proxy_port):
    """Pair client<->proxy directions for the given proxy port."""
    conns = {}
    for (src, sp, dst, dp), data in streams.items():
        if dp == proxy_port:     # client -> proxy
            conns.setdefault((src, sp), {})['c2p'] = data
        if sp == proxy_port:     # proxy -> client
            conns.setdefault((dst, dp), {})['p2c'] = data
    return conns


def looks_like_handshake(c2p, p2c):
    if len(c2p) < 4 + BLOCK or len(p2c) < PUBKEY:
        return False
    ln = struct.unpack_from('>I', c2p, 0)[0]
    return ln in (BLOCK, BLOCK + 1)


def autodetect_port(streams):
    """The proxy game port: most connections whose handshake parses."""
    by_port = defaultdict(lambda: [0, 0])   # port -> [conn_count, handshake_count]
    for port in sorted({p for (_, sp, __, dp) in streams for p in (sp, dp)}):
        conns = find_conns(streams, port)
        for d in conns.values():
            by_port[port][0] += 1
            if looks_like_handshake(d.get('c2p', b''), d.get('p2c', b'')):
                by_port[port][1] += 1
    best = None
    for port, (nc, nh) in by_port.items():
        if nh == 0:
            continue
        score = (nh, nc)
        if best is None or score > best[1]:
            best = (port, score)
    return best[0] if best else None


def main():
    ap = argparse.ArgumentParser(description="Decrypt the client<->proxy leg of an EnB capture.")
    ap.add_argument('capture', help="pcap/pcapng file")
    ap.add_argument('--proxy-port', type=int, default=None,
                    help="proxy game TCP port (auto-detected if omitted)")
    ap.add_argument('--max-ops', type=int, default=25,
                    help="how many top opcodes to print per connection")
    args = ap.parse_args()

    pk = read_pcapng(args.capture)
    streams = reassemble_tcp(pk)
    if not streams:
        print("no TCP streams found in capture", file=sys.stderr)
        return 1

    port = args.proxy_port or autodetect_port(streams)
    if not port:
        print("could not auto-detect a proxy game port; pass --proxy-port", file=sys.stderr)
        return 1
    print(f"proxy game port = {port}{'  (auto-detected)' if not args.proxy_port else ''}")

    conns = find_conns(streams, port)
    agg = Counter()
    for cli, d in sorted(conns.items()):
        c2p, p2c = d.get('c2p', b''), d.get('p2c', b'')
        print(f"\n=== client {cli} : c2p={len(c2p)}B p2c={len(p2c)}B ===")
        if not looks_like_handshake(c2p, p2c):
            print("  no parseable handshake on this connection, skipping")
            continue
        ln = struct.unpack_from('>I', c2p, 0)[0]
        if ln == BLOCK + 1 and c2p[4] == 0:      # DoKeyExchange: skip leading 0
            blk = c2p[5:5 + BLOCK]
        else:
            blk = c2p[4:4 + BLOCK]
        key = session_key_from_block(rsa_decrypt(blk))
        print(f"  session key = {key.hex()}")
        clear = RC4(key).xor(p2c[PUBKEY:])       # skip the 74-byte pubkey
        ops = walk(clear)
        h = Counter(op for op, _ in ops)
        good = sum(1 for op, _ in ops if 0 < op <= 0xFE)
        verdict = 'LOOKS DECRYPTED' if (len(ops) > 3 and good > len(ops) * 0.7) else 'maybe wrong'
        print(f"  decrypted p2c: {len(ops)} frames, in-range(0x01..0xFE)={good}/{len(ops)} -> {verdict}")
        for op, c in sorted(h.items(), key=lambda x: -x[1])[:args.max_ops]:
            print(f"      0x{op:04x}: {c}")
        agg.update(h)

    print("\n===== AGGREGATE client-leg opcodes (all connections) =====")
    for op, c in sorted(agg.items(), key=lambda x: -x[1]):
        print(f"   0x{op:04x}: {c}")
    print(f"\n0x25 ITEM_BASE  = {agg.get(0x25, 0)}")
    print(f"0x52 LOUNGE_NPC = {agg.get(0x52, 0)}")
    return 0


if __name__ == '__main__':
    sys.exit(main())
