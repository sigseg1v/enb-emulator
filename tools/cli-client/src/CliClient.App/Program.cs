// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

using N7.CliClient;
using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Inbound;
using N7.CliClient.Repl;
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

if (args[0] == "repl")
{
    var repl = new Repl();
    // Workflow commands (connect, login, chat, enumerate ...) get
    // registered here as Items 9-13 land. Today the REPL ships with
    // the built-in `help` / `quit` only.
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
    return await repl.RunAsync(Console.In, Console.Out, cts.Token);
}

if (args[0] == "connect-and-login")
{
    return await RunConnectAndLoginAsync(args.Skip(1).ToArray());
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

static void PrintHelp()
{
    Console.WriteLine($"""
        {ClientInfo.Name} {ClientInfo.Version}
        Headless passive observer client for the Earth & Beyond emulator.

        usage:
          cli-client [options] [command]

        options:
          -h, --help        print this help and exit
          -v, --version     print version and exit
          --smoke           print a one-line "ok" and exit (used by CI)

        commands:
          repl              interactive REPL (help, quit; more in Items 9-13)
          connect-and-login --user X --pass Y [--login-host h] [--login-port p]
                            [--global-host h] [--global-port p] [--idle 5]
                            [--strict-tls]
                            smoke-test workflow: TLS login + global TCP connect
                            + idle for N seconds + clean disconnect

        hard rules (see plans/19-phase-s-cli-client.md):
          1. never modify the server to ease the CLI client
          2. always respect server limits (no retry storms)
          3. may request broader data than the real client if the server allows
          4. real client wins on protocol disputes
        """);
}
