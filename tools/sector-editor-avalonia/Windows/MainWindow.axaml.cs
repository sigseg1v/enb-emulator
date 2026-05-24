// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// License: LICENSES/enb-emulator

using System;
using System.Data;
using Avalonia.Controls;
using N7.Sql;
using SectorEditorAvalonia.Dialogs;
using SectorEditorAvalonia.PiccoloShim;
using SectorEditorAvalonia.Utilities;

namespace SectorEditorAvalonia.Windows
{
    // Avalonia port of tools/sector-editor/GUI/mainFrm.cs. Wires the
    // tree pane, the three Piccolo-backed canvases (Sector / System /
    // Universe), the DataGrid Objects tab, and the right-side property
    // panel together — plus the File / Tools / Help menus.
    //
    // SQL DAOs (SystemsSql, SectorsSql, SectorObjectsSql, FactionSql,
    // MobsSQL, BaseAssetSQL) are constructed once on startup; menu
    // dialogs receive them by reference. Sprite/window code reaches
    // through EditorGlobals.Factions for faction lookups.
    public partial class MainWindow : Window
    {
        private SystemsSql _systems;
        private SectorsSql _sectors;
        private SectorObjectsSql _sectorObjects;
        private FactionSql _factions;
        private MobsSQL _mobs;
        private BaseAssetSQL _baseAssets;

        private TreeWindow _tw;
        private SectorWindow _activeSectorWindow;
        private SystemWindow _activeSystemWindow;
        private DataTable _objectsTable;

        private IPropertyHost _pg;
        private IGridSyncSink _gridSink;
        private INotificationSink _notify;

        public MainWindow()
        {
            InitializeComponent();
            _notify = new AvaloniaNotificationSink(this);
            _pg = new PropertyPanelHost(c_PropertyPanel);

            Opened += (_, _) => SafeBoot();
            c_Exit.Click += (_, _) => Close();
            c_About.Click += (_, _) => new AboutBox().ShowDialog(this);
            c_Settings.Click += (_, _) => new SettingsDialog().ShowDialog(this);

            c_New.Click += (_, _) => OpenNewDispatcher();
            c_SoundEffects.Click += (_, _) => new SoundEffectsDialog().ShowDialog(this);
            c_Options.Click += (_, _) => OpenOptions();
            c_Save.Click += (_, _) => { c_Status.Text = "(save: stub — DAO updateRow paths persist on edit)"; };

            c_Tree.SelectionChanged += (_, _) => OnTreeSelectionChanged();
        }

        private void SafeBoot()
        {
            try
            {
                _systems = new SystemsSql();
                _sectors = new SectorsSql();
                _factions = new FactionSql();
                _mobs = new MobsSQL();
                _baseAssets = new BaseAssetSQL();

                EditorGlobals.Factions = new FactionLookupAdapter(_factions);

                _tw = new TreeWindow(_systems.getSystemTable(), _sectors.getSectorTable());
                PopulateTree();
                c_Status.Text = "loaded — pick a sector in the tree to begin";
            }
            catch (Exception ex)
            {
                c_Status.Text = "boot failed (DB unreachable?) — " + ex.Message;
            }
        }

        private void PopulateTree()
        {
            var roots = _tw.setupInitialTree();
            var items = new System.Collections.Generic.List<TreeViewItem>();
            foreach (var systemNode in roots)
            {
                var sysItem = new TreeViewItem { Header = systemNode.Name, Tag = ("system", systemNode.Name) };
                foreach (var sectorNode in systemNode.Children)
                {
                    sysItem.Items.Add(new TreeViewItem { Header = sectorNode.Name, Tag = ("sector", sectorNode.Name) });
                }
                items.Add(sysItem);
            }
            c_Tree.ItemsSource = items;
        }

        private void OnTreeSelectionChanged()
        {
            if (c_Tree.SelectedItem is not TreeViewItem tvi) return;
            if (tvi.Tag is not ValueTuple<string, string> tag) return;
            var (kind, name) = tag;

            if (kind == "sector") LoadSector(name);
            else if (kind == "system") LoadSystem(name);
        }

