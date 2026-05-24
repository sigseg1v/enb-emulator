// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// Ported from N7.GUI.NewFrm under Net-7 Entertainment's CC BY-NC-SA 3.0;
// preservation modifications inherit under ShareAlike.
// License: LICENSES/enb-emulator

using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using SectorEditorAvalonia.Utilities;

namespace SectorEditorAvalonia.Dialogs
{
    // Three-way dispatcher: "what kind of thing do you want to make?"
    // — System / Sector / SectorObject. The original launched the
    // matching dialog directly; here we surface the user's choice
    // through onPicked so MainWindow can wire up the right ctor args.
    public class NewFrmDialog : Window
    {
        public NewFrmDialog(INotificationSink notify, System.Action<string> onPicked)
        {
            Title = "New...";
            Width = 280;
            Height = 160;
            CanResize = false;

            var combo = new ComboBox();
            combo.Items.Add("System");
            combo.Items.Add("Sector");
            combo.Items.Add("Sector Object");

            var stack = new StackPanel { Margin = new Thickness(12), Spacing = 8 };
            stack.Children.Add(new TextBlock { Text = "Create:" });
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
                if (combo.SelectedItem == null)
                {
                    notify.ShowError("Please Select an option from the dropdown menu.");
                    return;
                }
                onPicked?.Invoke(combo.SelectedItem.ToString());
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
