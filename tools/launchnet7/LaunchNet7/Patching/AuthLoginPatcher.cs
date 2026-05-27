using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.IO;

namespace LaunchNet7.Patching
{
    public class AuthLoginPatcher
    {
        static readonly byte Https = 0xc0;
        static readonly byte Http = 0x40;

        /// <summary>
        /// File version of authlogin.dll the offsets below are known to be
        /// valid for. Patching a different build risks corrupting unrelated
        /// bytes; callers should refuse to patch on mismatch.
        /// </summary>
        public const string ExpectedFileVersion = "3.3.0.6";

        public class UnsupportedAuthLoginVersionException : ApplicationException
        {
            public UnsupportedAuthLoginVersionException(string actual, string expected)
                : base("authlogin.dll version '" + actual + "' is not the expected '" + expected +
                       "'. Refusing to patch — the byte offsets are only valid for the expected build.")
            { }
        }

        /// <summary>
        /// Throws if the file at <paramref name="fileName"/> doesn't report the
        /// expected FileVersion. No-op if the file has no version resource
        /// (older test fixtures, etc.) — callers can opt into stricter checks
        /// via a separate gate.
        /// </summary>
        public static void VerifyVersion(string fileName)
        {
            if (!File.Exists(fileName)) throw new FileNotFoundException(fileName);

            FileVersionInfo info = FileVersionInfo.GetVersionInfo(fileName);
            string actual = info.FileVersion;
            if (string.IsNullOrEmpty(actual)) return;

            // FileVersion comes back as either "3.3.0.6" or "3, 3, 0, 6"
            // depending on how the resource was authored; normalise both.
            string normalised = actual.Replace(" ", "").Replace(",", ".");
            if (normalised != ExpectedFileVersion)
            {
                throw new UnsupportedAuthLoginVersionException(actual, ExpectedFileVersion);
            }
        }

        public static AuthPatcherInfo ReadInformation(string fileName)
        {
            if (File.Exists(fileName) == false) throw new FileNotFoundException();
            VerifyVersion(fileName);

            AuthPatcherInfo info = new AuthPatcherInfo();
            using (FileStream fs = File.OpenRead(fileName))
            {
                fs.Seek(0x8328, SeekOrigin.Begin);
                byte current = (byte)fs.ReadByte();
                if (current == Https)
                {
                    info.UseHttps = true;
                }
                else if (current == Http)
                {
                    info.UseHttps = false;
                }
                else
                {
                    throw new InvalidDataException();
                }

                fs.Seek(0x82AD, SeekOrigin.Begin);
                byte[] port = new byte[2];
                fs.Read(port, 0, 2);
                info.Port = BitConverter.ToUInt16(port, 0);

                fs.Seek(0x8292, SeekOrigin.Begin);
                byte[] timeout = new byte[2];
                fs.Read(timeout, 0, 2);
                info.TimeOut = BitConverter.ToUInt16(timeout, 0);
            }

            return info;
        }

        public static void WriteInformation(string fileName, AuthPatcherInfo infos)
        {
            if (infos == null) throw new ArgumentNullException("infos");
            if (File.Exists(fileName) == false) throw new FileNotFoundException();
            VerifyVersion(fileName);

            byte[] buffer;
            using (FileStream fs = File.OpenWrite(fileName))
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
}
