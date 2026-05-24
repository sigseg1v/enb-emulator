// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

using N7.CliClient.Auth;
using Xunit;

namespace N7.CliClient.UnitTests.Auth;

public sealed class AuthLoginClientTests
{
    [Fact]
    public void Constructor_RejectsBadPort()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AuthLoginClient("login.example.com", port: 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AuthLoginClient("login.example.com", port: 70000));
    }

    [Fact]
    public void Constructor_RejectsEmptyHost()
    {
        // ArgumentException.ThrowIfNullOrEmpty throws ArgumentNullException
        // for null and ArgumentException for empty (both inherit
        // ArgumentException, so the public contract is "throws ArgumentException").
        Assert.Throws<ArgumentException>(() => new AuthLoginClient(""));
        Assert.Throws<ArgumentNullException>(() => new AuthLoginClient(null!));
    }

    [Fact]
    public void BuildUrl_EmitsExpectedQueryString()
    {
        var req = new AuthLoginRequest(
            Username: "tester",
            Password: "hunter2",
            ServiceId: "EA-ENB",
            Version: "2.5");

        string url = AuthLoginClient.BuildUrl(req);

        Assert.Equal(
            "/AuthLogin?username=tester&password=hunter2&serviceID=EA-ENB&version=2.5",
            url);
    }

    [Fact]
    public void BuildUrl_UrlEncodesSpecialCharacters()
    {
        var req = new AuthLoginRequest(
            Username: "user with space",
            Password: "p&s=word/?",
            ServiceId: "EA-ENB",
            Version: "2.5");

        string url = AuthLoginClient.BuildUrl(req);

        // HttpUtility.UrlEncode emits '+' for space (form-encoding) and
        // %XX for reserved characters — the C++ server's strstr parser
        // looks for the literal "username=" tag and then takes
        // everything up to '&' or whitespace, so the encoding only
        // matters for &/=/space.
        Assert.Contains("username=user+with+space", url);
        Assert.Contains("password=p%26s%3dword%2f%3f", url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExtractBody_StripsCrlfHeaders()
    {
        string raw =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: text/plain\r\n" +
            "Content-Length: 32\r\n" +
            "\r\n" +
            "Valid=TRUE\r\nTicket=ABCDEF\r\n";

        string body = AuthLoginClient.ExtractBody(raw);

        Assert.Equal("Valid=TRUE\r\nTicket=ABCDEF\r\n", body);
    }

    [Fact]
    public void ExtractBody_StripsLfOnlyHeaders()
    {
        string raw =
            "HTTP/1.1 200 OK\n" +
            "Content-Type: text/plain\n" +
            "\n" +
            "Valid=False\n";

        string body = AuthLoginClient.ExtractBody(raw);

        Assert.Equal("Valid=False\n", body);
    }

    [Fact]
    public void ExtractBody_NoHeadersAtAll_ReturnsAsIs()
    {
        string raw = "Valid=TRUE";
        Assert.Equal("Valid=TRUE", AuthLoginClient.ExtractBody(raw));
    }
}
