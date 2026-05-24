using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;

namespace LaunchNet7Avalonia.Config
{
    // Replaces the original .NET-Framework System.Configuration
    // ConfigurationSection layer. LaunchNet7.cfg is a normal XML doc
    // (we drop the <configuration><configSections> wrapper convention)
    // wrapped in a <launchNet7> root that exposes <servers> and
    // <autoUpdate>. We parse it with XmlDocument so we don't need the
    // System.Configuration.ConfigurationManager NuGet package and so the
    // tool runs on any platform.
    public sealed class LauncherConfig
    {
        public string DefaultWebsite { get; set; } = "";
        public List<ServerConfig> Servers { get; } = new List<ServerConfig>();
        public List<AutoUpdateTask> AutoUpdateTasks { get; } = new List<AutoUpdateTask>();

        public static LauncherConfig Load(string path)
        {
            var cfg = new LauncherConfig();
            if (!File.Exists(path))
                return cfg;

            var doc = new XmlDocument();
            doc.Load(path);

            // Accept either <configuration><launchNet7>...</launchNet7></configuration>
            // (the original .NET Framework shape) or a bare <launchNet7> root.
            XmlNode root = doc.SelectSingleNode("//launchNet7");
            if (root == null) return cfg;

            cfg.DefaultWebsite = root.Attributes?["defaultWebsite"]?.Value ?? "";

            var autoUpdate = root.SelectSingleNode("autoUpdate");
            if (autoUpdate != null)
            {
                foreach (XmlNode task in autoUpdate.ChildNodes)
                {
                    if (task.NodeType != XmlNodeType.Element) continue;
                    if (task.Name != "autoUpdateTask") continue;
                    cfg.AutoUpdateTasks.Add(new AutoUpdateTask
                    {
                        Name            = task.Attributes?["name"]?.Value ?? "",
                        BaseUrl         = task.Attributes?["baseUrl"]?.Value ?? "",
                        FileListName    = task.Attributes?["fileListName"]?.Value ?? "Files.txt",
                        VersionFileName = task.Attributes?["versionFileName"]?.Value ?? "Version.txt",
                    });
                }
            }

            var servers = root.SelectSingleNode("servers");
            if (servers != null)
            {
                foreach (XmlNode srv in servers.ChildNodes)
                {
                    if (srv.NodeType != XmlNodeType.Element) continue;
                    if (srv.Name != "server") continue;

                    var s = new ServerConfig
                    {
                        Name        = srv.Attributes?["name"]?.Value ?? "",
                        DisplayName = srv.Attributes?["displayName"]?.Value ?? "",
                        LaunchName  = srv.Attributes?["launchName"]?.Value ?? "",
                    };
                    s.IsSinglePlayer        = ParseBool(srv.Attributes?["isSinglePlayer"]?.Value);
                    s.EnableAdvancedSettings = ParseBool(srv.Attributes?["enableAdvancedSettings"]?.Value);

                    foreach (XmlNode h in srv.ChildNodes)
                    {
                        if (h.NodeType != XmlNodeType.Element) continue;
                        if (h.Name != "host") continue;

                        var host = new HostConfig
                        {
                            Hostname = h.Attributes?["hostname"]?.Value ?? "",
                        };
                        host.SupportsSecureAuthentication = ParseBool(h.Attributes?["supportSecureAuthentication"]?.Value, true);
                        host.SecureAuthenticationPort     = ParseInt(h.Attributes?["secureAuthenticationPort"]?.Value, 443);
                        host.AuthenticationPort           = ParseInt(h.Attributes?["authenticationPort"]?.Value, 80);
                        s.Hosts.Add(host);
                    }
                    cfg.Servers.Add(s);
                }
            }

            return cfg;
        }

        static bool ParseBool(string s, bool def = false)
            => string.IsNullOrEmpty(s) ? def : bool.Parse(s);
        static int ParseInt(string s, int def)
            => int.TryParse(s, out var v) ? v : def;
    }

    public sealed class AutoUpdateTask
    {
        public string Name { get; set; }
        public string BaseUrl { get; set; }
        public string FileListName { get; set; }
        public string VersionFileName { get; set; }
    }
}
