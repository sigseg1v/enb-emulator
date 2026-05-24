// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// Ported from N7.GUI.NewSystem under Net-7 Entertainment's CC BY-NC-SA 3.0;
// preservation modifications inherit under ShareAlike.
// License: LICENSES/enb-emulator

using System;
using System.Data;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using N7.Props;
using N7;
using N7.Sql;
using SectorEditorAvalonia.Utilities;
using SectorEditorAvalonia.Windows;

namespace SectorEditorAvalonia.Dialogs
{
    // "New system" CRUD form. Original pushed a SystemProps into a
    // WinForms PropertyGrid; here we push into the IPropertyHost host
    // (currently NullPropertyHost — real reflection-driven panel lands
    // in a later wave). OK commits a DataRow to SystemsSql and grafts
    // a Node onto the tree; Cancel just closes.
    public class NewSystemDialog : Window
    {
        private readonly SystemsSql _systemSQL;
        private readonly TreeWindow.Node _treeRoot;
        private readonly IPropertyHost _pg;
        private SystemProps sp;

        public NewSystemDialog(TreeWindow.Node treeRoot, SystemsSql systemsSql, IPropertyHost pg)
        {
            _treeRoot = treeRoot;
            _systemSQL = systemsSql;
            _pg = pg;

            Title = "New System";
            Width = 380;
            Height = 320;

            sp = new SystemProps
            {
                Name = "<New System>",
                Color = System.Drawing.Color.Black,
            };
            _pg.SelectedObject = sp;

            var stack = new StackPanel { Margin = new Thickness(12), Spacing = 8 };
            stack.Children.Add(new TextBlock { Text = "Edit properties in the property panel, then click Add." });

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
            };
            var add = new Button { Content = "Add", Width = 80 };
            add.Click += (_, _) => CommitAndClose();
            var cancel = new Button { Content = "Cancel", Width = 80 };
            cancel.Click += (_, _) => Close();
            buttons.Children.Add(add);
            buttons.Children.Add(cancel);
            stack.Children.Add(buttons);

            Content = stack;
        }

        private void CommitAndClose()
        {
            DataTable tmp = _systemSQL.getSystemTable();
            DataRow newSystemRow = tmp.NewRow();

            string name = sp.Name.Replace("'", "''");
            string notes = sp.Notes != null ? sp.Notes.Replace("'", "''") : "";

            newSystemRow["name"] = name;
            newSystemRow["notes"] = notes;
            newSystemRow["galaxy_x"] = sp.GalaxyX;
            newSystemRow["galaxy_y"] = sp.GalaxyY;
            newSystemRow["galaxy_z"] = sp.GalaxyZ;
            newSystemRow["color_r"] = sp.Color.R;
            newSystemRow["color_g"] = sp.Color.G;
            newSystemRow["color_b"] = sp.Color.B;

            tmp.Rows.Add(newSystemRow);
            _treeRoot.Children.Add(new TreeWindow.Node { Name = name });
            _systemSQL.newRow(newSystemRow);

            Close();
        }
    }
}
