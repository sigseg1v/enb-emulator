// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient;
using N7.CliClient.Logging;
using N7.CliClient.Net;
using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Inbound;
using N7.CliClient.Opcodes.Outbound;
using N7.CliClient.Repl;
using N7.CliClient.Repl.Commands;
using N7.CliClient.Session;
using N7.CliClient.Workflows;

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    PrintHelp();
    return 0;
}

if (args[0] is "-v" or "--version")
{
    Console.WriteLine($"{ClientInfo.Name} {ClientInfo.Version} (phase {ClientInfo.Phase})");
    return 0;
}

if (args[0] == "--smoke")
{
    Console.WriteLine($"ok: {ClientInfo.Name} {ClientInfo.Version}");
    return 0;
}

if (args[0] is "repl" or "start")
{
    var replRegistry = new OpcodeRegistry();
    replRegistry.RegisterAllNamedOpaque();
    replRegistry.Register(new ServerRedirectCodec());
    replRegistry.Register(new GlobalAvatarListCodec());
    replRegistry.Register(new GlobalTicketCodec());

    await using var sessionCtx = new SessionContext(replRegistry);
    var repl = new Repl();
    repl.Register(new ConnectCommand(sessionCtx));
    repl.Register(new LoginCommand(sessionCtx));
    repl.Register(new ListCommand(sessionCtx));
    repl.Register(new CreateCommand(sessionCtx));
    repl.Register(new EnterCommand(sessionCtx));

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    Console.WriteLine($"{ClientInfo.Name} {ClientInfo.Version}");
    Console.WriteLine("type 'help' for commands, 'quit' to exit");
    return await repl.RunAsync(Console.In, Console.Out, cts.Token);
}

if (args[0] == "connect-and-login")
{
    return await RunConnectAndLoginAsync(args.Skip(1).ToArray());
}

if (args[0] == "send-chat")
{
    return await RunSendChatAsync(args.Skip(1).ToArray());
}

Console.Error.WriteLine($"unknown argument: {args[0]}");
Console.Error.WriteLine("run with --help for usage.");
return 2;

static async Task<int> RunConnectAndLoginAsync(string[] argv)
{
    string? user = null, pass = null;
    string loginHost = "127.0.0.1", globalHost = "127.0.0.1";
    int loginPort = 443, globalPort = 3500;
    int idleSeconds = 5;
    bool strictTls = false;

    for (int i = 0; i < argv.Length; i++)
    {
        string a = argv[i];
        string? next() => i + 1 < argv.Length ? argv[++i] : null;
        switch (a)
        {
            case "--user":        user = next(); break;
            case "--pass":        pass = next(); break;
            case "--login-host":  loginHost = next() ?? loginHost; break;
            case "--login-port":  loginPort = int.Parse(next() ?? "443"); break;
            case "--global-host": globalHost = next() ?? globalHost; break;
            case "--global-port": globalPort = int.Parse(next() ?? "3500"); break;
            case "--idle":        idleSeconds = int.Parse(next() ?? "5"); break;
            case "--strict-tls":  strictTls = true; break;
            default:
                Console.Error.WriteLine($"unknown option: {a}");
                return 2;
        }
    }

    if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
    {
        Console.Error.WriteLine("--user and --pass are required");
        return 2;
    }

    var registry = new OpcodeRegistry();
    registry.RegisterAllNamedOpaque();
    registry.Register(new ServerRedirectCodec());

    var options = new ConnectAndLoginOptions
    {
        Username = user,
        Password = pass,
        LoginHost = loginHost,
        LoginPort = loginPort,
        GlobalHost = globalHost,
        GlobalPort = globalPort,
        IdleDuration = TimeSpan.FromSeconds(idleSeconds),
        AcceptUntrustedLoginCertificate = !strictTls,
    };

    var console = new N7.CliClient.Logging.ConsoleSink();
    var workflow = new ConnectAndLogin(options, registry, packetLog: null, console: console);

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    using var guard = new HealthGuard(sink: console);
    var result = await workflow.RunAsync(guard, cts.Token);

    Console.WriteLine($"result: login_valid={result.LoginValid} stage={result.Stage} inbound={result.InboundPackets}");
    if (result.Aborted is not null)
    {
        Console.WriteLine($"aborted: {result.Aborted}");
        return 1;
    }
    return 0;
}

