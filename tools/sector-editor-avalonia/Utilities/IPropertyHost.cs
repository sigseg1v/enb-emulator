// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

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

    /// <summary>
    /// Abstracts the "push a name/asset_id change back into the editor's
    /// grid view" the original sprites do via direct DataGridView access
    /// (`_dgv.Rows[row.Index].Cells["name"].Value = ...`). Sprite code
    /// consumes only this sink; the SectorWindow's DataGrid implementation
    /// land in Wave 2+, the smoke harness uses a no-op sink.
    /// </summary>
    public interface IGridSyncSink
    {
        void OnCellChanged(string columnName, object newValue);

        // SectorWindow drives the grid for add/delete/select operations
        // that originated on the canvas. The MainWindow's implementation
        // routes these to the actual Avalonia DataGrid; the smoke harness
        // ignores them.
        void RemoveRowById(int id);
        void AppendRow(int sectorObjectId, string name, int baseAssetId, int type);
        void SelectRowById(int id);
    }

    public sealed class NullGridSyncSink : IGridSyncSink
    {
        public void OnCellChanged(string columnName, object newValue) { }
        public void RemoveRowById(int id) { }
        public void AppendRow(int sectorObjectId, string name, int baseAssetId, int type) { }
        public void SelectRowById(int id) { }
    }

    /// <summary>
    /// Replaces the original sector-editor's <c>MessageBox.Show</c> calls
    /// inside SectorWindow (currently only the "no object selected" error
    /// on delete). The MainWindow installs the real Avalonia message-box
    /// implementation; the smoke harness routes to Console.
    /// </summary>
    public interface INotificationSink
    {
        void ShowError(string message);
    }

    public sealed class NullNotificationSink : INotificationSink
    {
        public void ShowError(string message)
        {
            System.Console.Error.WriteLine("[notify] " + message);
        }
    }

    /// <summary>
    /// Position-picking dialog handed to SectorWindow.newSectorObject()
    /// in the original tool. The first canvas click after assignment
    /// fills in the position and reveals the dialog. The MainWindow
    /// supplies the real Avalonia window; the smoke harness leaves it
    /// null (and SectorWindow's canvasCamera_MouseDown short-circuits).
    /// </summary>
    public interface INewSectorObjectDialog
    {
        void setPosition(System.Drawing.PointF position);
        void Show();
    }

    /// <summary>
    /// Faction-id↔name lookup the original sprite code reaches through
    /// <c>mainFrm.factions</c>. Decoupled here so a sprite under test
    /// can install a stub. The MainWindow installs the real DAO-backed
    /// implementation at startup; the smoke harness leaves the null one.
    /// </summary>
    public interface IFactionLookup
    {
        string FindNameById(int id);
        int FindIdByName(string name);
    }

    public sealed class NullFactionLookup : IFactionLookup
    {
        public string FindNameById(int id) => "";
        public int FindIdByName(string name) => -1;
    }

    /// <summary>
    /// Replaces the original sector-editor's <c>mainFrm.selectedObjectID</c>
    /// static + the <c>mainFrm.factions</c> reach-through — used by 5+
    /// sprite/dialog call sites to share state across windows.
    /// Refactoring into a proper service is a Wave 3+ task that lands
    /// when the dialogs land (MobGroup, HarvestableResTypes). Kept as
    /// static fields for now to preserve cross-window behaviour with
    /// minimum diff.
    /// </summary>
    public static class EditorGlobals
    {
        public static int SelectedObjectId;
        // SectorWindow installs this on activation so dialogs (Destination,
        // MobGroup, HarvestableResTypes) can query within the current
        // sector context. Replaces `mainFrm.sectorID`.
        public static int SectorID;
        public static IFactionLookup Factions = new NullFactionLookup();
    }
}
