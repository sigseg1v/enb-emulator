// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// Ported from N7.Sprites.Sector under Net-7 Entertainment's CC BY-NC-SA
// 3.0; preservation modifications inherit under ShareAlike.

using System;
using System.Drawing;
using SectorEditorAvalonia.PiccoloShim;

namespace SectorEditorAvalonia.Sprites
{
    // Sample/placeholder sector drawn at fixed (100, 500) — used by the
    // original sector-editor's "New Sector" preview hook before SectorSprite
    // takes over with real DB-bound data.
    public class Sector
    {
        public Sector(PLayer layer, string name)
        {
            var rnd = new Random((int) DateTime.Now.Ticks);
            var penColor = Color.FromArgb(rnd.Next(0, 255), rnd.Next(0, 255), rnd.Next(0, 255));

            var sigPen = new Pen(penColor, 2.0F) { DashStyle = DashStyle.DashDotDot };

            var sigCircle = PPath.CreateEllipse(100, 500, 100, 100);
            sigCircle.Pen = sigPen;
            sigCircle.Brush = Brushes.Transparent;

            var pname = new PText(name)
            {
                TextBrush = Brushes.White,
                TextAlignment = StringAlignment.Center,
                X = sigCircle.X,
                Y = sigCircle.Y,
            };

            layer.AddChild(pname);
            layer.AddChild(sigCircle);
        }
    }
}
