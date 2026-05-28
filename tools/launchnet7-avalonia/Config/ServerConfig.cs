using System.Collections.Generic;

namespace LaunchNet7Avalonia.Config
{
    public sealed class ServerConfig
    {
        public string Name { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string LaunchName { get; set; } = "";
        public bool IsSinglePlayer { get; set; }
        public bool EnableAdvancedSettings { get; set; }
        public List<HostConfig> Hosts { get; } = new List<HostConfig>();

        public string GetDisplayName() => string.IsNullOrEmpty(DisplayName) ? Name : DisplayName;
        public string GetLaunchName()  => string.IsNullOrEmpty(LaunchName)  ? Name : LaunchName;

        public override string ToString() => GetDisplayName();
    }

    public sealed class HostConfig
    {
        public string Hostname { get; set; } = "";
        public int AuthenticationPort { get; set; } = 443;

        public override string ToString() => Hostname;
    }
}
