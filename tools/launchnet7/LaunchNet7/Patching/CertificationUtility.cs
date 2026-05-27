using System;
using System.Diagnostics;
using System.IO;

namespace LaunchNet7.Patching
{
    /// <summary>
    /// Manages the local TLS cert used by the proxy's embedded HTTPS listener.
    /// Wraps mkcert.exe (FiloSottile/mkcert). The local CA is added to the
    /// host's (or WINE prefix's) trust store the first time mkcert -install runs;
    /// no LocalMachine\Root write happens here.
    /// </summary>
    public static class CertificationUtility
    {
        public const string DefaultExecutable = "mkcert.exe";

        public class MkcertNotFoundException : ApplicationException
        {
            public MkcertNotFoundException(string path)
                : base("mkcert not found at '" + path + "'. Install from https://github.com/FiloSottile/mkcert and place mkcert.exe in the launcher's bin/ directory or on PATH.")
            { }
        }

        /// <summary>
        /// Ensure a cert exists for <paramref name="hostname"/> at the given output
        /// paths. Runs `mkcert -install` once (idempotent inside mkcert) then
        /// `mkcert -cert-file ... -key-file ... <hostname>`. Skipped entirely if both
        /// output files already exist and are non-empty.
        /// </summary>
        public static void EnsureLocalCert(string hostname, string certOutPath, string keyOutPath, string mkcertExecutable = null)
        {
            if (string.IsNullOrWhiteSpace(hostname)) throw new ArgumentException("hostname required", "hostname");
            if (string.IsNullOrWhiteSpace(certOutPath)) throw new ArgumentException("certOutPath required", "certOutPath");
            if (string.IsNullOrWhiteSpace(keyOutPath)) throw new ArgumentException("keyOutPath required", "keyOutPath");

            if (CertLooksValid(certOutPath) && CertLooksValid(keyOutPath))
            {
                return;
            }

            string mkcert = ResolveMkcert(mkcertExecutable);

            Run(mkcert, "-install");

            string args = string.Format(
                "-cert-file \"{0}\" -key-file \"{1}\" {2}",
                certOutPath, keyOutPath, hostname);
            Run(mkcert, args);
        }

        static string ResolveMkcert(string overridePath)
        {
            if (!string.IsNullOrWhiteSpace(overridePath))
            {
                if (!File.Exists(overridePath)) throw new MkcertNotFoundException(overridePath);
                return overridePath;
            }

            string binDir = Path.Combine(Directory.GetCurrentDirectory(), "bin");
            string bundled = Path.Combine(binDir, DefaultExecutable);
            if (File.Exists(bundled)) return bundled;

            return DefaultExecutable;
        }

        static bool CertLooksValid(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                return fi.Exists && fi.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        static void Run(string exe, string args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            try
            {
                using (var p = Process.Start(psi))
                {
                    string stdout = p.StandardOutput.ReadToEnd();
                    string stderr = p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    if (p.ExitCode != 0)
                    {
                        throw new ApplicationException(
                            "mkcert exited " + p.ExitCode + ".\nArgs: " + args +
                            "\nstdout: " + stdout + "\nstderr: " + stderr);
                    }
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                throw new MkcertNotFoundException(exe);
            }
        }
    }
}
