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
    // Plaintext-on-127.0.0.1 -> TLS-to-upstream relay.
    //
    // The client's authlogin.dll always talks HTTP to localhost (the
    // launcher patches the dll bytes to make this true regardless of the
    // selected server). This relay terminates that plaintext on loopback
    // and re-wraps each connection as TLS to the actual upstream auth
    // host. Net7SSL stays TLS-only end-to-end on the wire; the plaintext
    // hop never leaves the box (TcpListener bound to IPAddress.Loopback).
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

        readonly TcpListener _listener;
        readonly string _upstreamHost;
        readonly int _upstreamPort;
        readonly bool _upstreamIsLoopback;
        readonly CancellationTokenSource _cts = new();
        readonly Action<string> _log;

        public static LocalAuthRelay Start(string upstreamHost, int upstreamPort, Action<string> log = null)
        {
            var r = new LocalAuthRelay(upstreamHost, upstreamPort, log);
            r._listener.Start();
            _ = Task.Run(r.AcceptLoop);
            r._log($"auth relay: listening on 127.0.0.1:{ListenPort} -> {upstreamHost}:{upstreamPort} (verify={(r._upstreamIsLoopback ? "skip" : "full")})");
            return r;
        }

        LocalAuthRelay(string upstreamHost, int upstreamPort, Action<string> log)
        {
            _upstreamHost = upstreamHost ?? throw new ArgumentNullException(nameof(upstreamHost));
            _upstreamPort = upstreamPort;
            _upstreamIsLoopback = IsLoopback(upstreamHost);
            _log = log ?? (_ => { });
            _listener = new TcpListener(IPAddress.Loopback, ListenPort);
        }

        async Task AcceptLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false);
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
            try { _listener.Stop(); } catch { }
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
