// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using Avalonia;
using Avalonia.Media;
using Point = Avalonia.Point;

namespace SectorEditorAvalonia.PiccoloShim
{
    public class PText : PNode
    {
        private string _text = string.Empty;
        private FormattedText _ft;

        public PText() : this(string.Empty) { }

        public PText(string text)
        {
            Text = text ?? string.Empty;
        }

        public Brush TextBrush { get; set; } = Brushes.White;
        public StringAlignment TextAlignment { get; set; } = StringAlignment.Near;
        public float FontSize { get; set; } = 12.0f;

        public string Text
        {
            get => _text;
            set
            {
                _text = value ?? string.Empty;
                Rebuild();
            }
        }

        private void Rebuild()
        {
            var typeface = new Typeface("Inter");
            _ft = new FormattedText(
                _text,
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                FontSize,
                Brushes_Avalonia.White);
            Width = (float) _ft.Width;
            Height = (float) _ft.Height;
        }

        public override void Render(DrawingContext ctx)
        {
            if (_ft == null) Rebuild();
            var brush = ToAvalonia(TextBrush) ?? Brushes_Avalonia.White;
            _ft.SetForegroundBrush(brush);
            double drawX = X;
            if (TextAlignment == StringAlignment.Center) drawX = X - _ft.Width / 2.0;
            else if (TextAlignment == StringAlignment.Far) drawX = X - _ft.Width;
            ctx.DrawText(_ft, new Point(drawX, Y));
        }
    }

    internal static class Brushes_Avalonia
    {
        public static readonly IBrush White =
            new SolidColorBrush(Avalonia.Media.Color.FromRgb(255, 255, 255));
    }
}
