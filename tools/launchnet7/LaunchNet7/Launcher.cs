using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Diagnostics;
using System.Threading;
using System.Net;
using Microsoft.Win32;
using LaunchNet7.Patching;

namespace LaunchNet7
{
    public class Launcher
    {
        /// <summary>
        /// The hostname every patched client config file points at. The client
        /// only ever connects to the local proxy on this name; the proxy is
        /// what decides which upstream the traffic is forwarded to. mkcert
        /// generates a cert with this name as the CN so the embedded HTTPS
        /// listener validates correctly.
        /// </summary>
        public const string LocalHostname = "localhost";

        public Launcher(LaunchSetting setting)
        {
            if (setting == null) throw new ArgumentNullException("setting");
            m_Setting = setting;
        }

        public LaunchSetting Setting
        {
            get { return m_Setting; }
            set { m_Setting = value; }
        }
        private LaunchSetting m_Setting;

        public void Launch()
        {
            PatchRegistry();
            PatchRegistry64();
            PatchAuthLoginFile();
            PatchRegDataFileNames();
            PatchRegDataFile();
            PatchAuthIniFile();
            PatchNetworkIniFile();
            EnsureLocalCertIfRequested();

            switch (Setting.LaunchName.ToUpperInvariant())
            {
                case "NET7SP":
                    {
                        LaunchNet7Server();
                        Thread.Sleep(25000); //allow 25 seconds for server startup

                        LaunchNet7Proxy();
                    }
                    break;

                case "NET7MP":
                    {
                        LaunchNet7Proxy();
                    }
                    break;

                default:
                    {
                        LaunchClient();
                    }
                    break;
            }
        }

        private void LaunchClient()
        {
            IPAddress[] addresses = Dns.GetHostAddresses(LocalHostname);
            if (addresses.Length == 0) throw new InvalidOperationException(String.Format("Could not resolve hostname '{0}'.", LocalHostname));

            ProcessStartInfo info = new ProcessStartInfo();
            if (Setting.UseClientDetours)
            {
                info.WorkingDirectory = Path.Combine(Directory.GetCurrentDirectory(), "bin");
                info.FileName = Path.Combine(info.WorkingDirectory, "Detours.exe");
                info.Arguments = String.Format
                (
                    "/ADDR:{0} /CLIENT:\"{1}\"",
                    addresses[0].ToString(),
                    LauncherUtility.GetShortPathName(Setting.ClientPath)
                );
            }
            else
            {
                info.WorkingDirectory = Path.GetDirectoryName(Setting.ClientPath);
                info.FileName = Setting.ClientPath;
                info.Arguments = String.Format
                (
                    "-SERVER_ADDR {0} -PROTOCOL {1}",
                    addresses[0].ToString(),
                    "TCP"
                );
            }
            try
            {
                Process.Start(info);
            }
            catch (Exception e)
            {
                throw new ApplicationException(String.Format("Could not launch client.\nWorking Directory: {0}\nFileName: {1}\nArguments: {2}\nDetails: {3}", info.WorkingDirectory, info.FileName, info.Arguments, e.Message), e);
            }
        }

        private void LaunchNet7Proxy()
        {
            IPAddress[] addresses = Dns.GetHostAddresses(LocalHostname);
            if (addresses.Length == 0) throw new InvalidOperationException(String.Format("Could not resolve hostname '{0}'.", LocalHostname));

            ProcessStartInfo info = new ProcessStartInfo();

            info.WorkingDirectory = Path.Combine(Directory.GetCurrentDirectory(), "bin");
            info.FileName = Path.Combine(info.WorkingDirectory, "Net7Proxy.exe");
            // /CLIENT path is quoted so WINE prefixes / "Program Files" / any
            // path with a space survives the argv parse on the proxy side.
            info.Arguments = String.Format
            (
                "/ADDRESS:{0} /CLIENT:\"{1}\"",
                addresses[0].ToString(),
                LauncherUtility.GetShortPathName(Setting.ClientPath)
            );

            if (Setting.UseClientDetours)
            {
                info.Arguments += " /L";
            }

            if (Setting.UseLocalCert)
            {
                info.Arguments += " /LC";
                info.Arguments += String.Format
                (
                    " /SSL:{0}",
                    Setting.AuthenticationPort.ToString()
                );
            }

            // Pass the user-selected upstream through to the proxy. The proxy
            // honours NET7_UPSTREAM_HOST as the trivially-runtime-changeable
            // way to point traffic at a different deployment without rebuild.
            if (!String.IsNullOrEmpty(Setting.Hostname) &&
                !String.Equals(Setting.Hostname, LocalHostname, StringComparison.OrdinalIgnoreCase))
            {
                info.EnvironmentVariables["NET7_UPSTREAM_HOST"] = Setting.Hostname;
                info.UseShellExecute = false;
            }

            try
            {
                Process.Start(info);
            }
            catch (Exception e)
            {
                Program.LogException(e);
                throw new ApplicationException(String.Format("Could not launch Net7Proxy.\nWorking Directory: {0}\nFileName: {1}\nArguments: {2}\nDetails: {3}", info.WorkingDirectory, info.FileName, info.Arguments, e.Message), e);
            }
        }

