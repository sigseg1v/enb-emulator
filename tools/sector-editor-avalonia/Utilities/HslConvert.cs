// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System;
using System.Drawing;

namespace SectorEditorAvalonia.Utilities
{
    // Replaces the original sector-editor's Utilities/AdobeColors.cs
    // (~700 LOC of Win32-only Photoshop-style colour-picker code we don't
    // need). The mob/planet/etc. tables store tint as three [0,1] floats
    // in `h`, `s`, `v` columns; the only operations the sprite code does
    // are HSL<->Color round-trips for displaying and persisting tint.
    public static class HslConvert
    {
        public struct HSL
        {
            public float H;
            public float S;
            public float L;
        }

        public static Color HslToRgb(HSL hsl) => HslToRgb(hsl.H, hsl.S, hsl.L);

        public static Color HslToRgb(double h, double s, double l)
        {
            double r, g, b;
            if (s <= 0.0)
            {
                r = g = b = l;
            }
            else
            {
                double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
                double p = 2 * l - q;
                r = HueToRgb(p, q, h + 1.0 / 3);
                g = HueToRgb(p, q, h);
                b = HueToRgb(p, q, h - 1.0 / 3);
            }
            byte cr = (byte) Math.Round(Math.Clamp(r, 0, 1) * 255);
            byte cg = (byte) Math.Round(Math.Clamp(g, 0, 1) * 255);
            byte cb = (byte) Math.Round(Math.Clamp(b, 0, 1) * 255);
            return Color.FromArgb(255, cr, cg, cb);
        }

        public static HSL RgbToHsl(Color c)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double l = (max + min) / 2.0;
            double h = 0, s = 0;
            if (max != min)
            {
                double d = max - min;
                s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
                if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
                else if (max == g) h = (b - r) / d + 2;
                else h = (r - g) / d + 4;
                h /= 6.0;
            }
            return new HSL { H = (float) h, S = (float) s, L = (float) l };
        }

        private static double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 0.5) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }
    }
}
