// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Logging;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Outbound;
using N7.CliClient.Session;

namespace N7.CliClient.Workflows;

/// <summary>
/// Sends a single 0x0033 CLIENT_CHAT packet on an already-established
/// CliSession. The session must have completed the global → master →
/// sector handoff first; the server's chat handler requires the speaker
/// to be a fully-attached avatar. Per Phase K dependency: this workflow
/// can be wired today but live validation against an in-game avatar is
/// gated on Phase K's ticket handoff completing — track in Phase T.
/// </summary>
/// <remarks>
/// <para>
/// Hard-rule compliance:
/// </para>
/// <list type="bullet">
///   <item>Rule 1 (don't modify the server): this workflow speaks the
///         exact wire format the real Win32 client emits — see
///         <see cref="ClientChatCodec"/>.</item>
///   <item>Rule 2 (respect server limits): single packet per call. No
///         retry. Caller is responsible for not spamming.</item>
///   <item>Rule 3 (broader queries OK if safe): N/A — this is a write,
///         not a query.</item>
///   <item>Rule 4 (real client wins on protocol): if a capture proves
///         the wire layout differs, fix the codec, not the server.</item>
/// </list>
/// </remarks>
public sealed class SendChat
{
    private readonly CliSession _session;
    private readonly ClientChatCodec _codec;
    private readonly PacketLog? _packetLog;
    private readonly ConsoleSink? _console;

    public SendChat(
        CliSession session,
        PacketLog? packetLog = null,
        ConsoleSink? console = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        _codec = new ClientChatCodec();
        _packetLog = packetLog;
        _console = console;
    }

    /// <summary>
    /// Build, log, and send a single ClientChat packet.
    /// </summary>
    /// <param name="gameId">Speaker's avatar id. Must match the avatar the
    /// session is currently attached to — the server cross-checks.</param>
    /// <param name="channel">Target channel; see <see cref="ChatChannel"/>.</param>
    /// <param name="message">UTF-7-safe ASCII text. Leading '/' is
    /// interpreted as a GM/slash command by the server.</param>
    public async Task SendAsync(
        int gameId,
        ChatChannel channel,
        string message,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(message);

        var msg = new ClientChatMessage(gameId, channel, message);
        byte[] payload = _codec.EncodeOutbound(msg);
        Packet packet = Packet.ForOpcode(_codec.Opcode.Value, payload);

        _console?.Info($"chat: gameId={gameId} channel={channel} text={Truncate(message, 60)}");
        _packetLog?.Log(PacketDirection.Outbound, packet, decoded: msg);

        await _session.SendAsync(packet, ct).ConfigureAwait(false);
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
