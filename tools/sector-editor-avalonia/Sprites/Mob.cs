// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// Ported from N7.Sprites.Mob under Net-7 Entertainment's CC BY-NC-SA
// 3.0; preservation modifications inherit under ShareAlike.

using System.Drawing;
using System.IO;
using SectorEditorAvalonia.PiccoloShim;

namespace SectorEditorAvalonia.Sprites
{
    // Static placeholder used by the original sector-editor's "preview"
    // codepath before a real MobSprite takes over with DB-bound data.
    public class Mob
    {
        public Mob(PLayer layer, string name, float x, float y, float sigRadius, float rrRadius)
        {
            float sigDia = (sigRadius * 2) / 100;
            float rrDia = (rrRadius * 2) / 100;

            string path = ResolveImage("hostileMob.gif");
            var stationImage = new PImage(path);
            stationImage.X = (x - (stationImage.Width / 2)) / 100;
            stationImage.Y = (y - (stationImage.Height / 2)) / 100;

            float sigX = (x / 100) - ((sigDia / 2) - (stationImage.Width / 2));
            float sigY = (y / 100) - ((sigDia / 2) - (stationImage.Height / 2));
            float rrX = (x / 100) - ((rrDia / 2) - (stationImage.Width / 2));
            float rrY = (y / 100) - ((rrDia / 2) - (stationImage.Height / 2));

            var sigPen = new Pen(Color.Red, 2.0F) { DashStyle = DashStyle.DashDotDot };
            var rrPen = new Pen(Color.MistyRose, 1.0F);

            var sigCircle = PPath.CreateEllipse(sigX, sigY, sigDia, sigDia);
            sigCircle.Pen = sigPen;
            sigCircle.Brush = Brushes.Transparent;

            var rrCircle = PPath.CreateEllipse(rrX, rrY, rrDia, rrDia);
            rrCircle.Pen = rrPen;
            rrCircle.Brush = Brushes.Transparent;

            var pname = new PText(name);
            pname.TextBrush = Brushes.White;
            pname.TextAlignment = StringAlignment.Center;
            pname.X = (x / 100) - (pname.Width / 2);
            pname.Y = (y / 100) - 20;

            stationImage.AddChild(sigCircle);
            stationImage.AddChild(rrCircle);
            stationImage.AddChild(pname);

            layer.AddChild(stationImage);
        }

        // The original searched two relative paths (`Images/foo.gif` and
        // `../../Images/foo.gif`) because the WinForms tool ran either
        // from `bin/Debug/` or in-place. The Avalonia build copies the
        // Images/ tree next to the assembly, so the in-place path is the
        // common case. The fallback exists to keep working in odd
        // working-directory setups (e.g. `dotnet test` running from the
        // solution root).
        internal static string ResolveImage(string filename)
        {
            string p = Path.Combine("Images", filename);
            if (File.Exists(p)) return p;
            p = Path.Combine("..", "..", "Images", filename);
            if (File.Exists(p)) return p;
            return Path.Combine("Images", filename);
        }
    }
}
