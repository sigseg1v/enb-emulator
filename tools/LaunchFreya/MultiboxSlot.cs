using System;
using System.Net;
using System.Net.Sockets;

namespace LaunchFreya
{
    // Per-launcher instance slot for multi-box (running more than one client at
    // once on the same machine). Each instance gets its own loopback IP --
    // 127.0.0.1 for slot 0, 127.0.0.2 for slot 1, ... -- so the N co-located
    // proxies don't collide on the shared client-facing ports (the EnB client
    // dials FIXED ports compiled into client.exe -- 3500/3801/3805 -- so the
    // only way to run N proxies on one box is to give each its own IP, never its
    // own port). The whole 127.0.0.0/8 block is loopback on Linux/WINE, so every
    // 127.0.0.N is reachable with no interface setup.
    //
    // Slot 0 is the ordinary single-client launch: loopback 127.0.0.1, multi-box
    // OFF, so the client's single-instance guard, the proxy's INADDR_ANY bind,
    // and the position feed's loopback target are all left exactly as they were.
    // Only slot >= 1 turns multi-box on (the CreateMutexA bypass, the proxy's
    // per-instance bind, the per-instance position-feed target).
    //
    // The auth relay is NOT per-slot: it terminates the client's plaintext auth
    // on 127.0.0.1:4180 and re-wraps it as TLS to the (one, shared) upstream
    // login server, and it already handles concurrent connections -- so a single
    // relay owned by whichever launcher started first serves every instance. The
    // client-side auth config (auth.ini AAIUrl=localhost, the patched
    // authlogin.dll port) stays loopback for all slots and needs no per-instance
    // value, which is why the relay can be shared.
    public sealed class MultiboxSlot
    {
        // The client-facing TCP port the proxy listens on (PROXY_LOCAL_TCP_PORT
        // in common/include/net7/Ports.h). We probe a candidate loopback IP by
        // trying to bind it: in use -> that slot already has a proxy, try the
        // next IP.
        const int ProbePort = 3500;

        // A sane ceiling on concurrent instances. Far more than anyone runs by
        // hand, and it bounds the auto-detect scan.
        const int MaxSlots = 16;

        public int Index { get; }
        public string LoopbackIp { get; }
        public bool Multibox => Index > 0;

        MultiboxSlot(int index)
        {
            Index = index;
            LoopbackIp = "127.0.0." + (index + 1);
        }

        // Resolve this launcher's slot. An explicit FREYA_INSTANCE_SLOT env wins
        // (so a scripted "launch N at once" can pin slots 0..N-1 deterministically
        // and avoid the spawn race); otherwise auto-detect the lowest free slot by
        // probing 127.0.0.1, 127.0.0.2, ... for a bindable proxy port. This is
        // what makes "just launch the launcher again" land on a fresh instance
        // with no manual port juggling.
        public static MultiboxSlot Resolve(Action<string> log = null)
        {
            log ??= _ => { };

            var forced = Environment.GetEnvironmentVariable("FREYA_INSTANCE_SLOT");
            if (!string.IsNullOrWhiteSpace(forced) &&
                int.TryParse(forced, out var idx) && idx >= 0 && idx < MaxSlots)
            {
                var s = new MultiboxSlot(idx);
                log($"multi-box: using forced slot {idx} ({s.LoopbackIp}, multibox={s.Multibox})");
                return s;
            }

            for (int i = 0; i < MaxSlots; i++)
            {
                var ip = "127.0.0." + (i + 1);
                if (IsProxyPortFree(ip))
                {
                    var s = new MultiboxSlot(i);
                    if (i == 0)
                        log("multi-box: slot 0 (127.0.0.1) -- single-client launch");
                    else
                        log($"multi-box: slots 0..{i - 1} busy; using slot {i} ({ip})");
                    return s;
                }
            }

            // All slots look busy. Fall back to slot 0 rather than refusing to
            // launch -- worst case the proxy spawn reports the real bind error.
            log($"multi-box: all {MaxSlots} slots appear busy; falling back to slot 0");
            return new MultiboxSlot(0);
        }

        static bool IsProxyPortFree(string ip)
        {
            TcpListener probe = null;
            try
            {
                probe = new TcpListener(IPAddress.Parse(ip), ProbePort);
                probe.Start();
                return true;
            }
            catch (SocketException)
            {
                return false; // in use (or this loopback alias isn't bindable)
            }
            finally
            {
                try { probe?.Stop(); } catch { }
            }
        }
    }
}
