// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

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
/// The fixture does NOT seed fixture player accounts — that's Phase T
/// Item 2's job. Today the stack comes up empty; tests that need an
/// account will fail until Item 2 lands.
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

    private bool _ownsCompose;

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

        await SeedFixtureAccountsAsync(TimeSpan.FromMinutes(1));
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

    /// <summary>
    /// Apply <c>Fixtures/seed.sql</c> against the postgres container via
    /// <c>docker compose exec -T postgres psql ...</c>. Idempotent
    /// (seed.sql does DELETE + INSERT on a fixed ID range, so a re-run
    /// inside the same compose lifetime resets the seed pool cleanly).
    ///
    /// Phase N: switched from <c>mysql</c> to <c>postgres</c> (libpqxx
    /// migration). The seed file itself was ported from MySQL syntax
    /// (USE / backticks / MD5()) to Postgres (no USE, no backticks,
    /// pgcrypto digest()) at the same time.
    /// </summary>
    private async Task SeedFixtureAccountsAsync(TimeSpan timeout)
    {
        var seedPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "seed.sql");
        if (!File.Exists(seedPath))
            throw new FileNotFoundException(
                "Could not find Fixtures/seed.sql next to the test assembly. " +
                "Did the csproj <None Include=\"Fixtures/**/*\" CopyToOutputDirectory> entry survive?",
                seedPath);

        var psi = new ProcessStartInfo("docker",
            "compose exec -T -e PGPASSWORD=net7 postgres psql -U net7 -d net7_user -v ON_ERROR_STOP=1")
        {
            WorkingDirectory = RepoRoot.Path,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var p = Process.Start(psi) ?? throw new InvalidOperationException(
            "Failed to launch 'docker compose exec postgres' for seed.");

        var seedSql = await File.ReadAllTextAsync(seedPath);
        await p.StandardInput.WriteAsync(seedSql);
        p.StandardInput.Close();

        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await p.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw new TimeoutException(
                $"Fixture seed exceeded {timeout.TotalSeconds:F0}s.");
        }

        if (p.ExitCode != 0)
        {
            var stderr = await p.StandardError.ReadToEndAsync();
            throw new InvalidOperationException(
                $"Fixture seed exited with code {p.ExitCode}.\n--- stderr ---\n{stderr}");
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
