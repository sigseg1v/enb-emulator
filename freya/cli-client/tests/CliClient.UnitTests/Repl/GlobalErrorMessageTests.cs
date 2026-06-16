// SPDX-License-Identifier: MIT
// Part of the Earth & Beyond emulator preservation project -- Freya (MIT).
// License: LICENSES/Freya

using N7.CliClient.Repl;
using Xunit;

namespace N7.CliClient.UnitTests.Repl;

public sealed class GlobalErrorMessageTests
{
    // Pins the code -> reason mapping to the server's G_ERROR_* enum
    // (server/src/UDP_Global.cpp). If the server adds/renumbers a code, this
    // breaks loudly instead of the REPL printing the wrong reason.
    [Theory]
    [InlineData(0, "account banned")]
    [InlineData(1, "that character name is already taken")]
    [InlineData(2, "name contains invalid characters")]
    [InlineData(3, "name too short (minimum 3 characters)")]
    [InlineData(4, "name needs at least one vowel (a/e/i/o/u/y)")]
    [InlineData(5, "name has more than 3 repeating characters")]
    [InlineData(6, "character name is on the restricted list")]
    [InlineData(7, "login ticket invalid or expired")]
    [InlineData(8, "auth server unavailable")]
    [InlineData(9, "account inactive")]
    [InlineData(10, "ship name is on the restricted list")]
    [InlineData(11, "server internal error")]
    [InlineData(12, "server closed (stress-test gate)")]
    [InlineData(13, "account already in use (another session is logged in -- log it out first)")]
    [InlineData(14, "server is shutting down")]
    public void KnownCode_MapsToReason(int code, string expected)
        => Assert.Equal(expected, SectorEnterDriver.GlobalErrorMessage(code));

    [Theory]
    [InlineData(-1)]
    [InlineData(99)]
    public void UnknownCode_FallsBack(int code)
        => Assert.Equal("unknown reason", SectorEnterDriver.GlobalErrorMessage(code));
}
