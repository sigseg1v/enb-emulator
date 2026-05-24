using System;
using System.IO;

namespace LaunchNet7Avalonia.Patching
{
    // Verbatim port of LaunchNet7/Patching/AuthLoginPatcher.cs. The byte
    // offsets are fixed in the EnB client's authlogin.dll; the binary
    // patch is platform-independent.
    public class AuthLoginPatcher
    {
        static readonly byte Https = 0xc0;
        static readonly byte Http  = 0x40;

        public static AuthPatcherInfo ReadInformation(string fileName)
        {
            if (!File.Exists(fileName)) throw new FileNotFoundException(fileName);

            var info = new AuthPatcherInfo();
            using (var fs = File.OpenRead(fileName))
            {
                fs.Seek(0x8328, SeekOrigin.Begin);
                byte current = (byte)fs.ReadByte();
                if      (current == Https) info.UseHttps = true;
                else if (current == Http)  info.UseHttps = false;
                else throw new InvalidDataException(
                    $"authlogin.dll byte at 0x8328 is 0x{current:X2}; expected 0x{Http:X2} or 0x{Https:X2}");

                fs.Seek(0x82AD, SeekOrigin.Begin);
                var port = new byte[2];
                fs.ReadExactly(port, 0, 2);
                info.Port = BitConverter.ToUInt16(port, 0);

                fs.Seek(0x8292, SeekOrigin.Begin);
                var timeout = new byte[2];
                fs.ReadExactly(timeout, 0, 2);
                info.TimeOut = BitConverter.ToUInt16(timeout, 0);
            }
            return info;
        }

        public static void WriteInformation(string fileName, AuthPatcherInfo infos)
        {
            if (infos == null) throw new ArgumentNullException(nameof(infos));
            if (!File.Exists(fileName)) throw new FileNotFoundException(fileName);

            byte[] buffer;
            using (var fs = File.OpenWrite(fileName))
            {
                fs.Seek(0x8328, SeekOrigin.Begin);
                fs.WriteByte(infos.UseHttps ? Https : Http);

                fs.Seek(0x82AD, SeekOrigin.Begin);
                buffer = BitConverter.GetBytes(infos.Port);
                fs.Write(buffer, 0, 2);

                fs.Seek(0x8292, SeekOrigin.Begin);
                buffer = BitConverter.GetBytes(infos.TimeOut);
                fs.Write(buffer, 0, 2);
            }
        }
    }

    public sealed class AuthPatcherInfo
    {
        public ushort Port { get; set; }
        public ushort TimeOut { get; set; } = 30;
        public bool UseHttps { get; set; }
    }
}
