using System;
using System.IO;
using LaunchFreya.Patching;
using Xunit;

namespace LaunchFreya.Tests
{
    // The EnB authlogin.dll byte patch must work against both shipped builds:
    // the retail/Net-7 build and the original demo build, which place the
    // WinINet flags-push (and the InternetConnectA port immediate) at different
    // offsets. We do NOT ship the real DLLs (binary + license); instead we
    // synthesize the minimal byte layout the patcher keys off -- the
    // `68 00 30 <scheme> 84` flags push and a 2-byte port immediate -- at each
    // build's known offsets, and assert detect/read/write round-trips.
    public class AuthLoginPatcherTests
    {
        // (build name, scheme-byte offset, port u16 offset) -- mirrors
        // AuthLoginPatcher.Layouts.
        const int RetailScheme = 0x8328, RetailPort = 0x82AD;
        const int DemoScheme   = 0x22BA, DemoPort   = 0x20A3;

        // Build a fake authlogin.dll: zero-filled, with the flags-push signature
        // written so the scheme byte lands at schemeOffset, and a port immediate
        // at portOffset.
        static byte[] FakeDll(int schemeOffset, int portOffset, bool https, ushort port)
        {
            var dll = new byte[0x10000];
            int p = schemeOffset - 3;          // 68 00 30 <scheme> 84
            dll[p] = 0x68; dll[p + 1] = 0x00; dll[p + 2] = 0x30;
            dll[schemeOffset] = https ? (byte)0xc0 : (byte)0x40;
            dll[schemeOffset + 1] = 0x84;
            BitConverter.GetBytes(port).CopyTo(dll, portOffset);
            return dll;
        }

        static string WriteTemp(byte[] bytes)
        {
            string path = Path.Combine(Path.GetTempPath(),
                "freya-authlogin-" + Guid.NewGuid().ToString("N") + ".dll");
            File.WriteAllBytes(path, bytes);
            return path;
        }

        public static TheoryData<string, int, int> Builds() => new()
        {
            { "retail/Net-7", RetailScheme, RetailPort },
            { "demo",         DemoScheme,   DemoPort },
        };

        [Theory]
        [MemberData(nameof(Builds))]
        public void Read_DetectsBuildAndFields(string name, int schemeOffset, int portOffset)
        {
            string path = WriteTemp(FakeDll(schemeOffset, portOffset, https: true, port: 8891));
            try
            {
                var info = AuthLoginPatcher.ReadInformation(path);
                Assert.Equal(name, info.Build);
                Assert.True(info.UseHttps);
                Assert.Equal(8891, info.Port);
            }
            finally { File.Delete(path); }
        }

        [Theory]
        [MemberData(nameof(Builds))]
        public void Write_FlipsSchemeToHttpAndSetsPort_AtCorrectOffsets(
            string name, int schemeOffset, int portOffset)
        {
            string path = WriteTemp(FakeDll(schemeOffset, portOffset, https: true, port: 443));
            try
            {
                var info = AuthLoginPatcher.ReadInformation(path);
                Assert.Equal(name, info.Build);

                info.UseHttps = false;
                info.Port = 4180;
                AuthLoginPatcher.WriteInformation(path, info);

                // The exact patched offsets carry the new values; nothing else moved.
                byte[] dll = File.ReadAllBytes(path);
                Assert.Equal(0x40, dll[schemeOffset]);
                Assert.Equal(4180, BitConverter.ToUInt16(dll, portOffset));

                var reread = AuthLoginPatcher.ReadInformation(path);
                Assert.False(reread.UseHttps);
                Assert.Equal(4180, reread.Port);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Read_UnrecognizedBuild_Throws()
        {
            // No flags-push signature anywhere -> not a known authlogin.dll.
            string path = WriteTemp(new byte[0x10000]);
            try
            {
                var ex = Assert.Throws<InvalidDataException>(
                    () => AuthLoginPatcher.ReadInformation(path));
                Assert.Contains("not a recognized", ex.Message);
            }
            finally { File.Delete(path); }
        }

        [Fact]
        public void Read_MissingFile_ThrowsFileNotFound()
        {
            Assert.Throws<FileNotFoundException>(
                () => AuthLoginPatcher.ReadInformation(
                    Path.Combine(Path.GetTempPath(), "freya-no-such-" + Guid.NewGuid().ToString("N") + ".dll")));
        }
    }
}
