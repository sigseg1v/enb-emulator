// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// Ported from N7.Sprites.DecorationSprite under Net-7 Entertainment's
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
    // Renders one decoration (`sector_objects` row of `type=37`): image
    // + signature/radar/exploration circles + name. Uses BaseProps
    // directly (no decoration-specific properties).
    public class DecorationSprite
    {
        private readonly IPropertyHost _pg;
        private readonly IGridSyncSink _grid;
        private BaseProps dp;
        public bool appearsInRadar;
        private PText pname;
        private DataRow dr;
        private readonly PLayer _layer;
        private PImage decorationImage;
        private bool dragging;

        public DecorationSprite(PLayer layer, DataRow r, IPropertyHost pg, IGridSyncSink grid)
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

            float sigDia = (sigRadius * 2) / 100;
            float rrDia = (rrRadius * 2) / 100;
            float expDia = (explorationRange * 2) / 100;

            if (sigDia == 0) sigDia = 5;
            if (rrDia == 0) rrDia = 5;
            if (expDia == 0) expDia = 5;

            string imageName = appearsInRadar ? "standardNav.gif" : "hiddenNav.gif";
            string imagePath = Mob.ResolveImage(imageName);
            decorationImage = new PImage(imagePath);
            decorationImage.X = (x - (decorationImage.Width / 2)) / 100;
            decorationImage.Y = (y - (decorationImage.Height / 2)) / 100;

            float sigX = (x / 100) - ((sigDia / 2) - (decorationImage.Width / 2));
            float sigY = (y / 100) - ((sigDia / 2) - (decorationImage.Height / 2));
            float rrX  = (x / 100) - ((rrDia / 2)  - (decorationImage.Width / 2));
            float rrY  = (y / 100) - ((rrDia / 2)  - (decorationImage.Height / 2));
            float expX = (x / 100) - ((expDia / 2) - (decorationImage.Width / 2));
            float expY = (y / 100) - ((expDia / 2) - (decorationImage.Height / 2));

            var sigPen = new Pen(Color.DarkSlateGray, 3.0F);
            var rrPen  = new Pen(Color.Gray,          2.0F) { DashStyle = DashStyle.Dash };
            var expPen = new Pen(Color.LightGray,     1.0F) { DashStyle = DashStyle.DashDotDot };

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

            decorationImage.AddChild(sigCircle);
            decorationImage.AddChild(rrCircle);
            decorationImage.AddChild(expCircle);
            decorationImage.AddChild(pname);
            for (int i = 0; i < navType; i++)
                decorationImage.AddChild(new PNode());

            decorationImage.ChildrenPickable = false;
            decorationImage.Tag = this;

            decorationImage.MouseDown += Image_MouseDown;
            decorationImage.MouseUp   += Image_MouseUp;
            decorationImage.MouseDrag += Image_MouseDrag;

            layer.AddChild(decorationImage);
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

            dp = new BaseProps();
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
                    _layer.RemoveChild(decorationImage);
                    _pg.SelectedObject = null;
                    break;
                case "Scale":
                    dr["scale"] = float.Parse(changedValue); break;
                case "PositionX":
                {
                    dr["position_x"] = float.Parse(changedValue);
                    float dx = (float.Parse(changedValue) / 100) - decorationImage.X;
                    decorationImage.TranslateBy(dx, 0);
                    break;
                }
                case "PositionY":
                {
                    dr["position_y"] = float.Parse(changedValue);
                    float dy = (float.Parse(changedValue) / 100) - decorationImage.Y;
                    decorationImage.TranslateBy(0, dy);
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
                    float x = decorationImage.X;
                    float y = decorationImage.Y;
                    var nameNode = (PText) decorationImage.GetChild(3);
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
            float imageWidth = decorationImage.Width;
            float imageHeight = decorationImage.Height;
            float x = decorationImage.X;
            float y = decorationImage.Y;
            var c = decorationImage.GetChild(childIndex);
            c.X = (x + (imageWidth / 2)) - (radius / 100);
            c.Y = (y + (imageHeight / 2)) - (radius / 100);
            c.Width = (radius * 2) / 100;
            c.Height = (radius * 2) / 100;
        }

        private void changeImage(int type)
        {
            string imageName = type switch
            {
                0 => "hiddenNav.gif",
                1 => "standardNav.gif",
                _ => null,
            };
            if (imageName == null) return;
            appearsInRadar = type == 1;

            string p = Mob.ResolveImage(imageName);
            float x = decorationImage.X;
            float y = decorationImage.Y;
            if (System.IO.File.Exists(p))
            {
                decorationImage.Bitmap = new Bitmap(p);
                decorationImage.X = x;
                decorationImage.Y = y;
            }
        }
    }
}
