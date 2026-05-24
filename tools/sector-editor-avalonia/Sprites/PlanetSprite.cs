// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// Ported from N7.Sprites.PlanetSprite under Net-7 Entertainment's
// CC BY-NC-SA 3.0; preservation modifications inherit under ShareAlike.

using System;
using System.Data;
using System.Drawing;
using Avalonia.Media.Imaging;
using N7.Props;
using SectorEditorAvalonia.PiccoloShim;
using SectorEditorAvalonia.Utilities;

namespace SectorEditorAvalonia.Sprites
{
    // Renders one planet (`sector_objects` row of `type=3`) at sector-
    // local scale: image + signature/radar/exploration circles + floating
    // name label. Click → push PlanetProps into IPropertyHost. Drag →
    // update DataRow position + push back into PlanetProps. Image swaps
    // among planet.gif / planetLandable.gif depending on
    // AppearsInRadar / IsLandable.
    public class PlanetSprite
    {
        private readonly IPropertyHost _pg;
        private readonly IGridSyncSink _grid;
        private PlanetProps dp;
        private bool appearsInRadar;
        private PText pname;
        private DataRow dr;
        private readonly PLayer _layer;
        private PImage planetImage;
        private bool dragging;

        public PlanetSprite(PLayer layer, DataRow r, IPropertyHost pg, IGridSyncSink grid)
        {
            _pg = pg;
            _grid = grid ?? new NullGridSyncSink();
            setupData(r);
            dr = r;
            _layer = layer;

            string name = r["name"].ToString();
            float x = float.Parse(r["position_x"].ToString());
            float y = -(float.Parse(r["position_y"].ToString()));
            float sigRadius = float.Parse(r["signature"].ToString());
            float rrRadius = float.Parse(r["radar_range"].ToString());
            float explorationRange = float.Parse(r["exploration_range"].ToString());
            appearsInRadar = (bool) r["appears_in_radar"];
            int navType = int.Parse(r["nav_type"].ToString());
            bool isLandable;
            try { isLandable = bool.Parse(r["is_landable"].ToString()); }
            catch (Exception) { isLandable = false; }

            float sigDia = (sigRadius * 2) / 100;
            float rrDia = (rrRadius * 2) / 100;
            float expDia = (explorationRange * 2) / 100;

            // 5px-minimum diameter guard (matches MobSprite/original).
            if (sigDia == 0) sigDia = 5;
            if (rrDia == 0) rrDia = 5;
            if (expDia == 0) expDia = 5;

            // Original used planet.gif regardless of appearsInRadar (both
            // branches resolved to the same file); preserve that. The
            // landable override wins over both.
            string imageName = isLandable ? "planetLandable.gif" : "planet.gif";

            string imagePath = Mob.ResolveImage(imageName);
            planetImage = new PImage(imagePath);
            planetImage.X = (x - (planetImage.Width / 2)) / 100;
            planetImage.Y = (y - (planetImage.Height / 2)) / 100;

            float sigX = (x / 100) - ((sigDia / 2) - (planetImage.Width / 2));
            float sigY = (y / 100) - ((sigDia / 2) - (planetImage.Height / 2));
            float rrX  = (x / 100) - ((rrDia / 2)  - (planetImage.Width / 2));
            float rrY  = (y / 100) - ((rrDia / 2)  - (planetImage.Height / 2));
            float expX = (x / 100) - ((expDia / 2) - (planetImage.Width / 2));
            float expY = (y / 100) - ((expDia / 2) - (planetImage.Height / 2));

            var sigPen = new Pen(Color.Blue,      3.0F);
            var rrPen  = new Pen(Color.SkyBlue,   2.0F) { DashStyle = DashStyle.Dash };
            var expPen = new Pen(Color.LightBlue, 1.0F) { DashStyle = DashStyle.DashDotDot };

            var sigCircle = PPath.CreateEllipse(sigX, sigY, sigDia, sigDia);
            sigCircle.Pen = sigPen;
            sigCircle.Brush = Brushes.Transparent;

            var rrCircle = PPath.CreateEllipse(rrX, rrY, rrDia, rrDia);
            rrCircle.Pen = rrPen;
            rrCircle.Brush = Brushes.Transparent;

            var expCircle = PPath.CreateEllipse(expX, expY, expDia, expDia);
            expCircle.Pen = expPen;
            expCircle.Brush = Brushes.Transparent;

            pname = new PText(name);
            pname.TextBrush = Brushes.White;
            pname.TextAlignment = StringAlignment.Center;
            pname.X = (x / 100) - (pname.Width / 2);
            pname.Y = (y / 100) - 20;

            // Child order is indexed positionally in updateChangedInfo:
            //   0 = sig, 1 = rr, 2 = exp, 3 = pname,
            //   then `navType` placeholder PNodes.
            planetImage.AddChild(sigCircle);
            planetImage.AddChild(rrCircle);
            planetImage.AddChild(expCircle);
            planetImage.AddChild(pname);
            for (int i = 0; i < navType; i++)
                planetImage.AddChild(new PNode());

            planetImage.ChildrenPickable = false;
            planetImage.Tag = this;

            planetImage.MouseDown += Image_MouseDown;
            planetImage.MouseUp   += Image_MouseUp;
            planetImage.MouseDrag += Image_MouseDrag;

            layer.AddChild(planetImage);
        }

