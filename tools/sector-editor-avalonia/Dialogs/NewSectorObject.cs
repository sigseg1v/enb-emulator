// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// Ported from N7.GUI.NewSectorObject under Net-7 Entertainment's CC BY-NC-SA 3.0;
// preservation modifications inherit under ShareAlike.
// License: LICENSES/enb-emulator

using System;
using System.Data;
using System.Drawing;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using N7;
using N7.Props;
using N7.Sql;
using N7.Utilities;
using SectorEditorAvalonia.Utilities;
using SectorEditorAvalonia.Windows;

namespace SectorEditorAvalonia.Dialogs
{
    // Six-typed-object creator (Mobs / Planets / Stargates / Starbases /
    // Decorations / Harvestables). The original picked the right props
    // class by name, drove the WinForms PropertyGrid for editing, then
    // on OK built a sector_objects DataRow with type-specific columns,
    // newRow'd it, and addNewObject'd it back into the canvas. The
    // Avalonia port preserves that flow verbatim — the 6 branches still
    // populate the same columns the original did (including the
    // intentional `position_y` Y-flip in setPosition) — but the common
    // BaseProps columns are written by FillCommonFromBase() instead of
    // being copy-pasted across all 6 arms.
    //
    // Implements INewSectorObjectDialog so SectorWindow can drive the
    // "click to place" position-pick flow.
    public class NewSectorObjectDialog : Window, INewSectorObjectDialog
    {
        private readonly string _type;
        private readonly int _type2;
        private readonly SectorsSql _sectorsSql;
        private readonly SectorObjectsSql _sectorObjectsSql;
        private readonly IPropertyHost _pg;
        private readonly INotificationSink _notify;
        private readonly IFactionLookup _factions;
        private readonly SectorWindow _sw;
        private readonly Action<INewSectorObjectDialog> _requestPlace;

        private MobProps _mp;
        private PlanetProps _pp;
        private StargateProps _sgp;
        private StarbaseProps _sbp;
        private BaseProps _bp;
        private HarvestableProps _hp;

        private int _lastInsertId;
        private DataRow _newRow;

        public NewSectorObjectDialog(
            string typeName,
            string sectorName,
            SectorsSql sectorsSql,
            SectorObjectsSql sectorObjectsSql,
            IPropertyHost pgHost,
            INotificationSink notify,
            IFactionLookup factions,
            SectorWindow sw,
            Action<INewSectorObjectDialog> requestPlace)
        {
            _type = typeName;
            _sectorsSql = sectorsSql;
            _sectorObjectsSql = sectorObjectsSql;
            _pg = pgHost ?? new NullPropertyHost();
            _notify = notify ?? new NullNotificationSink();
            _factions = factions ?? EditorGlobals.Factions;
            _sw = sw;
            _requestPlace = requestPlace;

            EditorGlobals.SelectedObjectId = 0;

            int sectorId = _sectorsSql.getIDFromName(sectorName);

            Title = "New Sector Object";
            Width = 480;
            Height = 380;
            CanResize = false;

            // Original Load() picked the right type — preserve identical
            // string-match contract ("type.Contains(\"Mobs\")" etc.) so
            // NewSectorObjectType's labels still feed this dialog.
            if (_type.Contains("Mobs"))
            {
                _mp = NewBase<MobProps>(sectorId, "Mobs");
                _mp.Signature = 7000;
                _mp.RadarRange = 5000;
                _mp.ExplorationRange = 3000;
                _mp.SoundEffect = -1;
                _mp.SoundEffectRange = 0;
                _mp.SpawnGroup = "<Collection...>";
                _mp.SpawnRadius = 4000;
                _pg.SelectedObject = _mp;
                _type2 = 0;
            }
            else if (_type.Contains("Planets"))
            {
                _pp = NewBase<PlanetProps>(sectorId, "Planets");
                FillBaseDefaults(_pp);
                _pg.SelectedObject = _pp;
                _type2 = 3;
            }
            else if (_type.Contains("Stargates"))
            {
                _sgp = NewBase<StargateProps>(sectorId, "Stargates");
                FillBaseDefaults(_sgp);
                _pg.SelectedObject = _sgp;
                _type2 = 11;
            }
            else if (_type.Contains("Starbases"))
            {
                _sbp = NewBase<StarbaseProps>(sectorId, "Starbases");
                FillBaseDefaults(_sbp);
                _pg.SelectedObject = _sbp;
                _type2 = 12;
            }
            else if (_type.Contains("Decorations"))
            {
                _bp = NewBase<BaseProps>(sectorId, "Decorations");
                FillBaseDefaults(_bp);
                _pg.SelectedObject = _bp;
                _type2 = 37;
            }
            else if (_type.Contains("Harvestable"))
            {
                _hp = NewBase<HarvestableProps>(sectorId, "Harvestables");
                _hp.Signature = 7000;
                _hp.RadarRange = 5000;
                _hp.Field = "Random";
                _hp.ExplorationRange = 3000;
                _hp.SoundEffect = -1;
                _hp.SoundEffectRange = 0;
                _hp.SpawnGroup = "<Collection...>";
                _hp.MaxFieldRadius = 3000;
                _hp.MobSpawnRadius = 0;
                _hp.ResType = "<Collection...>";
                _pg.SelectedObject = _hp;
                _type2 = 38;
            }

            BuildContent();
        }

