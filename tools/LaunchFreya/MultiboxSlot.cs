using System;
using System.Net;
using System.Net.Sockets;

namespace LaunchFreya
{
    // Per-launcher instance slot for multi-box (running more than one client at
    // once on the same machine), expressed as a contiguous PORT BLOCK on
    // 127.0.0.1 -- NOT a distinct loopback IP. The EnB client dials FIXED ports
    // compiled into client.exe (sector 3500, master 3801, global 3805, posfeed
    // 3807), so co-located proxies cannot simply "use a different port" on their
    // own; the in-client connect() hook (enbmod netredirect) remaps those fixed
    // dials onto this block, and the proxy listens on the matching slots:
    //   base+0 sector, base+1 master, base+2 global, base+3 posfeed (UDP).
    // Same IP for everyone, different ports per instance -- which is portable to
    // native Windows (no 127.0.0.N alias juggling) and to rootless docker (which
    // collapses 127.0.0.0/8 to 127.0.0.1 in its port allocator anyway).
    //
    // The DEFAULT block base is 3500: when the stock ports are free this is the
    // first instance, multi-box is OFF, and the connect hook / proxy port offset
    // are both no-ops -- byte-identical to a non-multibox launch. Only when the
    // stock ports are taken does Resolve() autodetect the next free block, which
    // is exactly "press Play again and land on a fresh instance" with no manual
    // port juggling and no slot file.
    //
    // The auth relay is NOT per-slot: it terminates the client's plaintext auth
    // on 127.0.0.1:4180 and re-wraps it to the (one, shared) upstream login
    // server, and it already handles concurrent connections -- so a single relay
    // owned by whichever launcher started first serves every instance. Port 4180
    // is outside the remapped block, so the connect hook leaves it untouched.
    public sealed class MultiboxSlot
    {
        // A single instance needs 4 contiguous client-facing ports.
        const int PortSpan = 4;

        // The stock ports the client/proxy use with no remap. When base+0 == 3500
        // is free, this instance is the first one and runs on the defaults.
        public const int DefaultBase = 3500;

        // Where the autodetect scan looks for a free block once the defaults are
        // taken. Blocks are spaced PortSpan apart. 100 blocks is far more than
        // anyone runs by hand and bounds the scan.
        const int SearchStart = 23500;
        const int MaxBlocks = 100;

        public int PortBase { get; }
        public bool Multibox => PortBase != DefaultBase;

        public int SectorPort => PortBase + 0;
        public int MasterPort => PortBase + 1;
        public int GlobalPort => PortBase + 2;
        public int PosFeedPort => PortBase + 3;

        MultiboxSlot(int portBase) { PortBase = portBase; }

        // Resolve this launcher's port block. An explicit FREYA_GAME_PORT_BASE env
        // wins (the docker recipe pins it to match its published ports, and a
        // scripted "launch N at once" can pin deterministic blocks to avoid the
        // spawn race). Otherwise: use the stock block if it is free (first
        // instance), else autodetect the lowest free block. This is what makes
        // "just press Play again" land on a fresh instance.
        public static MultiboxSlot Resolve(Action<string> log = null)
        {
            log ??= _ => { };

            var forced = Environment.GetEnvironmentVariable("FREYA_GAME_PORT_BASE");
            if (!string.IsNullOrWhiteSpace(forced) &&
                int.TryParse(forced, out var fb) && fb > 0 && fb <= 65535 - (PortSpan - 1))
            {
                var s = new MultiboxSlot(fb);
                log($"multi-box: using forced port base {fb} (multibox={s.Multibox})");
                return s;
            }

            if (IsBlockFree(DefaultBase))
            {
                log($"multi-box: stock ports {DefaultBase} free -- single-client launch");
                return new MultiboxSlot(DefaultBase);
            }

            for (int i = 0; i < MaxBlocks; i++)
            {
                int b = SearchStart + i * PortSpan;
                if (b > 65535 - (PortSpan - 1))
                    break;
                if (IsBlockFree(b))
                {
                    log($"multi-box: stock ports busy; using port block {b}..{b + PortSpan - 1}");
                    return new MultiboxSlot(b);
                }
            }

            // Nothing free in the scan window. Fall back to the stock block rather
            // than refusing to launch -- worst case the proxy spawn reports the
            // real bind error.
            log($"multi-box: no free port block in {SearchStart}..; falling back to stock {DefaultBase}");
            return new MultiboxSlot(DefaultBase);
        }

        // A block is free when all 3 TCP ports AND the posfeed UDP port bind on
        // 127.0.0.1. We probe by binding (and immediately releasing); a busy slot
        // means another instance already owns that block.
        static bool IsBlockFree(int b)
        {
            for (int k = 0; k < 3; k++)
                if (!TcpFree(b + k))
                    return false;
            return UdpFree(b + 3);
        }

        static bool TcpFree(int port)
        {
            TcpListener probe = null;
            try
            {
                probe = new TcpListener(IPAddress.Loopback, port);
                probe.Start();
                return true;
            }
            catch (SocketException) { return false; }
            finally { try { probe?.Stop(); } catch { } }
        }

        static bool UdpFree(int port)
        {
            Socket probe = null;
            try
            {
                probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                probe.Bind(new IPEndPoint(IPAddress.Loopback, port));
                return true;
            }
            catch (SocketException) { return false; }
            finally { try { probe?.Dispose(); } catch { } }
        }
    }
}
