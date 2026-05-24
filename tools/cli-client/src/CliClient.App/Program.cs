// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

using N7.CliClient;

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

Console.Error.WriteLine($"unknown argument: {args[0]}");
Console.Error.WriteLine("run with --help for usage.");
return 2;

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
          (workflows + REPL land in Items 8/9 of plans/19-phase-s-cli-client.md)

        hard rules (see plans/19-phase-s-cli-client.md):
          1. never modify the server to ease the CLI client
          2. always respect server limits (no retry storms)
          3. may request broader data than the real client if the server allows
          4. real client wins on protocol disputes
        """);
}
