// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Auth;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Inbound;

namespace N7.CliClient.Repl.Commands;

/// <summary>
/// <c>login &lt;user&gt; &lt;pass&gt;</c> -- perform the TLS /AuthLogin
/// roundtrip, open the global TCP channel, send GlobalConnect, drain
/// the GlobalAvatarList, and print the avatar slots.
/// </summary>
public sealed class LoginCommand : ICommandHandler
{
    private readonly SessionContext _ctx;

    public LoginCommand(SessionContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        _ctx = ctx;
    }

    public string Name    => "login";
    public string Summary => "TLS login + global channel + show characters";
    public string Usage   => "login <user> <pass>";

    public async Task<int> ExecuteAsync(
        IReadOnlyList<string> args, TextWriter output, CancellationToken ct)
    {
        if (args.Count < 2)
        {
            await output.WriteLineAsync("usage: login <user> <pass>").ConfigureAwait(false);
            return 1;
        }

        if (_ctx.Global is not null)
        {
            await output.WriteLineAsync(
                "already logged in (a global channel is open); restart the REPL to switch accounts")
                .ConfigureAwait(false);
            return 1;
        }

        string user = args[0];
        string pass = args[1];

        await output.WriteLineAsync(
            $"auth: GET /AuthLogin -> {_ctx.Host}:{_ctx.AuthPort} (user={user})")
            .ConfigureAwait(false);

        AuthLoginResponse login;
        try
        {
            var auth = new AuthLoginClient(
                _ctx.Host, _ctx.AuthPort,
                acceptUntrustedCertificates: _ctx.AcceptUntrustedTls);
            login = await auth.LoginAsync(new AuthLoginRequest(user, pass), ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await output.WriteLineAsync($"auth transport error: {ex.Message}").ConfigureAwait(false);
            return 1;
        }

        if (!login.Valid || string.IsNullOrEmpty(login.Ticket))
        {
            await output.WriteLineAsync("auth: rejected").ConfigureAwait(false);
            return 1;
        }
        await output.WriteLineAsync($"auth: ok (ticket length={login.Ticket.Length})").ConfigureAwait(false);

        EncryptedTcpConnection global;
        try
        {
            global = await EncryptedTcpConnection.ConnectAsync(
                _ctx.Host, _ctx.GlobalPort, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await output.WriteLineAsync($"global connect failed: {ex.Message}").ConfigureAwait(false);
            return 1;
        }

        try
        {
            await SectorEnterDriver.SendGlobalConnectAsync(global, login.Ticket, ct);
            var reply = await SectorEnterDriver.DrainUntilOpcode(
                global, OpcodeId.Known.GlobalAvatarList.Value, ct);
            var avatars = (GlobalAvatarList)new GlobalAvatarListCodec()
                .DecodeInbound(reply.Payload.Span);

            _ctx.Username = user;
            _ctx.Ticket = login.Ticket;
            _ctx.Global = global;
            _ctx.AvatarList = avatars;

            await output.WriteLineAsync(
                $"global: connected ({_ctx.Host}:{_ctx.GlobalPort})").ConfigureAwait(false);
            await ListCommand.PrintAvatarsAsync(avatars, output).ConfigureAwait(false);
            return 0;
        }
        catch (Exception ex)
        {
            await global.DisposeAsync();
            await output.WriteLineAsync($"global handshake failed: {ex.Message}").ConfigureAwait(false);
            return 1;
        }
    }
}
