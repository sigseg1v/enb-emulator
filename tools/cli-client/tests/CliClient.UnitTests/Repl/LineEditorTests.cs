// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Repl;
using Xunit;

namespace N7.CliClient.UnitTests.Repl;

public sealed class LineEditorTests
{
    // A StringReader is never Console.In, so the editor takes the plain
    // ReadLine fallback -- which is what the test harness and the
    // `just *-replay` recipes rely on.

    [Fact]
    public async Task ReadLine_NonTty_FallsBackToReadLine_AndWritesPrompt()
    {
        var editor = new LineEditor(() => Array.Empty<CommandSpec>());
        var input  = new StringReader("connect 1.2.3.4\n");
        var output = new StringWriter();

        string? line = await editor.ReadLineAsync("> ", input, output, CancellationToken.None);

        Assert.Equal("connect 1.2.3.4", line);
        Assert.Contains("> ", output.ToString());
    }

    [Fact]
    public async Task ReadLine_NonTty_Eof_ReturnsNull()
    {
        var editor = new LineEditor(() => Array.Empty<CommandSpec>());
        string? line = await editor.ReadLineAsync(
            "> ", new StringReader(""), new StringWriter(), CancellationToken.None);
        Assert.Null(line);
    }
}