static async Task<int> RunSendChatAsync(string[] argv)
{
    string? user = null, pass = null, message = null;
    string loginHost = "127.0.0.1", globalHost = "127.0.0.1";
    int loginPort = 443, globalPort = 3500;
    int gameId = 0;
    ChatChannel channel = ChatChannel.Broadcast;
    bool strictTls = false;

    for (int i = 0; i < argv.Length; i++)
    {
        string a = argv[i];
        string? next() => i + 1 < argv.Length ? argv[++i] : null;
        switch (a)
        {
            case "--user":        user = next(); break;
            case "--pass":        pass = next(); break;
            case "--login-host":  loginHost = next() ?? loginHost; break;
            case "--login-port":  loginPort = int.Parse(next() ?? "443"); break;
            case "--global-host": globalHost = next() ?? globalHost; break;
            case "--global-port": globalPort = int.Parse(next() ?? "3500"); break;
            case "--game-id":     gameId = int.Parse(next() ?? "0"); break;
            case "--channel":     channel = ParseChannel(next()); break;
            case "--message":     message = next(); break;
            case "--strict-tls":  strictTls = true; break;
            default:
                Console.Error.WriteLine($"unknown option: {a}");
                return 2;
        }
    }

    if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
    {
        Console.Error.WriteLine("--user and --pass are required");
        return 2;
    }
    if (string.IsNullOrEmpty(message))
    {
        Console.Error.WriteLine("--message is required and must be non-empty");
        return 2;
    }
    if (gameId == 0)
    {
        // The server cross-checks GameId against the avatar attached to
        // the session. Phase S Items 10-12 / Phase K still need to wire
        // avatar-select before we can read this value end-to-end; pass it
        // explicitly for now.
        Console.Error.WriteLine("--game-id is required (non-zero avatar id)");
        return 2;
    }

    var registry = new OpcodeRegistry();
    registry.RegisterAllNamedOpaque();
    registry.Register(new ServerRedirectCodec());

    var console = new N7.CliClient.Logging.ConsoleSink();
    var options = new ConnectAndLoginOptions
    {
        Username = user,
        Password = pass,
        LoginHost = loginHost,
        LoginPort = loginPort,
        GlobalHost = globalHost,
        GlobalPort = globalPort,
        IdleDuration = TimeSpan.Zero,
        AcceptUntrustedLoginCertificate = !strictTls,
    };

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    using var guard = new HealthGuard(sink: console);

    // We need our own session here (ConnectAndLogin owns its session
    // internally and disposes it). Inline the auth + connect sequence.
    var auth = new N7.CliClient.Auth.AuthLoginClient(
        loginHost, loginPort,
        acceptUntrustedCertificates: !strictTls);
    var login = await auth.LoginAsync(
        new N7.CliClient.Auth.AuthLoginRequest(user, pass), cts.Token);
    if (!login.Valid || string.IsNullOrEmpty(login.Ticket))
    {
        Console.Error.WriteLine("login rejected");
        return 1;
    }

    await using var session = new CliSession(registry, login.Ticket);
    await session.ConnectGlobalAsync(globalHost, globalPort, cts.Token);

    var chat = new SendChat(session, packetLog: null, console: console);
    await chat.SendAsync(gameId, channel, message, cts.Token);

    Console.WriteLine($"sent: gameId={gameId} channel={channel}");
    return 0;
}

static ChatChannel ParseChannel(string? s) =>
    (s ?? "broadcast").ToLowerInvariant() switch
    {
        "target"    => ChatChannel.Target,
        "group"     => ChatChannel.Group,
        "guild"     => ChatChannel.Guild,
        "local"     => ChatChannel.Local,
        "broadcast" => ChatChannel.Broadcast,
        _ => throw new ArgumentException(
            $"unknown chat channel '{s}' (use: target|group|guild|local|broadcast)"),
    };

static void PrintHelp()
{
    Console.WriteLine($"""
        {ClientInfo.Name} {ClientInfo.Version}
        Headless passive observer client for the Earth & Beyond emulator.

        usage:
          enb-cli [options] [command]

        options:
          -h, --help        print this help and exit
          -v, --version     print version and exit
          --smoke           print a one-line "ok" and exit (used by CI)

        commands:
          start             interactive REPL (alias: repl)
                            commands inside the prompt:
                              connect <host>[:port]
                              login   <user> <pass>
                              list
                              create  <class> <firstname>   (e.g. JE Griever)
                              enter   <firstname>
                              help, quit
          connect-and-login --user X --pass Y [--login-host h] [--login-port p]
                            [--global-host h] [--global-port p] [--idle 5]
                            [--strict-tls]
                            smoke-test workflow: TLS login + global TCP connect
                            + idle for N seconds + clean disconnect
          send-chat         --user X --pass Y --game-id N --message "text"
                            [--channel target|group|guild|local|broadcast]
                            [--login-host h] [--login-port p]
                            [--global-host h] [--global-port p] [--strict-tls]
                            log in, connect, send one ClientChat packet, exit.
                            NOTE: server expects --game-id to match the avatar
                            currently attached to the session — Phase K's avatar
                            handoff must be live for this to do anything visible.

        hard rules (see plans/19-phase-s-cli-client.md):
          1. never modify the server to ease the CLI client
          2. always respect server limits (no retry storms)
          3. may request broader data than the real client if the server allows
          4. real client wins on protocol disputes
        """);
}
