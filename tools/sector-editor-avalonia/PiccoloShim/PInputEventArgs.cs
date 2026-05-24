// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Drawing;

namespace SectorEditorAvalonia.PiccoloShim
{
    public sealed class PInputEventArgs
    {
        public PointF Position;
        public PointF CanvasPosition;
        public PNode PickedNode;
        public bool Handled;
        public MouseButton Button;
        public int ClickCount;
        public float WheelDelta;
    }

    public enum MouseButton
    {
        None = 0,
        Left = 1,
        Right = 2,
        Middle = 3,
    }

    public delegate void PInputEventHandler(object sender, PInputEventArgs e);
}
