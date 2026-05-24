// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// Ported from N7.Sprites.Decoration under Net-7 Entertainment's
// CC BY-NC-SA 3.0; preservation modifications inherit under ShareAlike.

using System.Drawing;
using SectorEditorAvalonia.PiccoloShim;

namespace SectorEditorAvalonia.Sprites
{
    public class Decoration
    {
        public Decoration(PLayer layer, string name, float x, float y, float sigRadius, float rrRadius)
        {
            SpritePlaceholder.Build(layer, name, x, y, sigRadius, rrRadius,
                "standardNav.gif", Color.DarkSlateGray, Color.Gray);
        }
    }
}
