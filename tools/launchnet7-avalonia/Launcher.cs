using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using LaunchNet7Avalonia.Config;
using LaunchNet7Avalonia.Network;
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
        public string Hostname                 { get; set; }
        public string RegistrationHostname     { get; set; }
        public string LaunchName               { get; set; }

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

        // The relay's lifetime is tied to the launcher process. MainWindow
        // keeps the window open after Play so the relay (and the spawned
        // wine proxy + client) all stay up until the user hits Quit.
        public LocalAuthRelay AuthRelay { get; private set; }

        public void Launch()
        {
            StartLocalAuthRelay();
            try
            {
                var launch = (_setting.LaunchName ?? "").ToUpperInvariant();

                // Where the CLIENT dials for the game servers (master/global/
                // sector/chat/...). When we spawn a local Net7Proxy (SP and MP
                // online play), that is ALWAYS loopback: the proxy listens on
                // 127.0.0.1 for the client's TCP and bridges it to the real
                // server over UDP. Pointing the client straight at the public
                // host fails -- only the server's UDP game ports are reachable
                // there, not the client-facing TCP listeners (3801/3805/3500),
                // which exist solely on the local proxy. When no local proxy is
                // spawned (the Net7Local docker dev stack) the client dials
                // Hostname directly, which is the docker-published proxy.
                //
                // The proxy's UPSTREAM and the auth relay/registration host stay
                // _setting.Hostname (the real server) -- see LaunchNet7Proxy /
                // StartLocalAuthRelay / EffectiveRegistrationHostname.
                bool spawnsLocalProxy = launch == "NET7SP" || launch == "NET7MP";
                string gameHost = spawnsLocalProxy ? "127.0.0.1" : _setting.Hostname;

                PatchAuthLoginFile();
                PatchRegDataFileNames();
                PatchRegDataFile();
                PatchAuthIniFile();
                PatchNetworkIniFile(gameHost);
                PatchRegistry();

                switch (launch)
                {
                    case "NET7SP":
                        LaunchNet7Server();
                        System.Threading.Thread.Sleep(25000);
                        LaunchNet7Proxy();
                        System.Threading.Thread.Sleep(2000);
                        LaunchClient(gameHost);
                        break;

                    case "NET7MP":
                        LaunchNet7Proxy();
                        System.Threading.Thread.Sleep(2000);
                        LaunchClient(gameHost);
                        break;

                    default:
                        LaunchClient(gameHost);
                        break;
                }
            }
            catch
            {
                // A later step failed. The loopback relay we started above is
                // still holding its bound port; if we leave it running the next
                // "Play" can't rebind and fails with "Could not start local auth
                // relay". Tear it down so a retry starts clean.
                AuthRelay?.Dispose();
                AuthRelay = null;
                throw;
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

        void LaunchClient(string gameHost)
        {
            var addrs = Dns.GetHostAddresses(gameHost);
            if (addrs.Length == 0)
                throw new InvalidOperationException($"Could not resolve hostname '{gameHost}'.");

            var dir = Path.GetDirectoryName(_setting.ClientPath);
            var info = WinExe(dir, _setting.ClientPath,
                $"-SERVER_ADDR {addrs[0]} -PROTOCOL TCP");

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

            // Resolve relative to the launcher exe's own location, NOT the
            // process CWD. Double-clicking in Explorer happens to set CWD to
            // the exe folder, but launching from a shell elsewhere or via a
            // shortcut with a different "Start in" would otherwise break the
            // spawn. AppContext.BaseDirectory is the exe's folder (verified to
            // hold even for the single-file self-extracting package build).
            var dir = Path.Combine(AppContext.BaseDirectory, "bin");
            var exe = Path.Combine(dir, "Net7Proxy.exe");
            var info = WinExe(dir, exe, $"/ADDRESS:{addrs[0]}");

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
            // Resolve relative to the launcher exe's own location (see the
            // note in LaunchNet7Proxy) rather than the process CWD.
            var dir = Path.Combine(AppContext.BaseDirectory, "bin");
            var exe = Path.Combine(dir, "Net7.exe");
            var info = WinExe(dir, exe, null);

            try { Process.Start(info); }
            catch (Exception e)
            {
                throw new ApplicationException("Could not launch Net7 Server. Details: " + e.Message, e);
            }
        }

        // ---- patching ----

        void PatchNetworkIniFile(string host)
        {
            var file       = Path.Combine(_setting.CommonDirectoryName, "network.ini");
            var backup     = Path.Combine(_setting.CommonDirectoryName, "network.ini.orig");

            if (!File.Exists(file) && File.Exists(backup)) File.Copy(backup, file);

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
            var file   = Path.Combine(_setting.IniDirectoryName, "auth.ini");
            var backup = Path.Combine(_setting.IniDirectoryName, "auth.ini.orig");
            if (!File.Exists(file) && File.Exists(backup)) File.Copy(backup, file);

            var regHost = _setting.EffectiveRegistrationHostname;
            var url = new UriBuilder
            {
                Scheme = "https",
                Host   = regHost,
                Path   = "misc/touchsession.jsp",
                Query  = "lkey=%s",
            }.ToString();

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

            var url = new UriBuilder
            {
                Scheme = "https",
                Host   = _setting.EffectiveRegistrationHostname,
                Path   = "subsxml",
            }.ToString();

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
            // authlogin.dll always dials 127.0.0.1:LocalAuthRelay.ListenPort plaintext.
            // The relay terminates on loopback and re-wraps as TLS to the upstream.
            try
            {
                var info = AuthLoginPatcher.ReadInformation(_setting.AuthLoginFileName);
                if (info.Port != LocalAuthRelay.ListenPort || info.UseHttps)
                {
                    info.Port     = (ushort)LocalAuthRelay.ListenPort;
                    info.UseHttps = false;
                    AuthLoginPatcher.WriteInformation(_setting.AuthLoginFileName, info);
                }
            }
            catch (FileNotFoundException e)
            {
                throw new ApplicationException(
                    $"authlogin.dll not found at {_setting.AuthLoginFileName}. The standalone " +
                    "package does not ship the client mods -- you need a Net-7-patched Earth & " +
                    "Beyond install (authlogin.dll present in the client's release folder).", e);
            }
            catch (UnauthorizedAccessException e)
            {
                throw new ApplicationException(
                    $"Access denied writing {_setting.AuthLoginFileName}. Run the launcher as " +
                    "Administrator (the client lives under Program Files, which is write-protected).", e);
            }
            catch (Exception e)
            {
                throw new ApplicationException("Could not patch AuthLogin.dll.", e);
            }
        }

        void StartLocalAuthRelay()
        {
            try
            {
                AuthRelay?.Dispose();
                AuthRelay = LocalAuthRelay.Start(
                    upstreamHost: _setting.Hostname,
                    upstreamPort: _setting.AuthenticationPort,
                    log:          _warn);
            }
            catch (Exception e)
            {
                throw new ApplicationException("Could not start local auth relay.", e);
            }
        }

        void PatchRegistry()
        {
            if (OnWindows)
            {
                try { WindowsRegistryHelpers.EnsureRegistered(); }
                catch (Exception e)
                {
                    throw new ApplicationException("Could not patch registry-settings.", e);
                }
                return;
            }

            // Under WINE we have to write the per-prefix registry ourselves.
            // AuthLoginServer is HARDCODED to "localhost" — authlogin.dll
            // always dials the in-process LocalAuthRelay on loopback, which
            // re-wraps the call as TLS to the actual upstream. There is no
            // user-facing knob for this value; changing it would let plaintext
            // bytes leave the box, which is exactly what the relay exists to
            // prevent.
            (string key, string value, string type, string data)[] entries =
            {
                (@"HKLM\Software\EACom\AuthAuth",                       "AuthLoginServer",      "REG_SZ",   "localhost"),
                (@"HKLM\Software\EACom\AuthAuth",                       "AuthLoginBaseService", "REG_SZ",   "AuthLogin"),
                (@"HKLM\Software\Westwood Studios\Earth and Beyond\Registration",
                                                                        "Registered",           "REG_DWORD","1"),
            };
            foreach (var e in entries)
            {
                try { WineRegAdd(e.key, e.value, e.type, e.data); }
                catch (Exception ex)
                {
                    _warn($"WINE registry write failed for {e.key}\\{e.value}: {ex.Message}");
                }
            }
        }

        static void WineRegAdd(string key, string value, string type, string data)
        {
            // `wine reg add KEY /v VALUE /t TYPE /d DATA /f`. /f suppresses
            // the overwrite prompt. UseShellExecute=false so we inherit
            // WINEPREFIX from the launching process (set by `just play-local`).
            var psi = new ProcessStartInfo
            {
                FileName               = "wine",
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true,
            };
            psi.ArgumentList.Add("reg");
            psi.ArgumentList.Add("add");
            psi.ArgumentList.Add(key);
            psi.ArgumentList.Add("/v"); psi.ArgumentList.Add(value);
            psi.ArgumentList.Add("/t"); psi.ArgumentList.Add(type);
            psi.ArgumentList.Add("/d"); psi.ArgumentList.Add(data);
            psi.ArgumentList.Add("/f");

            using var p = Process.Start(psi) ?? throw new InvalidOperationException("wine reg add did not start");
            if (!p.WaitForExit(10000))
            {
                try { p.Kill(); } catch { }
                throw new TimeoutException("wine reg add timed out");
            }
            if (p.ExitCode != 0)
                throw new InvalidOperationException($"wine reg add exited {p.ExitCode}: {p.StandardError.ReadToEnd().Trim()}");
        }
    }
}
