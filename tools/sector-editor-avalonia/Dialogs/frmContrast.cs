// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// Ported from N7.GUI.frmContrast under Net-7 Entertainment's CC BY-NC-SA 3.0;
// preservation modifications inherit under ShareAlike.
// License: LICENSES/enb-emulator

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;

namespace SectorEditorAvalonia.Dialogs
{
    // Tiny dropdown-style window: a single Slider + a numeric readout
    // button. The original was a borderless Form embedded into a
    // PropertyGrid IWindowsFormsEditorService dropdown; the Avalonia
    // port surfaces it as a small frame-less Window that the editor
    // panel can position next to the value being edited. BarValue is
    // public so callers read it after Close.
    public class FrmContrast : Window
    {
        public int BarValue;

        public FrmContrast()
        {
            Title = "Contrast";
            Width = 200;
            Height = 80;
            CanResize = false;
            SystemDecorations = SystemDecorations.None;

            var slider = new Slider
            {
                Minimum = 0,
                Maximum = 100,
                TickFrequency = 10,
                TickPlacement = TickPlacement.Outside,
                Width = 150,
                Value = BarValue,
            };
            var btn = new Button
            {
                Width = 40,
                Content = BarValue.ToString(),
            };

            slider.PropertyChanged += (_, e) =>
            {
                if (e.Property == RangeBase.ValueProperty)
                {
                    BarValue = (int)slider.Value;
                    btn.Content = BarValue.ToString();
                }
            };
            btn.Click += (_, _) => Close();

            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Margin = new Thickness(8),
                VerticalAlignment = VerticalAlignment.Center,
            };
            panel.Children.Add(slider);
            panel.Children.Add(btn);
            Content = panel;
        }
    }
}
