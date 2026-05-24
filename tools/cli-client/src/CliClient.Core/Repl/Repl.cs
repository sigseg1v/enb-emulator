// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

namespace N7.CliClient.Repl;

/// <summary>
/// Read-eval-print loop. Reads one line at a time from a
/// <see cref="TextReader"/> (defaults to <see cref="Console.In"/>),
/// splits on whitespace, dispatches to the registered
/// <see cref="ICommandHandler"/> for the first token, and prints the
/// result back to a <see cref="TextWriter"/> (defaults to
/// <see cref="Console.Out"/>).
/// </summary>
/// <remarks>
/// <para>
/// Built-in commands: <c>help</c>, <c>quit</c>. Everything else
/// (<c>connect</c>, <c>login</c>, <c>chat</c>, <c>enumerate ...</c>)
/// gets registered by the workflow layer in Items 9-13 — keeping the
/// REPL itself a thin dispatcher with no network knowledge.
/// </para>
/// <para>
/// Tokenisation: whitespace-split with double-quote support
/// (<c>chat "hello world"</c> → two args). No shell-style escape
/// sequences — the REPL is for interactive use, not scripting.
/// </para>
/// </remarks>
public sealed class Repl
{
    private readonly Dictionary<string, ICommandHandler> _commands =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly string _prompt;

    public Repl(string prompt = "> ")
    {
        ArgumentNullException.ThrowIfNull(prompt);
        _prompt = prompt;
        Register(new HelpCommand(this));
        Register(new QuitCommand());
    }

    /// <summary>
    /// Register (or replace) a command. Names are case-insensitive.
    /// Throws if <paramref name="handler"/> is null or its Name is empty.
    /// </summary>
    public void Register(ICommandHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        if (string.IsNullOrWhiteSpace(handler.Name))
            throw new ArgumentException("handler has empty Name", nameof(handler));
        _commands[handler.Name] = handler;
    }

    /// <summary>The set of registered commands, sorted by name (snapshot).</summary>
    public IReadOnlyList<ICommandHandler> Commands
        => _commands.Values.OrderBy(h => h.Name, StringComparer.OrdinalIgnoreCase).ToArray();

    /// <summary>Look up a handler by name, or null if not registered.</summary>
    public ICommandHandler? Find(string name)
        => _commands.TryGetValue(name, out var h) ? h : null;

    /// <summary>
    /// Run the loop until input EOF, a handler returns a negative exit
    /// code, or <paramref name="ct"/> is cancelled. Returns the last
    /// non-zero exit code (or 0).
    /// </summary>
    public async Task<int> RunAsync(
        TextReader? input = null,
        TextWriter? output = null,
        CancellationToken ct = default)
    {
        input ??= Console.In;
        output ??= Console.Out;

        int lastExit = 0;
        while (!ct.IsCancellationRequested)
        {
            await output.WriteAsync(_prompt).ConfigureAwait(false);
            await output.FlushAsync(ct).ConfigureAwait(false);

            string? line = await input.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) return lastExit; // EOF

            line = line.Trim();
            if (line.Length == 0) continue;

            var tokens = Tokenise(line);
            if (tokens.Count == 0) continue;

            string cmd = tokens[0];
            var args = tokens.Skip(1).ToArray();

            if (!_commands.TryGetValue(cmd, out var handler))
            {
                await output.WriteLineAsync($"unknown command: {cmd}").ConfigureAwait(false);
                await output.WriteLineAsync("type 'help' for a list").ConfigureAwait(false);
                lastExit = 1;
                continue;
            }

            int rc;
            try
            {
                rc = await handler.ExecuteAsync(args, output, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return lastExit;
            }
            catch (Exception ex)
            {
                await output.WriteLineAsync($"error: {ex.Message}").ConfigureAwait(false);
                lastExit = 1;
                continue;
            }

            if (rc < 0) return -(rc + 1); // negative = quit; -1 → 0, -2 → 1, ...
            if (rc > 0) lastExit = rc;
        }
        return lastExit;
    }

    /// <summary>
    /// Split a line into tokens. Whitespace-separated, with double-quote
    /// grouping: <c>chat "hello world" team</c> → ["chat", "hello world", "team"].
    /// Unclosed quotes are tolerated — the rest of the line is one token.
    /// </summary>
    public static IReadOnlyList<string> Tokenise(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuote = false;
        foreach (char c in line)
        {
            if (c == '"')
            {
                inQuote = !inQuote;
                continue;
            }
            if (!inQuote && char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }
            current.Append(c);
        }
        if (current.Length > 0) tokens.Add(current.ToString());
        return tokens;
    }

    private sealed class HelpCommand : ICommandHandler
    {
        private readonly Repl _owner;
        public HelpCommand(Repl owner) { _owner = owner; }

        public string Name    => "help";
        public string Summary => "list commands or show usage for one";
        public string Usage   => "help [command]";

        public async Task<int> ExecuteAsync(
            IReadOnlyList<string> args, TextWriter output, CancellationToken ct)
        {
            if (args.Count == 0)
            {
                await output.WriteLineAsync("commands:").ConfigureAwait(false);
                foreach (var h in _owner.Commands)
                    await output.WriteLineAsync($"  {h.Name,-12} {h.Summary}").ConfigureAwait(false);
                return 0;
            }
            var target = _owner.Find(args[0]);
            if (target is null)
            {
                await output.WriteLineAsync($"unknown command: {args[0]}").ConfigureAwait(false);
                return 1;
            }
            await output.WriteLineAsync($"{target.Name}: {target.Summary}").ConfigureAwait(false);
            await output.WriteLineAsync($"usage: {target.Usage}").ConfigureAwait(false);
            return 0;
        }
    }

    private sealed class QuitCommand : ICommandHandler
    {
        public string Name    => "quit";
        public string Summary => "exit the REPL";
        public string Usage   => "quit";

        public Task<int> ExecuteAsync(
            IReadOnlyList<string> args, TextWriter output, CancellationToken ct)
            => Task.FromResult(-1);
    }
}