        private static T NewBase<T>(int sectorId, string typeLabel) where T : BaseProps, new()
        {
            var p = new T();
            p.SectorID = sectorId;
            p.Color = Color.Black;
            p.Type = typeLabel;
            p.Name = "<New Sector Object>";
            return p;
        }

        private static void FillBaseDefaults(BaseProps p)
        {
            p.Signature = 7000;
            p.RadarRange = 5000;
            p.ExplorationRange = 3000;
            p.SoundEffect = -1;
            p.SoundEffectRange = 0;
        }

        private void BuildContent()
        {
            var info = new TextBlock
            {
                Text = "Editing " + _type + " — pushed to property panel.\n"
                       + "Press OK to commit, Cancel to discard, Place to drop into the sector.",
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Margin = new Thickness(12),
            };

            var ok = new Button { Content = "OK", Width = 80 };
            ok.Click += (_, _) => Commit();
            var place = new Button { Content = "Place", Width = 80 };
            place.Click += (_, _) =>
            {
                Hide();
                _requestPlace?.Invoke(this);
            };
            var cancel = new Button { Content = "Cancel", Width = 80 };
            cancel.Click += (_, _) => Close();

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Margin = new Thickness(12),
            };
            buttons.Children.Add(place);
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);

            var dock = new DockPanel();
            DockPanel.SetDock(buttons, Dock.Bottom);
            dock.Children.Add(buttons);
            dock.Children.Add(info);
            Content = dock;
        }

        // Common across all 6 arms — sector_id/nav_type/signature/is_huge/
        // base_xp/exploration_range/base_asset_id/h/s/v/scale/position_x/
        // position_y/position_z/orientation_z/u/v/w/name/appears_in_radar/
        // radar_range/gate_to/sound_effect_id/sound_effect_range.
        private static void FillCommonFromBase(DataRow r, BaseProps b, int type, QuaternionCalc q)
        {
            r["sector_id"] = b.SectorID;
            r["nav_type"] = b.NavType;
            r["signature"] = b.Signature;
            r["is_huge"] = b.IsHuge;
            r["base_xp"] = b.BaseXP;
            r["exploration_range"] = b.ExplorationRange;
            r["base_asset_id"] = b.BaseAssetID;
            r["h"] = b.Color.GetHue();
            r["s"] = b.Color.GetSaturation();
            r["v"] = b.Color.GetBrightness();
            r["type"] = type;
            r["scale"] = b.Scale;
            r["position_x"] = b.PositionX;
            r["position_y"] = b.PositionY;
            r["position_z"] = b.PositionZ;

            double[] qd = q.AngleToQuat(b.Orientation_Yaw, b.Orientation_Pitch, b.Orientation_Roll);
            r["orientation_z"] = qd[0];
            r["orientation_u"] = qd[1];
            r["orientation_v"] = qd[2];
            r["orientation_w"] = qd[3];

            r["name"] = b.Name.Replace("'", "''");
            r["appears_in_radar"] = b.AppearsInRadar;
            r["radar_range"] = b.RadarRange;
            r["gate_to"] = b.Destination;
            r["sound_effect_id"] = b.SoundEffect;
            r["sound_effect_range"] = b.SoundEffectRange;
        }

        private void Commit()
        {
            if (_lastInsertId == 0)
            {
                DataTable tmp = _sectorObjectsSql.getSectorObject();
                _newRow = tmp.NewRow();
                var q = new QuaternionCalc();

                switch (_type2)
                {
                    case 0:
                        FillCommonFromBase(_newRow, _mp, 0, q);
                        _newRow["mob_spawn_radius"] = _mp.SpawnRadius;
                        _newRow["mob_count"] = _mp.Count;
                        _newRow["respawn_time"] = _mp.RespawnTime;
                        _newRow["delayed_spawn"] = _mp.DelayedSpawn;
                        break;
                    case 3:
                        FillCommonFromBase(_newRow, _pp, 3, q);
                        _newRow["orbit_id"] = _pp.OrbitID;
                        _newRow["orbit_dist"] = _pp.OrbitDist;
                        _newRow["orbit_angle"] = _pp.OrbitAngle;
                        _newRow["orbit_rate"] = _pp.OrbitRate;
                        _newRow["rotate_rate"] = _pp.RotateRate;
                        _newRow["rotate_angle"] = _pp.RotateAngle;
                        _newRow["tilt_angle"] = _pp.TiltAngle;
                        _newRow["is_landable"] = _pp.IsLandable;
                        break;
                    case 11:
                        FillCommonFromBase(_newRow, _sgp, 11, q);
                        _newRow["classSpecific"] = _sgp.IsClassSpecific;
                        _newRow["faction_id"] = _factions.FindIdByName(_sgp.FactionID);
                        break;
                    case 12:
                        FillCommonFromBase(_newRow, _sbp, 12, q);
                        _newRow["capShip"] = _sbp.IsCapShip;
                        _newRow["dockable"] = _sbp.IsDockable;
                        break;
                    case 37:
                        FillCommonFromBase(_newRow, _bp, 37, q);
                        break;
                    case 38:
                        FillCommonFromBase(_newRow, _hp, 38, q);
                        _newRow["level"] = _hp.Level;
                        _newRow["res_count"] = _hp.ResCount;
                        _newRow["spawn_radius"] = _hp.MobSpawnRadius;
                        _newRow["pop_rock_chance"] = _hp.PopRockChance;
                        _newRow["max_field_radius"] = _hp.MaxFieldRadius;
                        _newRow["field"] = FieldNameToId(_hp.Field);
                        break;
                }

                // Original: if a sound_effect is set but range is 0, default to 30000.
                int sid = int.Parse(_newRow["sound_effect_id"].ToString());
                float range = int.Parse(_newRow["sound_effect_range"].ToString());
                if (sid != -1 && range == 0)
                    _newRow["sound_effect_range"] = 30000;

                _sectorObjectsSql.getSectorObject().Rows.Add(_newRow);
                _sectorObjectsSql.newRow(_newRow);
            }

            switch (_type2)
            {
                case 0:
                    if (ValidateMobs(_lastInsertId)) { _sw?.addNewObject(_type2, _newRow); Close(); }
                    break;
                case 38:
                    if (ValidateHarvestables(_lastInsertId)) { _sw?.addNewObject(_type2, _newRow); Close(); }
                    break;
                default:
                    _sw?.addNewObject(_type2, _newRow);
                    Close();
                    break;
            }
        }

        private static int FieldNameToId(string name) => name switch
        {
            "Random" => 0,
            "Ring" => 1,
            "Donut" => 2,
            "Cylinder" => 3,
            "Sphere" => 4,
            "Gas Cloud Clump" => 5,
            _ => 0,
        };

        private bool ValidateMobs(int id)
        {
            _lastInsertId = id;
            if (_lastInsertId == 0)
            {
                DataTable tmp = Database.executeQuery(Database.DatabaseName.net7, "SELECT LAST_INSERT_ID()");
                foreach (DataRow z in tmp.Rows)
                {
                    _lastInsertId = int.Parse(z["LAST_INSERT_ID()"].ToString());
                    EditorGlobals.SelectedObjectId = _lastInsertId;
                }
            }

            string query = "SELECT * FROM mob_spawn_group where spawn_group_id='" + _lastInsertId + "';";
            DataTable groupMobs = Database.executeQuery(Database.DatabaseName.net7, query);

            if (groupMobs.Rows.Count == 0)
            {
                _notify.ShowError("You have no mobs in your spawn group, to save this record, add some mobs.");
                return false;
            }
            if (_type2 == 0 && _mp.Count == 0)
            {
                _notify.ShowError("Your Maximum spawn count must be > 0 when you have mobs in your group");
                return false;
            }
            return true;
        }

        private bool ValidateHarvestables(int id)
        {
            _lastInsertId = id;
            if (_lastInsertId == 0)
            {
                DataTable tmp = Database.executeQuery(Database.DatabaseName.net7, "SELECT LAST_INSERT_ID()");
                foreach (DataRow z in tmp.Rows)
                {
                    _lastInsertId = int.Parse(z["LAST_INSERT_ID()"].ToString());
                    EditorGlobals.SelectedObjectId = _lastInsertId;
                }
            }

            string q2 = "SELECT * FROM sector_objects_harvestable_restypes where group_id='" + _lastInsertId + "';";
            DataTable loadTypes = Database.executeQuery(Database.DatabaseName.net7, q2);

            if (loadTypes.Rows.Count == 0)
            {
                _notify.ShowError("You have no resource types in your field, to save this record, add at least 1 restype.");
                return false;
            }
            if (_hp.MaxFieldRadius == 0)
            {
                _notify.ShowError("Your Maximum field radius must be > 0.");
                return false;
            }
            if (_hp.MobSpawnRadius > 0)
            {
                if (!ValidateMobs(_lastInsertId)) return false;
                if (_hp.MobSpawnRadius == 0)
                {
                    _notify.ShowError("Since you have mobs guardians your Maximum spawn radius must be > 0.");
                    return false;
                }
            }
            return true;
        }

        // INewSectorObjectDialog — drives the canvas place-pick flow.
        // Original Y-flip preserved: world Y is negated.
        public void setPosition(PointF position)
        {
            switch (_type2)
            {
                case 0:  _mp.PositionX  = position.X * 100; _mp.PositionY  = -(position.Y * 100); break;
                case 3:  _pp.PositionX  = position.X * 100; _pp.PositionY  = -(position.Y * 100); break;
                case 11: _sgp.PositionX = position.X * 100; _sgp.PositionY = -(position.Y * 100); break;
                case 12: _sbp.PositionX = position.X * 100; _sbp.PositionY = -(position.Y * 100); break;
                case 37: _bp.PositionX  = position.X * 100; _bp.PositionY  = -(position.Y * 100); break;
                case 38: _hp.PositionX  = position.X * 100; _hp.PositionY  = -(position.Y * 100); break;
            }
        }
    }
}
