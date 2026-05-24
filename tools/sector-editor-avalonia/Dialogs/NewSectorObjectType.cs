// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// Ported from N7.GUI.NewSectorObjectType under Net-7 Entertainment's
// CC BY-NC-SA 3.0; preservation modifications inherit under ShareAlike.
// License: LICENSES/enb-emulator

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using SectorEditorAvalonia.Utilities;
using SectorEditorAvalonia.Windows;

namespace SectorEditorAvalonia.Dialogs
{
    // Tiny type-picker that gates the NewSectorObject dialog. Mirrors
    // the original strings exactly so downstream string-matching
    // (NewSectorObject's `type.Contains("Mobs")` etc.) keeps working.
    public class NewSectorObjectTypeDialog : Window
    {
        public static readonly string[] TypeNames =
        {
            "Mobs (Type 0)",
            "Planets (Type 3)",
            "Stargates (Type 11)",
            "Starbases (Type 12)",
            "Decorations (Type 37)",
            "Harvestables (Type 38)",
        };

        public NewSectorObjectTypeDialog(TreeWindow.Node selectedSectorNode,
                                         INotificationSink notify,
                                         System.Action<string> onPicked)
        {
            Title = "New Sector Object — Choose Type";
            Width = 320;
            Height = 160;
            CanResize = false;

            var combo = new ComboBox();
            foreach (var n in TypeNames) combo.Items.Add(n);
            combo.SelectedIndex = 0;

            var stack = new StackPanel { Margin = new Thickness(12), Spacing = 8 };
            stack.Children.Add(new TextBlock { Text = "Type:" });
            stack.Children.Add(combo);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
            };
            var ok = new Button { Content = "OK", Width = 80 };
            ok.Click += (_, _) =>
            {
                // Original required a sector-level tree selection
                // (a leaf with a parent). Avalonia port checks the
                // caller passed in a non-null sector node, since the
                // tree-binding shape is the MainWindow's call.
                if (selectedSectorNode == null)
                {
                    notify.ShowError("You Cannot add a Sector Object without first \n having a sector selected. Please select a sector and try again!");
                    return;
                }
                onPicked?.Invoke(combo.SelectedItem?.ToString() ?? TypeNames[0]);
                Close();
            };
            var cancel = new Button { Content = "Cancel", Width = 80 };
            cancel.Click += (_, _) => Close();
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            stack.Children.Add(buttons);

            Content = stack;
        }
    }
}