        private void LaunchNet7Server()
        {
            ProcessStartInfo info = new ProcessStartInfo();

            info.WorkingDirectory = Path.Combine(Directory.GetCurrentDirectory(), "bin");
            info.FileName = Path.Combine(info.WorkingDirectory, "Net7.exe");
            try
            {
                Process.Start(info);
            }
            catch (Exception e)
            {
                throw new ApplicationException("Could not launch Net7 Server. Details: " + e.Message, e);
            }
        }

        private void PatchNetworkIniFile()
        {
            string file = Path.Combine(Setting.CommonDirectoryName, "Network.ini");
            string fileBackup = Path.Combine(Setting.CommonDirectoryName, "Network.ini.orig");
            bool patchRequired = false;

            EnsureBackup(file, fileBackup);
            if (File.Exists(file) == false && File.Exists(fileBackup))
            {
                File.Copy(fileBackup, file);
            }

            string hostName = LocalHostname;

            string[] sections =
            {
                "MasterServer", "RegisterServer", "ReporterServer",
                "GlobalServer_Directory", "GlobalServer_Client",
                "GlobalServer_Register", "GlobalServer_Parent",
                "ChatServer", "ChatServer_Basic",
                "GroupServer", "GuildServer",
            };

            foreach (string s in sections)
            {
                if (!String.Equals(IniUtility.GetValue(file, s, "Name"), hostName, StringComparison.InvariantCultureIgnoreCase))
                {
                    patchRequired = true;
                    break;
                }
            }

            if (!patchRequired) return;

            foreach (string s in sections)
            {
                IniUtility.SetValue(file, s, "Name", hostName);
            }
        }

        private void PatchAuthIniFile()
        {
            string file = Path.Combine(Setting.IniDirectoryName, "Auth.ini");
            string fileBackup = Path.Combine(Setting.IniDirectoryName, "Auth.ini.orig");
            bool patchRequired = false;

            EnsureBackup(file, fileBackup);
            if (File.Exists(file) == false && File.Exists(fileBackup))
            {
                File.Copy(fileBackup, file);
            }

            string RegHostName = LocalHostname;

            UriBuilder builder = new UriBuilder();
            builder.Scheme = Setting.UseSecureAuthentication ? "https" : "http";
            builder.Host = RegHostName;
            builder.Path = "misc/touchsession.jsp";
            builder.Query = "lkey=%s";

            string url = builder.ToString();

            if (String.Equals(IniUtility.GetValue(file, "General", "AAIUrl"), RegHostName, StringComparison.InvariantCultureIgnoreCase) == false)
            {
                patchRequired = true;
            }
            else if (String.Equals(IniUtility.GetValue(file, "General", "LKeyUrl"), url, StringComparison.InvariantCultureIgnoreCase) == false)
            {
                patchRequired = true;
            }

            if (patchRequired == false) return;

            IniUtility.SetValue(file, "General", "AAIUrl", RegHostName);
            IniUtility.SetValue(file, "General", "LKeyUrl", url);
        }

        private void PatchRegDataFile()
        {
            string file = Path.Combine(Setting.IniDirectoryName, "rg_regdata.ini");
            string fileBackup = Path.Combine(Setting.IniDirectoryName, "rg_regdata.ini.orig");
            bool patchRequired = false;

            string RegHostName = LocalHostname;

            EnsureBackup(file, fileBackup);
            if (File.Exists(file) == false && File.Exists(fileBackup))
            {
                File.Copy(fileBackup, file);
            }

            UriBuilder builder = new UriBuilder();
            builder.Scheme = Setting.UseSecureAuthentication ? "https" : "http";
            builder.Host = RegHostName;
            builder.Path = "subsxml";

            string registrationUrl = builder.ToString();

            if (String.Equals(IniUtility.GetValue(file, "Connection", "regserverurl"), registrationUrl, StringComparison.InvariantCultureIgnoreCase) == false)
            {
                patchRequired = true;
            }

            if (patchRequired == false) return;

            IniUtility.SetValue(file, "Connection", "regserverurl", registrationUrl);
        }

