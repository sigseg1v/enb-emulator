// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// New code; project default license (LICENSES/enb-emulator).

using System.Drawing;
using Avalonia;
using Avalonia.Media;
using Point = Avalonia.Point;

namespace SectorEditorAvalonia.PiccoloShim
{
    public enum PPathShape { Ellipse, Rectangle, Line }

    public class PPath : PNode
    {
        public PPathShape Shape { get; private set; }
        public PointF LineStart { get; private set; }
        public PointF LineEnd { get; private set; }

        public static PPath CreateEllipse(float x, float y, float w, float h)
            => new PPath { Shape = PPathShape.Ellipse, X = x, Y = y, Width = w, Height = h };

        public static PPath CreateRectangle(float x, float y, float w, float h)
            => new PPath { Shape = PPathShape.Rectangle, X = x, Y = y, Width = w, Height = h };

        public static PPath CreateLine(float x1, float y1, float x2, float y2)
        {
            var p = new PPath { Shape = PPathShape.Line };
            p.LineStart = new PointF(x1, y1);
            p.LineEnd = new PointF(x2, y2);
            p.X = System.Math.Min(x1, x2);
            p.Y = System.Math.Min(y1, y2);
            p.Width = System.Math.Abs(x2 - x1);
            p.Height = System.Math.Abs(y2 - y1);
            return p;
        }

        public override void Render(DrawingContext ctx)
        {
            var fill = ToAvalonia(Brush);
            var stroke = ToAvalonia(Pen);
            switch (Shape)
            {
                case PPathShape.Ellipse:
                    ctx.DrawEllipse(fill, stroke,
                        new Point(X + Width / 2.0, Y + Height / 2.0), Width / 2.0, Height / 2.0);
                    break;
                case PPathShape.Rectangle:
                    ctx.DrawRectangle(fill, stroke, new Rect(X, Y, Width, Height));
                    break;
                case PPathShape.Line:
                    if (stroke != null)
                        ctx.DrawLine(stroke,
                            new Point(LineStart.X, LineStart.Y),
                            new Point(LineEnd.X, LineEnd.Y));
                    break;
            }
        }
    }
}
