// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

namespace SectorEditorAvalonia.Utilities
{
    /// <summary>
    /// Abstracts WinForms PropertyGrid for ported sprite code. The
    /// original sprites do `_pg.SelectedObject = props;` to push a data
    /// object into a reflection-driven property editor. The Avalonia
    /// port replaces that with an `IPropertyHost` consumer (a panel that
    /// reflects on the object's public properties and renders an editor
    /// per type — int/float/bool/string/Color).
    ///
    /// Stub for Tier 12e Wave 1: implementations live alongside the
    /// MainWindow's property panel. Sprite code consumes only this
    /// interface so the panel implementation can evolve without touching
    /// every sprite.
    /// </summary>
    public interface IPropertyHost
    {
        object SelectedObject { get; set; }
    }

    /// <summary>
    /// Null/console fallback used by smoke tests and headless harnesses.
    /// </summary>
    public sealed class NullPropertyHost : IPropertyHost
    {
        public object SelectedObject { get; set; }
    }
}
