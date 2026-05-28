using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using LaunchNet7Avalonia.Config;
using LaunchNet7Avalonia.Patching;

namespace LaunchNet7Avalonia
{
    public sealed class LaunchSetting
    {
        string _clientPath;
        public string ClientPath
        {
            get => _clientPath;
            set
            {
                _clientPath = value;
                if (!string.IsNullOrEmpty(value))
                {
                    var dir = Path.GetDirectoryName(value);
                    if (!string.IsNullOrEmpty(dir))
                        BaseFolder = Directory.GetParent(dir)?.FullName;
                }
            }
        }

        public string BaseFolder { get; private set; }

        public string AuthLoginFileName  => Path.Combine(BaseFolder ?? "", "release", "authlogin.dll");
        public string IniDirectoryName    => Path.Combine(BaseFolder ?? "", "Data", "client", "ini");
        public string CommonDirectoryName => Path.Combine(BaseFolder ?? "", "Data", "common");

        public int    AuthenticationPort       { get; set; } = 443;
        public bool   UseSecureAuthentication  { get; set; } = true;
        public string Hostname                 { get; set; }
        public string RegistrationHostname     { get; set; }
        public string LaunchName               { get; set; }
        public bool   UseClientDetours         { get; set; }
        public bool   UseLocalCert             { get; set; }

        public string EffectiveRegistrationHostname
            => string.IsNullOrEmpty(RegistrationHostname) ? Hostname : RegistrationHostname;
    }

    public sealed class Launcher
    {
        readonly LaunchSetting _setting;
        readonly Action<string> _warn;

        public Launcher(LaunchSetting setting, Action<string> warn = null)
        {
            _setting = setting ?? throw new ArgumentNullException(nameof(setting));
            _warn = warn ?? (_ => { });
        }

        public void Launch()
        {
            PatchAuthLoginFile();
            PatchRegDataFileNames();
            PatchRegDataFile();
            PatchAuthIniFile();
            PatchNetworkIniFile();
            PatchRegistry();

            switch ((_setting.LaunchName ?? "").ToUpperInvariant())
            {
                case "NET7SP":
                    LaunchNet7Server();
                    System.Threading.Thread.Sleep(25000);
                    LaunchNet7Proxy();
                    System.Threading.Thread.Sleep(2000);
                    LaunchClient();
                    break;

                case "NET7MP":
                    LaunchNet7Proxy();
                    System.Threading.Thread.Sleep(2000);
                    LaunchClient();
                    break;

                default:
                    LaunchClient();
                    break;
            }
        }

        // ---- launching ----

        bool OnWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        ProcessStartInfo WinExe(string workingDir, string exePath, string arguments)
        {
            // On Linux/macOS we need wine to start a Win32 .exe; on Windows
            // we start it directly. UseShellExecute=true lets the platform
            // pick the right loader on Windows; on non-Windows we always
            // route through wine.
            if (OnWindows)
            {
                return new ProcessStartInfo
                {
                    WorkingDirectory = workingDir,
                    FileName         = exePath,
                    Arguments        = arguments ?? "",
                    UseShellExecute  = true,
                };
            }
            else
            {
                return new ProcessStartInfo
                {
                    WorkingDirectory = workingDir,
                    FileName         = "wine",
                    Arguments        = string.IsNullOrEmpty(arguments)
                        ? $"\"{exePath}\""
                        : $"\"{exePath}\" {arguments}",
                    UseShellExecute = false,
                };
            }
        }

