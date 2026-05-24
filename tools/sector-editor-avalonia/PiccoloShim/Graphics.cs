// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.Drawing;

namespace SectorEditorAvalonia.PiccoloShim
{
    // Lightweight, GDI+-free Pen / Brush / DashStyle / StringAlignment data
    // types. System.Drawing.Pen and friends would force a libgdiplus runtime
    // dep on Linux — defeating the whole point of porting to Avalonia. These
    // mirror the System.Drawing surface that the sector editor sprite code
    // touches, so the Tier 12e mechanical port stays drop-in.

    public enum DashStyle
    {
        Solid = 0,
        Dash = 1,
        Dot = 2,
        DashDot = 3,
        DashDotDot = 4,
        Custom = 5,
    }

    public enum StringAlignment
    {
        Near = 0,
        Center = 1,
        Far = 2,
    }

    public abstract class Brush
    {
        public abstract Color Color { get; }
    }

    public sealed class SolidBrush : Brush
    {
        public override Color Color { get; }
        public SolidBrush(Color color) { Color = color; }
    }

    public static class Brushes
    {
        public static readonly SolidBrush White = new SolidBrush(Color.White);
        public static readonly SolidBrush Black = new SolidBrush(Color.Black);
        public static readonly SolidBrush Red = new SolidBrush(Color.Red);
        public static readonly SolidBrush Green = new SolidBrush(Color.Green);
        public static readonly SolidBrush Blue = new SolidBrush(Color.Blue);
        public static readonly SolidBrush Yellow = new SolidBrush(Color.Yellow);
        public static readonly SolidBrush Transparent = new SolidBrush(Color.Transparent);
        public static readonly SolidBrush MistyRose = new SolidBrush(Color.MistyRose);
    }

    public sealed class Pen
    {
        public Color Color;
        public float Width;
        public DashStyle DashStyle = DashStyle.Solid;

        public Pen(Color color) { Color = color; Width = 1.0f; }
        public Pen(Color color, float width) { Color = color; Width = width; }
    }
}
