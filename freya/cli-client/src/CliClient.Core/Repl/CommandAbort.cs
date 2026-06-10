// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

namespace N7.CliClient.Repl;

/// <summary>
/// Lets a long-running command (move / warp) be aborted with the Escape key.
/// While a command runs the interactive <see cref="LineEditor"/> is parked
/// (it already returned the committed line), so the console key reader is
/// free -- we poll it on a worker and trip a linked token source on ESC.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Begin"/> returns a <see cref="CancellationTokenSource"/> linked
/// to the REPL's own token (Ctrl-C) plus an ESC watcher. The caller flies its
/// loop on <c>cts.Token</c>; on cancellation it distinguishes an ESC abort
/// (the outer <paramref name="ct"/> is NOT cancelled) from a real Ctrl-C and
/// returns cleanly instead of rethrowing. The caller MUST dispose the returned
/// handle (await its DisposeAsync) to stop the watcher and release the key
/// reader before the REPL reads the next line.
/// </para>
/// <para>
/// Non-interactive (piped stdin/stdout, the test harness) gets a plain linked
/// CTS and a no-op watcher -- there is no console to poll.
/// </para>
/// </remarks>
internal sealed class CommandAbort : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts;
    private readonly Task _watcher;

    private CommandAbort(CancellationTokenSource cts, Task watcher)
    {
        _cts = cts;
        _watcher = watcher;
    }

    /// <summary>Token the command flies its loop on (cancels on ESC or Ctrl-C).</summary>
    public CancellationToken Token => _cts.Token;

    /// <summary>
    /// True when cancellation came from ESC (this watcher) rather than the
    /// outer token. Lets the caller swallow ESC and return 0 while still
    /// propagating a genuine Ctrl-C.
    /// </summary>
    public bool AbortedByEscape(CancellationToken outer)
        => _cts.IsCancellationRequested && !outer.IsCancellationRequested;

    public static CommandAbort Begin(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (!LineEditor.IsInteractive)
            return new CommandAbort(cts, Task.CompletedTask);

        var watcher = Task.Run(() =>
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    if (Console.KeyAvailable)
                    {
                        var k = Console.ReadKey(intercept: true);
                        if (k.Key == ConsoleKey.Escape) { cts.Cancel(); return; }
                        // Any other key while moving is swallowed (consumed
                        // above): keystrokes during a flight are abort intent,
                        // not input for the next prompt.
                    }
                    else
                    {
                        Thread.Sleep(20);
                    }
                }
            }
            catch
            {
                // Console went away mid-flight -- nothing to abort on.
            }
        });
        return new CommandAbort(cts, watcher);
    }

    public async ValueTask DisposeAsync()
    {
        try { _cts.Cancel(); } catch { }
        try { await _watcher.ConfigureAwait(false); } catch { }
        _cts.Dispose();
    }
}
