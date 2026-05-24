// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// Ported from N7.SectorWindow under Net-7 Entertainment's CC BY-NC-SA 3.0;
// preservation modifications inherit under ShareAlike.

using System;
using System.Collections;
using System.Data;
using System.Drawing;
using N7.Props;
using N7.Sql;
using SectorEditorAvalonia.PiccoloShim;
using SectorEditorAvalonia.Sprites;
using SectorEditorAvalonia.Utilities;

namespace SectorEditorAvalonia.Windows
{
    // Renders one sector: bounds + 6 object layers (mobs, planets,
    // stargates, starbases, decorations, harvestables). Owns the
    // selection state (which sprite the user last clicked), routes
    // property-grid edits to the right sprite, and dispatches the
    // layer/circle/text toggle commands the original main form
    // wired into its View menu.
    //
    // Original used PropertyGrid + DataGridView + NewSectorObject
    // (Form) directly. The Avalonia port routes those through
    // IPropertyHost / IGridSyncSink / INotificationSink /
    // INewSectorObjectDialog so the smoke harness can run with
    // null implementations and the MainWindow installs real ones.
    public class SectorWindow
    {
        private readonly PCanvas canvas;
        private readonly PLayer boundsLayer;
        private readonly PLayer masterLayer;
        private readonly PLayer mobsLayer;
        private readonly PLayer planetsLayer;
        private readonly PLayer stargatesLayer;
        private readonly PLayer starbasesLayer;
        private readonly PLayer decorationsLayer;
        private readonly PLayer harvestableLayer;
        private PNode pSelectedNode;
        private readonly SectorObjectsSql so;
        private readonly DataRow dr;
        private readonly Hashtable deletedObjectsID = new Hashtable();
        private readonly Hashtable deletedObjectsType = new Hashtable();
        private readonly IPropertyHost _pg;
        private readonly IGridSyncSink _grid;
        private readonly INotificationSink _notify;
        private INewSectorObjectDialog _nso;
        private SectorProps sp;

