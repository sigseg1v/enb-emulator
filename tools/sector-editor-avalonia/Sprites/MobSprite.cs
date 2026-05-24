// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// Ported from N7.Sprites.MobSprite under Net-7 Entertainment's
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
    // Renders one mob spawn (`sector_objects` row of `type=0`) at sector-
    // local scale: image + signature/radar/exploration/spawn circles +
    // floating name label. Click → push MobProps into IPropertyHost.
    // Drag → update DataRow position + push back into MobProps.
    public class MobSprite
    {
        private readonly IPropertyHost _pg;
        private readonly IGridSyncSink _grid;
        private MobProps dp;
        private bool appearsInRadar;
        private PText pname;
        private DataRow dr;
        private readonly PLayer _layer;
        private PImage mobImage;
        private bool dragging;

        public MobSprite(PLayer layer, DataRow r, IPropertyHost pg, IGridSyncSink grid)
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
            float spawnRadius = float.Parse(r["mob_spawn_radius"].ToString());

            float sigDia = (sigRadius * 2) / 100;
            float rrDia = (rrRadius * 2) / 100;
            float expDia = (explorationRange * 2) / 100;
            float spawnDia = (spawnRadius * 2) / 100;

            // Original code forced a 5px minimum diameter so zero-radius
            // mobs still had a clickable indicator. Preserve.
            if (sigDia == 0) sigDia = 5;
            if (rrDia == 0) rrDia = 5;
            if (expDia == 0) expDia = 5;
            if (spawnDia == 0) spawnDia = 5;

            string imagePath = Mob.ResolveImage("hostileMob.gif");
            mobImage = new PImage(imagePath);
            mobImage.X = (x - (mobImage.Width / 2)) / 100;
            mobImage.Y = (y - (mobImage.Height / 2)) / 100;

            float sigX = (x / 100) - ((sigDia / 2) - (mobImage.Width / 2));
            float sigY = (y / 100) - ((sigDia / 2) - (mobImage.Height / 2));
            float rrX  = (x / 100) - ((rrDia / 2)  - (mobImage.Width / 2));
            float rrY  = (y / 100) - ((rrDia / 2)  - (mobImage.Height / 2));
            float expX = (x / 100) - ((expDia / 2) - (mobImage.Width / 2));
            float expY = (y / 100) - ((expDia / 2) - (mobImage.Height / 2));
            float spawnX = (x / 100) - ((spawnDia / 2) - (mobImage.Width / 2));
            float spawnY = (y / 100) - ((spawnDia / 2) - (mobImage.Height / 2));

            var sigPen   = new Pen(Color.Red,      3.0F);
            var rrPen    = new Pen(Color.MistyRose, 2.0F) { DashStyle = DashStyle.Dash };
            var expPen   = new Pen(Color.Maroon,    1.0F) { DashStyle = DashStyle.DashDotDot };
            var spawnPen = new Pen(Color.Fuchsia,   1.0F) { DashStyle = DashStyle.Dot };

            var sigCircle = PPath.CreateEllipse(sigX, sigY, sigDia, sigDia);
            sigCircle.Pen = sigPen;
            sigCircle.Brush = Brushes.Transparent;

            var rrCircle = PPath.CreateEllipse(rrX, rrY, rrDia, rrDia);
            rrCircle.Pen = rrPen;
            rrCircle.Brush = Brushes.Transparent;

            var expCircle = PPath.CreateEllipse(expX, expY, expDia, expDia);
            expCircle.Pen = expPen;
            expCircle.Brush = Brushes.Transparent;

            var spawnCircle = PPath.CreateEllipse(spawnX, spawnY, spawnDia, spawnDia);
            spawnCircle.Pen = spawnPen;
            spawnCircle.Brush = Brushes.Transparent;

            pname = new PText(name);
            pname.TextBrush = Brushes.White;
            pname.TextAlignment = StringAlignment.Center;
            pname.X = (x / 100) - (pname.Width / 2);
            pname.Y = (y / 100) - 20;

            // Child order matters — updateChangedInfo indexes by position:
            //   0 = sig, 1 = rr, 2 = exp, 3 = pname,
            //   then `navType` placeholder PNodes (nav-type visualization
            //   hook the original tool left empty), then spawn at the end.
            mobImage.AddChild(sigCircle);
            mobImage.AddChild(rrCircle);
            mobImage.AddChild(expCircle);
            mobImage.AddChild(pname);
            for (int i = 0; i < navType; i++)
                mobImage.AddChild(new PNode());
            mobImage.AddChild(spawnCircle);
            mobImage.ChildrenPickable = false;
            mobImage.Tag = this;

            mobImage.MouseDown += Image_MouseDown;
            mobImage.MouseUp   += Image_MouseUp;
            mobImage.MouseDrag += Image_MouseDrag;

            layer.AddChild(mobImage);
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

            dp = new MobProps();
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
            // Original code did `if (ang1[0] == double.NaN)` which is always
            // false (NaN != anything incl. itself). Use double.IsNaN to
            // actually catch the case — the original intent was "guard
            // against gimbal-lock NaN", and the broken check meant the
            // guard never fired in practice. This is a *bug fix in
            // behaviour*, not a port divergence — leaving the broken
            // check would only re-introduce the gimbal-lock corruption.
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

            dp.SpawnGroup = "<Collection...>";
            dp.Count = int.Parse(r["mob_count"].ToString());
            dp.SpawnRadius = float.Parse(r["mob_spawn_radius"].ToString());
            dp.RespawnTime = float.Parse(r["respawn_time"].ToString());
            dp.DelayedSpawn = (bool) r["delayed_spawn"];
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
                    // Original commented-out the type-switch entirely; we
                    // preserve that behaviour. Just drop the sprite and
                    // null out the property panel — the user can re-pick
                    // an object to re-populate.
                    _layer.RemoveChild(mobImage);
                    _pg.SelectedObject = null;
                    break;
                case "Scale":
                    dr["scale"] = float.Parse(changedValue); break;
                case "PositionX":
                {
                    dr["position_x"] = float.Parse(changedValue);
                    float dx = (float.Parse(changedValue) / 100) - mobImage.X;
                    mobImage.TranslateBy(dx, 0);
                    break;
                }
                case "PositionY":
                {
                    dr["position_y"] = float.Parse(changedValue);
                    float dy = (float.Parse(changedValue) / 100) - mobImage.Y;
                    mobImage.TranslateBy(0, dy);
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
                    float x = mobImage.X;
                    float y = mobImage.Y;
                    var nameNode = (PText) mobImage.GetChild(3);
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
                case "SpawnRadius":
                {
                    dr["mob_spawn_radius"] = float.Parse(changedValue);
                    int navType = int.Parse(dr["nav_type"].ToString());
                    // Spawn-circle child index = 4 base nodes + navType
                    // placeholders. Original used `(3 + navType) + 1`
                    // which is the same arithmetic.
                    int nodeCount = (3 + navType) + 1;
                    ResizeCircleChild(nodeCount, float.Parse(changedValue));
                    break;
                }
                case "Count":
                    dr["mob_count"] = int.Parse(changedValue); break;
                case "SoundEffect":
                    dr["sound_effect_id"] = int.Parse(changedValue); break;
                case "SoundEffectRange":
                    dr["sound_effect_range"] = float.Parse(changedValue); break;
                case "RespawnTime":
                    dr["respawn_time"] = int.Parse(changedValue); break;
                case "DelayedSpawn":
                    dr["delayed_spawn"] = bool.Parse(changedValue); break;
            }

            if (dr.RowState != DataRowState.Modified)
                dr.SetModified();
        }

        private void ResizeCircleChild(int childIndex, float radius)
        {
            float imageWidth = mobImage.Width;
            float imageHeight = mobImage.Height;
            float x = mobImage.X;
            float y = mobImage.Y;
            var c = mobImage.GetChild(childIndex);
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
            float x = mobImage.X;
            float y = mobImage.Y;
            if (System.IO.File.Exists(p))
            {
                mobImage.Bitmap = new Bitmap(p);
                mobImage.X = x;
                mobImage.Y = y;
            }
        }
    }
}