        private void setupData(DataRow r)
        {
            int objectType = int.Parse(r["type"].ToString());
            string oType = objectType switch
            {
                0  => "Mobs",
                3  => "Planets",
                11 => "Stargates",
                12 => "Starbases",
                37 => "Decorations",
                38 => "Harvestables",
                _  => "",
            };

            dp = new PlanetProps();
            dp.SectorID = int.Parse(r["sector_id"].ToString());
            dp.NavType = r["nav_type"].ToString();
            dp.Signature = float.Parse(r["signature"].ToString());
            dp.IsHuge = (bool) r["is_huge"];
            dp.BaseXP = int.Parse(r["base_xp"].ToString());
            dp.ExplorationRange = float.Parse(r["exploration_range"].ToString());
            dp.BaseAssetID = int.Parse(r["base_asset_id"].ToString());

            var hslColor = new HslConvert.HSL
            {
                H = float.Parse(r["h"].ToString()),
                S = float.Parse(r["s"].ToString()),
                L = float.Parse(r["v"].ToString()),
            };
            dp.Color = HslConvert.HslToRgb(hslColor);

            dp.Type = oType;
            dp.Scale = float.Parse(r["scale"].ToString());
            dp.PositionX = float.Parse(r["position_x"].ToString());
            dp.PositionY = float.Parse(r["position_y"].ToString());
            dp.PositionZ = float.Parse(r["position_z"].ToString());

            var quat1 = new double[4];
            quat1[0] = double.Parse(r["orientation_z"].ToString());
            quat1[1] = double.Parse(r["orientation_u"].ToString());
            quat1[2] = double.Parse(r["orientation_v"].ToString());
            quat1[3] = double.Parse(r["orientation_w"].ToString());

            var qc1 = new N7.Utilities.QuaternionCalc();
            double[] ang1 = qc1.QuatToAngle(quat1);
            // Same dead-code NaN guard as MobSprite — original used `==
            // double.NaN` which is always false. Replace with IsNaN so
            // the guard actually fires.
            if (double.IsNaN(ang1[0])) ang1[0] = 0;
            if (double.IsNaN(ang1[1])) ang1[1] = 0;
            if (double.IsNaN(ang1[2])) ang1[2] = 0;
            dp.Orientation_Yaw   = Math.Round(ang1[0], 0);
            dp.Orientation_Pitch = Math.Round(ang1[1], 0);
            dp.Orientation_Roll  = Math.Round(ang1[2], 0);

            dp.Name = r["name"].ToString();
            dp.AppearsInRadar = (bool) r["appears_in_radar"];
            dp.RadarRange = float.Parse(r["radar_range"].ToString());
            dp.Destination = int.Parse(r["gate_to"].ToString());
            dp.SoundEffect = int.Parse(r["sound_effect_id"].ToString());
            dp.SoundEffectRange = float.Parse(r["sound_effect_range"].ToString());

            try
            {
                dp.OrbitID = int.Parse(r["orbit_id"].ToString());
                dp.OrbitDist = float.Parse(r["orbit_dist"].ToString());
                dp.OrbitAngle = float.Parse(r["orbit_angle"].ToString());
                dp.OrbitRate = float.Parse(r["orbit_rate"].ToString());
                dp.RotateAngle = float.Parse(r["rotate_angle"].ToString());
                dp.RotateRate = float.Parse(r["rotate_rate"].ToString());
                dp.TiltAngle = float.Parse(r["tilt_angle"].ToString());
                dp.IsLandable = bool.Parse(r["is_landable"].ToString());
            }
            catch (Exception)
            {
                dp.OrbitID = 0;
                dp.OrbitDist = 0;
                dp.OrbitAngle = 0;
                dp.OrbitRate = 0;
                dp.RotateAngle = 0;
                dp.RotateRate = 0;
                dp.TiltAngle = 0;
                dp.IsLandable = false;
            }
        }

