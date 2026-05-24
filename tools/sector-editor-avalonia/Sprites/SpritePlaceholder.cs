// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Drawing;
using SectorEditorAvalonia.PiccoloShim;

namespace SectorEditorAvalonia.Sprites
{
    // Shared construction for the static placeholder sprites the original
    // sector-editor uses in its "preview" hook (Mob / Planet / Stargate /
    // Starbase / Decoration / Harvestable). All five clones differed
    // only in image name + signature-ring and radar-ring pen colours, so
    // the port factors the common code into one helper.
    internal static class SpritePlaceholder
    {
        public static void Build(PLayer layer, string name, float x, float y,
                                 float sigRadius, float rrRadius,
                                 string imageName, Color sigColor, Color rrColor)
        {
            float sigDia = (sigRadius * 2) / 100;
            float rrDia = (rrRadius * 2) / 100;

            string path = Mob.ResolveImage(imageName);
            var stationImage = new PImage(path);
            stationImage.X = (x - (stationImage.Width / 2)) / 100;
            stationImage.Y = (y - (stationImage.Height / 2)) / 100;

            float sigX = (x / 100) - ((sigDia / 2) - (stationImage.Width / 2));
            float sigY = (y / 100) - ((sigDia / 2) - (stationImage.Height / 2));
            float rrX = (x / 100) - ((rrDia / 2) - (stationImage.Width / 2));
            float rrY = (y / 100) - ((rrDia / 2) - (stationImage.Height / 2));

            var sigPen = new Pen(sigColor, 2.0F) { DashStyle = DashStyle.DashDotDot };
            var rrPen = new Pen(rrColor, 1.0F);

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
    }
}