        void LaunchClient()
        {
            var addrs = Dns.GetHostAddresses(_setting.Hostname);
            if (addrs.Length == 0)
                throw new InvalidOperationException($"Could not resolve hostname '{_setting.Hostname}'.");

            ProcessStartInfo info;
            if (_setting.UseClientDetours)
            {
                var dir  = Path.Combine(Directory.GetCurrentDirectory(), "bin");
                var exe  = Path.Combine(dir, "Detours.exe");
                // GetShortPathName was a Windows-only helper to avoid spaces
                // in the path. The client accepts the long form too; we just
                // skip the 8.3 conversion on non-Windows and pass the path
                // as-is.
                var clientPath = OnWindows ? ShortPath.Get(_setting.ClientPath) : _setting.ClientPath;
                info = WinExe(dir, exe,
                    $"/ADDR:{addrs[0]} /CLIENT:{clientPath}");
            }
            else
            {
                var dir = Path.GetDirectoryName(_setting.ClientPath);
                info = WinExe(dir, _setting.ClientPath,
                    $"-SERVER_ADDR {addrs[0]} -PROTOCOL TCP");
            }

            try { Process.Start(info); }
            catch (Exception e)
            {
                throw new ApplicationException(
                    $"Could not launch client.\nWorking Directory: {info.WorkingDirectory}\nFileName: {info.FileName}\nArguments: {info.Arguments}\nDetails: {e.Message}", e);
            }
        }

        void LaunchNet7Proxy()
        {
            var addrs = Dns.GetHostAddresses(_setting.Hostname);
            if (addrs.Length == 0)
                throw new InvalidOperationException($"Could not resolve hostname '{_setting.Hostname}'.");

            var dir = Path.Combine(Directory.GetCurrentDirectory(), "bin");
            var exe = Path.Combine(dir, "Net7Proxy.exe");
            var clientPath = OnWindows ? ShortPath.Get(_setting.ClientPath) : _setting.ClientPath;
            var args = $"/ADDRESS:{addrs[0]} /CLIENT:{clientPath}";
            if (_setting.UseClientDetours) args += " /L";
            if (_setting.UseLocalCert)
            {
                args += " /LC";
                args += $" /SSL:{_setting.AuthenticationPort}";
            }
            var info = WinExe(dir, exe, args);

            try { Process.Start(info); }
            catch (Exception e)
            {
                throw new ApplicationException(
                    $"Could not launch Net7Proxy.\nWorking Directory: {info.WorkingDirectory}\nFileName: {info.FileName}\nArguments: {info.Arguments}\nDetails: {e.Message}", e);
            }
        }

        void LaunchNet7Server()
        {
            // Net7 server's "Net7.exe" is the legacy Win32 build. The
            // modern path on Linux is the native server binary at
            // server/build/server (or the docker-compose service). This
            // launcher path therefore only fires on Windows / WINE where
            // someone genuinely wants the SP loopback flow.
            var dir = Path.Combine(Directory.GetCurrentDirectory(), "bin");
            var exe = Path.Combine(dir, "Net7.exe");
            var info = WinExe(dir, exe, null);

            try { Process.Start(info); }
            catch (Exception e)
            {
                throw new ApplicationException("Could not launch Net7 Server. Details: " + e.Message, e);
            }
        }

        // ---- patching ----

        void PatchNetworkIniFile()
        {
            var file       = Path.Combine(_setting.CommonDirectoryName, "Network.ini");
            var backup     = Path.Combine(_setting.CommonDirectoryName, "Network.ini.orig");

            if (!File.Exists(file) && File.Exists(backup)) File.Copy(backup, file);

            var host = _setting.UseLocalCert ? "local.net-7.org" : _setting.Hostname;
            string[] sections =
            {
                "MasterServer","RegisterServer","ReporterServer",
                "GlobalServer_Directory","GlobalServer_Client","GlobalServer_Register","GlobalServer_Parent",
                "ChatServer","ChatServer_Basic","GroupServer","GuildServer"
            };

            bool needPatch = false;
            foreach (var s in sections)
            {
                if (!string.Equals(IniFile.GetValue(file, s, "Name"), host, StringComparison.OrdinalIgnoreCase))
                {
                    needPatch = true;
                    break;
                }
            }
            if (!needPatch) return;
            if (File.Exists(file)) File.Copy(file, backup, true);
            foreach (var s in sections) IniFile.SetValue(file, s, "Name", host);
        }

