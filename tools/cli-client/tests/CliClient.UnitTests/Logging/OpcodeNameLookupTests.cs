// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Logging;
using N7.CliClient.Opcodes;
using Xunit;

namespace N7.CliClient.UnitTests.Logging;

public sealed class OpcodeNameLookupTests
{
    [Fact]
    public void KnownOpcodes_ResolveToTheirFieldName()
    {
        Assert.Equal("MasterJoin",     OpcodeNameLookup.TryGetName(OpcodeId.Known.MasterJoin));
        Assert.Equal("ServerRedirect", OpcodeNameLookup.TryGetName(OpcodeId.Known.ServerRedirect));
        Assert.Equal("Login",          OpcodeNameLookup.TryGetName(OpcodeId.Known.Login));
    }

    [Fact]
    public void UnknownOpcode_ReturnsNull()
    {
        Assert.Null(OpcodeNameLookup.TryGetName(new OpcodeId(0xDEAD)));
    }

    [Fact]
    public void KnownCount_MatchesNumberOfStaticFields()
    {
        // Sanity: there are >= 10 known opcodes today (see OpcodeId.Known)
        // and the count should grow as Phase S Item 15 ratchets up.
        Assert.True(OpcodeNameLookup.KnownCount >= 10,
            $"expected >= 10 known opcodes, got {OpcodeNameLookup.KnownCount}");
    }
}
