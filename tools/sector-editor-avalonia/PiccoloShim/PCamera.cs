// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Drawing;

namespace SectorEditorAvalonia.PiccoloShim
{
    /// <summary>
    /// Piccolo's PCamera abstracted to a 2D view transform — translation
    /// + uniform scale, exposed for pan/zoom controllers.
    /// </summary>
    public sealed class PCamera
    {
        public float TranslateX;
        public float TranslateY;
        public float Scale = 1.0f;

        // Piccolo's PCamera intercepted all mouse input before layers got
        // it. The original sector editor wired `canvas.Camera.MouseDown`
        // to a "place-new-object" position-picker. PCanvas raises this on
        // every pointer-press regardless of pick result.
        public event PInputEventHandler MouseDown;
        internal void RaiseMouseDown(PInputEventArgs e) => MouseDown?.Invoke(this, e);

        // API-compat alias for callsites that used Piccolo's `ViewScale`.
        public float ViewScale
        {
            get => Scale;
            set => Scale = value;
        }

        public void Pan(float dx, float dy)
        {
            TranslateX += dx;
            TranslateY += dy;
        }

        public void ZoomBy(float factor, PointF aroundCanvas)
        {
            if (factor <= 0) return;
            float oldScale = Scale;
            Scale *= factor;
            if (Scale < 0.05f) Scale = 0.05f;
            if (Scale > 20f) Scale = 20f;
            float k = Scale / oldScale;
            TranslateX = aroundCanvas.X - k * (aroundCanvas.X - TranslateX);
            TranslateY = aroundCanvas.Y - k * (aroundCanvas.Y - TranslateY);
        }

        public PointF CanvasToWorld(PointF canvas)
        {
            return new PointF((canvas.X - TranslateX) / Scale, (canvas.Y - TranslateY) / Scale);
        }
    }
}