        public SectorWindow(PCanvas pcanvas, DataRow[] sectorRows, IPropertyHost pg,
                            IGridSyncSink grid, INotificationSink notify = null)
        {
            canvas = pcanvas;
            dr = sectorRows[0];
            _pg = pg;
            _grid = grid ?? new NullGridSyncSink();
            _notify = notify ?? new NullNotificationSink();

            // The original switched on `Properties.Settings.Default.zoomSelection`
            // and only installed the wheel controller for case 0. No other case
            // existed in the codebase, so the switch was effectively a no-op
            // wrapper — preserved as an unconditional install here.
            _ = new MouseWheelZoomController(canvas.Camera);

            canvas.BackColor = Color.Black;
            masterLayer = canvas.Layer;

            boundsLayer       = new PLayer();
            mobsLayer         = new PLayer();
            planetsLayer      = new PLayer();
            stargatesLayer    = new PLayer();
            starbasesLayer    = new PLayer();
            decorationsLayer  = new PLayer();
            harvestableLayer  = new PLayer();

            string sectorName       = sectorRows[0]["name"].ToString();
            int    sectorID         = int.Parse(sectorRows[0]["sector_id"].ToString());
            float  xmin             = float.Parse(sectorRows[0]["x_min"].ToString());
            float  xmax             = float.Parse(sectorRows[0]["x_max"].ToString());
            float  ymin             = float.Parse(sectorRows[0]["y_min"].ToString());
            float  ymax             = float.Parse(sectorRows[0]["y_max"].ToString());
            float  zmin             = float.Parse(sectorRows[0]["z_min"].ToString());
            float  zmax             = float.Parse(sectorRows[0]["z_max"].ToString());
            int    gridx            = int.Parse(sectorRows[0]["grid_x"].ToString());
            int    gridy            = int.Parse(sectorRows[0]["grid_y"].ToString());
            int    gridz            = int.Parse(sectorRows[0]["grid_z"].ToString());
            float  fognear          = float.Parse(sectorRows[0]["fog_near"].ToString());
            float  fogfar           = float.Parse(sectorRows[0]["fog_far"].ToString());
            int    debrismode       = int.Parse(sectorRows[0]["debris_mode"].ToString());
            bool   lightbackdrop    = (bool) sectorRows[0]["light_backdrop"];
            bool   fogbackdrop      = (bool) sectorRows[0]["fog_backdrop"];
            bool   swapbackdrop     = (bool) sectorRows[0]["swap_backdrop"];
            float  backdropfognear  = float.Parse(sectorRows[0]["backdrop_fog_near"].ToString());
            float  backdropfogfar   = float.Parse(sectorRows[0]["backdrop_fog_far"].ToString());
            float  maxtilt          = float.Parse(sectorRows[0]["max_tilt"].ToString());
            bool   autolevel        = (bool) sectorRows[0]["auto_level"];
            float  impulserate      = float.Parse(sectorRows[0]["impulse_rate"].ToString());
            float  decayvelocity    = float.Parse(sectorRows[0]["decay_velocity"].ToString());
            float  decayspin        = float.Parse(sectorRows[0]["decay_spin"].ToString());
            int    backdropasset    = int.Parse(sectorRows[0]["backdrop_asset"].ToString());
            string greetings        = sectorRows[0]["greetings"].ToString();
            string notes            = sectorRows[0]["notes"].ToString();
            int    systemid         = int.Parse(sectorRows[0]["system_id"].ToString());
            float  galaxyx          = float.Parse(sectorRows[0]["galaxy_x"].ToString());
            float  galaxyy          = float.Parse(sectorRows[0]["galaxy_y"].ToString());
            float  galaxyz          = float.Parse(sectorRows[0]["galaxy_z"].ToString());
            int    sector_type      = int.Parse(sectorRows[0]["sector_type"].ToString());

            so = new SectorObjectsSql(sectorName);
            DataTable sot = so.getSectorObject();

            float width  = xmax - xmin;
            float height = ymax - ymin;
            float depth  = zmax - zmin;

            sp = new SectorProps
            {
                Name             = sectorName,
                SectorID         = sectorID,
                Width            = width,
                Height           = height,
                Depth            = depth,
                GridX            = gridx,
                GridY            = gridy,
                GridZ            = gridz,
                FogNear          = fognear,
                FogFar           = fogfar,
                DebrisMode       = debrismode,
                LightBackdrop    = lightbackdrop,
                FogBackdrop      = fogbackdrop,
                SwapBackdrop     = swapbackdrop,
                BackdropFogNear  = backdropfognear,
                BackdropFogFar   = backdropfogfar,
                MaxTilt          = maxtilt,
                AutoLevel        = autolevel,
                ImpulseRate      = impulserate,
                DecayVelocity    = decayvelocity,
                DecaySpin        = decayspin,
                BackdropAsset    = backdropasset,
                Greetings        = greetings,
                Notes            = notes,
                SystemID         = systemid,
                GalaxyX          = galaxyx,
                GalaxyY          = galaxyy,
                GalaxyZ          = galaxyz,
                SectorType       = sector_type switch
                {
                    0 => "Space Sector",
                    1 => "Rocky Planet Surface",
                    2 => "Gas Giant Surface",
                    _ => "",
                },
            };

            pg.SelectedObject = sp;

            new SectorBoundsSprite(boundsLayer, xmin, ymin, xmax, ymax);

            foreach (DataRow r in sot.Rows)
            {
                int type = int.Parse(r["type"].ToString());
                switch (type)
                {
                    case  0: new MobSprite        (mobsLayer,        r, pg, _grid); break;
                    case  3: new PlanetSprite     (planetsLayer,     r, pg, _grid); break;
                    case 11: new StargateSprite   (stargatesLayer,   r, pg, _grid); break;
                    case 12: new StarbaseSprite   (starbasesLayer,   r, pg, _grid); break;
                    case 37: new DecorationSprite (decorationsLayer, r, pg, _grid); break;
                    case 38: new HarvestableSprite(harvestableLayer, r, pg, _grid); break;
                }
            }

            masterLayer.AddChild(boundsLayer);
            masterLayer.AddChild(mobsLayer);
            masterLayer.AddChild(planetsLayer);
            masterLayer.AddChild(stargatesLayer);
            masterLayer.AddChild(starbasesLayer);
            masterLayer.AddChild(decorationsLayer);
            masterLayer.AddChild(harvestableLayer);

            masterLayer.MouseDown    += MasterLayer_OnMouseDown;
            canvas.Camera.MouseDown  += canvasCamera_MouseDown;

            // Zoom all the way out (matches original initial view).
            canvas.Camera.ViewScale = .375f;
        }

