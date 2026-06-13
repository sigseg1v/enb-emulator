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

        // Inject the enbmod Lua mod runtime (enbmod.dll) into client.exe, enabling
        // client-side UI mods without patching the game. Experimental; opt-in.
        public bool   EnableClientMods         { get; set; }

        // Per-mod enable state by mod id (scripts/mods/<id>). A mod absent from the
        // map is enabled. StageClientMods stages only enabled mods. Mirrors
        // UserSettings.ModStates; copied across in MainWindow before launch.
        public System.Collections.Generic.Dictionary<string, bool> ModStates { get; set; } = new();

        public string EffectiveRegistrationHostname
            => string.IsNullOrEmpty(RegistrationHostname) ? Hostname : RegistrationHostname;
    }

    public sealed class Launcher
    {
        readonly LaunchSetting _setting;
        readonly Action<string> _warn;

        // In-client DLL injection plan, prepared by ConfigureInjection and consumed
        // by LaunchClient. WINE has no AppInit_DLLs, so any in-client DLL (the MVAS
        // position feed and/or the enbmod Lua runtime) is injected at launch via
        // FreyaInject.exe (CreateProcess-suspended + remote-LoadLibrary, N DLLs in
        // one go). _injectorExe is null / _injectDlls is empty unless at least one
        // in-client feature is enabled AND its artifacts were staged -- LaunchClient
        // falls back to a plain launch then.
        string _injectorExe;                          // unix path to bin/FreyaInject.exe (wine runs it)
        readonly List<string> _injectDlls = new();    // DOS paths the injector hands to the client's LoadLibraryA, in order

        // The spawned FreyaProxy.exe (under wine on Linux, directly on Windows).
        // Tracked so we can (a) tear it down on relaunch -- a stale proxy still
        // holding the loopback listen ports makes the next launch collide -- and
        // (b) suppress its console window now that the Advanced log viewer reads
        // the proxy's own _YYYY_MM_DD.log instead.
        Process _proxyProcess;

        public Launcher(LaunchSetting setting, Action<string> warn = null)
        {
            _setting = setting ?? throw new ArgumentNullException(nameof(setting));
            _warn = warn ?? (_ => { });
        }

        // Kill a previously-spawned proxy (if any) and forget it. Safe to call
        // when none is running. Used both on relaunch and on launcher shutdown.
        public void StopProxy()
        {
            var p = _proxyProcess;
            _proxyProcess = null;
            if (p == null) return;
            try
            {
                if (!p.HasExited)
                    p.Kill(entireProcessTree: true);
            }
            catch { /* already gone / not killable -- nothing more to do */ }
            finally { try { p.Dispose(); } catch { } }
        }

        // Tear everything this launcher spawned down: the wine proxy and the
        // loopback auth relay. MainWindow calls this on relaunch and on close.
        public void Dispose()
        {
            StopProxy();
            AuthRelay?.Dispose();
            AuthRelay = null;
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
                ConfigureInjection();

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
            if (_injectorExe != null && _injectDlls.Count > 0)
            {
                // In-client injection: don't launch client.exe directly. Spawn
                // FreyaInject.exe, which starts client.exe suspended,
                // remote-LoadLibrary's each enabled DLL (position feed and/or
                // enbmod) into it, and resumes it. The injector forwards everything
                // after the `--` delimiter as the client's own argv -- so the client
                // still sees -SERVER_ADDR / etc. We pass the client's DOS path (the
                // injector calls CreateProcessA), then the DLL DOS paths the client's
                // loader resolves, then `--`, then the client args.
                //
                // FreyaInject.exe and the DLLs are Win32 PEs: the remote-thread
                // injection is identical on native Windows and under WINE. The ONLY
                // platform difference is the `wine` prefix, so this single path serves
                // both -- it all runs on Windows (natively or via WINE).
                var clientDos = WinePathToDos(_setting.ClientPath) ?? _setting.ClientPath;
                var sb = new StringBuilder();
                sb.Append('"').Append(clientDos).Append('"');
                foreach (var dll in _injectDlls)
                    sb.Append(" \"").Append(dll).Append('"');
                sb.Append(" -- ").Append(clientArgs);
                var injectArgs = sb.ToString();
                if (OnWindows)
                {
                    info = new ProcessStartInfo
                    {
                        WorkingDirectory = dir,
                        FileName         = _injectorExe,
                        Arguments        = injectArgs,
                        UseShellExecute  = false,
                    };
                }
                else
                {
                    info = new ProcessStartInfo
                    {
                        WorkingDirectory = dir,
                        FileName         = "wine",
                        Arguments        = $"\"{_injectorExe}\" {injectArgs}",
                        UseShellExecute  = false,
                    };
                }
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

            // A proxy from a previous Play is still bound to the loopback listen
            // ports; relaunching over it collides. Kill it first.
            StopProxy();

            var info = WinExe(dir, exe, "/ADDRESS:127.0.0.1");

            // Run the proxy headless: it is a console-subsystem PE, so without
            // this it pops a console window (a wineconsole under WINE). The
            // Advanced log viewer reads the proxy's own _YYYY_MM_DD.log, so the
            // console is redundant. CreateNoWindow + redirecting stdio is what
            // actually suppresses the window on BOTH native Windows and WINE
            // (WINE won't allocate a console when stdout/stderr are inherited).
            // We drain and discard the pipes so the proxy never blocks on a full
            // buffer -- the file log is the real record.
            info.UseShellExecute        = false;
            info.CreateNoWindow         = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError  = true;

            try
            {
                var p = Process.Start(info);
                _proxyProcess = p;
                if (p != null)
                {
                    p.OutputDataReceived += (_, __) => { };
                    p.ErrorDataReceived  += (_, __) => { };
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();
                }
            }
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

        // ---- in-client DLL injection (position feed + enbmod Lua mods) ----

        // Prepare the in-client DLL injection plan. WINE (tested wine-11.8) does NOT
        // implement the Windows AppInit_DLLs loader hook -- registering a DLL there
        // loads it into nothing. So instead of a registry hook we inject at launch:
        // LaunchClient spawns FreyaInject.exe, which starts client.exe suspended,
        // remote-LoadLibrary's each staged DLL, and resumes it. This method only
        // stages the artifacts and records the plan in _injectorExe / _injectDlls;
        // the actual injection happens in LaunchClient. Nothing persists in the
        // prefix, so there is no teardown step when every feature is off.
        void ConfigureInjection()
        {
            // Start from a clean plan every call: with no feature enabled, a missing
            // injector, or every staging step failing, _injectDlls stays empty and
            // LaunchClient does a plain launch.
            _injectorExe = null;
            _injectDlls.Clear();

            if (!_setting.EnablePositionFeed && !_setting.EnableClientMods)
                return;

            // FreyaInject.exe and the DLLs are Win32 PEs that run natively on Windows
            // and under WINE on Linux, so the staging + inject plan is platform-agnostic
            // (WinePathToDos and LaunchClient handle the wine-vs-native difference).
            var injector = LocateInjectorExe();
            if (injector == null)
            {
                _warn("In-client injection enabled but FreyaInject.exe was not found. " +
                      "Build it with `just build-posfeed-dll`. Continuing without injection.");
                return;
            }

            if (_setting.EnablePositionFeed)
            {
                var dos = StagePositionFeedDll();
                if (dos != null) _injectDlls.Add(dos);
            }
            if (_setting.EnableClientMods)
            {
                var dos = StageClientMods();
                if (dos != null) _injectDlls.Add(dos);
            }

            if (_injectDlls.Count == 0)
                return;   // everything failed to stage; fall back to a plain launch

            _injectorExe = injector;
            _warn($"In-client injection: {_injectDlls.Count} DLL(s) into client.exe via FreyaInject.exe.");
        }

        // Stage the 32-bit position-feed DLL next to client.exe (the EnB release
        // folder) and return its DOS path, or null on any failure. We are NOT using
        // AppInit_DLLs (which split on spaces and forced a no-space system32 path) --
        // the injector hands the full DOS path straight to the client's remote
        // LoadLibraryA, which takes a path with spaces fine. So the DLL lives with
        // the game, not the Windows system dirs, and no WOW64 system32/syswow64
        // dance is needed.
        string StagePositionFeedDll()
        {
            var dllSrc = LocatePositionFeedDll();
            if (dllSrc == null)
            {
                _warn("Position feed enabled but FreyaPosFeed.dll was not found. " +
                      "Build it with `just build-posfeed-dll`. Continuing without the feed.");
                return null;
            }
            return StageDllNextToClient(dllSrc, "FreyaPosFeed.dll", "Position feed");
        }

        // Stage the enbmod Lua runtime (enbmod.dll) AND its scripts/ folder next to
        // client.exe, and return the DLL's DOS path, or null on any failure. enbmod
        // resolves scripts/init.lua relative to its own module directory, so the
        // scripts MUST sit beside the staged DLL (i.e. in the client release folder).
        string StageClientMods()
        {
            var dllSrc = LocateEnbmodDll();
            var scriptsSrc = LocateEnbmodScripts();
            if (dllSrc == null)
            {
                _warn("Client mods enabled but enbmod.dll was not found. " +
                      "Build it with `just build-enbmod`. Continuing without client mods.");
                return null;
            }

            var dos = StageDllNextToClient(dllSrc, "enbmod.dll", "Client mods");
            if (dos == null) return null;

            // Make sure the persistent mod store (<launcher-dir>/mods) is populated
            // with our bundled mods on a fresh/offline install before we stage from
            // it. The self-updater refreshes our mods there when online; the store
            // is the single source of truth for both ours and the user's mods.
            ModStore.SeedFromBundle(_warn);

            // Copy the scripts/ tree beside the staged DLL, but stage only the
            // ENABLED mods: disabled mod folders are skipped on copy AND pruned from
            // a previous launch's staging, so a disabled mod is never present for
            // init.lua to discover -- i.e. it is neither loaded nor injected. Missing
            // scripts is not fatal -- enbmod just logs an init.lua error and keeps
            // ticking -- but warn so the user knows why their mods aren't running.
            try
            {
                var clientDir = Path.GetDirectoryName(_setting.ClientPath);
                if (scriptsSrc != null && !string.IsNullOrEmpty(clientDir))
                    StageScriptsWithEnabledMods(scriptsSrc, Path.Combine(clientDir, "scripts"));
                else
                    _warn("Client mods: enbmod scripts/ folder not found; injecting the DLL with no scripts.");
            }
            catch (Exception ex)
            {
                _warn($"Client mods: could not stage scripts/: {ex.Message}. Injecting the DLL anyway.");
            }
            return dos;
        }

        // Stage scripts/ next to the client, honouring the per-mod enable state in
        // _setting.ModStates. The shared bootstrap (init.lua, lib/, and the
        // runtime-generated calib_data.lua) is copied from the bundled scripts
        // tree as-is. The MODS, however, come from the persistent mod store
        // (<launcher-dir>/mods, ours + the user's), NOT the bundle: only enabled
        // mod folders are copied (minus the 'modhash' bookkeeping file), and any
        // disabled or removed folder lingering from a previous launch is deleted
        // at the destination.
        void StageScriptsWithEnabledMods(string scriptsSrc, string scriptsDst)
        {
            Directory.CreateDirectory(scriptsDst);

            // Top-level files (init.lua, ...). Note: we never delete files at the
            // destination root, so a runtime-written calib_data.lua survives re-staging.
            foreach (var file in Directory.GetFiles(scriptsSrc))
                File.Copy(file, Path.Combine(scriptsDst, Path.GetFileName(file)), overwrite: true);

            // Non-mods subdirs (lib/, ...) copy wholesale.
            foreach (var sub in Directory.GetDirectories(scriptsSrc))
            {
                var name = Path.GetFileName(sub);
                if (string.Equals(name, "mods", StringComparison.OrdinalIgnoreCase)) continue;
                CopyDirectory(sub, Path.Combine(scriptsDst, name));
            }

            var modsSrc = ModStore.Dir();
            var modsDst = Path.Combine(scriptsDst, "mods");
            if (!Directory.Exists(modsSrc))
            {
                // No store at all: nothing to stage, but still prune any leftovers.
                if (Directory.Exists(modsDst)) Directory.Delete(modsDst, recursive: true);
                return;
            }
            Directory.CreateDirectory(modsDst);

            var states = _setting.ModStates;
            int staged = 0, skipped = 0;
            foreach (var modDir in Directory.GetDirectories(modsSrc))
            {
                var id = Path.GetFileName(modDir);
                var destDir = Path.Combine(modsDst, id);
                if (ModCatalog.IsEnabled(states, id))
                {
                    CopyDirectory(modDir, destDir);
                    // 'modhash' is launcher bookkeeping, not a mod file -- don't
                    // ship it into the game's load location.
                    var hashFile = Path.Combine(destDir, ModStore.ModHashFileName);
                    if (File.Exists(hashFile)) File.Delete(hashFile);
                    staged++;
                }
                else
                {
                    // Disabled: ensure no stale copy remains from a prior launch.
                    if (Directory.Exists(destDir)) Directory.Delete(destDir, recursive: true);
                    skipped++;
                }
            }

            // Prune any destination mod folder that no longer exists in the source.
            foreach (var destDir in Directory.GetDirectories(modsDst))
            {
                var id = Path.GetFileName(destDir);
                if (!Directory.Exists(Path.Combine(modsSrc, id)))
                    Directory.Delete(destDir, recursive: true);
            }

            _warn($"Client mods: staged {staged} mod(s), skipped {skipped} disabled.");
        }

        // Copy a DLL into the client release folder and return its DOS path, or null
        // (with a warning tagged `label`) on any failure.
        string StageDllNextToClient(string dllSrc, string stagedName, string label)
        {
            string dllStaged;
            try
            {
                var clientDir = Path.GetDirectoryName(_setting.ClientPath);
                if (string.IsNullOrEmpty(clientDir) || !Directory.Exists(clientDir))
                    throw new IOException($"client folder not found ('{clientDir}')");
                dllStaged = Path.Combine(clientDir, stagedName);
                File.Copy(dllSrc, dllStaged, overwrite: true);
            }
            catch (Exception ex)
            {
                _warn($"{label}: could not stage {stagedName}: {ex.Message}. Continuing without it.");
                return null;
            }

            var dosPath = WinePathToDos(dllStaged);
            if (string.IsNullOrEmpty(dosPath))
            {
                _warn($"{label}: could not resolve {stagedName}'s DOS path. Continuing without it.");
                return null;
            }
            _warn($"{label}: will inject {dosPath} into client.exe via FreyaInject.exe.");
            return dosPath;
        }

        static void CopyDirectory(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var file in Directory.GetFiles(src))
                File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), overwrite: true);
            foreach (var sub in Directory.GetDirectories(src))
                CopyDirectory(sub, Path.Combine(dst, Path.GetFileName(sub)));
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

        // Find the built enbmod runtime across dev (play-local, CWD=repo root) and
        // packaged (next to the launcher exe / its bin/) layouts.
        static string LocateEnbmodDll()
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "bin", "enbmod.dll"),
                Path.Combine(AppContext.BaseDirectory, "enbmod.dll"),
                Path.Combine(Directory.GetCurrentDirectory(), "bin", "enbmod.dll"),
            };
            foreach (var c in candidates)
                if (File.Exists(c)) return c;
            return null;
        }

        // Find the enbmod scripts/ folder (staged beside enbmod.dll by `just
        // build-enbmod`) across the same dev / packaged layouts.
        static string LocateEnbmodScripts()
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "bin", "scripts"),
                Path.Combine(AppContext.BaseDirectory, "scripts"),
                Path.Combine(Directory.GetCurrentDirectory(), "bin", "scripts"),
            };
            foreach (var c in candidates)
                if (Directory.Exists(c)) return c;
            return null;
        }

        // Public accessor for the source scripts/ dir, so ModCatalog (and the
        // "Configure Mods" UI) reads the same tree StageClientMods stages from.
        public static string LocateScriptsDir() => LocateEnbmodScripts();

        // `winepath -w <unix>` -> the DOS path WINE uses to open the file. On
        // native Windows there is no WINE and the path is already a DOS path, so
        // return it unchanged.
        static string WinePathToDos(string unixPath)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return unixPath;
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
