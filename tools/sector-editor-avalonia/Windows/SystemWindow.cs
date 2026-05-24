// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// Ported from N7.SystemWindow (kyp/Net7Tools/SectorEditor) under
// Net-7 Entertainment's CC BY-NC-SA 3.0; preservation modifications
// inherit under ShareAlike.

using System;
using System.Data;
using System.Drawing;
using N7.Props;
using SectorEditorAvalonia.PiccoloShim;
using SectorEditorAvalonia.Sprites;
using SectorEditorAvalonia.Utilities;

namespace SectorEditorAvalonia.Windows
{
    /// <summary>
    /// Renders one solar system's sectors as a galaxy-scale scene graph.
    /// Each sector is a clickable PPath ellipse with a label; clicking
    /// pushes its SectorProps into the IPropertyHost panel.
    /// </summary>
    public class SystemWindow
    {
        private readonly PCanvas canvas;
        private readonly PLayer masterLayer;
        private readonly PLayer sectorLayer;
        private PNode pSelectedNode;
        private readonly DataRow dr;
        private readonly IPropertyHost _pg;

        public SystemWindow(PCanvas pcanvas, string systemName, DataRow[] sectorTable,
                            IPropertyHost pg, DataRow selectedRow)
        {
            canvas = pcanvas;
            dr = selectedRow;
            _pg = pg;

            sectorLayer = new PLayer();

            // Original tool installed a mouse-wheel zoom controller on the
            // canvas camera here. The shim's PCanvas already handles wheel
            // zoom natively (OnPointerWheelChanged); the ctor-compat call
            // is retained for API parity.
            _ = new MouseWheelZoomController(canvas.Camera);

            canvas.BackColor = Color.Black;
            masterLayer = canvas.Layer;

            for (int i = 0; i < sectorTable.Length; i++)
            {
                string sectorName = sectorTable[i]["name"].ToString();
                float xmin = float.Parse(sectorTable[i]["x_min"].ToString());
                float xmax = float.Parse(sectorTable[i]["x_max"].ToString());
                float ymin = float.Parse(sectorTable[i]["y_min"].ToString());
                float ymax = float.Parse(sectorTable[i]["y_max"].ToString());
                float x = float.Parse(sectorTable[i]["galaxy_x"].ToString());
                float y = float.Parse(sectorTable[i]["galaxy_y"].ToString());

                DataRow r = sectorTable[i];
                _ = new SectorSprite(sectorLayer, pg, r, sectorName, x, y, xmin, xmax, ymin, ymax);
            }

            masterLayer.AddChild(sectorLayer);
            masterLayer.MouseDown += MasterLayer_OnMouseDown;
        }

        // Original WinForms version was wired to PropertyGrid's
        // PropertyValueChanged event. The Avalonia property panel will
        // call this with (propertyName, newValue) once that panel exists
        // (tracked in Tier 12e Wave 2 — IPropertyHost only carries
        // SelectedObject for now).
        public void OnPropertyValueChanged(string propertyName, object value)
        {
            try
            {
                if (pSelectedNode?.Tag is SectorSprite tmp)
                    tmp.updateChangedInfo(propertyName, value?.ToString() ?? "");
            }
            catch (Exception)
            {
                updateChangedInfo(propertyName, value);
            }
        }

        private void MasterLayer_OnMouseDown(object sender, PInputEventArgs e)
        {
            if (e.PickedNode?.Tag is SectorSprite tmp2)
            {
                setOriginalText(pSelectedNode);
                PText pnameNew = tmp2.getText();
                pnameNew.TextBrush = Brushes.Red;
            }
            pSelectedNode = e.PickedNode;
        }

        private void setOriginalText(PNode pickedNode)
        {
            if (pSelectedNode == null) return;
            if (pickedNode?.Tag is SectorSprite tmp)
            {
                PText pname = tmp.getText();
                pname.TextBrush = Brushes.White;
            }
        }

        public void updateChangedInfo(string propertyName, object _changedValue)
        {
            string value = _changedValue?.ToString() ?? "";
            string changedValue = value.Replace("'", "''");

            switch (propertyName)
            {
                case "Name":     dr["name"] = changedValue; break;
                case "GalaxyX":  dr["galaxy_x"] = float.Parse(changedValue); break;
                case "GalaxyY":  dr["galaxy_y"] = float.Parse(changedValue); break;
                case "GalaxyZ":  dr["galaxy_z"] = float.Parse(changedValue); break;
                case "Color":
                    if (_changedValue is Color color)
                    {
                        dr["color_r"] = color.R;
                        dr["color_g"] = color.G;
                        dr["color_b"] = color.B;
                    }
                    break;
                case "Notes":    dr["notes"] = changedValue; break;
            }

            if (dr.RowState != DataRowState.Modified)
                dr.SetModified();
        }

        public void newSector(DataRow ndr)
        {
            string sectorName = ndr["name"].ToString();
            float xmin = float.Parse(ndr["x_min"].ToString());
            float xmax = float.Parse(ndr["x_max"].ToString());
            float ymin = float.Parse(ndr["y_min"].ToString());
            float ymax = float.Parse(ndr["y_max"].ToString());
            float x = float.Parse(ndr["galaxy_x"].ToString());
            float y = float.Parse(ndr["galaxy_y"].ToString());

            _ = new SectorSprite(sectorLayer, _pg, ndr, sectorName, x, y, xmin, xmax, ymin, ymax);
        }
    }
}
