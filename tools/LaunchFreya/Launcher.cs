using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using LaunchFreya.Config;
using LaunchFreya.Network;
using LaunchFreya.Patching;

namespace LaunchFreya
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

        // PB-2: inject the in-client MVAS position-feed DLL into client.exe.
        public bool   EnablePositionFeed       { get; set; }

        public string EffectiveRegistrationHostname
            => string.IsNullOrEmpty(RegistrationHostname) ? Hostname : RegistrationHostname;
    }

    public sealed class Launcher
    {
        readonly LaunchSetting _setting;
        readonly Action<string> _warn;

        // PB-2 injection plan, prepared by ConfigurePositionFeedInjection and
        // consumed by LaunchClient. WINE has no AppInit_DLLs, so the feed DLL is
        // injected at launch via FreyaInject.exe (CreateProcess-suspended +
        // remote-LoadLibrary). Both null/empty unless the feed is enabled AND its
        // artifacts were located -- LaunchClient falls back to a plain launch then.
        string _posFeedInjectorExe;   // unix path to bin/FreyaInject.exe (wine runs it)
        string _posFeedDllDos;        // DOS path the injector hands to the client's LoadLibraryA

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
                // _setting.Hostname (the real server) -- see LaunchFreyaProxy /
                // StartLocalAuthRelay / EffectiveRegistrationHostname.
                bool spawnsLocalProxy = launch == "NET7SP" || launch == "NET7MP";
                string gameHost = spawnsLocalProxy ? "127.0.0.1" : _setting.Hostname;

                PatchAuthLoginFile();
                PatchRegDataFileNames();
                PatchRegDataFile();
                PatchAuthIniFile();
                PatchNetworkIniFile(gameHost);
                PatchRegistry();
                ConfigurePositionFeedInjection();

                switch (launch)
                {
                    case "NET7SP":
                        LaunchNet7Server();
                        System.Threading.Thread.Sleep(25000);
                        LaunchFreyaProxy();
                        System.Threading.Thread.Sleep(2000);
                        LaunchClient(gameHost);
                        break;

                    case "NET7MP":
                        LaunchFreyaProxy();
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
            var clientArgs = $"-SERVER_ADDR {addrs[0]} -PROTOCOL TCP";

            ProcessStartInfo info;
            if (_posFeedInjectorExe != null && _posFeedDllDos != null && !OnWindows)
            {
                // PB-2 position feed: don't launch client.exe directly. Spawn
                // FreyaInject.exe (under WINE), which starts client.exe suspended,
                // remote-LoadLibrary's FreyaPosFeed.dll into it, and resumes it.
                // The injector forwards everything after the DLL path as the
                // client's own argv -- so the client still sees -SERVER_ADDR / etc.
                // We pass the client's DOS path (the injector calls CreateProcessA),
                // and the DLL's DOS path (the client's loader resolves it).
                var clientDos = WinePathToDos(_setting.ClientPath) ?? _setting.ClientPath;
                info = new ProcessStartInfo
                {
                    WorkingDirectory = dir,
                    FileName         = "wine",
                    Arguments        = $"\"{_posFeedInjectorExe}\" \"{clientDos}\" \"{_posFeedDllDos}\" {clientArgs}",
                    UseShellExecute  = false,
                };
            }
            else
            {
                info = WinExe(dir, _setting.ClientPath, clientArgs);
            }

            try { Process.Start(info); }
            catch (Exception e)
            {
                throw new ApplicationException(
                    $"Could not launch client.\nWorking Directory: {info.WorkingDirectory}\nFileName: {info.FileName}\nArguments: {info.Arguments}\nDetails: {e.Message}", e);
            }
        }

        void LaunchFreyaProxy()
        {
            // Resolve relative to the launcher exe's own location, NOT the
            // process CWD. Double-clicking in Explorer happens to set CWD to
            // the exe folder, but launching from a shell elsewhere or via a
            // shortcut with a different "Start in" would otherwise break the
            // spawn. AppContext.BaseDirectory is the exe's folder (verified to
            // hold even for the single-file self-extracting package build).
            var dir = Path.Combine(AppContext.BaseDirectory, "bin");
            var exe = Path.Combine(dir, "FreyaProxy.exe");

            // Hand the proxy its DTLS config BEFORE spawning it. The proxy<->
            // server UDP leg is DTLS, fail-closed (Phase AH): the proxy refuses
            // to start unless it knows the cert hostname to verify, and -- under
            // WINE, where its OpenSSL has no system CA bundle -- a trust anchor
            // to verify against.
            //
            // The UPSTREAM (dialled) host is conveyed entirely via
            // NET7_UPSTREAM_HOST (-> NET7_GAME_SERVER_HOST), which the proxy
            // resolves itself; /ADDRESS is a SEPARATE thing -- the proxy's local
            // listen/advertise identity, i.e. the address it stamps into the
            // sector ServerRedirect (opcode 0x0036) telling the client where to
            // reconnect for the sector. The proxy ALWAYS runs co-located with the
            // client (here, under WINE on the player's own machine), so that
            // address must be loopback, NEVER the dialled server IP. Feeding the
            // dialled IP here pointed the client's sector TCP at the remote server
            // instead of the local proxy, so it never connected, timed out at
            // ~15s, and got kicked back to login. (play-local happened to work
            // because its hostname already resolves to 127.0.0.1.)
            ConfigureProxyDtlsEnv(_setting.Hostname, _setting.AuthenticationPort, dir);

            var info = WinExe(dir, exe, "/ADDRESS:127.0.0.1");

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
            // note in LaunchFreyaProxy) rather than the process CWD.
            var dir = Path.Combine(AppContext.BaseDirectory, "bin");
            var exe = Path.Combine(dir, "Net7.exe");
            var info = WinExe(dir, exe, null);

            try { Process.Start(info); }
            catch (Exception e)
            {
                throw new ApplicationException("Could not launch Net7 Server. Details: " + e.Message, e);
            }
        }

        // ---- proxy DTLS provisioning ----

        // Set the environment the spawned Net7Proxy reads for its proxy<->server
        // DTLS policy. Inherited by the child (and by WINE, which forwards host
        // env to the Win32 process). See proxy/Net7.cpp::InitProxyDtlsPolicy.
        void ConfigureProxyDtlsEnv(string hostname, int authPort, string proxyDir)
        {
            // The proxy connects to the server by IP but VERIFIES the cert by
            // name -- give it the upstream hostname (NET7_UPSTREAM_HOST ->
            // g_UpstreamHost -> the DTLS verify domain). Without this the proxy
            // FATALs at startup with "no server cert hostname is configured".
            Environment.SetEnvironmentVariable("NET7_UPSTREAM_HOST", hostname);

            // The loopback dev stack speaks cleartext UDP (its compose opts out
            // of DTLS), so a local proxy pointed at it must match -- and there is
            // no public cert to fetch. Online play uses real PKI; fetch + pin it.
            if (IsLoopbackHost(hostname))
            {
                Environment.SetEnvironmentVariable(
                    "NET7_DTLS_ALLOW_PLAINTEXT", "i-accept-unencrypted-udp");
                Environment.SetEnvironmentVariable("NET7_DTLS_CA", null);
                return;
            }

            Environment.SetEnvironmentVariable("NET7_DTLS_ALLOW_PLAINTEXT", null);

            // The proxy's MinGW/WINE OpenSSL has no system CA bundle in the
            // prefix, so it can't verify a public cert against the trust store.
            // Fetch the server's cert chain over TLS on Play and pin it as the
            // proxy's trust anchor. The login :443 endpoint presents the SAME
            // ${DOMAIN} cert the DTLS game server does (deploy mounts one cert
            // into both), so harvesting it here is correct.
            try
            {
                string pem = FetchServerCertChainPem(
                    hostname, authPort, TimeSpan.FromSeconds(10));
                string caPath = Path.Combine(proxyDir, "server-dtls-ca.pem");
                File.WriteAllText(caPath, pem);
                // The proxy runs with WorkingDirectory = proxyDir; pass the bare
                // file name so the Win32/WINE fopen resolves it via CWD and we
                // avoid Unix->Windows path translation inside the prefix.
                Environment.SetEnvironmentVariable("NET7_DTLS_CA", "server-dtls-ca.pem");
                _warn($"Pinned server DTLS cert from {hostname}:{authPort} -> {caPath}");
            }
            catch (Exception e)
            {
                // Don't pin a cert we couldn't verify. The proxy will then fall
                // back to the (empty-under-WINE) system store and DTLS will fail
                // -- surface why rather than letting it look like a proxy crash.
                Environment.SetEnvironmentVariable("NET7_DTLS_CA", null);
                _warn($"WARNING: could not fetch server cert from {hostname}:{authPort} " +
                      $"({e.Message}). DTLS verification will have no trust anchor.");
            }
        }

        static bool IsLoopbackHost(string host)
        {
            if (string.IsNullOrWhiteSpace(host)) return false;
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
            return IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip);
        }

        // Open a TLS connection to host:port, validate the presented cert against
        // the launcher's (real) system trust store, and return the server's cert
        // chain as concatenated PEM. Throws if the cert doesn't validate -- we
        // refuse to pin an untrusted cert.
        static string FetchServerCertChainPem(string host, int port, TimeSpan timeout)
        {
            using var tcp = new TcpClient();
            if (!tcp.ConnectAsync(host, port).Wait(timeout))
                throw new TimeoutException($"connect to {host}:{port} timed out");

            var pems = new List<string>();
            using var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false,
                userCertificateValidationCallback: (sender, cert, chain, errors) =>
                {
                    if (chain != null)
                        foreach (var el in chain.ChainElements)
                            pems.Add(el.Certificate.ExportCertificatePem());
                    else if (cert is X509Certificate2 c2)
                        pems.Add(c2.ExportCertificatePem());
                    // Only accept a cert that validates against the launcher's
                    // trust store (name + chain). AuthenticateAsClient throws if
                    // we return false here.
                    return errors == SslPolicyErrors.None;
                });
            ssl.AuthenticateAsClient(host);
            if (pems.Count == 0)
                throw new InvalidOperationException("server presented no certificate");
            return string.Join("\n", pems) + "\n";
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

            // AAIUrl is the HOST authlogin.dll dials for /AuthLogin. The dll is
            // patched (PatchAuthLoginFile) to clear HTTPS and use port 4180, so it
            // builds "http://<AAIUrl>:4180/AuthLogin". For the relay to catch that
            // call, AAIUrl MUST be loopback -- ALWAYS, local and online alike. The
            // relay (StartLocalAuthRelay) is the only thing that knows the real
            // upstream and re-wraps the call as TLS to it. Writing the upstream host
            // here (the old behaviour) bypassed the relay: online the dll dialed
            // http://<upstream>:4180 -- plaintext on a port the server doesn't serve
            // -- and WinINet failed with 12029. See docs/17: the client only ever
            // talks to loopback; only the relay's upstream moves for a remote deploy.
            const string authHost = "localhost";

            // LKeyUrl (touchsession.jsp) is a full https:// URL the client calls
            // DIRECTLY (not via authlogin.dll, not via the relay), so it points at
            // the real registration host over real TLS.
            var lkeyUrl = new UriBuilder
            {
                Scheme = "https",
                Host   = _setting.EffectiveRegistrationHostname,
                Path   = "misc/touchsession.jsp",
                Query  = "lkey=%s",
            }.ToString();

            bool needPatch =
                !string.Equals(IniFile.GetValue(file, "General", "AAIUrl"),  authHost, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(IniFile.GetValue(file, "General", "LKeyUrl"), lkeyUrl,  StringComparison.OrdinalIgnoreCase);

            if (!needPatch) return;
            if (File.Exists(file)) File.Copy(file, backup, true);
            IniFile.SetValue(file, "General", "AAIUrl",  authHost);
            IniFile.SetValue(file, "General", "LKeyUrl", lkeyUrl);
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

        // ---- PB-2: in-client position-feed DLL injection ----

        // Prepare the PB-2 position-feed injection. WINE (tested wine-11.8) does
        // NOT implement the Windows AppInit_DLLs loader hook -- registering the DLL
        // there loads it into nothing. So instead of a registry hook we inject at
        // launch: LaunchClient spawns FreyaInject.exe, which starts client.exe
        // suspended, remote-LoadLibrary's the DLL, and resumes it. This method only
        // stages the artifacts and records the plan in _posFeedInjectorExe /
        // _posFeedDllDos; the actual injection happens in LaunchClient. Nothing
        // persists in the prefix, so there is no teardown step when the feed is off.
        void ConfigurePositionFeedInjection()
        {
            // Start from a clean plan every call: a disabled feed, a missing
            // artifact, or the Windows path all leave the fields null and
            // LaunchClient does a plain launch.
            _posFeedInjectorExe = null;
            _posFeedDllDos      = null;

            if (!_setting.EnablePositionFeed)
                return;

            // Native-Windows injection (Detours withdll) is a separate mechanism and
            // is not wired here -- this path is the WINE prefix only.
            if (OnWindows)
            {
                _warn("Position feed: in-client injection on native Windows is not wired " +
                      "in this launcher; only the WINE (Linux) path is. Skipping.");
                return;
            }

            var dllSrc = LocatePositionFeedDll();
            var injector = LocateInjectorExe();
            if (dllSrc == null || injector == null)
            {
                _warn("Position feed enabled but its artifacts were not found " +
                      $"(dll={dllSrc ?? "<missing>"}, injector={injector ?? "<missing>"}). " +
                      "Build them with `just build-posfeed-dll`. Continuing without the feed.");
                return;
            }

            // Stage the 32-bit DLL next to client.exe (the EnB release folder).
            // We are NOT using AppInit_DLLs (which split on spaces and forced a
            // no-space system32 path) -- the injector hands the full DOS path
            // straight to the client's remote LoadLibraryA, which takes a path with
            // spaces fine. So the DLL lives with the game, not in the Windows
            // system dirs, and no WOW64 system32/syswow64 dance is needed.
            string dllStaged;
            try
            {
                var clientDir = Path.GetDirectoryName(_setting.ClientPath);
                if (string.IsNullOrEmpty(clientDir) || !Directory.Exists(clientDir))
                    throw new IOException($"client folder not found ('{clientDir}')");
                dllStaged = Path.Combine(clientDir, "FreyaPosFeed.dll");
                File.Copy(dllSrc, dllStaged, overwrite: true);
            }
            catch (Exception ex)
            {
                _warn($"Position feed: could not stage FreyaPosFeed.dll: {ex.Message}. Continuing without the feed.");
                return;
            }

            var dosPath = WinePathToDos(dllStaged);
            if (string.IsNullOrEmpty(dosPath))
            {
                _warn("Position feed: could not resolve the DLL's DOS path. Continuing without the feed.");
                return;
            }

            _posFeedInjectorExe = injector;
            _posFeedDllDos      = dosPath;
            _warn($"Position feed: will inject {dosPath} into client.exe via FreyaInject.exe.");
        }

        // Find the built injector across dev (play-local, CWD=repo root) and
        // packaged (next to the launcher exe / its bin/) layouts.
        static string LocateInjectorExe()
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "bin", "FreyaInject.exe"),
                Path.Combine(AppContext.BaseDirectory, "FreyaInject.exe"),
                Path.Combine(Directory.GetCurrentDirectory(), "bin", "FreyaInject.exe"),
            };
            foreach (var c in candidates)
                if (File.Exists(c)) return c;
            return null;
        }

        // Find the built feed DLL across dev (play-local, CWD=repo root) and
        // packaged (next to the launcher exe / its bin/) layouts.
        static string LocatePositionFeedDll()
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "bin", "FreyaPosFeed.dll"),
                Path.Combine(AppContext.BaseDirectory, "FreyaPosFeed.dll"),
                Path.Combine(Directory.GetCurrentDirectory(), "bin", "FreyaPosFeed.dll"),
            };
            foreach (var c in candidates)
                if (File.Exists(c)) return c;
            return null;
        }

        // `winepath -w <unix>` -> the DOS path WINE uses to open the file.
        static string WinePathToDos(string unixPath)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName               = "wine",
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true,
                };
                psi.ArgumentList.Add("winepath");
                psi.ArgumentList.Add("-w");
                psi.ArgumentList.Add(unixPath);

                using var p = Process.Start(psi);
                if (p == null) return null;
                string outp = p.StandardOutput.ReadToEnd().Trim();
                if (!p.WaitForExit(10000)) { try { p.Kill(); } catch { } return null; }
                return p.ExitCode == 0 && outp.Length > 0 ? outp : null;
            }
            catch { return null; }
        }

        static void WineRegAdd(string key, string value, string type, string data)
        {
            // The EnB client is a 32-bit process, so when it reads HKLM\Software\
            // <key> it is WOW64-redirected to HKLM\Software\Wow6432Node\<key>. A
            // plain `wine reg add` writes the 64-bit view, which the client never
            // reads -- leaving the stale retail value (e.g. AuthLoginServer =
            // "www.ea.com") in the 32-bit view. The client then dials www.ea.com
            // instead of the local relay and authlogin fails with WinINet 12029.
            // Write BOTH views (/reg:32 and /reg:64) so the value is correct no
            // matter which bitness reads it.
            foreach (var view in new[] { "32", "64" })
                WineRegAddView(key, value, type, data, view);
        }

        static void WineRegAddView(string key, string value, string type, string data, string view)
        {
            // `wine reg add KEY /v VALUE /t TYPE /d DATA /f /reg:32|64`. /f
            // suppresses the overwrite prompt. UseShellExecute=false so we inherit
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
            psi.ArgumentList.Add($"/reg:{view}");

            using var p = Process.Start(psi) ?? throw new InvalidOperationException("wine reg add did not start");
            if (!p.WaitForExit(10000))
            {
                try { p.Kill(); } catch { }
                throw new TimeoutException("wine reg add timed out");
            }
            if (p.ExitCode != 0)
                throw new InvalidOperationException($"wine reg add (/reg:{view}) exited {p.ExitCode}: {p.StandardError.ReadToEnd().Trim()}");
        }
    }
}