        public SectorObjectsSql getSectorObjectsSQL() => so;

        public void updateChangedInfo(string propertyName, string _changedValue)
        {
            string changedValue = _changedValue.Replace("'", "''");
            switch (propertyName)
            {
                case "Name":     dr["name"]      = changedValue; break;
                case "SectorID": dr["sector_id"] = int.Parse(changedValue); break;
                case "Width":
                case "Height":
                case "Depth":
                {
                    float xmin = 0, xmax = 0, ymin = 0, ymax = 0, zmin = 0, zmax = 0;
                    if (sp.Width  != 0) { xmin = -(sp.Width  / 2); xmax = sp.Width  / 2; }
                    if (sp.Height != 0) { ymin = -(sp.Height / 2); ymax = sp.Height / 2; }
                    if (sp.Depth  != 0) { zmin = -(sp.Depth  / 2); zmax = sp.Depth  / 2; }

                    dr["x_min"] = xmin; dr["x_max"] = xmax;
                    dr["y_min"] = ymin; dr["y_max"] = ymax;
                    dr["z_min"] = zmin; dr["z_max"] = zmax;

                    float x = -(sp.Width  / 2) / 100;
                    float y = -(sp.Height / 2) / 100;
                    var b0 = boundsLayer.GetChild(0);
                    b0.X = x; b0.Y = y;
                    b0.Width  = sp.Width  / 100;
                    b0.Height = sp.Height / 100;
                    break;
                }
                // Misspellings preserved: the original wrote `grix_*` (sic) and
                // `mex_tilt` (sic) into the DataRow, which made these edits
                // silently no-op on the actual MySQL columns. Behaviour-faithful.
                case "GridX":            dr["grix_x"]            = int.Parse(changedValue); break;
                case "GridY":            dr["grix_y"]            = int.Parse(changedValue); break;
                case "GridZ":            dr["grix_z"]            = int.Parse(changedValue); break;
                case "FogNear":          dr["fog_near"]          = float.Parse(changedValue); break;
                case "FogFar":           dr["fog_far"]           = float.Parse(changedValue); break;
                case "DebrisMode":       dr["debris_mode"]       = int.Parse(changedValue); break;
                case "LightBackdrop":    dr["light_backdrop"]    = bool.Parse(changedValue); break;
                case "FogBackdrop":      dr["fog_backdrop"]      = bool.Parse(changedValue); break;
                case "SwapBackdrop":     dr["swap_backdrop"]     = bool.Parse(changedValue); break;
                case "BackdropFogNear":  dr["backdrop_fog_near"] = float.Parse(changedValue); break;
                case "BackdropFogFar":   dr["backdrop_fog_far"]  = float.Parse(changedValue); break;
                case "MaxTilt":          dr["mex_tilt"]          = float.Parse(changedValue); break;
                case "AutoLevel":        dr["auto_level"]        = bool.Parse(changedValue); break;
                case "ImpulseRate":      dr["impulse_rate"]      = float.Parse(changedValue); break;
                case "DecayVelocity":    dr["decay_velocity"]    = float.Parse(changedValue); break;
                case "DecaySpin":        dr["decay_spin"]        = float.Parse(changedValue); break;
                case "BackdropAsset":    dr["backdrop_asset"]    = int.Parse(changedValue); break;
                case "Greetings":        dr["greetings"]         = changedValue; break;
                case "Notes":            dr["notes"]             = changedValue; break;
                case "SystemID":         dr["system_id"]         = int.Parse(changedValue); break;
                case "GalaxyX":          dr["galaxy_x"]          = float.Parse(changedValue); break;
                case "GalaxyY":          dr["galaxy_y"]          = float.Parse(changedValue); break;
                case "GalaxyZ":          dr["galaxy_z"]          = float.Parse(changedValue); break;
                case "SectorType":
                    dr["sector_type"] = changedValue switch
                    {
                        "Space Sector"         => 0,
                        "Rocky Planet Surface" => 1,
                        "Gas Giant Surface"    => 2,
                        _                      => 0,
                    };
                    break;
            }

            if (dr.RowState != DataRowState.Modified)
                dr.SetModified();
        }

        #region Events

