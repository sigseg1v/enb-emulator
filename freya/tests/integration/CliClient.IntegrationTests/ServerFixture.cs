// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System.Diagnostics;
using System.Net.Sockets;
using Xunit;

namespace N7.CliClient.IntegrationTests;

/// <summary>
/// xUnit collection fixture that owns the docker-compose stack for the
/// integration suite: <c>docker compose up -d</c> on startup, TCP-probe
/// the listening ports until they accept, and <c>docker compose down -v</c>
/// on dispose.
///
/// <para>
/// One stack per test run, not per test class — bound via
/// <see cref="ServerCollection"/>. Tests that need to talk to the stack
/// take a <c>ServerFixture</c> constructor parameter and get the
/// already-up instance.
/// </para>
///
/// <para>
/// Tests provision their own accounts via <see cref="TestAccounts.New"/>,
/// which inserts a fresh row into net7_user.accounts with a process-unique
/// username on each call. No upfront seed step.
/// </para>
///
/// <para>
/// CI env: set <c>CLI_INTEGRATION_SKIP_COMPOSE=1</c> to point tests at
/// an externally-managed stack (e.g. a docker-compose already running
/// in a sibling job) instead of starting/stopping our own. The TCP
/// probe still runs; the up/down commands are skipped.
/// </para>
/// </summary>
public sealed class ServerFixture : IAsyncLifetime
{
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(120);
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(1);

    // Host-side ports published by docker-compose.yml. These are the
    // endpoints the CliClient.Core types connect to from the test
    // process — which lives on the docker host, not inside the
    // compose network.
    public string LoginHost  { get; } = "127.0.0.1";
    public int    LoginPort  { get; } = 4443;   // host-side remap of 443
    public string GlobalHost { get; } = "127.0.0.1";
    public int    GlobalPort { get; } = 3805;   // proxy GLOBAL_SERVER_PORT
    public string MasterHost { get; } = "127.0.0.1";
    public int    MasterPort { get; } = 3801;   // proxy MASTER_SERVER_PORT
    public string SectorHost { get; } = "127.0.0.1";
    public int    SectorPort { get; } = 3500;   // proxy SECTOR_SERVER_PORT
    public int    PostgresPort  { get; } = 5434;   // host-side remap of 5432

    /// <summary>
    /// Connection string for the net7_user database (accounts +
    /// avatars). Reachable from the test process because docker-compose
    /// publishes 5434:5432. Used by <see cref="TestAccounts.New"/> to
    /// provision per-test accounts on demand.
    /// </summary>
    public string PostgresConnectionString { get; }

    private bool _ownsCompose;

    // Serialises proxy recycles and de-dups bursts. The Net7Proxy is a
    // documented single-client bridge; driving hundreds of serial
    // connect->login->disconnect cycles through one instance latches
    // per-session proxy state that wedges every later sector login at
    // stage 12 (the stage whose confirm coincides with the first in-game
    // position fan-out). A controlled experiment proved the wedge is
    // PROXY-side and accumulation-dependent: restarting ONLY the proxy
    // (server untouched) recovers the suite, while re-establishing the
    // handshake in place does NOT. SectorHandshake recycles the proxy on
    // that wedge via RestartProxyAsync. The precise never-reset proxy
    // global is tracked as a real proxy defect in
    // plans/11-phase-k-ingame.md -- this is the test-infra mitigation, not
    // a server-fidelity change (no wire bytes change; the server only sees
    // a clean disconnect + fresh reconnect, exactly as if the client had
    // crashed and relaunched).
    private readonly SemaphoreSlim _proxyRestartLock = new(1, 1);
    private DateTime _lastProxyRestartUtc = DateTime.MinValue;
    private static readonly TimeSpan ProxyRestartDedupWindow =
        TimeSpan.FromSeconds(12);

    public ServerFixture()
    {
        PostgresConnectionString =
            $"Host={LoginHost};Port={PostgresPort};Username=net7;Password=net7;" +
            "Database=net7_user;Pooling=true;MaxPoolSize=20";
    }

