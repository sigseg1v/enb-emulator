using LaunchFreya;
using Xunit;

namespace LaunchFreya.Tests
{
    // Unit tests for the pure host/URL string logic extracted from MainWindow
    // (Phase AT-5). No UI / no I/O, so it is directly unit-testable here.
    public class HostResolverTests
    {
        // ---- NormalizeHost ----

        [Theory]
        [InlineData("https://enb.example.land", "enb.example.land")]
        [InlineData("http://enb.example.land/", "enb.example.land")]
        [InlineData("https://enb.example.land/path/to/thing", "enb.example.land")]
        [InlineData("enb.example.land", "enb.example.land")]
        [InlineData("  enb.example.land  ", "enb.example.land")]
        [InlineData("host:443", "host:443")]
        public void NormalizeHost_StripsSchemeAndPath(string raw, string expected)
        {
            Assert.Equal(expected, HostResolver.NormalizeHost(raw));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void NormalizeHost_PassesThroughBlank(string raw)
        {
            Assert.Equal(raw, HostResolver.NormalizeHost(raw));
        }

        // ---- WebsiteUrlFor ----

        [Theory]
        [InlineData("", "http://localhost:8088")]
        [InlineData("localhost", "http://localhost:8088")]
        [InlineData("LOCALHOST", "http://localhost:8088")]
        [InlineData("127.0.0.1", "http://localhost:8088")]
        [InlineData("http://localhost/", "http://localhost:8088")]
        public void WebsiteUrlFor_LoopbackMapsToDevSite(string raw, string expected)
        {
            Assert.Equal(expected, HostResolver.WebsiteUrlFor(raw));
        }

        [Theory]
        [InlineData("enb.example.land", "https://enb.example.land")]
        [InlineData("https://enb.example.land", "https://enb.example.land")]
        [InlineData("enb.example.land:443", "https://enb.example.land")]
        [InlineData("https://enb.example.land/foo", "https://enb.example.land")]
        public void WebsiteUrlFor_RemoteMapsToHttpsAndDropsPort(string raw, string expected)
        {
            Assert.Equal(expected, HostResolver.WebsiteUrlFor(raw));
        }
    }
}