        // Routed from the IPropertyHost panel when a property value changes.
        // Dispatches to the selected sprite's updateChangedInfo, falling back
        // to this SectorWindow's own updateChangedInfo when the click did not
        // hit a sprite.
        public void OnPropertyValueChanged(string propertyName, object value)
        {
            try
            {
                if (pSelectedNode == null) { updateChangedInfo(propertyName, value?.ToString() ?? ""); return; }

                switch (pSelectedNode.Tag)
                {
                    case MobSprite ms:         ms.updateChangedInfo(propertyName, value); return;
                    case PlanetSprite ps:      ps.updateChangedInfo(propertyName, value); return;
                    case StargateSprite gs:    gs.updateChangedInfo(propertyName, value); return;
                    case StarbaseSprite bs:    bs.updateChangedInfo(propertyName, value); return;
                    case HarvestableSprite hs: hs.updateChangedInfo(propertyName, value); return;
                    case DecorationSprite ds:  ds.updateChangedInfo(propertyName, value); return;
                }
            }
            catch (Exception) { /* fall through to sector update */ }

            updateChangedInfo(propertyName, value?.ToString() ?? "");
        }

        public void canvasCamera_MouseDown(object sender, PInputEventArgs e)
        {
            if (_nso != null)
            {
                _nso.setPosition(e.Position);
                _nso.Show();
                _nso = null;
            }
        }

        public void MasterLayer_OnMouseDown(object sender, PInputEventArgs e)
        {
            // Right-click is a no-op in the original (the menu was handled
            // elsewhere — by the main form's context-menu hook).
            if (e.Button == MouseButton.Right) return;
            if (e.PickedNode == null) return;
            if (e.PickedNode.ChildrenCount < 3) return;

            int id = 0;
            DataRow row = null;
            if (TryHighlightAndGetRow(e.PickedNode, out row))
            {
                if (row != null)
                    id = int.Parse(row["sector_object_id"].ToString());
            }
            pSelectedNode = e.PickedNode;

            // Mirror the canvas selection into the data grid.
            _grid.SelectRowById(id);
        }

        private bool TryHighlightAndGetRow(PNode node, out DataRow row)
        {
            row = null;
            switch (node.Tag)
            {
                case MobSprite ms:         setOriginalText(pSelectedNode); ms.getText().TextBrush = Brushes.Red; row = ms.getRow(); return true;
                case PlanetSprite ps:      setOriginalText(pSelectedNode); ps.getText().TextBrush = Brushes.Red; row = ps.getRow(); return true;
                case StargateSprite gs:    setOriginalText(pSelectedNode); gs.getText().TextBrush = Brushes.Red; row = gs.getRow(); return true;
                case StarbaseSprite bs:    setOriginalText(pSelectedNode); bs.getText().TextBrush = Brushes.Red; row = bs.getRow(); return true;
                case HarvestableSprite hs: setOriginalText(pSelectedNode); hs.getText().TextBrush = Brushes.Red; row = hs.getRow(); return true;
                case DecorationSprite ds:  setOriginalText(pSelectedNode); ds.getText().TextBrush = Brushes.Red; row = ds.getRow(); return true;
            }
            return false;
        }

        public void setOriginalText(PNode pickedNode)
        {
            if (pSelectedNode == null || pickedNode == null) return;
            switch (pickedNode.Tag)
            {
                case MobSprite _:         if (pSelectedNode.Tag is MobSprite a)         a.getText().TextBrush = Brushes.White; break;
                case PlanetSprite _:      if (pSelectedNode.Tag is PlanetSprite b)      b.getText().TextBrush = Brushes.White; break;
                case StargateSprite _:    if (pSelectedNode.Tag is StargateSprite c)    c.getText().TextBrush = Brushes.White; break;
                case StarbaseSprite _:    if (pSelectedNode.Tag is StarbaseSprite d)    d.getText().TextBrush = Brushes.White; break;
                case HarvestableSprite _: if (pSelectedNode.Tag is HarvestableSprite f) f.getText().TextBrush = Brushes.White; break;
                case DecorationSprite _:  if (pSelectedNode.Tag is DecorationSprite g)  g.getText().TextBrush = Brushes.White; break;
            }
        }

        public void newSectorObject(INewSectorObjectDialog nso) { _nso = nso; }

        public void setSelected(int _id)
        {
            // Skip child 0 (boundsLayer) — matches original.
            for (int i = 1; i < masterLayer.ChildrenCount; i++)
            {
                var lyr = masterLayer.GetChild(i);
                for (int j = 0; j < lyr.ChildrenCount; j++)
                {
                    var node = lyr.GetChild(j);
                    if (TrySelectIfMatches(node, _id)) return;
                }
            }
        }

