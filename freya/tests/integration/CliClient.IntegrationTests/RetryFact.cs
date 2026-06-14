// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace N7.CliClient.IntegrationTests;

/// <summary>
/// A <see cref="FactAttribute"/> that re-runs the test when it fails with a
/// TRANSIENT infrastructure error, recycling the proxy between attempts.
///
/// <para><b>Why this exists.</b> The Net7Proxy is a documented single-client
/// bridge. The integration suite drives hundreds of serial
/// connect-&gt;login-&gt;disconnect cycles through one instance, and accumulated
/// per-session proxy state can wedge a later sector session so that an in-band
/// reply is silently dropped -- the establish succeeds but a post-establish
/// drain hangs to its cancellation deadline. The ESTABLISH path already
/// recovers (recycle + retry in <c>SectorHandshake.WithProxyRecycleOnWedgeAsync</c>);
/// the post-establish drain does not, so a single test per shard occasionally
/// times out. The failing test rotates run-to-run -- the signature of a flaky
/// shared dependency, not a deterministic defect. The underlying proxy
/// never-reset global is a real proxy defect tracked in
/// <c>plans/11-phase-k-ingame.md</c>; this attribute is the test-infra
/// mitigation, NOT a server/proxy/wire change -- the server only ever sees a
/// clean disconnect + a fresh reconnect, exactly as if the client had crashed
/// and relaunched.</para>
///
/// <para><b>Why it does not mask real bugs.</b> A retry fires ONLY when the
/// failure exception is on the transient allowlist (cancellation / socket /
/// IO / timeout from a wedged stream) AND is NOT an xUnit assertion failure.
/// A byte-pin <c>Assert</c> that fails throws <c>Xunit.Sdk.XunitException</c>,
/// which is never retried -- a real wire regression hard-fails on the first
/// attempt exactly as before. See <see cref="RetryMessageBus.ShouldRetry"/>.</para>
/// </summary>
[XunitTestCaseDiscoverer(
    "N7.CliClient.IntegrationTests.RetryFactDiscoverer", "CliClient.IntegrationTests")]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class RetryFactAttribute : FactAttribute
{
    /// <summary>Maximum number of RE-runs after the first attempt (so total
    /// attempts = MaxRetries + 1). Default 2 -&gt; up to 3 attempts.</summary>
    public int MaxRetries { get; set; } = 2;
}

public sealed class RetryFactDiscoverer : IXunitTestCaseDiscoverer
{
    private readonly IMessageSink _diagnosticMessageSink;

    public RetryFactDiscoverer(IMessageSink diagnosticMessageSink)
        => _diagnosticMessageSink = diagnosticMessageSink;

    public IEnumerable<IXunitTestCase> Discover(
        ITestFrameworkDiscoveryOptions discoveryOptions,
        ITestMethod testMethod,
        IAttributeInfo factAttribute)
    {
        var maxRetries = factAttribute.GetNamedArgument<int>(nameof(RetryFactAttribute.MaxRetries));
        if (maxRetries < 0) maxRetries = 0;

        yield return new RetryTestCase(
            _diagnosticMessageSink,
            discoveryOptions.MethodDisplayOrDefault(),
            discoveryOptions.MethodDisplayOptionsOrDefault(),
            testMethod,
            maxRetries);
    }
}

public sealed class RetryTestCase : XunitTestCase
{
    private int _maxRetries;

    [EditorBrowsable(EditorBrowsableState.Never)]
    [Obsolete("Called only by the de-serializer; use the other constructor.")]
    public RetryTestCase() { }

    public RetryTestCase(
        IMessageSink diagnosticMessageSink,
        TestMethodDisplay defaultMethodDisplay,
        TestMethodDisplayOptions defaultMethodDisplayOptions,
        ITestMethod testMethod,
        int maxRetries)
        : base(diagnosticMessageSink, defaultMethodDisplay, defaultMethodDisplayOptions, testMethod)
        => _maxRetries = maxRetries;