        private void PatchRegDataFileNames()
        {
            string file1 = Path.Combine(Setting.IniDirectoryName, "rg_regdata_org");
            string file2 = Path.Combine(Setting.IniDirectoryName, "rg_regdata.ini");

            if (File.Exists(file1) && File.Exists(file2) == false)
            {
                try
                {
                    File.Move(file1, file2);
                }
                catch (Exception e)
                {
                    throw new ApplicationException("Could not repair rg_regdata.ini filename.", e);
                }
            }
        }

        private void PatchAuthLoginFile()
        {
            try
            {
                EnsureBackup(Setting.AuthLoginFileName, Setting.AuthLoginFileName + ".orig");
                AuthPatcherInfo info = AuthLoginPatcher.ReadInformation(Setting.AuthLoginFileName);
                if (info.Port != Setting.AuthenticationPort || info.UseHttps != Setting.UseSecureAuthentication)
                {
                    info.Port = (ushort)Setting.AuthenticationPort;
                    info.UseHttps = Setting.UseSecureAuthentication;
                    AuthLoginPatcher.WriteInformation(Setting.AuthLoginFileName, info);
                }
            }
            catch (Exception e)
            {
                throw new ApplicationException("Could not patch AuthLogin.dll.", e);
            }
        }

        private void PatchRegistry()
        {
            string keyName = "HKEY_LOCAL_MACHINE\\Software\\Westwood Studios\\Earth and Beyond\\Registration";
            object value = Registry.GetValue(keyName, "Registered", 0);
            if (value == null || ((int)value) != 1)
            {
                try
                {
                    Registry.SetValue(keyName, "Registered", 1, RegistryValueKind.DWord);
                }
                catch (Exception e)
                {
                    throw new ApplicationException("Could not patch registry-settings.", e);
                }
            }
        }

        /// <summary>
        /// 64-bit hosts running the 32-bit client/launcher under WoW64 (or
        /// WINE's 64-bit prefix with the 32-bit client) write registry keys
        /// under Wow6432Node. Mirror the same Registered=1 value there so
        /// either bitness of the client sees a registered install.
        /// </summary>
        private void PatchRegistry64()
        {
            string keyName = "HKEY_LOCAL_MACHINE\\Software\\Wow6432Node\\Westwood Studios\\Earth and Beyond\\Registration";
            try
            {
                object value = Registry.GetValue(keyName, "Registered", null);
                if (value == null || !(value is int) || ((int)value) != 1)
                {
                    Registry.SetValue(keyName, "Registered", 1, RegistryValueKind.DWord);
                }
            }
            catch (Exception e)
            {
                // On a 32-bit host (or a WINE prefix without Wow6432Node) the
                // write may be redirected or fail. That's fine — the 32-bit
                // path in PatchRegistry() already covered it.
                Program.LogException(e);
            }
        }

        private void EnsureLocalCertIfRequested()
        {
            if (!Setting.UseLocalCert) return;

            string binDir = Path.Combine(Directory.GetCurrentDirectory(), "bin");
            string certPath = Path.Combine(binDir, "local.crt");
            string keyPath = Path.Combine(binDir, "local.key");

            try
            {
                CertificationUtility.EnsureLocalCert(LocalHostname, certPath, keyPath);
            }
            catch (Exception e)
            {
                throw new ApplicationException(
                    "Could not provision local TLS cert via mkcert. " +
                    "Install mkcert from https://github.com/FiloSottile/mkcert " +
                    "and place mkcert.exe in the launcher's bin/ directory.\n" +
                    "Details: " + e.Message,
                    e);
            }
        }

        /// <summary>
        /// Copy <paramref name="src"/> to <paramref name="backup"/> the FIRST
        /// time we ever see <paramref name="src"/> without a backup. The
        /// backup is never overwritten or modified after that. Callers wanting
        /// to re-patch from a clean baseline must copy <paramref name="backup"/>
        /// back over <paramref name="src"/> themselves.
        /// </summary>
        private static void EnsureBackup(string src, string backup)
        {
            if (!File.Exists(src)) return;
            if (File.Exists(backup)) return;
            File.Copy(src, backup);
        }
    }
}