        protected void Image_MouseDown(object sender, PInputEventArgs e)
        {
            _pg.SelectedObject = dp;
        }

        protected void Image_MouseDrag(object sender, PInputEventArgs e)
        {
            dragging = true;
        }

        protected void Image_MouseUp(object sender, PInputEventArgs e)
        {
            if (dragging)
            {
                dp.PositionX = e.Position.X * 100;
                dp.PositionY = -(e.Position.Y * 100);
                dr["position_x"] = e.Position.X * 100;
                dr["position_y"] = -(e.Position.Y * 100);
                dragging = false;
            }
            _pg.SelectedObject = dp;
            EditorGlobals.SelectedObjectId = int.Parse(dr["sector_object_id"].ToString());
        }

        public bool getAppearsInRader() => appearsInRadar;
        public PText getText() => pname;
        public DataRow getRow() => dr;
        public void setPropGrid() { _pg.SelectedObject = dp; }

        public void updateChangedInfo(string propertyName, object _changedValue)
        {
            string changedValue = _changedValue?.ToString() ?? "";

            switch (propertyName)
            {
                case "SectorID":
                    dr["sector_id"] = int.Parse(changedValue); break;
                case "NavType":
                    dr["nav_type"] = changedValue; break;
                case "Signature":
                    dr["signature"] = float.Parse(changedValue);
                    ResizeCircleChild(0, float.Parse(changedValue));
                    break;
                case "IsHuge":
                    dr["is_huge"] = bool.Parse(changedValue); break;
                case "BaseXP":
                    dr["base_xp"] = int.Parse(changedValue); break;
                case "ExplorationRange":
                    dr["exploration_range"] = float.Parse(changedValue);
                    ResizeCircleChild(2, float.Parse(changedValue));
                    break;
                case "BaseAssetID":
                    dr["base_asset_id"] = int.Parse(changedValue);
                    _grid.OnCellChanged("base_asset_id", int.Parse(changedValue));
                    break;
                case "Color":
                    if (_changedValue is Color color)
                    {
                        var hsv = HslConvert.RgbToHsl(color);
                        dr["h"] = hsv.H;
                        dr["s"] = hsv.S;
                        dr["v"] = hsv.L;
                    }
                    break;
                case "Type":
                    // Original commented out the cross-type rebuild; we
                    // preserve that behaviour.
                    _layer.RemoveChild(planetImage);
                    _pg.SelectedObject = null;
                    break;
                case "Scale":
                    dr["scale"] = float.Parse(changedValue); break;
                case "PositionX":
                {
                    dr["position_x"] = float.Parse(changedValue);
                    float dx = (float.Parse(changedValue) / 100) - planetImage.X;
                    planetImage.TranslateBy(dx, 0);
                    break;
                }
                case "PositionY":
                {
                    dr["position_y"] = float.Parse(changedValue);
                    float dy = (float.Parse(changedValue) / 100) - planetImage.Y;
                    planetImage.TranslateBy(0, dy);
                    break;
                }
                case "PositionZ":
                    dr["position_z"] = float.Parse(changedValue); break;
                case "Orientation_Yaw":
                case "Orientation_Pitch":
                case "Orientation_Roll":
                {
                    var qtmp = new N7.Utilities.QuaternionCalc();
                    double[] q1 = qtmp.AngleToQuat(dp.Orientation_Yaw, dp.Orientation_Pitch, dp.Orientation_Roll);
                    dr["orientation_z"] = q1[0];
                    dr["orientation_u"] = q1[1];
                    dr["orientation_v"] = q1[2];
                    dr["orientation_w"] = q1[3];
                    break;
                }
                case "Name":
                {
                    dr["name"] = changedValue;
                    float x = planetImage.X;
                    float y = planetImage.Y;
                    var nameNode = (PText) planetImage.GetChild(3);
                    nameNode.Text = changedValue;
                    nameNode.TextAlignment = StringAlignment.Center;
                    nameNode.X = x - (nameNode.Width / 2);
                    nameNode.Y = y - 20;
                    _grid.OnCellChanged("name", changedValue);
                    break;
                }
                case "AppearsInRadar":
                    dr["appears_in_radar"] = bool.Parse(changedValue);
                    changeImage(bool.Parse(changedValue) ? 1 : 0);
                    break;
                case "RadarRange":
                    dr["radar_range"] = float.Parse(changedValue);
                    ResizeCircleChild(1, float.Parse(changedValue));
                    break;
                case "Destination":
                    dr["gate_to"] = int.Parse(changedValue); break;
                case "OrbitID":
                    dr["orbit_id"] = int.Parse(changedValue); break;
                case "OrbitDist":
                    // Original cast OrbitDist via int.Parse despite the
                    // schema column being float; preserve that quirk so
                    // round-trip with the original tool stays
                    // byte-identical. (Trims fractional input.)
                    dr["orbit_dist"] = int.Parse(changedValue); break;
                case "OrbitAngle":
                    dr["orbit_angle"] = float.Parse(changedValue); break;
                case "OrbitRate":
                    dr["orbit_rate"] = float.Parse(changedValue); break;
                case "RotateRate":
                    dr["rotate_rate"] = float.Parse(changedValue); break;
                case "RotateAngle":
                    dr["rotate_angle"] = float.Parse(changedValue); break;
                case "TiltAngle":
                    dr["tilt_angle"] = float.Parse(changedValue); break;
                case "IsLandable":
                {
                    dr["is_landable"] = bool.Parse(changedValue);
                    if (bool.Parse(changedValue))
                    {
                        changeImage(2);
                    }
                    else
                    {
                        bool inRadar = bool.Parse(dr["appears_in_radar"].ToString());
                        changeImage(inRadar ? 1 : 0);
                    }
                    break;
                }
                case "SoundEffect":
                    dr["sound_effect_id"] = int.Parse(changedValue); break;
                case "SoundEffectRange":
                    dr["sound_effect_range"] = float.Parse(changedValue); break;
            }

            if (dr.RowState != DataRowState.Modified)
                dr.SetModified();
        }

        private void ResizeCircleChild(int childIndex, float radius)
        {
            float imageWidth = planetImage.Width;
            float imageHeight = planetImage.Height;
            float x = planetImage.X;
            float y = planetImage.Y;
            var c = planetImage.GetChild(childIndex);
            c.X = (x + (imageWidth / 2)) - (radius / 100);
            c.Y = (y + (imageHeight / 2)) - (radius / 100);
            c.Width = (radius * 2) / 100;
            c.Height = (radius * 2) / 100;
        }

        private void changeImage(int type)
        {
            string imageName = type switch
            {
                0 => "planet.gif",
                1 => "planet.gif",
                2 => "planetLandable.gif",
                _ => null,
            };
            if (imageName == null) return;
            if (type == 0) appearsInRadar = false;
            else if (type == 1) appearsInRadar = true;
            // type==2 (IsLandable) leaves appearsInRadar untouched, same
            // as the original.

            string p = Mob.ResolveImage(imageName);
            float x = planetImage.X;
            float y = planetImage.Y;
            if (System.IO.File.Exists(p))
            {
                planetImage.Bitmap = new Bitmap(p);
                planetImage.X = x;
                planetImage.Y = y;
            }
        }
    }
}
