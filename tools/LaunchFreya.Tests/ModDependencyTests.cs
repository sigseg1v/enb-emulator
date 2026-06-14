using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace LaunchFreya.Tests
{
    // ModCatalog.UnmetDependencies drives the red "requires ..." row in the
    // Configure Mods window: an ENABLED mod whose declared deps are not all
    // present-and-enabled is flagged. See MOD-STRUCTURE.md "dependencies".
    public class ModDependencyTests
    {
        static ModInfo Mod(string id, params string[] deps) => new ModInfo
        {
            Id = id, Name = id, Dependencies = deps,
        };

        static readonly List<ModInfo> Catalog = new()
        {
            new ModInfo { Id = "freya-hud", Name = "Freya HUD", Dependencies = new[] { "hide-ui" } },
            new ModInfo { Id = "hide-ui", Name = "Hide Native UI" },
            new ModInfo { Id = "autocalibrate", Name = "Autocalibrate" },
        };

        [Fact]
        public void NoDeps_NeverUnmet()
        {
            var states = new Dictionary<string, bool>();
            Assert.Empty(ModCatalog.UnmetDependencies(states, Catalog, Mod("autocalibrate")));
        }

        [Fact]
        public void DepPresentAndEnabled_Satisfied()
        {
            // Both default-on (absent from map == enabled).
            var states = new Dictionary<string, bool>();
            var hud = Catalog.First(m => m.Id == "freya-hud");
            Assert.Empty(ModCatalog.UnmetDependencies(states, Catalog, hud));
        }

        [Fact]
        public void DepDisabled_ReportsDisabled()
        {
            var states = new Dictionary<string, bool> { ["hide-ui"] = false };
            var hud = Catalog.First(m => m.Id == "freya-hud");
            var unmet = ModCatalog.UnmetDependencies(states, Catalog, hud);
            Assert.Equal(new[] { "hide-ui (disabled)" }, unmet);
        }

        [Fact]
        public void DepMissingFromCatalog_ReportsMissing()
        {
            var states = new Dictionary<string, bool>();
            var unmet = ModCatalog.UnmetDependencies(states, Catalog, Mod("ghost", "nope"));
            Assert.Equal(new[] { "nope (missing)" }, unmet);
        }

        [Fact]
        public void DisabledMod_RaisesNoError()
        {
            // freya-hud itself off + its dep off: not active, so no error.
            var states = new Dictionary<string, bool> { ["freya-hud"] = false, ["hide-ui"] = false };
            var hud = Catalog.First(m => m.Id == "freya-hud");
            Assert.Empty(ModCatalog.UnmetDependencies(states, Catalog, hud));
        }
    }
}
