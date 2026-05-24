using System;
using Avalonia.Controls;

namespace SectorEditorAvalonia.Windows
{
    // Avalonia port of tools/sector-editor/GUI/mainFrm.cs. The original is
    // a 1024x768 MDI-style WinForms editor: a TreeView on the left (System
    // -> Sector hierarchy), a tabbed scene-graph canvas in the middle
    // (Sector / System / Universe views drawn through Piccolo2D's
    // PCanvas+PLayer+PNode hierarchy), a PropertyGrid on the right, and
    // a DataGridView for tabular object editing.
    //
    // **Tier 12a (this commit) ships only the window shell** so the
    // SectorEditorAvalonia project builds clean and `--smoke` is green.
    // The data layer (Sql/), the dialogs (NewSystem/NewSector/etc.), the
    // Piccolo2D-on-Avalonia shim, the sprite classes, and the actual
    // tree/canvas wiring all land in Tier 12b-12e. See plans/12-phase-l-avalonia.md
    // for the per-tier breakdown.
    //
    // The Piccolo2D dependency (`UMD.HCIL.Piccolo.dll`) has no Avalonia
    // port — it's a defunct .NET 2.0-era scene-graph library that
    // builds on System.Drawing.Graphics. The plan is to implement a
    // minimal shim layer in PiccoloShim/ that maps the ~12 Piccolo types
    // the editor uses (PCanvas, PLayer, PNode, PPath, PImage, PText,
    // PInputEventHandler, etc.) onto Avalonia primitives (Canvas,
    // Control with custom Render(), ScaleTransform for zoom). Sprite code
    // can then port largely 1:1 to keep the diff readable.
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            c_Status.Text = "Tier 12a scaffold — tree/canvas/properties wiring pending Tier 12b+";

            c_Exit.Click += (_, _) => Close();
            c_About.Click += (_, _) => c_Status.Text = "Net7 Sector Editor (Avalonia port) — see tools/sector-editor for the WinForms original";
        }
    }
}
