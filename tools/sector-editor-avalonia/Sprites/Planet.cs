// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// Ported from N7.Sprites.Planet under Net-7 Entertainment's CC BY-NC-SA
// 3.0; preservation modifications inherit under ShareAlike.

using System.Drawing;
using SectorEditorAvalonia.PiccoloShim;

namespace SectorEditorAvalonia.Sprites
{
    // Static placeholder used by the original sector-editor's "preview"
    // codepath before PlanetSprite takes over with DB-bound data.
    public class Planet
    {
        public Planet(PLayer layer, string name, float x, float y, float sigRadius, float rrRadius)
        {
            SpritePlaceholder.Build(layer, name, x, y, sigRadius, rrRadius,
                "planet.gif", Color.Blue, Color.SkyBlue);
        }
    }
}
