// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Web;

namespace N7.CliClient.Auth;

/// <summary>
/// Issues <c>/AuthLogin</c> requests against a Net7SSL server over TLS
/// and parses the resulting ticket. This is the first step in every
/// EnB session — the ticket the server returns here is what every
/// downstream connection (global → master → sector) authenticates with.
/// </summary>
/// <remarks>
/// <para>
/// Wire: HTTP/1.1 GET over TLS, port 443 (see <c>SSL_PORT</c> in
/// <c>common/include/net7/Ports.h</c>). The real Win32 client only
/// ever sends GET requests against this endpoint
/// (<c>login-server/Net7SSL/LinuxAuth.cpp:41</c>), so we do the same.
/// </para>
/// <para>
/// TLS version: we let the OS pick (Tls12 / Tls13). The Net7SSL server
/// is configured for TLSv1.3 by the Phase J Linux port (with TLSv1.2
/// as a fallback the C++ code accepts). We don't pin to TLSv1.3 here
/// because the integration tests run against the same server image in
/// CI, and we want failures to be "server rejected this version" loud
/// rather than "we asked for a version the test env doesn't have."
/// </para>
/// <para>
/// Certificate validation: by default we require a valid chain. For
/// local dev / docker-compose / CI against a self-signed dev cert,
/// pass <see cref="AcceptUntrustedCertificates"/> = true. The flag is
/// loud on the wire (logs "WARNING: accepting untrusted TLS cert") so
/// it can't get accidentally left on in production. There is no env
/// variable that flips this on without the caller asking.
/// </para>
/// </remarks>
public sealed class AuthLoginClient
{
    private readonly string _host;
    private readonly int _port;
    private readonly bool _acceptUntrusted;
    private readonly Action<string>? _diagnostics;

    /// <summary>
    /// Construct a client. <paramref name="host"/> is the Net7SSL
    /// hostname (or IP); <paramref name="port"/> is its TLS port (443
    /// in prod, often 8443 or similar in local dev — match what your
    /// docker-compose exposes).
    /// </summary>
    /// <param name="acceptUntrustedCertificates">When true, any TLS
    /// cert presented by the server is accepted, including self-signed
    /// dev certs. Loud-by-design — pass true only for explicit dev /
    /// CI runs against a local server.</param>
    /// <param name="diagnostics">Optional sink for one-liner diagnostic
    /// messages (TLS handshake details, untrusted-cert warnings).
    /// Routed to the CLI client's log layer in production; null in
    /// tests.</param>
    public AuthLoginClient(
        string host,
        int port = 443,
        bool acceptUntrustedCertificates = false,
        Action<string>? diagnostics = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(host);
        if (port <= 0 || port > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), port, "port must be 1..65535");

        _host = host;
        _port = port;
        _acceptUntrusted = acceptUntrustedCertificates;
        _diagnostics = diagnostics;
    }

    /// <summary>
    /// POST-style flow but actually a GET — the real client appends the
    /// credentials to the query string and sends an empty-body GET
    /// (mirrors what the server's <c>HandleAuthLogin</c> parses out of
    /// the raw recv buffer).
    /// </summary>
    public async Task<AuthLoginResponse> LoginAsync(
        AuthLoginRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string url = BuildUrl(request);
        string httpRequest = BuildHttpRequest(url);

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(_host, _port, cancellationToken).ConfigureAwait(false);

        using var tls = new SslStream(
            tcp.GetStream(),
            leaveInnerStreamOpen: false,
            userCertificateValidationCallback: ValidateServerCertificate);

        var tlsOptions = new SslClientAuthenticationOptions
        {
            TargetHost = _host,
            EnabledSslProtocols = SslProtocols.None, // OS default; Tls12 + Tls13
            RemoteCertificateValidationCallback = ValidateServerCertificate,
        };

        await tls.AuthenticateAsClientAsync(tlsOptions, cancellationToken).ConfigureAwait(false);

        byte[] requestBytes = Encoding.ASCII.GetBytes(httpRequest);
        await tls.WriteAsync(requestBytes, cancellationToken).ConfigureAwait(false);
        await tls.FlushAsync(cancellationToken).ConfigureAwait(false);

        string rawResponse = await ReadAllAsync(tls, cancellationToken).ConfigureAwait(false);
        string body = ExtractBody(rawResponse);

        return AuthLoginResponse.Parse(body);
    }

    /// <summary>
    /// URL-encode the credentials and build the query string that
    /// matches <c>HandleAuthLogin</c>'s parser (it pulls
    /// <c>username=</c>, <c>password=</c>, <c>serviceID=</c>,
    /// <c>version=</c> out of the recv buffer with <c>strstr</c>).
    /// </summary>
    internal static string BuildUrl(AuthLoginRequest request)
    {
        string u = HttpUtility.UrlEncode(request.Username);
        string p = HttpUtility.UrlEncode(request.Password);
        string s = HttpUtility.UrlEncode(request.ServiceId);
        string v = HttpUtility.UrlEncode(request.Version);
        return $"/AuthLogin?username={u}&password={p}&serviceID={s}&version={v}";
    }

    /// <summary>
    /// Build the minimal HTTP/1.1 GET that the C++ server's
    /// <c>strstr</c>-based parser accepts. The server doesn't actually
    /// care about most headers — it scans the raw buffer for the four
    /// query tags — but we send standard headers so a captured request
    /// looks identical to one from the real client.
    /// </summary>
    private string BuildHttpRequest(string url)
    {
        return
            $"GET {url} HTTP/1.1\r\n" +
            $"Host: {_host}\r\n" +
            "User-Agent: EnB-Client\r\n" +
            "Accept: */*\r\n" +
            "Connection: close\r\n" +
            "\r\n";
    }

    private bool ValidateServerCertificate(
        object sender,
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors sslPolicyErrors)
    {
        if (sslPolicyErrors == SslPolicyErrors.None)
            return true;

        if (_acceptUntrusted)
        {
            _diagnostics?.Invoke(
                $"WARNING: accepting untrusted TLS cert ({sslPolicyErrors}) from {_host}:{_port}");
            return true;
        }

        _diagnostics?.Invoke(
            $"TLS cert rejected ({sslPolicyErrors}) from {_host}:{_port}");
        return false;
    }

    private static async Task<string> ReadAllAsync(SslStream tls, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        byte[] buf = new byte[4096];
        while (true)
        {
            int n = await tls.ReadAsync(buf, ct).ConfigureAwait(false);
            if (n <= 0)
                break;
            ms.Write(buf, 0, n);
        }
        return Encoding.ASCII.GetString(ms.ToArray());
    }

    /// <summary>
    /// Strip the HTTP headers and return the body. The Net7SSL server
    /// always sends Content-Length-style or simple non-chunked bodies
    /// for /AuthLogin, so we look for the blank line that ends the
    /// header block.
    /// </summary>
    internal static string ExtractBody(string rawResponse)
    {
        int separator = rawResponse.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (separator < 0)
        {
            // Some intermediaries normalise to \n-only — be lenient.
            separator = rawResponse.IndexOf("\n\n", StringComparison.Ordinal);
            if (separator < 0)
                return rawResponse;
            return rawResponse[(separator + 2)..];
        }
        return rawResponse[(separator + 4)..];
    }
}
