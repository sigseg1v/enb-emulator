using System;
using System.IO;

namespace LaunchFreya.Patching
{
    // The EnB client's authlogin.dll dials the auth server through WinINet, and
    // two historical builds ship different code layouts, so the bytes we patch
    // live at different offsets:
    //   - the retail / Net-7 build (scheme byte @0x8328, port @0x82AD)
    //   - the original demo build  (scheme byte @0x22BA, port @0x20A3)
    // Both encode the HttpOpenRequestA flags as `push 0x84c03000` (68 00 30 C0
    // 84): the third immediate byte is INTERNET_FLAG_SECURE -- 0xC0 = HTTPS,
    // 0x40 = HTTP. The port is the nServerPort u16 immediate of the preceding
    // InternetConnectA `push`. We detect the build by that flags-push signature
    // and patch the matching offsets; the byte patch is platform-independent.
    public class AuthLoginPatcher
    {
        const byte Https = 0xc0;
        const byte Http = 0x40;

        sealed class Layout
        {
            public string Name;
            public int SchemeOffset; // INTERNET_FLAG_SECURE byte (0xc0 https / 0x40 http)
            public int PortOffset;   // InternetConnectA nServerPort, u16 little-endian
        }

        // Known builds. Detection is by signature, not by order, so listing is
        // just for the "unrecognized build" message.
        static readonly Layout[] Layouts =
        {
            new Layout { Name = "retail/Net-7", SchemeOffset = 0x8328, PortOffset = 0x82AD },
            new Layout { Name = "demo",         SchemeOffset = 0x22BA, PortOffset = 0x20A3 },
        };

        // The flags push is the 5-byte `68 00 30 <scheme> 84`; the scheme byte
        // sits 3 bytes in. Verifying the whole signature (not just a 0xc0/0x40
        // byte) makes a false match require that exact instruction at that exact
        // offset, so the two builds never alias each other.
        static bool SignatureMatches(byte[] dll, int schemeOffset)
        {
            int p = schemeOffset - 3;
            if (p < 0 || schemeOffset + 1 >= dll.Length) return false;
            byte s = dll[schemeOffset];
            return dll[p] == 0x68 && dll[p + 1] == 0x00 && dll[p + 2] == 0x30
                && dll[schemeOffset + 1] == 0x84 && (s == Http || s == Https);
        }

        static Layout Detect(byte[] dll, string fileName)
        {
            foreach (var l in Layouts)
                if (SignatureMatches(dll, l.SchemeOffset)) return l;
            throw new InvalidDataException(
                $"{Path.GetFileName(fileName)} is not a recognized Earth & Beyond authlogin.dll " +
                "build (retail/Net-7 or demo): the WinINet flags-push signature was not found at " +
                "any known offset. The client install may ship an unsupported authlogin.dll.");
        }

        public static AuthPatcherInfo ReadInformation(string fileName)
        {
            if (!File.Exists(fileName)) throw new FileNotFoundException(fileName);

            byte[] dll = File.ReadAllBytes(fileName);
            var layout = Detect(dll, fileName);
            return new AuthPatcherInfo
            {
                Build = layout.Name,
                UseHttps = dll[layout.SchemeOffset] == Https,
                Port = BitConverter.ToUInt16(dll, layout.PortOffset),
            };
        }

        public static void WriteInformation(string fileName, AuthPatcherInfo infos)
        {
            if (infos == null) throw new ArgumentNullException(nameof(infos));
            if (!File.Exists(fileName)) throw new FileNotFoundException(fileName);

            byte[] dll = File.ReadAllBytes(fileName);
            var layout = Detect(dll, fileName);
            dll[layout.SchemeOffset] = infos.UseHttps ? Https : Http;
            BitConverter.GetBytes(infos.Port).CopyTo(dll, layout.PortOffset);
            File.WriteAllBytes(fileName, dll);
        }
    }

    public sealed class AuthPatcherInfo
    {
        public ushort Port { get; set; }
        public bool UseHttps { get; set; }
        // Which authlogin.dll build the patcher matched ("retail/Net-7" or "demo").
        public string Build { get; set; }
    }
}
