// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

using System.Drawing;

namespace SectorEditorAvalonia.PiccoloShim
{
    /// <summary>
    /// Built-in node-drag handler — translates whatever the canvas
    /// captured on PointerPressed by the canvas-space pointer delta.
    /// Attach to a node's MouseDrag event.
    /// </summary>
    public sealed class PDragEventHandler
    {
        private PointF _lastWorld;
        private bool _havePrev;

        public void Attach(PNode node)
        {
            node.MouseDown += (_, e) =>
            {
                _lastWorld = e.Position;
                _havePrev = true;
            };
            node.MouseDrag += (sender, e) =>
            {
                if (!_havePrev) { _lastWorld = e.Position; _havePrev = true; return; }
                float dx = e.Position.X - _lastWorld.X;
                float dy = e.Position.Y - _lastWorld.Y;
                ((PNode) sender).TranslateBy(dx, dy);
                _lastWorld = e.Position;
            };
            node.MouseUp += (_, _) => _havePrev = false;
        }
    }
}
