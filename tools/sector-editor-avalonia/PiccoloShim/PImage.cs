// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System.IO;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace SectorEditorAvalonia.PiccoloShim
{
    /// <summary>
    /// Image node. The original Piccolo2D PImage wrapped System.Drawing.Image;
    /// we take an Avalonia Bitmap directly (or load from a path / stream) so
    /// the shim doesn't drag in libgdiplus on Linux.
    /// </summary>
    public class PImage : PNode
    {
        private Bitmap _bitmap;

        public PImage() { }

        public PImage(Bitmap bitmap)
        {
            Bitmap = bitmap;
        }

        public PImage(string path)
        {
            if (File.Exists(path))
                Bitmap = new Bitmap(path);
        }

        public Bitmap Bitmap
        {
            get => _bitmap;
            set
            {
                _bitmap = value;
                if (_bitmap != null)
                {
                    if (Width == 0) Width = (float) _bitmap.PixelSize.Width;
                    if (Height == 0) Height = (float) _bitmap.PixelSize.Height;
                }
            }
        }

        public static PImage FromStream(Stream stream)
            => new PImage(new Bitmap(stream));

        public override void Render(DrawingContext ctx)
        {
            if (_bitmap != null)
            {
                ctx.DrawImage(_bitmap, new Rect(X, Y, Width, Height));
            }
            else
            {
                var fill = ToAvalonia(Brush) ?? new SolidColorBrush(Avalonia.Media.Color.FromArgb(64, 128, 128, 128));
                ctx.DrawRectangle(fill, ToAvalonia(Pen), new Rect(X, Y, Width, Height));
            }
        }
    }
}
