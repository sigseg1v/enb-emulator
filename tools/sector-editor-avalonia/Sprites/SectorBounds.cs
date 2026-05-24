// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// Ported from N7.Sprites.SectorBounds (kyp/Net7Tools/SectorEditor) under
// Net-7 Entertainment's CC BY-NC-SA 3.0; preservation modifications
// inherit under ShareAlike.

using System;
using System.Drawing;
using SectorEditorAvalonia.PiccoloShim;

namespace SectorEditorAvalonia.Sprites
{
    // Used by UniverseWindow to draw a sector's bounding box at galaxy scale.
    public class SectorBounds
    {
        public SectorBounds(PLayer layer, float x_min, float y_min, float x_max, float y_max)
        {
            float width = (x_max - x_min) / 100;
            float height = (y_max - y_min) / 100;
            float x = x_min / 100;
            float y = (y_min / 100) - (height / 2);

            var boundsPen = new Pen(Color.Red, 10.0F) { DashStyle = DashStyle.DashDotDot };

            var boundsRectangle = PPath.CreateRectangle(x, y, width, height);
            boundsRectangle.Brush = Brushes.Transparent;
            boundsRectangle.Pen = boundsPen;

            layer.AddChild(boundsRectangle);
        }
    }
}
