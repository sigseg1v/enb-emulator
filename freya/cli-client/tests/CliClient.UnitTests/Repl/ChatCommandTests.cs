// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using N7.CliClient.Opcodes;
using N7.CliClient.Opcodes.Outbound;
using N7.CliClient.Repl;
using N7.CliClient.Repl.Commands;
using Xunit;

namespace N7.CliClient.UnitTests.Repl;

public sealed class ChatCommandTests
{
    [Theory]
    [InlineData("sector",  "hello",  ChatChannel.Broadcast, "hello")]
    [InlineData("whisper", "psst",   ChatChannel.Target,    "psst")]
    [InlineData("gm",      "help me", ChatChannel.Broadcast, "/gm help me")]
    [InlineData("dev",     "ping",   ChatChannel.Broadcast, "/dev ping")]
    [InlineData("beta",    "yo",     ChatChannel.Broadcast, "/beta yo")]
    public void MapChannel_ProducesFaithfulWire(
        string channel, string message, ChatChannel expectedType, string expectedWire)
    {
        var (type, wire) = ChatCommand.MapChannel(channel, message);
        Assert.Equal(expectedType, type);
        Assert.Equal(expectedWire, wire);
    }

    [Fact]
    public void MapChannel_SlashChannels_AreServerRoutable()
    {
        // The gm/dev/beta wires must start with '/' so the server's
        // HandleClientChat routes them through HandleSlashCommands rather
        // than broadcasting verbatim.
        Assert.StartsWith("/", ChatCommand.MapChannel("gm", "x").Wire);
        Assert.StartsWith("/", ChatCommand.MapChannel("dev", "x").Wire);
        Assert.StartsWith("/", ChatCommand.MapChannel("beta", "x").Wire);
    }

    [Fact]
    public async Task Execute_NotInSector_ReturnsError()
    {
        var ctx = new SessionContext(new OpcodeRegistry());
        var cmd = new ChatCommand(ctx);
        var output = new StringWriter();

        int rc = await cmd.ExecuteAsync(new[] { "hello" }, output, CancellationToken.None);

        Assert.Equal(1, rc);
        Assert.Contains("not in a sector", output.ToString());
    }

    [Fact]
    public void Name_IsChat()
    {
        var cmd = new ChatCommand(new SessionContext(new OpcodeRegistry()));
        Assert.Equal("chat", cmd.Name);
    }
}
