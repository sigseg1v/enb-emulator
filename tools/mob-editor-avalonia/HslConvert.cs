using System;
using Avalonia.Media;

namespace MobEditorAvalonia
{
    // Minimal HSL <-> RGB helpers, replacing the original mob-editor's
    // Utilities/AdobeColors.cs (425 LOC of Win32-only Photoshop-style
    // colour-picker code we don't need). The mob table stores the tint
    // as three [0,1] floats in `h`, `s`, `v` columns; we only need the
    // round-trip between those floats and a Color so the small swatch
    // rectangle on the General tab reflects the current values.
    public static class HslConvert
    {
        public static Color HslToRgb(double h, double s, double l)
        {
            double r, g, b;
            if (s <= 0.0)
            {
                r = g = b = l;
            }
            else
            {
                double q  = l < 0.5 ? l * (1 + s) : l + s - l * s;
                double p  = 2 * l - q;
                r = HueToRgb(p, q, h + 1.0 / 3);
                g = HueToRgb(p, q, h);
                b = HueToRgb(p, q, h - 1.0 / 3);
            }
            byte cr = (byte)Math.Round(Math.Clamp(r, 0, 1) * 255);
            byte cg = (byte)Math.Round(Math.Clamp(g, 0, 1) * 255);
            byte cb = (byte)Math.Round(Math.Clamp(b, 0, 1) * 255);
            return Color.FromRgb(cr, cg, cb);
        }

        static double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 0.5)     return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }
    }
}