        private bool TrySelectIfMatches(PNode node, int _id)
        {
            DataRow r = null;
            switch (node.Tag)
            {
                case MobSprite ms:         r = ms.getRow(); break;
                case PlanetSprite ps:      r = ps.getRow(); break;
                case StargateSprite gs:    r = gs.getRow(); break;
                case StarbaseSprite bs:    r = bs.getRow(); break;
                case HarvestableSprite hs: r = hs.getRow(); break;
                case DecorationSprite ds:  r = ds.getRow(); break;
                default: return false;
            }
            int id = int.Parse(r["sector_object_id"].ToString());
            if (id != _id) return false;

            setOriginalText(pSelectedNode);
            switch (node.Tag)
            {
                case MobSprite ms:         ms.getText().TextBrush = Brushes.Red; pSelectedNode = node; ms.setPropGrid(); return true;
                case PlanetSprite ps:      ps.getText().TextBrush = Brushes.Red; pSelectedNode = node; ps.setPropGrid(); return true;
                case StargateSprite gs:    gs.getText().TextBrush = Brushes.Red; pSelectedNode = node; gs.setPropGrid(); return true;
                case StarbaseSprite bs:    bs.getText().TextBrush = Brushes.Red; pSelectedNode = node; bs.setPropGrid(); return true;
                case HarvestableSprite hs: hs.getText().TextBrush = Brushes.Red; pSelectedNode = node; hs.setPropGrid(); return true;
                case DecorationSprite ds:  ds.getText().TextBrush = Brushes.Red; pSelectedNode = node; ds.setPropGrid(); return true;
            }
            return false;
        }

        #endregion

        #region Sector Options Methods (collapsed from 200+ LOC switch chains)

        // The original had one large `switch (type)` per toggle method, each
        // arm doing exactly the same loop against its own per-type PLayer.
        // Replaced with a type→layer lookup; behaviour identical, source
        // ~70% shorter.
        private PLayer LayerForType(int type) => type switch
        {
            0  => mobsLayer,
            3  => planetsLayer,
            11 => stargatesLayer,
            12 => starbasesLayer,
            37 => decorationsLayer,
            38 => harvestableLayer,
            _  => null,
        };

        public void hideLayer(int type) { var l = LayerForType(type); if (l != null) l.Visible = false; }
        public void showLayer(int type) { var l = LayerForType(type); if (l != null) l.Visible = true;  }

        private void SetChildVisible(int type, int childIndex, bool visible)
        {
            var lyr = LayerForType(type); if (lyr == null) return;
            for (int i = 0; i < lyr.ChildrenCount; i++)
                lyr.GetChild(i).GetChild(childIndex).Visible = visible;
        }

        public void turnOffText(int type)         => SetChildVisible(type, 3, false);
        public void turnOnText(int type)          => SetChildVisible(type, 3, true);
        public void explorationRangeOn(int type)  => SetChildVisible(type, 2, true);
        public void explorationRangeOff(int type) => SetChildVisible(type, 2, false);
        public void radarRangeOn(int type)        => SetChildVisible(type, 1, true);
        public void radarRangeOff(int type)       => SetChildVisible(type, 1, false);
        public void SignatureOn(int type)         => SetChildVisible(type, 0, true);
        public void SignatureOff(int type)        => SetChildVisible(type, 0, false);

        // navType toggles flip the sprite's own visibility based on the
        // PNode's child count (which equals 4/5/6 depending on whether it
        // has 0, 1, or 2 nav-type placeholder children added — see each
        // sprite's `for (int i = 0; i < navType; i++) AddChild(new PNode())`).
        private void SetSpriteVisibleByChildCount(int type, int childCount, bool visible)
        {
            var lyr = LayerForType(type); if (lyr == null) return;
            for (int i = 0; i < lyr.ChildrenCount; i++)
            {
                var sprite = lyr.GetChild(i);
                if (sprite.ChildrenCount == childCount) sprite.Visible = visible;
            }
        }

