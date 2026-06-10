// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Buffers.Binary;
using N7.CliClient.Logging;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;

namespace N7.CliClient.Repl;

/// <summary>
/// The proxy's view of its own proxy&lt;-&gt;server link, as reported over the
/// 0x3009/0x300A introspection exchange. The CLI never negotiates the
/// proxy&lt;-&gt;server DTLS itself (it speaks to the proxy), so the proxy is the
/// only ground-truth source for whether that leg is encrypted.
/// </summary>
/// <param name="DtlsRequired">
/// The proxy is enforcing DTLS on the server leg. False only when the operator
/// explicitly opted into plaintext (NET7_DTLS_ALLOW_PLAINTEXT), e.g. a local
/// play-cli bridge.
/// </param>
/// <param name="LiveDtlsAssociations">
/// Count of handshake-complete DTLS associations to the server (0 in plaintext
/// mode, and 0 before the first server leg has finished its handshake).
/// </param>
/// <param name="Connected">The proxy has an active server-side UDP link.</param>
public sealed record ProxyStatus(bool DtlsRequired, int LiveDtlsAssociations, bool Connected)
{
    /// <summary>Wire size of the proxy's ProxyStatusReply (4 packed bytes).</summary>
    private const int ReplyBytes = 4;

    /// <summary>
    /// Ask the proxy over <paramref name="global"/> (the CLI's global-plane
    /// connection) for its link state. Returns null if the proxy does not
    /// answer within <paramref name="timeout"/> -- an older proxy that predates
    /// this opcode simply never replies, so the caller renders "unknown" rather
    /// than hanging.
    /// </summary>
    public static async Task<ProxyStatus?> QueryAsync(
        EncryptedTcpConnection global, TimeSpan timeout, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(global);

        await global.SendAsync(
            Packet.ForOpcode(OpcodeId.Known.CliStatusRequest.Value, ReadOnlyMemory<byte>.Empty),
            ct).ConfigureAwait(false);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            var reply = await SectorEnterDriver
                .DrainUntilOpcode(global, OpcodeId.Known.CliStatusReply.Value, cts.Token)
                .ConfigureAwait(false);
            return Parse(reply.Payload.Span);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;   // proxy too old / no reply in time
        }
    }

    /// <summary>Decode the 4-byte ProxyStatusReply payload.</summary>
    public static ProxyStatus Parse(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < ReplyBytes)
            throw new ArgumentException(
                $"ProxyStatusReply is {payload.Length}B but the wire format is {ReplyBytes}B");
        return new ProxyStatus(
            DtlsRequired: payload[0] != 0,
            LiveDtlsAssociations: payload[1],
            Connected: payload[2] != 0);
    }

    /// <summary>
    /// The coloured one-liner shown at login: <c>dtls-encryption=on</c> in green
    /// when the server leg is encrypted and a live association proves it,
    /// <c>dtls-encryption=DISABLED</c> in red caps when the operator opted into
    /// plaintext, and a yellow <c>PENDING</c>/<c>unknown</c> for the honest
    /// in-between states. Never overstates encryption it cannot confirm.
    /// </summary>
    public static string DtlsLine(ProxyStatus? status)
    {
        string label = AnsiPalette.Muted("dtls-encryption=");
        if (status is null)
            return label + AnsiPalette.Warn("unknown");
        if (!status.DtlsRequired)
            return label + AnsiPalette.Colorize(AnsiPalette.Red, "DISABLED");
        if (status.LiveDtlsAssociations > 0)
            return label + AnsiPalette.Colorize(AnsiPalette.Green, "on");
        // Enforced, but no handshake has completed yet on this leg.
        return label + AnsiPalette.Warn("PENDING");
    }
}
