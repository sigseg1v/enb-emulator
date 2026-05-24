// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Drawing;
using Avalonia.Controls;
using Avalonia.Input;

namespace SectorEditorAvalonia.PiccoloShim
{
    /// <summary>
    /// Piccolo's built-in camera-pan-on-drag handler. Attach to a
    /// PCanvas to enable middle-mouse / drag-empty-space panning.
    /// </summary>
    public sealed class PPanEventHandler
    {
        private readonly PCanvas _canvas;
        private PointF _lastDown;
        private bool _panning;

        public PPanEventHandler(PCanvas canvas)
        {
            _canvas = canvas;
            _canvas.AddHandler(InputElement.PointerPressedEvent, OnPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            _canvas.AddHandler(InputElement.PointerMovedEvent, OnMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            _canvas.AddHandler(InputElement.PointerReleasedEvent, OnReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        }

        private void OnPressed(object sender, PointerPressedEventArgs e)
        {
            var p = e.GetCurrentPoint(_canvas).Properties;
            if (!(p.IsMiddleButtonPressed || (p.IsLeftButtonPressed && IsOverEmptySpace(e))))
                return;
            var pt = e.GetPosition(_canvas);
            _lastDown = new PointF((float) pt.X, (float) pt.Y);
            _panning = true;
        }

        private void OnMoved(object sender, PointerEventArgs e)
        {
            if (!_panning) return;
            var pt = e.GetPosition(_canvas);
            float dx = (float) pt.X - _lastDown.X;
            float dy = (float) pt.Y - _lastDown.Y;
            _canvas.Camera.Pan(dx, dy);
            _lastDown = new PointF((float) pt.X, (float) pt.Y);
            _canvas.InvalidateVisual();
        }

        private void OnReleased(object sender, PointerReleasedEventArgs e) => _panning = false;

        private bool IsOverEmptySpace(PointerEventArgs e)
        {
            var pt = e.GetPosition(_canvas);
            var world = _canvas.Camera.CanvasToWorld(new PointF((float) pt.X, (float) pt.Y));
            return _canvas.Layer.PickTopDown(world) == null;
        }
    }
}
