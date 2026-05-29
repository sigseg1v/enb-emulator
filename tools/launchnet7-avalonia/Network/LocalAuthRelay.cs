using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;

namespace LaunchNet7Avalonia.Network
{
    // Plaintext-on-loopback -> TLS-to-upstream relay.
    //
    // The client's authlogin.dll always talks HTTP to localhost (the
    // launcher patches the dll bytes to make this true regardless of the
    // selected server). This relay terminates that plaintext on loopback
    // and re-wraps each connection as TLS to the actual upstream auth
    // host. Net7SSL stays TLS-only end-to-end on the wire; the plaintext
    // hop never leaves the box (listeners bound to loopback only).
    //
    // Dual-family loopback (v4 + v6) is required: WINE's wininet resolves
    // "localhost" via getaddrinfo, and on most modern distros the v6
    // address `::1` is returned first. A v4-only listener would refuse
    // those connections and authlogin.dll would report
    // ERROR_INTERNET_CANNOT_CONNECT (12029). We bind two listeners —
    // one on 127.0.0.1, one on [::1] — because TcpListener+DualMode bound
    // to IPv6Loopback does NOT accept v4 traffic (the bound address has
    // no v4-mapped equivalent), and IPv6Any+DualMode would expose the
    // port off-box, which is exactly what the relay exists to prevent.
    //
    // Cert validation policy is split by upstream:
    //   - Loopback upstream (dev stack): skip verification. The cert is
    //     the dev self-signed one on this same machine; a MITM here
    //     would require already owning the loopback interface.
    //   - Remote upstream: full validation against the OS trust store.
    //     A remote deploy is expected to ship a real CA-signed cert.
    public sealed class LocalAuthRelay : IDisposable
    {
        public const int ListenPort = 4180;

        readonly TcpListener _v4;
        readonly TcpListener _v6;
        readonly string _upstreamHost;
        readonly int _upstreamPort;
        readonly bool _upstreamIsLoopback;
        readonly CancellationTokenSource _cts = new();
        readonly Action<string> _log;

        public static LocalAuthRelay Start(string upstreamHost, int upstreamPort, Action<string> log = null)
        {
            var r = new LocalAuthRelay(upstreamHost, upstreamPort, log);
            r._v4.Start();
            r._v6.Start();
            _ = Task.Run(() => r.AcceptLoop(r._v4));
            _ = Task.Run(() => r.AcceptLoop(r._v6));
            r._log($"auth relay: listening on 127.0.0.1:{ListenPort} and [::1]:{ListenPort} -> {upstreamHost}:{upstreamPort} (verify={(r._upstreamIsLoopback ? "skip" : "full")})");
            return r;
        }

        LocalAuthRelay(string upstreamHost, int upstreamPort, Action<string> log)
        {
            _upstreamHost = upstreamHost ?? throw new ArgumentNullException(nameof(upstreamHost));
            _upstreamPort = upstreamPort;
            _upstreamIsLoopback = IsLoopback(upstreamHost);
            _log = log ?? (_ => { });
            _v4 = new TcpListener(IPAddress.Loopback,     ListenPort);
            _v6 = new TcpListener(IPAddress.IPv6Loopback, ListenPort);
        }

        async Task AcceptLoop(TcpListener listener)
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException)    { break; }
                catch (Exception ex)
                {
                    _log($"auth relay accept failed: {ex.Message}");
                    continue;
                }
                _ = Task.Run(() => HandleClient(client));
            }
        }

        async Task HandleClient(TcpClient client)
        {
            using (client)
            {
                client.NoDelay = true;
                TcpClient upstream = null;
                SslStream tls = null;
                try
                {
                    upstream = new TcpClient { NoDelay = true };
                    await upstream.ConnectAsync(_upstreamHost, _upstreamPort).ConfigureAwait(false);

                    tls = new SslStream(
                        upstream.GetStream(),
                        leaveInnerStreamOpen: false,
                        userCertificateValidationCallback: _upstreamIsLoopback
                            ? (sender, cert, chain, errs) => true
                            : null);

                    await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                    {
                        TargetHost = _upstreamHost,
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    }, _cts.Token).ConfigureAwait(false);

                    var c2u = client.GetStream().CopyToAsync(tls, _cts.Token);
                    var u2c = tls.CopyToAsync(client.GetStream(), _cts.Token);
                    await Task.WhenAny(c2u, u2c).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _log($"auth relay conn error: {ex.Message}");
                }
                finally
                {
                    tls?.Dispose();
                    upstream?.Dispose();
                }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _v4.Stop(); } catch { }
            try { _v6.Stop(); } catch { }
        }

        public static bool IsLoopback(string host)
        {
            if (string.IsNullOrWhiteSpace(host)) return false;
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
            if (IPAddress.TryParse(host, out var ip)) return IPAddress.IsLoopback(ip);
            return false;
        }
    }
}
