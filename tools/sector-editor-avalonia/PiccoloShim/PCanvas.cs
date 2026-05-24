// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Drawing;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace SectorEditorAvalonia.PiccoloShim
{
    /// <summary>
    /// Avalonia-backed canvas hosting a single root PLayer + a PCamera.
    /// Piccolo2D's PCanvas exposed `Layer` (root layer) and `Camera`
    /// directly; we keep that shape.
    /// </summary>
    public class PCanvas : Control
    {
        public PLayer Layer { get; } = new PLayer();
        public PCamera Camera { get; } = new PCamera();

        public PNode LastPickedNode { get; private set; }

        // System.Drawing.Color BackColor mirror, for callsite compat with
        // the original Piccolo PCanvas (`canvas.BackColor = Color.Black;`).
        public System.Drawing.Color BackColor { get; set; } = System.Drawing.Color.FromArgb(255, 20, 20, 30);

        public PCanvas()
        {
            Focusable = true;
            ClipToBounds = true;
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);
            context.FillRectangle(
                new SolidColorBrush(Avalonia.Media.Color.FromArgb(BackColor.A, BackColor.R, BackColor.G, BackColor.B)),
                new Rect(Bounds.Size));

            using (context.PushTransform(Matrix.CreateTranslation(Camera.TranslateX, Camera.TranslateY)
                                       * Matrix.CreateScale(Camera.Scale, Camera.Scale)))
            {
                Layer.RenderTree(context);
            }
        }

        private PointF _lastPointer;
        private bool _dragging;
        private MouseButton _activeButton;
        private PNode _capturedNode;

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var pt = e.GetPosition(this);
            _lastPointer = new PointF((float) pt.X, (float) pt.Y);
            _activeButton = MapButton(e.GetCurrentPoint(this).Properties);
            _dragging = true;
            Focus();

            var world = Camera.CanvasToWorld(_lastPointer);
            var picked = Layer.PickTopDown(world);
            LastPickedNode = picked;
            _capturedNode = picked;
            if (picked != null)
            {
                picked.RaiseMouseDown(MakeArgs(world, picked));
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var pt = e.GetPosition(this);
            var canvas = new PointF((float) pt.X, (float) pt.Y);
            var world = Camera.CanvasToWorld(canvas);

            if (_dragging && _capturedNode != null)
            {
                _capturedNode.RaiseMouseDrag(MakeArgs(world, _capturedNode));
            }
            else
            {
                var picked = Layer.PickTopDown(world);
                picked?.RaiseMouseMove(MakeArgs(world, picked));
            }
            _lastPointer = canvas;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            var pt = e.GetPosition(this);
            var world = Camera.CanvasToWorld(new PointF((float) pt.X, (float) pt.Y));
            if (_capturedNode != null)
            {
                _capturedNode.RaiseMouseUp(MakeArgs(world, _capturedNode));
                _capturedNode = null;
            }
            _dragging = false;
            _activeButton = MouseButton.None;
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            var pt = e.GetPosition(this);
            float factor = e.Delta.Y > 0 ? 1.1f : 0.9f;
            Camera.ZoomBy(factor, new PointF((float) pt.X, (float) pt.Y));
            InvalidateVisual();
        }

        private PInputEventArgs MakeArgs(PointF world, PNode picked)
        {
            return new PInputEventArgs
            {
                Position = world,
                CanvasPosition = _lastPointer,
                PickedNode = picked,
                Button = _activeButton,
            };
        }

        private static MouseButton MapButton(PointerPointProperties p)
        {
            if (p.IsLeftButtonPressed) return MouseButton.Left;
            if (p.IsRightButtonPressed) return MouseButton.Right;
            if (p.IsMiddleButtonPressed) return MouseButton.Middle;
            return MouseButton.None;
        }
    }
}
