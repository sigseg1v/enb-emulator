// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

using N7.CliClient.Auth;
using Xunit;

namespace N7.CliClient.UnitTests.Auth;

public sealed class AuthLoginResponseTests
{
    [Fact]
    public void Parse_Success_ExtractsTicket()
    {
        // Exactly what login-server/Net7SSL/LinuxAuth.cpp:408 emits:
        //   "Valid=TRUE\r\nTicket=%s\r\n"
        string body = "Valid=TRUE\r\nTicket=ABCDEF1234567890\r\n";

        var resp = AuthLoginResponse.Parse(body);

        Assert.True(resp.Valid);
        Assert.Equal("ABCDEF1234567890", resp.Ticket);
        Assert.Equal(body, resp.RawBody);
    }

    [Fact]
    public void Parse_Failure_NoTicket()
    {
        // login-server/Net7SSL/LinuxAuth.cpp:382 default: "Valid=False\r\n"
        string body = "Valid=False\r\n";

        var resp = AuthLoginResponse.Parse(body);

        Assert.False(resp.Valid);
        Assert.Equal(string.Empty, resp.Ticket);
    }

    [Fact]
    public void Parse_LowercaseTrue_IsNotValid()
    {
        // The C++ server emits the literal "TRUE" (uppercase); anything
        // else should be treated as failure. This is a deliberate
        // tightening on the client side — if the server starts saying
        // "true" or "yes", that's a server change and we want to notice.
        var resp = AuthLoginResponse.Parse("Valid=true\r\nTicket=X\r\n");
        Assert.False(resp.Valid);
    }

    [Fact]
    public void Parse_AcceptsLfOnlyLineEndings()
    {
        var resp = AuthLoginResponse.Parse("Valid=TRUE\nTicket=XYZ\n");
        Assert.True(resp.Valid);
        Assert.Equal("XYZ", resp.Ticket);
    }

    [Fact]
    public void Parse_IgnoresUnknownKeys()
    {
        var resp = AuthLoginResponse.Parse(
            "Valid=TRUE\r\nTicket=X\r\nServerVersion=2.5\r\nMOTD=hello\r\n");
        Assert.True(resp.Valid);
        Assert.Equal("X", resp.Ticket);
    }

    [Fact]
    public void Parse_NullBody_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => AuthLoginResponse.Parse(null!));
    }
}
