// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using N7.CliClient.Session;
using Xunit;

namespace N7.CliClient.IntegrationTests.Robustness;

/// <summary>
/// What the CLI client does when the server completes the handshake
/// and then ships garbage opcode frames. The expected behaviour is
/// "abort the session loudly, do not retry, do not desync silently".
/// </summary>
public sealed class MalformedReplyTests
{
    [Fact]
    public async Task FrameWithSizeBelowHeaderSize_ReceiveThrowsInvalidData()
    {
        // Server completes the handshake, then sends an encrypted frame
        // whose header.Size = 2 — below the 4-byte header itself.
        // EncryptedTcpConnection guards against this in ReceiveAsync
        // (a non-sensical size is the canonical "RC4 desynced" signal).
        await using var bad = new ScriptedServer(async (stream, ct) =>
        {
            var (_, outbound) = await ScriptedServer.HandshakeAsServerAsync(stream, ct);

            // Hand-build a 4-byte frame: size=2 LE, opcode=0x0099 LE.
            // Then RC4-encrypt it and ship it. The client will decrypt
            // it correctly (RC4 streams are in lockstep) and see the
            // bad size.
            byte[] frame = { 0x02, 0x00, 0x99, 0x00 };
            outbound.Transform(frame);
            await stream.WriteAsync(frame, ct);
            await stream.FlushAsync(ct);

            // Hold the connection open so the client's ReceiveAsync sees
            // the frame and not an EOF first.
            try { await Task.Delay(TimeSpan.FromSeconds(2), ct); }
            catch (OperationCanceledException) { }
        });

        await using var conn = await EncryptedTcpConnection.ConnectAsync(
            bad.Host, bad.Port, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await conn.ReceiveAsync(CancellationToken.None);
        });
        Assert.Contains("desynced RC4", ex.Message);
    }

    [Fact]
    public async Task UnexpectedOpcode_TripsHealthGuard_WhenWorkflowExpectsRedirect()
    {
        // Server completes handshake then sends an opcode that does
        // not satisfy the workflow's expected response filter. The
        // BeginExpectResponse timer fires, HealthGuard trips, the
        // workflow's Token cancels — proving the "no silent retry"
        // contract end-to-end with a real TCP path.
        await using var bad = new ScriptedServer(async (stream, ct) =>
        {
            var (_, outbound) = await ScriptedServer.HandshakeAsServerAsync(stream, ct);
            byte[] frame = ScriptedServer.EncryptFrame(outbound, opcode: 0x00AA, payload: Array.Empty<byte>());
            await stream.WriteAsync(frame, ct);
            await stream.FlushAsync(ct);
            try { await Task.Delay(TimeSpan.FromSeconds(2), ct); }
            catch (OperationCanceledException) { }
        });

        using var guard = new HealthGuard();
        await using var conn = await EncryptedTcpConnection.ConnectAsync(
            bad.Host, bad.Port, CancellationToken.None);

        // Workflow: "send X, expect ServerRedirect (0x0036) back".
        using (guard.BeginExpectResponse(
            "wait-for-server-redirect",
            timeout: TimeSpan.FromMilliseconds(300),
            opcodeFilter: new OpcodeId(0x0036)))
        {
            Packet? p = await conn.ReceiveAsync(CancellationToken.None);
            Assert.NotNull(p);

            // The unexpected opcode does NOT satisfy the expectation:
            guard.OnPacketReceived(p!);

            // The 0x0036 expectation will time out.
            await Task.Delay(500);
        }

        Assert.True(guard.Tripped);
        Assert.Contains("response timeout", guard.Reason!);
        Assert.Contains("wait-for-server-redirect", guard.Reason!);
    }

    [Fact]
    public async Task FrameWithLargePayload_DecryptsAndReturnsFullPayload()
    {
        // Sanity counter-test: a perfectly well-formed but unfamiliar
        // opcode round-trips. Confirms the InvalidDataException above
        // is genuinely diagnosing a malformation, not just any frame
        // with an unregistered opcode. Without this, a regression that
        // started rejecting all unknown opcodes would silently pass
        // the malformed-frame test for the wrong reason.
        byte[] payload = new byte[64];
        for (int i = 0; i < payload.Length; i++) payload[i] = (byte) (i ^ 0x5A);

        await using var bad = new ScriptedServer(async (stream, ct) =>
        {
            var (_, outbound) = await ScriptedServer.HandshakeAsServerAsync(stream, ct);
            byte[] frame = ScriptedServer.EncryptFrame(outbound, opcode: 0x1234, payload);
            await stream.WriteAsync(frame, ct);
            await stream.FlushAsync(ct);
            try { await Task.Delay(TimeSpan.FromSeconds(2), ct); }
            catch (OperationCanceledException) { }
        });

        await using var conn = await EncryptedTcpConnection.ConnectAsync(
            bad.Host, bad.Port, CancellationToken.None);
        Packet? p = await conn.ReceiveAsync(CancellationToken.None);
        Assert.NotNull(p);
        Assert.Equal(0x1234, p!.Header.Opcode);
        Assert.Equal(payload.Length, p.Header.PayloadLength);
        Assert.Equal(payload, p.Payload.ToArray());
    }
}
