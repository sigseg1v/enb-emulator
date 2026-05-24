// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// Ported from N7.Sprites.SectorBoundsSprite under Net-7 Entertainment's
// CC BY-NC-SA 3.0; preservation modifications inherit under ShareAlike.

using System.Drawing;
using SectorEditorAvalonia.PiccoloShim;

namespace SectorEditorAvalonia.Sprites
{
    // Renders a single sector's coordinate frame (XY axes + bounds rect)
    // inside SectorWindow at sector-local scale.
    public class SectorBoundsSprite
    {
        public SectorBoundsSprite(PLayer layer, float x_min, float y_min, float x_max, float y_max)
        {
            float width = (x_max - x_min) / 100;
            float height = (y_max - y_min) / 100;
            float x = -(width / 2);
            float y = -(height / 2);

            var boundsPen = new Pen(Color.Red, 10.0F) { DashStyle = DashStyle.DashDotDot };
            var xEdgePen  = new Pen(Color.White, 2.5F) { DashStyle = DashStyle.Solid };

            var boundsRectangle = PPath.CreateRectangle(x, y, width, height);
            boundsRectangle.Brush = Brushes.Transparent;
            boundsRectangle.Pen = boundsPen;

            var xEdge = new PPath();
            xEdge.AddLine(-50, 0, 50, 0);
            xEdge.Pen = xEdgePen;

            var yEdge = new PPath();
            yEdge.AddLine(0, -50, 0, 50);
            yEdge.Pen = xEdgePen;

            var xy = new PText("0,0")
            {
                TextBrush = Brushes.White,
                TextAlignment = StringAlignment.Center,
                X = 5, Y = 5,
            };
            var posX = new PText("+X")
            {
                TextBrush = Brushes.White,
                TextAlignment = StringAlignment.Center,
                X = 52, Y = -9,
            };
            var posY = new PText("+Y")
            {
                TextBrush = Brushes.White,
                TextAlignment = StringAlignment.Center,
                X = -12, Y = 52,
            };

            boundsRectangle.AddChild(xy);
            boundsRectangle.AddChild(posX);
            boundsRectangle.AddChild(posY);
            boundsRectangle.AddChild(xEdge);
            boundsRectangle.AddChild(yEdge);
            boundsRectangle.Pickable = false;

            layer.AddChild(boundsRectangle);
        }
    }
}
