// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// Ported from N7.AboutBox1 / N7.AboutBox2 under Net-7 Entertainment's
// CC BY-NC-SA 3.0; preservation modifications inherit under ShareAlike.
// License: LICENSES/enb-emulator

using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace SectorEditorAvalonia.Dialogs
{
    // Combines the originals' two AboutBox forms (visually identical) into
    // a single window. Pulls Title / Version / Description / Product /
    // Copyright / Company from assembly attributes via reflection,
    // matching the original's AssemblyTitle/AssemblyVersion/etc. accessors.
    public class AboutBox : Window
    {
        public AboutBox()
        {
            var asm = Assembly.GetExecutingAssembly();
            string title = GetAttr<AssemblyTitleAttribute>(asm)?.Title ?? "Sector Editor";
            string product = GetAttr<AssemblyProductAttribute>(asm)?.Product ?? "";
            string version = asm.GetName().Version?.ToString() ?? "";
            string copyright = GetAttr<AssemblyCopyrightAttribute>(asm)?.Copyright ?? "";
            string company = GetAttr<AssemblyCompanyAttribute>(asm)?.Company ?? "";
            string description = GetAttr<AssemblyDescriptionAttribute>(asm)?.Description ?? "";

            Title = $"About {title}";
            Width = 460;
            Height = 320;
            CanResize = false;

            var stack = new StackPanel
            {
                Margin = new Thickness(16),
                Spacing = 6,
            };
            stack.Children.Add(new TextBlock { Text = product, FontSize = 18, FontWeight = Avalonia.Media.FontWeight.Bold });
            stack.Children.Add(new TextBlock { Text = $"Version {version}" });
            stack.Children.Add(new TextBlock { Text = copyright });
            stack.Children.Add(new TextBlock { Text = company });
            stack.Children.Add(new TextBox
            {
                Text = description,
                IsReadOnly = true,
                AcceptsReturn = true,
                Height = 150,
            });
            var ok = new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Right, Width = 80 };
            ok.Click += (_, _) => Close();
            stack.Children.Add(ok);

            Content = stack;
        }

        private static T GetAttr<T>(Assembly asm) where T : System.Attribute
        {
            var attrs = asm.GetCustomAttributes(typeof(T), false);
            return attrs.Length > 0 ? (T) attrs[0] : null;
        }
    }
}
