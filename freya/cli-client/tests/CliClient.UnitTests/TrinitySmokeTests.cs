// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using Xunit;

namespace N7.CliClient.UnitTests;

/// <summary>
/// Trinity smoke check: verifies the library + console + tests are all
/// wired into the same build graph. If this passes, Item 1 of Phase S
/// (scaffold) is structurally sound.
/// </summary>
public class TrinitySmokeTests
{
    [Fact]
    public void CoreLibraryIsReferenced()
    {
        Assert.False(string.IsNullOrEmpty(ClientInfo.Name));
        Assert.False(string.IsNullOrEmpty(ClientInfo.Version));
        Assert.Equal("S", ClientInfo.Phase);
    }
}
