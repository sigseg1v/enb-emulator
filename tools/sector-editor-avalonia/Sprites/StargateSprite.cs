// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// Ported from N7.Sprites.StargateSprite under Net-7 Entertainment's
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
    // Renders one stargate (`sector_objects` row of `type=11`) at sector-
    // local scale: image + signature/radar/exploration circles + floating
    // name. Image swaps among:
    //   standardGate.gif      - in radar, no class/faction restriction
    //   hiddenGate.gif        - not in radar, no class/faction restriction
    //   classSpecificGate.gif - class-restricted (regardless of radar)
    //   FactionSpecificGate.gif - faction-restricted (wins over class)
    public class StargateSprite
    {
        private readonly IPropertyHost _pg;
        private readonly IGridSyncSink _grid;
        private StargateProps dp;
        private bool appearsInRadar;
        private PText pname;
        private DataRow dr;
        private readonly PLayer _layer;
        private PImage stargateImage;
        private bool dragging;

        public StargateSprite(PLayer layer, DataRow r, IPropertyHost pg, IGridSyncSink grid)
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
            bool isClassSpecific = (bool) r["classSpecific"];
            int factionID = int.Parse(r["faction_id"].ToString());

            float sigDia = (sigRadius * 2) / 100;
            float rrDia = (rrRadius * 2) / 100;
            float expDia = (explorationRange * 2) / 100;

            if (sigDia == 0) sigDia = 5;
            if (rrDia == 0) rrDia = 5;
            if (expDia == 0) expDia = 5;

            // Image-name priority matches original: radar → classSpecific
            // overrides → factionID overrides everything.
            string imageName = appearsInRadar ? "standardGate.gif" : "hiddenGate.gif";
            if (isClassSpecific) imageName = "classSpecificGate.gif";
            if (factionID != -1) imageName = "FactionSpecificGate.gif";

            string imagePath = Mob.ResolveImage(imageName);
            stargateImage = new PImage(imagePath);
            stargateImage.X = (x - (stargateImage.Width / 2)) / 100;
            stargateImage.Y = (y - (stargateImage.Height / 2)) / 100;

            float sigX = (x / 100) - ((sigDia / 2) - (stargateImage.Width / 2));
            float sigY = (y / 100) - ((sigDia / 2) - (stargateImage.Height / 2));
            float rrX  = (x / 100) - ((rrDia / 2)  - (stargateImage.Width / 2));
            float rrY  = (y / 100) - ((rrDia / 2)  - (stargateImage.Height / 2));
            float expX = (x / 100) - ((expDia / 2) - (stargateImage.Width / 2));
            float expY = (y / 100) - ((expDia / 2) - (stargateImage.Height / 2));

            var sigPen = new Pen(Color.ForestGreen, 3.0F);
            var rrPen  = new Pen(Color.GreenYellow, 2.0F) { DashStyle = DashStyle.Dash };
            var expPen = new Pen(Color.LightGreen,  1.0F) { DashStyle = DashStyle.DashDotDot };

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

            // Child indexes: 0=sig, 1=rr, 2=exp, 3=pname, then navType
            // placeholder PNodes.
            stargateImage.AddChild(sigCircle);
            stargateImage.AddChild(rrCircle);
            stargateImage.AddChild(expCircle);
            stargateImage.AddChild(pname);
            for (int i = 0; i < navType; i++)
                stargateImage.AddChild(new PNode());

            stargateImage.ChildrenPickable = false;
            stargateImage.Tag = this;

            stargateImage.MouseDown += Image_MouseDown;
            stargateImage.MouseUp   += Image_MouseUp;
            stargateImage.MouseDrag += Image_MouseDrag;

            layer.AddChild(stargateImage);
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

            dp = new StargateProps();
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
            // == double.NaN dead-code guard fix; see MobSprite.cs.
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

            dp.IsClassSpecific = (bool) r["classSpecific"];
            // mainFrm.factions → EditorGlobals.Factions abstraction.
            int factionId = int.Parse(r["faction_id"].ToString());
            dp.FactionID = EditorGlobals.Factions.FindNameById(factionId);
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
                    _layer.RemoveChild(stargateImage);
                    _pg.SelectedObject = null;
                    break;
                case "Scale":
                    dr["scale"] = float.Parse(changedValue); break;
                case "PositionX":
                {
                    dr["position_x"] = float.Parse(changedValue);
                    float dx = (float.Parse(changedValue) / 100) - stargateImage.X;
                    stargateImage.TranslateBy(dx, 0);
                    break;
                }
                case "PositionY":
                {
                    dr["position_y"] = float.Parse(changedValue);
                    float dy = (float.Parse(changedValue) / 100) - stargateImage.Y;
                    stargateImage.TranslateBy(0, dy);
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
                    float x = stargateImage.X;
                    float y = stargateImage.Y;
                    var nameNode = (PText) stargateImage.GetChild(3);
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
                case "IsClassSpecific":
                {
                    dr["classSpecific"] = bool.Parse(changedValue);
                    if (bool.Parse(changedValue))
                    {
                        changeImage(2);
                    }
                    else
                    {
                        bool inRadar = bool.Parse(dr["appears_in_radar"].ToString());
                        changeImage(inRadar ? 1 : 0);
                        if (int.Parse(dr["faction_id"].ToString()) > 0)
                            changeImage(3);
                    }
                    break;
                }
                case "FactionID":
                {
                    int id = EditorGlobals.Factions.FindIdByName(changedValue);
                    dr["faction_id"] = id;
                    // Dropped a `Console.Out.WriteLine("test2")` debug
                    // leftover from the original.
                    if (id > 0)
                    {
                        changeImage(3);
                    }
                    else
                    {
                        bool inRadar = bool.Parse(dr["appears_in_radar"].ToString());
                        changeImage(inRadar ? 1 : 0);
                        if (bool.Parse(dr["classSpecific"].ToString()))
                            changeImage(2);
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
            float imageWidth = stargateImage.Width;
            float imageHeight = stargateImage.Height;
            float x = stargateImage.X;
            float y = stargateImage.Y;
            var c = stargateImage.GetChild(childIndex);
            c.X = (x + (imageWidth / 2)) - (radius / 100);
            c.Y = (y + (imageHeight / 2)) - (radius / 100);
            c.Width = (radius * 2) / 100;
            c.Height = (radius * 2) / 100;
        }

        private void changeImage(int type)
        {
            string imageName = type switch
            {
                0 => "hiddenGate.gif",
                1 => "standardGate.gif",
                2 => "classSpecificGate.gif",
                3 => "FactionSpecificGate.gif",
                _ => null,
            };
            if (imageName == null) return;
            if (type == 0) appearsInRadar = false;
            else if (type == 1) appearsInRadar = true;

            string p = Mob.ResolveImage(imageName);
            float x = stargateImage.X;
            float y = stargateImage.Y;
            if (System.IO.File.Exists(p))
            {
                stargateImage.Bitmap = new Bitmap(p);
                stargateImage.X = x;
                stargateImage.Y = y;
            }
        }
    }
}