    public async Task InitializeAsync()
    {
        _ownsCompose = Environment.GetEnvironmentVariable("CLI_INTEGRATION_SKIP_COMPOSE") != "1";

        if (_ownsCompose)
        {
            await RunComposeAsync("up -d --wait", TimeSpan.FromMinutes(5));
        }

        await WaitForPortAsync(LoginHost,  LoginPort,  ReadyTimeout);
        await WaitForPortAsync(GlobalHost, GlobalPort, ReadyTimeout);
        await WaitForPortAsync(MasterHost, MasterPort, ReadyTimeout);
        await WaitForPortAsync(SectorHost, SectorPort, ReadyTimeout);

        // The proxy's TCP listeners come up in seconds, but the server's
        // sector subsystem takes ~30-60s to load all ~130 sectors' metadata
        // and nav skeleton from postgres plus generate the mission catalog.
        //
        // Phase AI (on-demand sector start): sectors are no longer bound at
        // boot -- the target sector is content-loaded, UDP-bound, and
        // thread-started lazily on the FIRST handoff to it (the first login
        // test triggers it, well within the proxy's MasterLogin retry window
        // since a single sector's deferred load is sub-second). So the boot
        // readiness gate can no longer key on a specific sector's
        // "BeginSectorThread" line. Instead we wait for the server to finish
        // boot and start its master + MVAS receivers -- it logs "Registering
        // sector server: port=" immediately before entering its main loop, at
        // which point it is ready to route handoffs on demand.
        await WaitForServerBootCompleteAsync(TimeSpan.FromMinutes(3));
    }

    private static async Task WaitForServerBootCompleteAsync(TimeSpan timeout)
    {
        var marker = "Registering sector server: port=";
        var deadline = DateTime.UtcNow + timeout;
        string lastTail = "";
        while (DateTime.UtcNow < deadline)
        {
            var (exit, stdout, stderr) = await RunCaptureAsync(
                "docker", "compose logs --no-color --no-log-prefix server",
                TimeSpan.FromSeconds(15));
            if (exit == 0 && stdout.Contains(marker))
                return;
            lastTail = (stdout.Length > 0 ? stdout : stderr);
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
        throw new TimeoutException(
            $"Server did not log '{marker}' within {timeout.TotalSeconds:F0}s. " +
            $"Last logs tail:\n{lastTail[Math.Max(0, lastTail.Length - 2000)..]}");
    }

    /// <summary>
    /// Captures the full <c>docker compose logs</c> for one service.
    /// Used by the boot-log-health guard (<c>Smoke/BootLogHealthTests</c>).
    /// Works in both owns-compose and externally-managed
    /// (<c>CLI_INTEGRATION_SKIP_COMPOSE=1</c>) modes -- it only reads logs.
    /// </summary>
    public async Task<string> CaptureServiceLogsAsync(string service)
    {
        var (exit, stdout, stderr) = await RunCaptureAsync(
            "docker", $"compose logs --no-color --no-log-prefix {service}",
            TimeSpan.FromSeconds(30));
        return exit == 0 ? stdout : stdout + stderr;
    }

    /// <summary>
    /// Recycle the proxy container to clear accumulated per-session proxy
    /// state (the single-client wedge -- see the <c>_proxyRestartLock</c>
    /// note above). Runs <c>docker compose restart proxy</c>, then waits for
    /// the fresh process to finish booting before returning, so a caller's
    /// fresh master-join does not race a half-booted proxy. The server,
    /// login, and postgres containers are left untouched, so the loaded
    /// sector threads survive and no <c>WaitForServerSectorAsync</c> re-poll
    /// is needed.
    ///
    /// <para>
    /// Readiness is gated on the proxy's own boot log, NOT a bare TCP
    /// port-probe. The listener sockets accept connections very early in
    /// boot -- before the proxy has re-resolved the game server and bound
    /// its two UDP planes to it -- so a port-probe returns while the proxy
    /// still cannot serve a login, and the very next handshake wedges again.
    /// The last line a freshly-booted proxy prints before it is able to
    /// relay a login is the global UDP bind ("... (unconnected default
    /// peer)" in <c>UDPClient_linux.cpp::ResolveGameServerIP</c>); we poll
    /// the log tail for it. Because a <c>restart</c> (not recreate) preserves
    /// the log stream and no client reconnects while this lock is held, that
    /// marker in the tail is unambiguously the fresh process's.
    /// </para>
    ///
    /// <para>
    /// Serialised and de-duped: a single wedge stalls every subsequent
    /// login, so several handshakes may request a recycle inside the same
    /// wedge window. One restart clears it for all of them; requests within
    /// <see cref="ProxyRestartDedupWindow"/> of the last completed restart
    /// are no-ops. Works in both owns-compose and
    /// <c>CLI_INTEGRATION_SKIP_COMPOSE=1</c> modes -- it only needs the
    /// compose CLI and the running stack, both present in CI.
    /// </para>
    /// </summary>
    public async Task RestartProxyAsync(CancellationToken ct = default)
    {
        await _proxyRestartLock.WaitAsync(ct);
        try
        {
            if (DateTime.UtcNow - _lastProxyRestartUtc < ProxyRestartDedupWindow)
                return;

            Console.Error.WriteLine(
                "[ServerFixture] recycling the proxy container to clear the " +
                "single-client session-accumulation wedge (server untouched).");

            await RunComposeAsync("restart proxy", TimeSpan.FromSeconds(60));

            // Listeners accept early in boot; the port-probe only confirms
            // the new container is up. The authoritative gate is the boot-log
            // marker below -- the proxy cannot relay a login until it has
            // bound its UDP planes to the game server.
            await WaitForPortAsync(GlobalHost, GlobalPort, TimeSpan.FromSeconds(30));
            await WaitForPortAsync(MasterHost, MasterPort, TimeSpan.FromSeconds(30));
            await WaitForPortAsync(SectorHost, SectorPort, TimeSpan.FromSeconds(30));
            await WaitForProxyBootCompleteAsync(TimeSpan.FromSeconds(30), ct);

            _lastProxyRestartUtc = DateTime.UtcNow;
        }
        finally
        {
            _proxyRestartLock.Release();
        }
    }

    /// <summary>
    /// Block until the freshly-restarted proxy logs its boot-complete marker
    /// -- the global UDP plane bind ("... (unconnected default peer)"), the
    /// last line printed before it can relay a login. See the readiness note
    /// on <see cref="RestartProxyAsync"/>. A <c>restart</c> preserves the log
    /// stream, so we read the tail (where the fresh boot lines land; no
    /// client reconnects while the restart lock is held) and poll until the
    /// marker appears. Matches on the IP-independent suffix so a loopback
    /// DNS-fallback boot is NOT silently accepted as ready -- if the proxy
    /// fell back to 127.0.0.1 it is broken and the next handshake should
    /// fail loudly rather than be masked here.
    /// </summary>
    private static async Task WaitForProxyBootCompleteAsync(TimeSpan timeout, CancellationToken ct)
    {
        const string BootCompleteMarker = "(unconnected default peer)";
        var deadline = DateTime.UtcNow + timeout;
        string lastTail = "";
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            var (exit, stdout, stderr) = await RunCaptureAsync(
                "docker", "compose logs --no-color --no-log-prefix --tail 25 proxy",
                TimeSpan.FromSeconds(10));
            lastTail = exit == 0 ? stdout : stdout + stderr;
            if (lastTail.Contains(BootCompleteMarker, StringComparison.Ordinal))
                return;
            await Task.Delay(ProbeInterval, ct);
        }
        throw new TimeoutException(
            $"Proxy did not log its boot-complete marker '{BootCompleteMarker}' " +
            $"within {timeout.TotalSeconds:F0}s of restart. Last log tail:\n{lastTail}");
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunCaptureAsync(
        string fileName, string arguments, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = RepoRoot.Path,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to launch '{fileName} {arguments}'.");
        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(timeout);
        try { await p.WaitForExitAsync(cts.Token); }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch { /* best effort */ }
            return (-1, "", $"'{fileName} {arguments}' exceeded {timeout.TotalSeconds:F0}s.");
        }
        return (p.ExitCode, await stdoutTask, await stderrTask);
    }

