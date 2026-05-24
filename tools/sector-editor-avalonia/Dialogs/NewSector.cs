// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// Ported from N7.GUI.NewSector under Net-7 Entertainment's CC BY-NC-SA 3.0;
// preservation modifications inherit under ShareAlike.
// License: LICENSES/enb-emulator

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
    // Original took the WinForms TreeView so it could (a) read the
    // selected system's name and (b) graft a new sector node under it.
    // The Avalonia port takes the parent TreeWindow.Node directly to
    // make the dependency explicit; the systems DAO comes via ctor.
    public class NewSectorDialog : Window
    {
        private readonly TreeWindow.Node _parentSystemNode;
        private readonly SectorsSql _sectorsSQL;
        private readonly SystemsSql _systemsSQL;
        private readonly IPropertyHost _pg;
        private readonly INotificationSink _notify;
        private readonly System.Action<DataRow> _onAdded;
        private SectorProps sp;

        public NewSectorDialog(TreeWindow.Node parentSystemNode,
                               SectorsSql sectorsSql,
                               SystemsSql systemsSql,
                               IPropertyHost pg,
                               INotificationSink notify,
                               System.Action<DataRow> onAdded)
        {
            _parentSystemNode = parentSystemNode;
            _sectorsSQL = sectorsSql;
            _systemsSQL = systemsSql;
            _pg = pg;
            _notify = notify;
            _onAdded = onAdded;

            Title = "New Sector";
            Width = 380;
            Height = 320;

            sp = new SectorProps
            {
                SystemID = _systemsSQL.getIDFromName(parentSystemNode.Name),
                Width = 200000,
                Height = 200000,
                Depth = 50000,
                Name = "<New Sector>",
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
            DataTable tmp = _sectorsSQL.getSectorTable();

            // Original ID-collision check — preserved including the
            // odd `0` always-taken rule (id 0 is reserved).
            bool idTaken = false;
            foreach (DataRow tr in tmp.Rows)
            {
                if (tr["sector_id"].ToString() == sp.SectorID.ToString()) idTaken = true;
                else if (sp.SectorID.ToString() == "0") idTaken = true;
            }
            if (idTaken)
            {
                _notify.ShowError("Sorry, the entered sector Id is already taken. \n Please enter a unique one");
                return;
            }

            DataRow newSectorRow = tmp.NewRow();
            string name = sp.Name.Replace("'", "''");
            string notes = sp.Notes != null ? sp.Notes.Replace("'", "''") : "";
            string greetings = sp.Greetings != null ? sp.Greetings.Replace("'", "''") : "";

            newSectorRow["name"] = name;
            newSectorRow["notes"] = notes;
            newSectorRow["greetings"] = greetings;
            newSectorRow["sector_id"] = sp.SectorID;

            float xmin = -(sp.Width / 2),  xmax = sp.Width / 2;
            float ymin = -(sp.Height / 2), ymax = sp.Height / 2;
            float zmin = -(sp.Depth / 2),  zmax = sp.Depth / 2;

            newSectorRow["x_min"] = xmin; newSectorRow["x_max"] = xmax;
            newSectorRow["y_min"] = ymin; newSectorRow["y_max"] = ymax;
            newSectorRow["z_min"] = zmin; newSectorRow["z_max"] = zmax;
            newSectorRow["grid_x"] = sp.GridX;
            newSectorRow["grid_y"] = sp.GridY;
            newSectorRow["grid_z"] = sp.GridZ;
            newSectorRow["fog_near"] = sp.FogNear;
            newSectorRow["fog_far"] = sp.FogFar;
            newSectorRow["debris_mode"] = sp.DebrisMode;
            newSectorRow["light_backdrop"] = sp.LightBackdrop;
            newSectorRow["fog_backdrop"] = sp.FogBackdrop;
            newSectorRow["swap_backdrop"] = sp.SwapBackdrop;
            newSectorRow["backdrop_fog_near"] = sp.BackdropFogNear;
            newSectorRow["backdrop_fog_far"] = sp.BackdropFogFar;
            newSectorRow["max_tilt"] = sp.MaxTilt;
            newSectorRow["auto_level"] = sp.AutoLevel;
            newSectorRow["impulse_rate"] = sp.ImpulseRate;
            newSectorRow["decay_velocity"] = sp.DecayVelocity;
            newSectorRow["decay_spin"] = sp.DecaySpin;
            newSectorRow["backdrop_asset"] = sp.BackdropAsset;
            newSectorRow["system_id"] = sp.SystemID;
            newSectorRow["galaxy_x"] = sp.GalaxyX;
            newSectorRow["galaxy_y"] = sp.GalaxyY;
            newSectorRow["galaxy_z"] = sp.GalaxyZ;

            tmp.Rows.Add(newSectorRow);
            _parentSystemNode.Children.Add(new TreeWindow.Node { Name = name });
            _sectorsSQL.newRow(newSectorRow);

            // Original called mainFrm.systemWindow.newSector(...) here.
            // Avalonia version routes through an onAdded callback so the
            // caller decides which SystemWindow gets the new row.
            _onAdded?.Invoke(newSectorRow);

            Close();
        }
    }
}
