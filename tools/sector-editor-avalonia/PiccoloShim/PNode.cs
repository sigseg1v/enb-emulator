// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

using System.Collections.Generic;
using System.Drawing;
using Avalonia.Media;

namespace SectorEditorAvalonia.PiccoloShim
{
    /// <summary>
    /// Scene-graph node — a Piccolo2D PNode lookalike sized to what
    /// the sector editor's sprite/window code actually consumes.
    /// </summary>
    public class PNode
    {
        public float X;
        public float Y;
        public float Width;
        public float Height;
        public Brush Brush;
        public Pen Pen;
        public object Tag;
        public bool ChildrenPickable = true;
        public bool Visible = true;

        private readonly List<PNode> _children = new List<PNode>();
        public PNode Parent { get; internal set; }

        public IReadOnlyList<PNode> Children => _children;
        public int ChildCount => _children.Count;
        public PNode GetChild(int index) => _children[index];

        public virtual void AddChild(PNode child)
        {
            if (child == null) return;
            child.Parent?._children.Remove(child);
            child.Parent = this;
            _children.Add(child);
        }

        public virtual bool RemoveChild(PNode child)
        {
            if (child == null) return false;
            bool ok = _children.Remove(child);
            if (ok) child.Parent = null;
            return ok;
        }

        public virtual void RemoveAllChildren()
        {
            foreach (var c in _children) c.Parent = null;
            _children.Clear();
        }

        public void TranslateBy(float dx, float dy)
        {
            X += dx;
            Y += dy;
        }

        public event PInputEventHandler MouseDown;
        public event PInputEventHandler MouseUp;
        public event PInputEventHandler MouseDrag;
        public event PInputEventHandler MouseMove;

        internal void RaiseMouseDown(PInputEventArgs e) => MouseDown?.Invoke(this, e);
        internal void RaiseMouseUp(PInputEventArgs e) => MouseUp?.Invoke(this, e);
        internal void RaiseMouseDrag(PInputEventArgs e) => MouseDrag?.Invoke(this, e);
        internal void RaiseMouseMove(PInputEventArgs e) => MouseMove?.Invoke(this, e);

        public virtual bool HitTest(PointF localPoint)
        {
            return localPoint.X >= X && localPoint.X <= X + Width
                && localPoint.Y >= Y && localPoint.Y <= Y + Height;
        }

        public virtual void Render(DrawingContext ctx) { }

        internal void RenderTree(DrawingContext ctx)
        {
            if (!Visible) return;
            Render(ctx);
            foreach (var c in _children) c.RenderTree(ctx);
        }

        internal PNode PickTopDown(PointF localPoint)
        {
            if (!Visible) return null;
            for (int i = _children.Count - 1; i >= 0; i--)
            {
                if (!ChildrenPickable) break;
                var hit = _children[i].PickTopDown(localPoint);
                if (hit != null) return hit;
            }
            return HitTest(localPoint) ? this : null;
        }

        internal static IBrush ToAvalonia(Brush sysBrush)
        {
            if (sysBrush == null) return null;
            var c = sysBrush.Color;
            return new SolidColorBrush(Avalonia.Media.Color.FromArgb(c.A, c.R, c.G, c.B));
        }

        internal static IPen ToAvalonia(Pen sysPen)
        {
            if (sysPen == null) return null;
            var c = sysPen.Color;
            var brush = new SolidColorBrush(Avalonia.Media.Color.FromArgb(c.A, c.R, c.G, c.B));
            var pen = new Avalonia.Media.Pen(brush, sysPen.Width);
            switch (sysPen.DashStyle)
            {
                case DashStyle.Dash:
                    pen.DashStyle = Avalonia.Media.DashStyle.Dash;
                    break;
                case DashStyle.Dot:
                    pen.DashStyle = Avalonia.Media.DashStyle.Dot;
                    break;
                case DashStyle.DashDot:
                    pen.DashStyle = Avalonia.Media.DashStyle.DashDot;
                    break;
                case DashStyle.DashDotDot:
                    pen.DashStyle = Avalonia.Media.DashStyle.DashDotDot;
                    break;
            }
            return pen;
        }
    }

    /// <summary>
    /// Z-ordered container — Piccolo2D's PLayer. No additional behaviour
    /// beyond PNode for what the sector editor needs (Piccolo's layer
    /// semantics around cameras and edit-mode coordination aren't used).
    /// </summary>
    public class PLayer : PNode { }
}
