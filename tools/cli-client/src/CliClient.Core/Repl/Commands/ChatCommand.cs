// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Outbound;

namespace N7.CliClient.Repl.Commands;

/// <summary>
/// <c>chat [sector|gm|dev|beta|whisper] &lt;message&gt;</c> -- send one
/// 0x0033 CLIENT_CHAT packet on the in-sector connection. Defaults to the
/// sector channel when the first token isn't a recognised channel.
/// </summary>
/// <remarks>
/// <para>Channel mapping (faithful to <c>Player::HandleClientChat</c> and
/// the slash-command parser in <c>HandleSlashCommands</c>):</para>
/// <list type="bullet">
///   <item><c>sector</c>  → Type 4 (To Entire Sector / BroadcastChat).</item>
///   <item><c>whisper</c> → Type 0 (To Target -- goes to the currently
///         selected target; the CLI has no target picker, so this only
///         lands if something is targeted).</item>
///   <item><c>gm</c> / <c>dev</c> / <c>beta</c> → the message text the real
///         client emits for those channels: a leading <c>/gm </c> etc. The
///         server routes it via <c>ChatSendChannel</c> and gates it on
///         admin level -- the CLI does not (and must not) bypass that gate;
///         a non-privileged account simply gets no broadcast.</item>
/// </list>
/// <para>Outbound echo is handled by the PacketSent hook in
/// <see cref="SessionContext"/>, so this command prints no confirmation of
/// its own -- the <c>--&gt; [channel] you: ...</c> line is the receipt.</para>
/// </remarks>
public sealed class ChatCommand : ICommandHandler
{
    private static readonly string[] KnownChannels =
        { "sector", "gm", "dev", "beta", "whisper" };

    private readonly SessionContext _ctx;

    public ChatCommand(SessionContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        _ctx = ctx;
    }

    public string Name    => "chat";
    public string Summary => "send a chat message (default channel: sector)";
    public string Usage   => "chat [sector|gm|dev|beta|whisper] <message>";
    public string? Placeholder => "[sector|gm|dev|beta|whisper] <message>";

    // Only usable once attached to an in-sector avatar.
    public bool Available => _ctx.Sector is not null && _ctx.GameId is not null;

    public async Task<int> ExecuteAsync(
        IReadOnlyList<string> args, TextWriter output, CancellationToken ct)
    {
        if (_ctx.Sector is null)
        {
            await output.WriteLineAsync("not in a sector -- run `enter` first").ConfigureAwait(false);
            return 1;
        }
        if (_ctx.GameId is null)
        {
            await output.WriteLineAsync("no avatar id for this session -- enter a sector first").ConfigureAwait(false);
            return 1;
        }
        if (args.Count == 0)
        {
            await output.WriteLineAsync($"usage: {Usage}").ConfigureAwait(false);
            return 1;
        }

        // First token is the channel iff it's one of the known keywords;
        // otherwise the whole line is a sector message.
        string channel;
        IEnumerable<string> words;
        if (KnownChannels.Contains(args[0], StringComparer.OrdinalIgnoreCase))
        {
            channel = args[0].ToLowerInvariant();
            words = args.Skip(1);
        }
        else
        {
            channel = "sector";
            words = args;
        }

        string message = string.Join(' ', words);
        if (message.Length == 0)
        {
            await output.WriteLineAsync($"nothing to say on '{channel}' -- give a message").ConfigureAwait(false);
            return 1;
        }

        var (type, wire) = MapChannel(channel, message);
        var codec = new ClientChatCodec();
        byte[] payload = codec.EncodeOutbound(new ClientChatMessage(_ctx.GameId.Value, type, wire));
        Packet packet = Packet.ForOpcode(OpcodeId.Known.ClientChat.Value, payload);

        // SendAsync fires PacketSent synchronously -> SessionContext echoes
        // the outbound line, so no extra print here.
        await _ctx.Sector.SendAsync(packet, ct).ConfigureAwait(false);
        return 0;
    }

    /// <summary>
    /// Map a channel keyword + raw message to the (Type byte, wire string)
    /// the real Win32 client would emit. Slash-command channels embed the
    /// command in the string; the Type byte is then ignored server-side.
    /// </summary>
    internal static (ChatChannel Type, string Wire) MapChannel(string channel, string message)
        => channel switch
        {
            "sector"  => (ChatChannel.Broadcast, message),
            "whisper" => (ChatChannel.Target, message),
            "gm"      => (ChatChannel.Broadcast, "/gm " + message),
            "dev"     => (ChatChannel.Broadcast, "/dev " + message),
            "beta"    => (ChatChannel.Broadcast, "/beta " + message),
            _         => (ChatChannel.Broadcast, message),
        };
}
