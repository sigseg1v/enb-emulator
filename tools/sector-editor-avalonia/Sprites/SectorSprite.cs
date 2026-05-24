// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// Ported from N7.Sprites.SectorSprite under Net-7 Entertainment's
// CC BY-NC-SA 3.0; preservation modifications inherit under ShareAlike.

using System;
using System.Data;
using System.Drawing;
using N7.Props;
using SectorEditorAvalonia.PiccoloShim;
using SectorEditorAvalonia.Utilities;

namespace SectorEditorAvalonia.Sprites
{
    // Renders one sector's name+bounds circle in the galaxy-scale
    // SystemWindow. Click → push SectorProps into the property host.
    public class SectorSprite
    {
        private readonly IPropertyHost _pg;
        private SectorProps sp;
        private PText pname;
        private DataRow dr;

        public SectorSprite(PLayer layer, IPropertyHost pg, DataRow r,
                            string name, float galaxy_x, float galaxy_y,
                            float xmin, float xmax, float ymin, float ymax)
        {
            _pg = pg;
            dr = r;
            setupData(r);

            float width = (xmax - xmin) / 1000;
            // height unused in original beyond the (square) circle diameter

            var sigPen = new Pen(Color.Honeydew, 2.0F) { DashStyle = DashStyle.Dash };
            var sigCircle = PPath.CreateEllipse(galaxy_x, galaxy_y, width, width);
            sigCircle.Pen = sigPen;
            sigCircle.Brush = Brushes.Transparent;

            pname = new PText(name);
            pname.TextBrush = Brushes.White;
            pname.TextAlignment = StringAlignment.Center;
            pname.X = sigCircle.X + ((sigCircle.Width / 2) - (pname.Width / 2));
            pname.Y = sigCircle.Y + (sigCircle.Height / 2);

            sigCircle.Tag = this;
            sigCircle.AddChild(pname);
            layer.AddChild(sigCircle);
            sigCircle.ChildrenPickable = false;
            sigCircle.MouseDown += Image_MouseDown;
        }

        private void setupData(DataRow r)
        {
            string sectorName = r["name"].ToString();
            float xmin = float.Parse(r["x_min"].ToString());
            float xmax = float.Parse(r["x_max"].ToString());
            float ymin = float.Parse(r["y_min"].ToString());
            float ymax = float.Parse(r["y_max"].ToString());
            float zmin = float.Parse(r["z_min"].ToString());
            float zmax = float.Parse(r["z_max"].ToString());
            int gridx = int.Parse(r["grid_x"].ToString());
            int gridy = int.Parse(r["grid_y"].ToString());
            int gridz = int.Parse(r["grid_z"].ToString());
            float fognear = float.Parse(r["fog_near"].ToString());
            float fogfar = float.Parse(r["fog_far"].ToString());
            int debrismode = int.Parse(r["debris_mode"].ToString());
            bool lightbackdrop = (bool) r["light_backdrop"];
            bool fogbackdrop = (bool) r["fog_backdrop"];
            bool swapbackdrop = (bool) r["swap_backdrop"];
            float backdropfognear = float.Parse(r["backdrop_fog_near"].ToString());
            float backdropfogfar = float.Parse(r["backdrop_fog_far"].ToString());
            float maxtilt = float.Parse(r["max_tilt"].ToString());
            bool autolevel = (bool) r["auto_level"];
            float impulserate = float.Parse(r["impulse_rate"].ToString());
            float decayvelocity = float.Parse(r["decay_velocity"].ToString());
            float decayspin = float.Parse(r["decay_spin"].ToString());
            int backdropasset = int.Parse(r["backdrop_asset"].ToString());
            string greetings = r["greetings"].ToString();
            string notes = r["notes"].ToString();
            int systemid = int.Parse(r["system_id"].ToString());
            float galaxyx = float.Parse(r["galaxy_x"].ToString());
            float galaxyy = float.Parse(r["galaxy_y"].ToString());
            float galaxyz = float.Parse(r["galaxy_z"].ToString());
            int sector_type = int.Parse(r["sector_type"].ToString());

            float width = xmax - xmin;
            float height = ymax - ymin;
            float depth = zmax - zmin;

            sp = new SectorProps();
            sp.Name = sectorName;
            sp.Width = width;
            sp.Height = height;
            sp.Depth = depth;
            sp.GridX = gridx;
            sp.GridY = gridy;
            sp.GridZ = gridz;
            sp.FogNear = fognear;
            sp.FogFar = fogfar;
            sp.DebrisMode = debrismode;
            sp.LightBackdrop = lightbackdrop;
            sp.FogBackdrop = fogbackdrop;
            sp.SwapBackdrop = swapbackdrop;
            sp.BackdropFogNear = backdropfognear;
            sp.BackdropFogFar = backdropfogfar;
            sp.MaxTilt = maxtilt;
            sp.AutoLevel = autolevel;
            sp.ImpulseRate = impulserate;
            sp.DecayVelocity = decayvelocity;
            sp.DecaySpin = decayspin;
            sp.BackdropAsset = backdropasset;
            sp.Greetings = greetings;
            sp.Notes = notes;
            sp.SystemID = systemid;
            sp.GalaxyX = galaxyx;
            sp.GalaxyY = galaxyy;
            sp.GalaxyZ = galaxyz;

            sp.SectorType = sector_type switch
            {
                0 => "Space Sector",
                1 => "Rocky Planet Surface",
                2 => "Gas Giant Surface",
                _ => "",
            };
        }