    public async Task DisposeAsync()
    {
        if (_ownsCompose)
        {
            // -v wipes the named volumes (pgdata, net7-ipc). Faster
            // than tearing down without; per-test-run isolation.
            await RunComposeAsync("down -v", TimeSpan.FromMinutes(2));
        }
    }

    private static async Task RunComposeAsync(string args, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo("docker", $"compose {args}")
        {
            WorkingDirectory = RepoRoot.Path,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var p = Process.Start(psi) ?? throw new InvalidOperationException(
            $"Failed to launch 'docker compose {args}'.");

        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await p.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw new TimeoutException(
                $"'docker compose {args}' exceeded {timeout.TotalSeconds:F0}s.");
        }

        if (p.ExitCode != 0)
        {
            var stdout = await p.StandardOutput.ReadToEndAsync();
            var stderr = await p.StandardError.ReadToEndAsync();
            throw new InvalidOperationException(
                $"'docker compose {args}' exited with code {p.ExitCode}.\n" +
                $"--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}");
        }
    }

    private static async Task WaitForPortAsync(string host, int port, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        Exception? lastError = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var tcp = new TcpClient();
                using var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await tcp.ConnectAsync(host, port, connectCts.Token);
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                await Task.Delay(ProbeInterval);
            }
        }
        throw new TimeoutException(
            $"TCP probe of {host}:{port} did not succeed within " +
            $"{timeout.TotalSeconds:F0}s. Last error: {lastError?.Message ?? "n/a"}");
    }
}