        private void LoadSector(string sectorName)
        {
            try
            {
                DataRow[] rows = _sectors.findRowsByName(sectorName);
                if (rows.Length == 0) { c_Status.Text = "no sector named " + sectorName; return; }

                EditorGlobals.SectorID = int.Parse(rows[0]["sector_id"].ToString());

                _sectorObjects = new SectorObjectsSql(sectorName);
                _objectsTable = _sectorObjects.getSectorObject();
                _gridSink = new DataGridSyncSink(c_ObjectsGrid, _objectsTable);

                var canvas = new PCanvas();
                c_SectorHost.Child = canvas;
                _activeSectorWindow = new SectorWindow(canvas, rows, _pg, _gridSink, _notify);
                c_Tabs.SelectedIndex = 0;
                c_Status.Text = "sector: " + sectorName;
            }
            catch (Exception ex)
            {
                c_Status.Text = "failed to load sector: " + ex.Message;
            }
        }

        private void LoadSystem(string systemName)
        {
            try
            {
                DataRow[] systemRows = _systems.findRowsByName(systemName);
                if (systemRows.Length == 0) { c_Status.Text = "no system named " + systemName; return; }

                string systemId = systemRows[0]["system_id"].ToString();
                DataRow[] sectorRows = _sectors.getRowsBySystemID(systemId);

                var canvas = new PCanvas();
                c_SystemHost.Child = canvas;
                _activeSystemWindow = new SystemWindow(canvas, systemName, sectorRows, _pg, systemRows[0]);
                c_Tabs.SelectedIndex = 1;
                c_Status.Text = "system: " + systemName;
            }
            catch (Exception ex)
            {
                c_Status.Text = "failed to load system: " + ex.Message;
            }
        }

        private void OpenNewDispatcher()
        {
            var dlg = new NewFrmDialog(_notify, picked =>
            {
                if (picked == "System") OpenNewSystem();
                else if (picked == "Sector") OpenNewSector();
                else if (picked == "Sector Object") OpenNewSectorObjectFlow();
            });
            dlg.ShowDialog(this);
        }

        private void OpenNewSystem()
        {
            if (_systems == null || _tw == null) return;
            var rootStub = new TreeWindow.Node { Name = "Universe" };
            var dlg = new NewSystemDialog(rootStub, _systems, _pg);
            dlg.ShowDialog(this);
        }

        private void OpenNewSector()
        {
            if (_sectors == null || _systems == null) return;
            if (c_Tree.SelectedItem is not TreeViewItem tvi ||
                tvi.Tag is not ValueTuple<string, string> tag || tag.Item1 != "system")
            {
                _notify.ShowError("Select a parent system in the tree first.");
                return;
            }
            var parentNode = new TreeWindow.Node { Name = tag.Item2 };
            var dlg = new NewSectorDialog(parentNode, _sectors, _systems, _pg, _notify,
                                          newRow => _activeSystemWindow?.newSector(newRow));
            dlg.ShowDialog(this);
        }

        private void OpenNewSectorObjectFlow()
        {
            if (_sectors == null || _sectorObjects == null) return;
            if (c_Tree.SelectedItem is not TreeViewItem tvi ||
                tvi.Tag is not ValueTuple<string, string> tag || tag.Item1 != "sector")
            {
                _notify.ShowError("Select a parent sector in the tree first.");
                return;
            }
            string sectorName = tag.Item2;
            var sectorNode = new TreeWindow.Node { Name = sectorName };
            var typeDlg = new NewSectorObjectTypeDialog(sectorNode, _notify, typeName =>
            {
                var dlg = new NewSectorObjectDialog(
                    typeName, sectorName, _sectors, _sectorObjects, _pg, _notify,
                    EditorGlobals.Factions, _activeSectorWindow,
                    nso => _activeSectorWindow?.newSectorObject(nso));
                dlg.ShowDialog(this);
            });
            typeDlg.ShowDialog(this);
        }

        private void OpenOptions()
        {
            if (_activeSectorWindow == null)
            {
                _notify.ShowError("Open a sector first.");
                return;
            }
            var w = new Window { Title = "Options", Width = 720, Height = 280 };
            var og = new OptionsGui(_activeSectorWindow);
            w.Content = og;
            w.Opened += (_, _) => og.LoadAll();
            w.ShowDialog(this);
        }
    }
}