        private void Image_MouseDown(object sender, PInputEventArgs e)
        {
            _pg.SelectedObject = sp;
        }

        public PText getText() => pname;

        public void updateChangedInfo(string propertyName, string _changedValue)
        {
            string changedValue = _changedValue.Replace("'", "''");
            switch (propertyName)
            {
                case "Name":               dr["name"] = changedValue; break;
                case "Width":
                case "Height":
                case "Depth":
                    if (sp.Width != 0)  { dr["x_min"] = -(sp.Width / 2);  dr["x_max"] = sp.Width / 2; }
                    if (sp.Height != 0) { dr["y_min"] = -(sp.Height / 2); dr["y_max"] = sp.Height / 2; }
                    if (sp.Depth != 0)  { dr["z_min"] = -(sp.Depth / 2);  dr["z_max"] = sp.Depth / 2; }
                    break;
                // NOTE: original code wrote dr["grix_x"] (typo for grid_x) — preserved
                // verbatim; if anyone ever validates this against the schema, they'll
                // need a separate audit pass to decide whether to fix-up or migrate.
                case "GridX":              dr["grix_x"] = int.Parse(changedValue); break;
                case "GridY":              dr["grix_y"] = int.Parse(changedValue); break;
                case "GridZ":              dr["grix_z"] = int.Parse(changedValue); break;
                case "FogNear":            dr["fog_near"] = float.Parse(changedValue); break;
                case "FogFar":             dr["fog_far"] = float.Parse(changedValue); break;
                case "DebrisMode":         dr["debris_mode"] = int.Parse(changedValue); break;
                case "LightBackdrop":      dr["light_backdrop"] = bool.Parse(changedValue); break;
                case "FogBackdrop":        dr["fog_backdrop"] = bool.Parse(changedValue); break;
                case "SwapBackdrop":       dr["swap_backdrop"] = bool.Parse(changedValue); break;
                case "BackdropFogNear":    dr["backdrop_fog_near"] = float.Parse(changedValue); break;
                case "BackdropFogFar":     dr["backdrop_fog_far"] = float.Parse(changedValue); break;
                // NOTE: original used "mex_tilt" — typo preserved verbatim, same caveat as above.
                case "MaxTilt":            dr["mex_tilt"] = float.Parse(changedValue); break;
                case "AutoLevel":          dr["auto_level"] = bool.Parse(changedValue); break;
                case "ImpulseRate":        dr["impulse_rate"] = float.Parse(changedValue); break;
                case "DecayVelocity":      dr["decay_velocity"] = float.Parse(changedValue); break;
                case "DecaySpin":          dr["decay_spin"] = float.Parse(changedValue); break;
                case "BackdropAsset":      dr["backdrop_asset"] = int.Parse(changedValue); break;
                case "Greetings":          dr["greetings"] = changedValue; break;
                case "Notes":              dr["notes"] = changedValue; break;
                case "SystemID":           dr["system_id"] = int.Parse(changedValue); break;
                case "GalaxyX":            dr["galaxy_x"] = float.Parse(changedValue); break;
                case "GalaxyY":            dr["galaxy_y"] = float.Parse(changedValue); break;
                case "GalaxyZ":            dr["galaxy_z"] = float.Parse(changedValue); break;
                case "SectorType":
                    if (changedValue == "Space Sector") dr["sector_type"] = 0;
                    else if (changedValue == "Rocky Planet Surface") dr["sector_type"] = 1;
                    else if (changedValue == "Gas Giant Surface") dr["sector_type"] = 2;
                    break;
            }

            if (dr.RowState != DataRowState.Modified)
                dr.SetModified();
        }
    }
}
