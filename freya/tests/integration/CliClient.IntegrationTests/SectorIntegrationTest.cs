// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.IntegrationTests.Opcodes;
using Xunit;

namespace N7.CliClient.IntegrationTests;

/// <summary>
/// Base class for the sector-login integration tests. Owns the per-test
/// <see cref="ServerFixture"/>/<see cref="ClientFixture"/> wiring and, more
/// importantly, the post-test character cleanup that every test used to repeat
/// by hand:
/// <code>
///     finally
///     {
///         using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
///         try { await SectorHandshake.DeleteCreatedCharacterAsync(session.Global, slot, cleanupCts.Token); }
///         catch { /* best-effort cleanup */ }
///     }
/// </code>
/// Instead of that copy-pasted block, a test wraps each established session in
/// <see cref="Track{T}"/> and lets the base <see cref="DisposeAsync"/> delete
/// the character and dispose the session afterwards -- in reverse establish
/// order, best-effort, exactly as the old finally did but in one place. The
/// session already carries the slot it belongs to
/// (<see cref="SectorHandshake.Session.Slot"/>), so no slot needs threading
/// through.
///
/// <para>
/// xUnit constructs a fresh instance of the derived class per test and runs
/// <see cref="InitializeAsync"/> before / <see cref="DisposeAsync"/> after each
/// one, so the tracked-session list is naturally per-test. Cleanup runs even
/// when the test body throws -- xUnit still calls DisposeAsync -- which is why
/// tests no longer need their own try/finally.
/// </para>
///
/// <para>
/// Concrete derived classes keep their own <c>[Collection(ServerCollection.Name)]</c>
/// attribute -- xUnit collection discovery does not reliably read it through
/// inheritance, and the serial-execution contract the whole suite relies on
/// (single shared docker stack) must not regress to parallel.
/// </para>
/// </summary>
public abstract class SectorIntegrationTest : IAsyncLifetime
{
    protected readonly ServerFixture _server;
    protected readonly ClientFixture _client;

    private readonly List<SectorHandshake.Session> _tracked = new();

    protected SectorIntegrationTest(ServerFixture server)
    {
        _server = server;
        _client = new ClientFixture(server);
    }

    /// <summary>
    /// Register an established session for automatic teardown after the test.
    /// Returns the session so it can be used inline:
    /// <c>var session = Track(await SectorHandshake.EstablishAsync(...));</c>.
    /// </summary>
    protected SectorHandshake.Session Track(SectorHandshake.Session session)
    {
        _tracked.Add(session);
        return session;
    }

    public virtual Task InitializeAsync() => Task.CompletedTask;

    public virtual async Task DisposeAsync()
    {
        // Reverse establish order: tear down the most recently established
        // session first (mirrors nested `await using` disposal order for the
        // two-player tests). Delete-then-dispose so DeleteCreatedCharacterAsync
        // still runs on the live global connection before the session's clean
        // logoff closes the sockets.
        for (int i = _tracked.Count - 1; i >= 0; i--)
        {
            var session = _tracked[i];
            try
            {
                using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await SectorHandshake.DeleteCreatedCharacterAsync(
                    session.Global, session.Slot, cleanupCts.Token);
            }
            catch { /* best-effort cleanup; primary test failure already reported */ }

            await session.DisposeAsync();
        }
    }
}
