// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// Ported from N7.Sprites.Harvestable under Net-7 Entertainment's
// CC BY-NC-SA 3.0; preservation modifications inherit under ShareAlike.

using System.Drawing;
using SectorEditorAvalonia.PiccoloShim;

namespace SectorEditorAvalonia.Sprites
{
    public class Harvestable
    {
        public Harvestable(PLayer layer, string name, float x, float y, float sigRadius, float rrRadius)
        {
            SpritePlaceholder.Build(layer, name, x, y, sigRadius, rrRadius,
                "resource.png", Color.Violet, Color.Pink);
        }
    }
}