        public void navTypeZeroOn (int type) => SetSpriteVisibleByChildCount(type, 4, true);
        public void navTypeOneOn  (int type) => SetSpriteVisibleByChildCount(type, 5, true);
        public void navTypeTwoOn  (int type) => SetSpriteVisibleByChildCount(type, 6, true);
        public void navTypeZeroOff(int type) => SetSpriteVisibleByChildCount(type, 4, false);
        public void navTypeOneOff (int type) => SetSpriteVisibleByChildCount(type, 5, false);
        public void navTypeTwoOff (int type) => SetSpriteVisibleByChildCount(type, 6, false);

        // appearsInRadar* methods toggle visibility on sprites whose sprite
        // class reports getAppearsInRader()==true. The "Off" variant looks
        // odd (hides only those that appear in radar — so the toggle pair
        // is really "show all radar-visible" / "hide all radar-visible")
        // but matches the original exactly.
        private static bool SpriteAppearsInRadar(PNode node)
        {
            return node.Tag switch
            {
                MobSprite ms         => ms.getAppearsInRader(),
                PlanetSprite ps      => ps.getAppearsInRader(),
                StargateSprite gs    => gs.getAppearsInRader(),
                StarbaseSprite bs    => bs.getAppearsInRader(),
                DecorationSprite ds  => ds.getAppearsInRader(),
                HarvestableSprite hs => hs.getAppearsInRader(),
                _                    => false,
            };
        }

        public void appearsInRadarOn(int type)
        {
            var lyr = LayerForType(type); if (lyr == null) return;
            for (int i = 0; i < lyr.ChildrenCount; i++)
            {
                var node = lyr.GetChild(i);
                if (SpriteAppearsInRadar(node)) node.Visible = true;
            }
        }

        public void appearsInRadarOff(int type)
        {
            var lyr = LayerForType(type); if (lyr == null) return;
            for (int i = 0; i < lyr.ChildrenCount; i++)
            {
                var node = lyr.GetChild(i);
                if (SpriteAppearsInRadar(node)) node.Visible = false;
            }
        }

        public void deleteSelectedObject()
        {
            if (pSelectedNode == null)
            {
                _notify.ShowError("Sorry there are no Sector objects Selected. \n Please Try Again.");
                return;
            }

            pSelectedNode.RemoveFromParent();

            DataRow dr2 = pSelectedNode.Tag switch
            {
                MobSprite ms         => ms.getRow(),
                PlanetSprite ps      => ps.getRow(),
                StargateSprite gs    => gs.getRow(),
                StarbaseSprite bs    => bs.getRow(),
                HarvestableSprite hs => hs.getRow(),
                DecorationSprite ds  => ds.getRow(),
                _                    => null,
            };
            if (dr2 == null) return;

            int id   = int.Parse(dr2["sector_object_id"].ToString());
            int type = int.Parse(dr2["type"].ToString());

            deletedObjectsID.Add(deletedObjectsID.Count, id);
            deletedObjectsType.Add(deletedObjectsType.Count, type);
            dr2.Delete();
            dr2.AcceptChanges();

            _grid.RemoveRowById(id);
            _pg.SelectedObject = null;
            pSelectedNode = null;
        }

        public void clearDeletedHashTables()
        {
            deletedObjectsID.Clear();
            deletedObjectsType.Clear();
        }

        public Hashtable getDeletedObjectsID()   => deletedObjectsID;
        public Hashtable getDeletedObjectsType() => deletedObjectsType;

        public void addNewObject(int type, DataRow ndr)
        {
            switch (type)
            {
                case  0: new MobSprite        (mobsLayer,        ndr, _pg, _grid); break;
                case  3: new PlanetSprite     (planetsLayer,     ndr, _pg, _grid); break;
                case 11: new StargateSprite   (stargatesLayer,   ndr, _pg, _grid); break;
                case 12: new StarbaseSprite   (starbasesLayer,   ndr, _pg, _grid); break;
                case 37: new DecorationSprite (decorationsLayer, ndr, _pg, _grid); break;
                case 38: new HarvestableSprite(harvestableLayer, ndr, _pg, _grid); break;
            }

            int id          = int.Parse(ndr["sector_object_id"].ToString());
            string name     = ndr["name"].ToString();
            int baseAssetId = int.Parse(ndr["base_asset_id"].ToString());
            int rowType     = int.Parse(ndr["type"].ToString());
            _grid.AppendRow(id, name, baseAssetId, rowType);
            _grid.SelectRowById(id);

            setSelected(id);
        }
        #endregion
    }
}