        void PatchAuthIniFile()
        {
            var file   = Path.Combine(_setting.IniDirectoryName, "Auth.ini");
            var backup = Path.Combine(_setting.IniDirectoryName, "Auth.ini.orig");
            if (!File.Exists(file) && File.Exists(backup)) File.Copy(backup, file);

            var regHost = _setting.UseLocalCert ? "local.net-7.org" : _setting.EffectiveRegistrationHostname;
            var builder = new UriBuilder
            {
                Scheme = _setting.UseSecureAuthentication ? "https" : "http",
                Host   = regHost,
                Path   = "misc/touchsession.jsp",
                Query  = "lkey=%s",
            };
            var url = builder.ToString();

            bool needPatch =
                !string.Equals(IniFile.GetValue(file, "General", "AAIUrl"),  regHost, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(IniFile.GetValue(file, "General", "LKeyUrl"), url,     StringComparison.OrdinalIgnoreCase);

            if (!needPatch) return;
            if (File.Exists(file)) File.Copy(file, backup, true);
            IniFile.SetValue(file, "General", "AAIUrl",  regHost);
            IniFile.SetValue(file, "General", "LKeyUrl", url);
        }

        void PatchRegDataFile()
        {
            var file   = Path.Combine(_setting.IniDirectoryName, "rg_regdata.ini");
            var backup = Path.Combine(_setting.IniDirectoryName, "rg_regdata.ini.orig");
            if (!File.Exists(file) && File.Exists(backup)) File.Copy(backup, file);

            var regHost = _setting.UseLocalCert ? "local.net-7.org" : _setting.EffectiveRegistrationHostname;
            var builder = new UriBuilder
            {
                Scheme = _setting.UseSecureAuthentication ? "https" : "http",
                Host   = regHost,
                Path   = "subsxml",
            };
            var url = builder.ToString();

            if (string.Equals(IniFile.GetValue(file, "Connection", "regserverurl"), url, StringComparison.OrdinalIgnoreCase))
                return;
            if (File.Exists(file)) File.Copy(file, backup, true);
            IniFile.SetValue(file, "Connection", "regserverurl", url);
        }

        void PatchRegDataFileNames()
        {
            var src = Path.Combine(_setting.IniDirectoryName, "rg_regdata_org");
            var dst = Path.Combine(_setting.IniDirectoryName, "rg_regdata.ini");
            if (File.Exists(src) && !File.Exists(dst))
            {
                try { File.Move(src, dst); }
                catch (Exception e)
                {
                    throw new ApplicationException("Could not repair rg_regdata.ini filename.", e);
                }
            }
        }

        void PatchAuthLoginFile()
        {
            try
            {
                var info = AuthLoginPatcher.ReadInformation(_setting.AuthLoginFileName);
                if (info.Port != _setting.AuthenticationPort || info.UseHttps != _setting.UseSecureAuthentication)
                {
                    info.Port     = (ushort)_setting.AuthenticationPort;
                    info.UseHttps = _setting.UseSecureAuthentication;
                    AuthLoginPatcher.WriteInformation(_setting.AuthLoginFileName, info);
                }
            }
            catch (Exception e)
            {
                throw new ApplicationException("Could not patch AuthLogin.dll.", e);
            }
        }

        void PatchRegistry()
        {
            // On Linux/macOS the EnB client runs under WINE, which manages
            // its own (per-prefix) registry. Microsoft.Win32.Registry
            // throws PlatformNotSupportedException on non-Windows, so we
            // skip with a warning. WINE will create the
            // Westwood\Earth and Beyond\Registration entry on first run
            // anyway; if a user hits a registration-related crash we can
            // document `wine regedit` as the workaround.
            if (!OnWindows)
            {
                _warn("Skipping Windows registry patch on non-Windows host (WINE handles this in its per-prefix registry).");
                return;
            }
            try
            {
                WindowsRegistryHelpers.EnsureRegistered();
            }
            catch (Exception e)
            {
                throw new ApplicationException("Could not patch registry-settings.", e);
            }
        }
    }
}