    public override async Task<RunSummary> RunAsync(
        IMessageSink diagnosticMessageSink,
        IMessageBus messageBus,
        object[] constructorArguments,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource)
    {
        var attempt = 0;
        while (true)
        {
            attempt++;
            // Buffer this attempt's messages so a transient failure can be
            // discarded silently before we retry. A real (assertion) failure,
            // a pass, or the final give-up flushes through to the real bus.
            var buffer = new RetryMessageBus(messageBus);
            var summary = await base.RunAsync(
                diagnosticMessageSink, buffer, constructorArguments, aggregator, cancellationTokenSource);

            var lastAttempt = attempt > _maxRetries;
            if (summary.Failed == 0 || lastAttempt || cancellationTokenSource.IsCancellationRequested
                || !buffer.ShouldRetry())
            {
                buffer.Flush();
                return summary;
            }

            // Transient infra failure with retries left: drop the buffered
            // failure messages, recycle the proxy, and re-run from scratch
            // (a fresh test-class ctor -> fresh CLI client -> fresh establish).
            diagnosticMessageSink.OnMessage(new DiagnosticMessage(
                $"[RetryFact] {DisplayName} attempt {attempt} failed with a transient " +
                $"proxy-wedge error ({buffer.FailureSummary}); recycling the proxy and retrying."));
            try
            {
                if (ServerFixture.Current is { } fixture)
                    await fixture.RestartProxyAsync(cancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                // If the recycle itself fails we cannot improve the next
                // attempt; surface the original failure rather than hiding it.
                diagnosticMessageSink.OnMessage(new DiagnosticMessage(
                    $"[RetryFact] proxy recycle before retry failed: {ex.Message}; " +
                    "flushing the original failure."));
                buffer.Flush();
                return summary;
            }
        }
    }

    public override void Serialize(IXunitSerializationInfo data)
    {
        base.Serialize(data);
        data.AddValue(nameof(_maxRetries), _maxRetries);
    }

    public override void Deserialize(IXunitSerializationInfo data)
    {
        base.Deserialize(data);
        _maxRetries = data.GetValue<int>(nameof(_maxRetries));
    }
}

/// <summary>
/// Buffers all test messages for one attempt. <see cref="Flush"/> forwards
/// them to the real bus; if the attempt is going to be retried instead, the
/// buffer is dropped (never flushed) so the transient failure is invisible.
/// </summary>
internal sealed class RetryMessageBus : IMessageBus
{
    private readonly IMessageBus _inner;
    private readonly List<IMessageSinkMessage> _messages = new();

    // Exact xUnit assertion base type. A failure carrying this in its
    // exception chain is a real test failure and must NEVER be retried.
    private const string AssertionExceptionType = "Xunit.Sdk.XunitException";

    // Transient infrastructure failures produced by a wedged proxy stream:
    // the per-test cancellation deadline firing on a silent ReceiveAsync, or
    // the underlying socket faulting on that cancellation.
    private static readonly string[] TransientExceptionTypes =
    {
        "System.OperationCanceledException",
        "System.Threading.Tasks.TaskCanceledException",
        "System.Net.Sockets.SocketException",
        "System.IO.IOException",
        "System.TimeoutException",
    };

    public RetryMessageBus(IMessageBus inner) => _inner = inner;

    public string FailureSummary { get; private set; } = "";

    public bool QueueMessage(IMessageSinkMessage message)
    {
        _messages.Add(message);
        // Keep executing; nothing is forwarded until Flush so we retain the
        // option to discard. Returning true never cancels the run.
        return true;
    }

    /// <summary>
    /// True only when every failure in this attempt is a transient infra
    /// error and none is an xUnit assertion failure.
    /// </summary>
    public bool ShouldRetry()
    {
        var failures = _messages.OfType<ITestFailed>().ToList();
        if (failures.Count == 0) return false;

        foreach (var f in failures)
        {
            var types = f.ExceptionTypes ?? Array.Empty<string>();
            FailureSummary = string.Join(", ", types);

            if (types.Any(t => t == AssertionExceptionType))
                return false; // a real assertion failure -- do not retry

            var anyTransient = types.Any(t =>
                TransientExceptionTypes.Any(tt => string.Equals(t, tt, StringComparison.Ordinal)));
            if (!anyTransient)
                return false; // unknown failure shape -- fail loudly, don't retry
        }
        return true;
    }

    public void Flush()
    {
        foreach (var message in _messages)
            _inner.QueueMessage(message);
        _messages.Clear();
    }

    public void Dispose() { }
}
