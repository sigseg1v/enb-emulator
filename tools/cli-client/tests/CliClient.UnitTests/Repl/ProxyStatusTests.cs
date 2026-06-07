// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using N7.CliClient.Logging;
using N7.CliClient.Repl;
using Xunit;

namespace N7.CliClient.UnitTests.Repl;

/// <summary>
/// Pins the decode of the proxy's 4-byte ProxyStatusReply (0x300A) and the
/// honest rendering of the login DTLS line. Colour is gated off under a
/// redirected test stdout, so the rendered text is plain -- we assert on the
/// substrings that carry the meaning.
/// </summary>
public sealed class ProxyStatusTests
{
    [Fact]
    public void Parse_DecodesAllFour_Fields()
    {
        // dtls_required=1, live_assocs=3, connected=1, reserved=0
        var status = ProxyStatus.Parse(new byte[] { 1, 3, 1, 0 });
        Assert.True(status.DtlsRequired);
        Assert.Equal(3, status.LiveDtlsAssociations);
        Assert.True(status.Connected);
    }

    [Fact]
    public void Parse_PlaintextOptOut_HasNoAssocs()
    {
        var status = ProxyStatus.Parse(new byte[] { 0, 0, 1, 0 });
        Assert.False(status.DtlsRequired);
        Assert.Equal(0, status.LiveDtlsAssociations);
        Assert.True(status.Connected);
    }

    [Fact]
    public void Parse_IgnoresTrailingBytes()
    {
        // A longer payload (future fields) still decodes the first four.
        var status = ProxyStatus.Parse(new byte[] { 1, 2, 0, 0, 9, 9 });
        Assert.True(status.DtlsRequired);
        Assert.Equal(2, status.LiveDtlsAssociations);
        Assert.False(status.Connected);
    }

    [Fact]
    public void Parse_ShortPayload_Throws()
    {
        Assert.Throws<ArgumentException>(() => ProxyStatus.Parse(new byte[] { 1, 0, 0 }));
    }

    [Fact]
    public void DtlsLine_NullStatus_IsUnknown()
    {
        // An older proxy that never answers -> we say "unknown", not "on".
        string line = ProxyStatus.DtlsLine(null);
        Assert.Contains("dtls-encryption=", line);
        Assert.Contains("unknown", line);
    }

    [Fact]
    public void DtlsLine_PlaintextOptOut_IsDisabled()
    {
        var status = new ProxyStatus(DtlsRequired: false, LiveDtlsAssociations: 0, Connected: true);
        string line = ProxyStatus.DtlsLine(status);
        Assert.Contains("DISABLED", line);
    }

    [Fact]
    public void DtlsLine_EnforcedWithLiveAssoc_IsOn()
    {
        var status = new ProxyStatus(DtlsRequired: true, LiveDtlsAssociations: 1, Connected: true);
        string line = ProxyStatus.DtlsLine(status);
        Assert.Contains("dtls-encryption=on", StripPalette(line));
    }

    [Fact]
    public void DtlsLine_EnforcedButNoHandshakeYet_IsPending()
    {
        // Never claim "on" before a handshake has actually completed.
        var status = new ProxyStatus(DtlsRequired: true, LiveDtlsAssociations: 0, Connected: true);
        string line = ProxyStatus.DtlsLine(status);
        Assert.Contains("PENDING", line);
        Assert.DoesNotContain("DISABLED", line);
    }

    private static string StripPalette(string s) =>
        s.Replace(AnsiPalette.Reset, "")
         .Replace(AnsiPalette.Red, "")
         .Replace(AnsiPalette.Green, "")
         .Replace(AnsiPalette.Yellow, "")
         .Replace(AnsiPalette.Dim, "");
}
