// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System;
using System.ComponentModel;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace SectorEditorAvalonia.Utilities
{
    // Reflection-driven property editor that fills a host StackPanel
    // with one row per public property. Replaces the WinForms
    // PropertyGrid the original tool used.
    //
    // Per type:
    //   bool        -> CheckBox
    //   int/float/double -> NumericUpDown
    //   string      -> TextBox
    //   System.Drawing.Color -> swatch (RGB-only quick edit)
    //   anything else -> read-only TextBox showing ToString()
    //
    // Properties are grouped by [Category] (the original used the same
    // attribute on Props classes). Within a category they're rendered
    // in declaration order. Tooltip uses [Description] if present.
    //
    // First-pass scope: no Color-picker chrome (just hex+swatch), no
    // sub-dialog "<Collection...>" launchers — the dialogs that own
    // those collections (MobGroup, HarvestableResTypes) open through
    // their own menu paths.
    public sealed class PropertyPanelHost : IPropertyHost
    {
        private readonly StackPanel _host;
        private object _selected;

        public PropertyPanelHost(StackPanel host) { _host = host; }

        public object SelectedObject
        {
            get => _selected;
            set { _selected = value; Rebuild(); }
        }

        private void Rebuild()
        {
            _host.Children.Clear();
            if (_selected == null) return;

            var props = _selected.GetType()
                .GetProperties(BindingFlags.Public | BindingFlags.Instance);

            string lastCategory = null;
            foreach (var p in props)
            {
                if (!p.CanRead) continue;

                var cat = p.GetCustomAttribute<CategoryAttribute>()?.Category ?? "Misc";
                if (cat != lastCategory)
                {
                    _host.Children.Add(new TextBlock
                    {
                        Text = cat,
                        FontWeight = FontWeight.Bold,
                        Margin = new Thickness(0, 8, 0, 4),
                    });
                    lastCategory = cat;
                }

                _host.Children.Add(BuildRow(p));
            }
        }

        private Control BuildRow(PropertyInfo p)
        {
            var label = new TextBlock
            {
                Text = p.Name,
                Width = 130,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var desc = p.GetCustomAttribute<DescriptionAttribute>()?.Description;
            if (!string.IsNullOrEmpty(desc))
                ToolTip.SetTip(label, desc);

            Control editor = BuildEditor(p);

            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Margin = new Thickness(0, 1, 0, 1),
            };
            row.Children.Add(label);
            row.Children.Add(editor);
            return row;
        }

        private Control BuildEditor(PropertyInfo p)
        {
            object current = SafeGet(p);
            Type t = p.PropertyType;

            if (t == typeof(bool))
            {
                var cb = new CheckBox { IsChecked = (bool)(current ?? false) };
                if (p.CanWrite)
                    cb.IsCheckedChanged += (_, _) => SafeSet(p, cb.IsChecked == true);
                else cb.IsEnabled = false;
                return cb;
            }

            if (t == typeof(int) || t == typeof(float) || t == typeof(double))
            {
                var nud = new NumericUpDown
                {
                    Width = 160,
                    FormatString = t == typeof(int) ? "0" : "0.###",
                    Value = current == null ? 0 : Convert.ToDecimal(current),
                    Minimum = decimal.MinValue,
                    Maximum = decimal.MaxValue,
                };
                if (p.CanWrite)
                {
                    nud.ValueChanged += (_, _) =>
                    {
                        if (nud.Value is decimal d)
                        {
                            if (t == typeof(int)) SafeSet(p, (int)d);
                            else if (t == typeof(float)) SafeSet(p, (float)d);
                            else SafeSet(p, (double)d);
                        }
                    };
                }
                else nud.IsEnabled = false;
                return nud;
            }

            if (t == typeof(System.Drawing.Color))
            {
                var col = (System.Drawing.Color)(current ?? System.Drawing.Color.Black);
                return BuildColorEditor(p, col);
            }

            if (t == typeof(string))
            {
                var tb = new TextBox { Width = 220, Text = (string)current ?? "" };
                if (p.CanWrite)
                    tb.LostFocus += (_, _) => SafeSet(p, tb.Text);
                else tb.IsReadOnly = true;
                return tb;
            }

            // Fallback — read-only display.
            return new TextBox
            {
                Width = 220,
                Text = current?.ToString() ?? "",
                IsReadOnly = true,
            };
        }

        private Control BuildColorEditor(PropertyInfo p, System.Drawing.Color initial)
        {
            var swatch = new Border
            {
                Width = 24, Height = 18,
                Background = new SolidColorBrush(global::Avalonia.Media.Color.FromArgb(initial.A, initial.R, initial.G, initial.B)),
                BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1),
            };
            var hex = new TextBox
            {
                Width = 100,
                Text = $"#{initial.R:X2}{initial.G:X2}{initial.B:X2}",
            };
            if (p.CanWrite)
            {
                hex.LostFocus += (_, _) =>
                {
                    if (TryParseHex(hex.Text, out var c))
                    {
                        SafeSet(p, c);
                        swatch.Background = new SolidColorBrush(global::Avalonia.Media.Color.FromArgb(c.A, c.R, c.G, c.B));
                    }
                };
            }
            else hex.IsReadOnly = true;

            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            panel.Children.Add(swatch);
            panel.Children.Add(hex);
            return panel;
        }

        private static bool TryParseHex(string s, out System.Drawing.Color c)
        {
            c = System.Drawing.Color.Black;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim().TrimStart('#');
            if (s.Length != 6) return false;
            try
            {
                int r = Convert.ToInt32(s.Substring(0, 2), 16);
                int g = Convert.ToInt32(s.Substring(2, 2), 16);
                int b = Convert.ToInt32(s.Substring(4, 2), 16);
                c = System.Drawing.Color.FromArgb(255, r, g, b);
                return true;
            }
            catch { return false; }
        }

        private object SafeGet(PropertyInfo p)
        {
            try { return p.GetValue(_selected); }
            catch { return null; }
        }

        private void SafeSet(PropertyInfo p, object value)
        {
            try { p.SetValue(_selected, value); }
            catch { /* swallow — reflection write-back is best-effort */ }
        }
    }
}
