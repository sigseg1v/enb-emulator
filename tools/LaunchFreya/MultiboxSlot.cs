using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

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

            if (TryReserveBlock(DefaultBase))
            {
                log($"multi-box: stock ports {DefaultBase} free -- single-client launch");
                return new MultiboxSlot(DefaultBase);
            }

            for (int i = 0; i < MaxBlocks; i++)
            {
                int b = SearchStart + i * PortSpan;
                if (b > 65535 - (PortSpan - 1))
                    break;
                if (TryReserveBlock(b))
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

        // Named mutexes we successfully claimed, held for the launcher's lifetime
        // so a racing launcher sees the block as taken. Kept referenced so the GC
        // never finalizes them (which would release the OS handle early).
        static readonly List<Mutex> s_heldSlotLocks = new();

        // Try to CLAIM block b for this launcher. A block is available only when
        // it is BOTH (1) not already served -- nothing accepts on its sector port
        // (base+0) -- AND (2) not already claimed by a racing launcher.
        //
        // (1) is a CONNECT probe, not a BIND probe. A bind probe is unreliable on
        // Windows: the running proxy holds the port with a plain (non-exclusive)
        // bind, and a second socket with SO_REUSEADDR -- which is what a default
        // .NET TcpListener uses (ExclusiveAddressUse == false) -- is allowed to
        // bind the SAME 127.0.0.1:port on Windows. So the bind probe wrongly
        // reports the busy stock port 3500 as "free", the launcher decides this
        // is the first instance, never arms the single-instance mutex bypass, and
        // the second client dies on "Earth & Beyond is already running".
        // (Linux/WINE forbid the shared active bind, which is why that bug never
        // showed under WINE.) A connect probe is unambiguous on both. base+0 is
        // the block's discriminator and the proxy listens on it from startup,
        // before any client connects, so it is a reliable liveness signal.
        //
        // (2) closes the fast-double-click RACE the connect probe alone cannot:
        // the probe only sees proxies that have ALREADY bound, so two launchers
        // started within the same sub-second window both see stock 3500 free
        // before either proxy binds, both pick it, and the second client lands on
        // the first's single-client proxy -- session hijack (cross-logout, shared
        // chat/group). A session-scoped named mutex per block serializes the
        // claim: the first launcher to take it holds it for its whole lifetime,
        // so the racer skips the block. Session-scoped (bare name, no Global\)
        // because co-located clients run in one interactive user session and it
        // needs no extra privilege. This path only runs on the native-Windows
        // manual-multibox launch; the docker/dev path pins FREYA_GAME_PORT_BASE
        // and returns before ever reaching here.
        static bool TryReserveBlock(int b)
        {
            if (TcpListening(b))
                return false;

            Mutex m;
            try { m = new Mutex(false, $"FreyaSlot_{b}"); }
            catch { return true; } // can't create a named mutex here -- probe-only

            bool owned;
            try { owned = m.WaitOne(0); }
            catch (AbandonedMutexException) { owned = true; } // prior owner died -> ours now
            catch { m.Dispose(); return true; }

            if (owned)
            {
                s_heldSlotLocks.Add(m);
                return true;
            }
            m.Dispose();
            return false;
        }

        // True if something accepts a TCP connection on 127.0.0.1:port right now.
        static bool TcpListening(int port)
        {
            try
            {
                using var probe = new TcpClient();
                var ar = probe.BeginConnect(IPAddress.Loopback, port, null, null);
                // A loopback connect completes near-instantly; bound the wait so a
                // busy machine never stalls the Play click. No answer in time is
                // treated as "not listening" (free) -- the same lenient direction
                // the old bind probe took on error.
                if (!ar.AsyncWaitHandle.WaitOne(TimeSpan.FromMilliseconds(400)))
                    return false;
                probe.EndConnect(ar); // throws if the connect was refused -> free
                return true;
            }
            catch (SocketException) { return false; }
            catch (ObjectDisposedException) { return false; }
        }
    }
}
