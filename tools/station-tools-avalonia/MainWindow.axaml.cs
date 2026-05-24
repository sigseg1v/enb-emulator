using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CommonTools.Database;
using MsBox.Avalonia;
using MsBoxIcon = MsBox.Avalonia.Enums.Icon;

namespace StationToolsAvalonia
{
    // Avalonia port of tools/station-tools/Main.cs. The original was a
    // 1146×619 WinForms TabControl + TreeView. This port keeps the same
    // 5-tab layout (Station / Room / Terminal / NPC / Venders) and the
    // same tree shape (Station → Room → {Terminals folder, NPCs folder}),
    // but uses Avalonia's TreeView+HierarchicalDataTemplate and replaces
    // the per-form sprintf-style SQL with DB.Instance parameter binding.
    //
    // Dropped vs. the original:
    //   * Runtime image loading from Application.StartupPath + "/ico/*.gif|.ico"
    //     (no icons in the repo; using text labels instead).
    //   * The DisplayStation method that composes a per-station preview
    //     image from a tree of bitmaps (Form1.cs lines 1231-1305) —
    //     visual feedback only, not load-bearing.
    //   * Drag-drop of avatar template files onto NPC nodes (Form1.cs
    //     lines 1836-1925) — file picker + Add Avatar button covers the
    //     same use case without the DnD plumbing.
    //   * The embedded TalkTreeEditor/ subdir and the EditTalkTree.cs
    //     thin wrapper — replaced by Process.Start of the sibling
    //     talktreeeditor-avalonia project (same pattern as the editor
    //     launcher).
    //   * StationSQL.cs — was an empty stub in the original; dropped.
    public partial class MainWindow : Window
    {
        public enum NodeKind { Station, Room, Terminal, Npc, TerminalsFolder, NpcsFolder }

        public class TreeNodeVM
        {
            public string Label { get; set; }
            public NodeKind Kind { get; set; }
            public int Id { get; set; }
            public int StationType { get; set; }
            public ObservableCollection<TreeNodeVM> Children { get; } = new();
        }

        readonly ObservableCollection<TreeNodeVM> _roots = new();

        string m_CurrentStationName;
        int m_CurrentStationID;
        int m_CurrentStationType;
        TreeNodeVM _selectedNode;
        string m_TalkTreeData = "";
        readonly List<int> m_VenderGroups = new();

        public MainWindow()
        {
            InitializeComponent();

            c_StationTree.ItemsSource = _roots;
            SetupHierarchicalTemplate();

            try
            {
                SetupStationTabCombos();
                SetupRoomTabCombos();
                SetupTerminalTabCombos();
                SetupNPCTabCombos();
                LoadStarbasesIntoCombo();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("MainWindow init DB failed: " + ex.Message);
            }
        }

        void SetupHierarchicalTemplate()
        {
            // HierarchicalDataTemplate is set via XAML normally; doing it
            // in code keeps the AXAML simpler and avoids cross-reference.
            var tmpl = new Avalonia.Controls.Templates.FuncTreeDataTemplate<TreeNodeVM>(
                (vm, _) => new TextBlock { Text = vm.Label },
                vm => vm.Children);
            c_StationTree.ItemTemplate = tmpl;
        }

        // ----- combo setup -----

        void SetupStationTabCombos()
        {
            c_StationType.Items.Clear();
            foreach (var t in new[] { "Terran", "Jenquai", "Progen", "Solsec", "Highport",
                                       "Net7", "PleasurePort", "Pirates", "Thule" })
                c_StationType.Items.Add(t);

            c_StationActive.Items.Clear();
            c_StationActive.Items.Add("False");
            c_StationActive.Items.Add("True");

            c_StationFaction.Items.Clear();
            var dt = DB.Instance.executeQuery(
                "SELECT `name` FROM `factions` ORDER BY `faction_id` ASC",
                new string[0], new string[0]);
            if (dt != null)
                foreach (DataRow r in dt.Rows)
                    c_StationFaction.Items.Add(r["name"].ToString());

            c_NPCFaction.Items.Clear();
            if (dt != null)
                foreach (DataRow r in dt.Rows)
                    c_NPCFaction.Items.Add(r["name"].ToString());
        }

        void SetupRoomTabCombos()
        {
            c_RoomType.Items.Clear();
            foreach (var t in new[] { "Hangar", "Main Lobby", "Bazaar", "Lounge" })
                c_RoomType.Items.Add(t);
        }

        void SetupTerminalTabCombos()
        {
            c_TerminalLocation.Items.Clear();
            for (int x = 0; x < 15; x++)
                c_TerminalLocation.Items.Add(x.ToString());

            c_TerminalType.Items.Clear();
            foreach (var t in new[]
            {
                "Refining", "Analyze", "Manufacturing", "Jobs",
                "Intergalactic Net", "Customize Ship", "Customize Avatar",
                "Training (Don't Use)"
            })
                c_TerminalType.Items.Add(t);
        }

        void SetupNPCTabCombos()
        {
            c_NPCLocation.Items.Clear();
            for (int x = 0; x < 15; x++)
                c_NPCLocation.Items.Add(x.ToString());

            c_NPCLevel.Items.Clear();
            for (int x = 0; x <= 9; x++)
                c_NPCLevel.Items.Add(x.ToString());

            c_NPCBoothType.Items.Clear();
            foreach (var t in new[]
            {
                "No Booth",
                "Red (Weapons)",
                "Light Blue ()",
                "Gray (Engines, Reactors, Shields)",
                "Purple (Consumables)",
                "Yellow (Trade Goods)",
                "Brown (Components)",
                "Green (Ore)"
            })
                c_NPCBoothType.Items.Add(t);

            c_VenderGroupBox.Items.Clear();
            c_VenderGroupBox.Items.Add("Not a Vender");
            m_VenderGroups.Clear();
            m_VenderGroups.Add(-1);
            var dt = DB.Instance.executeQuery(
                "SELECT `GroupID`, `GroupName` FROM `starbase_vender_groups`",
                new string[0], new string[0]);
            if (dt != null)
            {
                foreach (DataRow r in dt.Rows)
                {
                    m_VenderGroups.Add(Convert.ToInt32(r["GroupID"]));
                    c_VenderGroupBox.Items.Add(r["GroupName"].ToString());
                }
            }
        }

        void LoadStarbasesIntoCombo()
        {
            c_StationCombo.Items.Clear();
            var dt = DB.Instance.executeQuery(
                "SELECT `name` FROM `starbases` ORDER BY `name`",
                new string[0], new string[0]);
            if (dt != null)
                foreach (DataRow r in dt.Rows)
                    c_StationCombo.Items.Add(r["name"].ToString());
        }

        // ----- toolbar actions -----

        void OnLoadStation(object sender, RoutedEventArgs e)
        {
            if (c_StationCombo.SelectedItem is not string name) return;
            m_CurrentStationName = name;
            LoadStationTree(name);
            LoadStationData();
            c_Tabs.SelectedIndex = 0;
            c_Status.Text = $"loaded station {name}";
        }

        void OnNewStation(object sender, RoutedEventArgs e)
        {
            try
            {
                DB.Instance.executeCommand(
                    "INSERT INTO `starbases` (`sector_id`, `name`, `type`, `is_active`, `description`, `welcome_message`, `target_sector_object`, `faction_id`) " +
                    "VALUES ('0', 'New Station', '0', '0', 'Enter Description', 'Enter Welcome', '0', '1')",
                    new string[0], new string[0]);
                LoadStarbasesIntoCombo();
                c_Status.Text = "created new station";
            }
            catch (Exception ex) { Error("SQL: " + ex.Message); }
        }

        void OnReload(object sender, RoutedEventArgs e)
        {
            LoadStarbasesIntoCombo();
            if (!string.IsNullOrEmpty(m_CurrentStationName))
            {
                LoadStationTree(m_CurrentStationName);
                LoadStationData();
            }
            c_Status.Text = "reloaded";
        }

        void OnSaveAll(object sender, RoutedEventArgs e)
        {
            try
            {
                if (m_CurrentStationID > 0) SaveStation();
                if (_selectedNode?.Kind == NodeKind.Room)     SaveRoom();
                if (_selectedNode?.Kind == NodeKind.Terminal) SaveTerminal();
                if (_selectedNode?.Kind == NodeKind.Npc)      SaveNpc();
                c_Status.Text = "saved";
            }
            catch (Exception ex) { Error("SQL: " + ex.Message); }
        }

        // ----- tree handling -----

        void LoadStationTree(string stationName)
        {
            _roots.Clear();

            var rooms = DB.Instance.executeQuery(
                "SELECT `starbase_rooms`.`room_id`, `starbase_rooms`.`starbase_id`, `starbases`.`type` " +
                "FROM `starbases` INNER JOIN `starbase_rooms` ON `starbases`.`starbase_id` = `starbase_rooms`.`starbase_id` " +
                "WHERE `starbases`.`name` = @n",
                new[] { "@n" }, new[] { stationName });

            if (rooms == null || rooms.Rows.Count == 0) return;

            m_CurrentStationType = Convert.ToInt32(rooms.Rows[0]["type"]);
            m_CurrentStationID   = Convert.ToInt32(rooms.Rows[0]["starbase_id"]);
            m_CurrentStationName = stationName;

            var stationNode = new TreeNodeVM
            {
                Label = stationName, Kind = NodeKind.Station,
                Id = m_CurrentStationID, StationType = m_CurrentStationType
            };
            _roots.Add(stationNode);

            int rIndex = 0;
            foreach (DataRow rr in rooms.Rows)
            {
                int roomId = Convert.ToInt32(rr["room_id"]);
                var roomNode = new TreeNodeVM
                {
                    Label = $"Room {rIndex}", Kind = NodeKind.Room, Id = roomId
                };
                rIndex++;

                var termFolder = new TreeNodeVM { Label = "Terminals", Kind = NodeKind.TerminalsFolder, Id = roomId };
                var npcFolder  = new TreeNodeVM { Label = "NPC's",     Kind = NodeKind.NpcsFolder,      Id = roomId };

                var terms = DB.Instance.executeQuery(
                    "SELECT `terminal_id`, `terminal_index` FROM `starbase_terminals` WHERE `room_id` = @r ORDER BY `terminal_index`",
                    new[] { "@r" }, new[] { roomId.ToString() });
                if (terms != null)
                {
                    int ti = 0;
                    foreach (DataRow tr in terms.Rows)
                        termFolder.Children.Add(new TreeNodeVM
                        {
                            Label = $"Terminal {ti++}",
                            Kind = NodeKind.Terminal,
                            Id = Convert.ToInt32(tr["terminal_id"])
                        });
                }

                var npcs = DB.Instance.executeQuery(
                    "SELECT `npc_Id`, `first_name`, `last_name` FROM `starbase_npcs` WHERE `room_id` = @r",
                    new[] { "@r" }, new[] { roomId.ToString() });
                if (npcs != null)
                {
                    foreach (DataRow nr in npcs.Rows)
                        npcFolder.Children.Add(new TreeNodeVM
                        {
                            Label = $"{nr["first_name"]} {nr["last_name"]}",
                            Kind = NodeKind.Npc,
                            Id = Convert.ToInt32(nr["npc_Id"])
                        });
                }

                roomNode.Children.Add(termFolder);
                roomNode.Children.Add(npcFolder);
                stationNode.Children.Add(roomNode);
            }
        }

        void OnTreeSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (c_StationTree.SelectedItem is not TreeNodeVM vm) return;
            _selectedNode = vm;

            try
            {
                switch (vm.Kind)
                {
                    case NodeKind.Station:  LoadStationData();  c_Tabs.SelectedIndex = 0; break;
                    case NodeKind.Room:     LoadRoomData(vm.Id); c_Tabs.SelectedIndex = 1; break;
                    case NodeKind.Terminal: LoadTerminalData(vm.Id); c_Tabs.SelectedIndex = 2; break;
                    case NodeKind.Npc:      LoadNpcData(vm.Id);  c_Tabs.SelectedIndex = 3; break;
                }
            }
            catch (Exception ex) { Error("Load failed: " + ex.Message); }
        }

        // ----- load methods -----

        void LoadStationData()
        {
            if (string.IsNullOrEmpty(m_CurrentStationName)) return;

            var dt = DB.Instance.executeQuery(
                "SELECT `starbase_id`, `sector_id`, `name`, `type`, `is_active`, `description`, `welcome_message`, `target_sector_object`, `faction_id` " +
                "FROM `starbases` WHERE `name` = @n",
                new[] { "@n" }, new[] { m_CurrentStationName });
            if (dt == null || dt.Rows.Count == 0) { Error("Could not load station"); return; }

            var r = dt.Rows[0];
            c_StationName.Text       = r["name"].ToString();
            c_StationID.Text         = r["starbase_id"].ToString();
            c_StationSectorID.Text   = r["sector_id"].ToString();
            c_StationObjectID.Text   = r["target_sector_object"].ToString();
            c_StationDisc.Text       = r["description"].ToString();
            c_StationWelcome.Text    = r["welcome_message"].ToString();
            int fid = Convert.ToInt32(r["faction_id"]) - 1;
            if (fid >= 0 && fid < c_StationFaction.Items.Count) c_StationFaction.SelectedIndex = fid;
            c_StationType.SelectedIndex   = Convert.ToInt32(r["type"]);
            c_StationActive.SelectedIndex = Convert.ToInt32(r["is_active"]);
            m_CurrentStationID = Convert.ToInt32(r["starbase_id"]);
        }

        void LoadRoomData(int roomId)
        {
            var dt = DB.Instance.executeQuery(
                "SELECT `type`, `style`, `fog_near`, `fog_far`, `description`, `fog_red`, `fog_green`, `fog_blue` " +
                "FROM `starbase_rooms` WHERE `room_id` = @r",
                new[] { "@r" }, new[] { roomId.ToString() });
            if (dt == null || dt.Rows.Count == 0) return;

            var r = dt.Rows[0];
            c_RoomDiscription.Text = r["description"].ToString();
            c_RoomFogNear.Text     = r["fog_near"].ToString();
            c_RoomFogFar.Text      = r["fog_far"].ToString();
            c_RoomType.SelectedIndex = Convert.ToInt32(r["type"]);
            c_RoomFogR.Text = (r["fog_red"]   == DBNull.Value ? "0" : r["fog_red"].ToString());
            c_RoomFogG.Text = (r["fog_green"] == DBNull.Value ? "0" : r["fog_green"].ToString());
            c_RoomFogB.Text = (r["fog_blue"]  == DBNull.Value ? "0" : r["fog_blue"].ToString());

            uint style = Convert.ToUInt32(r["style"]);
            c_RoomDeviders.IsChecked  = (style & 0x001) > 0;
            c_RoomRafters.IsChecked   = (style & 0x002) > 0;
            c_RoomBar.IsChecked       = (style & 0x004) > 0;
            c_RoomTables.IsChecked    = (style & 0x008) > 0;
            c_RoomMonitors.IsChecked  = (style & 0x010) > 0;
            c_RoomEyecandy.IsChecked  = (style & 0x020) > 0;
            c_RoomSmallroom.IsChecked = (style & 0x040) > 0;
            c_RoomAltroom.IsChecked   = (style & 0x080) > 0;
            c_RoomFog.IsChecked       = (style & 0x100) > 0;
        }

        void LoadTerminalData(int terminalId)
        {
            var dt = DB.Instance.executeQuery(
                "SELECT `location`, `type`, `attribute`, `description` " +
                "FROM `starbase_terminals` WHERE `terminal_id` = @t",
                new[] { "@t" }, new[] { terminalId.ToString() });
            if (dt == null || dt.Rows.Count == 0) return;

            var r = dt.Rows[0];
            c_TerminalAttribute.Text = r["attribute"].ToString();
            c_TerminalDisc.Text      = r["description"].ToString();
            c_TerminalLocation.SelectedIndex = Convert.ToInt32(r["location"]);
            c_TerminalType.SelectedIndex     = Convert.ToInt32(r["type"]);
        }

        void LoadNpcData(int npcId)
        {
            var dt = DB.Instance.executeQuery(
                "SELECT `description`, `faction_id`, `location`, `last_name`, `first_name`, `level`, `booth_type`, `npc_Id`, `groupid`, `talk_tree_handle` " +
                "FROM `starbase_npcs` INNER JOIN `starbase_vendors` ON `starbase_npcs`.`npc_Id` = `starbase_vendors`.`vendor_id` " +
                "WHERE `npc_Id` = @n",
                new[] { "@n" }, new[] { npcId.ToString() });
            if (dt == null || dt.Rows.Count == 0) return;

            var r = dt.Rows[0];
            c_NPCDiscription.Text = r["description"].ToString();
            c_NPCFirstName.Text   = r["first_name"].ToString();
            c_NPCLastName.Text    = r["last_name"].ToString();
            c_NPCAvatarID.Text    = r["npc_Id"].ToString();
            c_NPCLocation.SelectedIndex = Convert.ToInt32(r["location"]);
            int fid = Convert.ToInt32(r["faction_id"]) - 1;
            if (fid >= 0 && fid < c_NPCFaction.Items.Count) c_NPCFaction.SelectedIndex = fid;
            c_NPCLevel.SelectedIndex = Convert.ToInt32(r["level"]);
            int booth = Convert.ToInt32(r["booth_type"]);
            if (booth > 6) booth = 6;
            c_NPCBoothType.SelectedIndex = booth + 1;

            int gid = Convert.ToInt32(r["groupid"]);
            for (int i = 0; i < m_VenderGroups.Count; i++)
                if (m_VenderGroups[i] == gid) { c_VenderGroupBox.SelectedIndex = i; break; }

            m_TalkTreeData = r["talk_tree_handle"]?.ToString() ?? "";
        }

        // ----- save methods -----

        void OnSaveStation(object sender, RoutedEventArgs e)
        {
            try { SaveStation(); c_Status.Text = "saved station"; }
            catch (Exception ex) { Error("SQL: " + ex.Message); }
        }
        void OnSaveRoom(object sender, RoutedEventArgs e)
        {
            try { SaveRoom(); c_Status.Text = "saved room"; }
            catch (Exception ex) { Error("SQL: " + ex.Message); }
        }
        void OnSaveTerminal(object sender, RoutedEventArgs e)
        {
            try { SaveTerminal(); c_Status.Text = "saved terminal"; }
            catch (Exception ex) { Error("SQL: " + ex.Message); }
        }
        void OnSaveNpc(object sender, RoutedEventArgs e)
        {
            try { SaveNpc(); c_Status.Text = "saved NPC"; }
            catch (Exception ex) { Error("SQL: " + ex.Message); }
        }

        void SaveStation()
        {
            if (m_CurrentStationID <= 0) return;
            DB.Instance.executeCommand(
                "UPDATE `starbases` SET `name` = @n, `sector_id` = @s, `type` = @t, `is_active` = @a, " +
                "`description` = @d, `welcome_message` = @w, `target_sector_object` = @o, `faction_id` = @f " +
                "WHERE `starbase_id` = @id",
                new[] { "@n", "@s", "@t", "@a", "@d", "@w", "@o", "@f", "@id" },
                new[]
                {
                    c_StationName.Text,
                    c_StationSectorID.Text,
                    c_StationType.SelectedIndex.ToString(),
                    c_StationActive.SelectedIndex.ToString(),
                    c_StationDisc.Text,
                    c_StationWelcome.Text,
                    c_StationObjectID.Text,
                    (c_StationFaction.SelectedIndex + 1).ToString(),
                    m_CurrentStationID.ToString()
                });
            m_CurrentStationName = c_StationName.Text;
        }

        void SaveRoom()
        {
            if (_selectedNode == null || _selectedNode.Kind != NodeKind.Room) return;

            uint style = 0;
            if (c_RoomDeviders.IsChecked  ?? false) style |= 0x001;
            if (c_RoomRafters.IsChecked   ?? false) style |= 0x002;
            if (c_RoomBar.IsChecked       ?? false) style |= 0x004;
            if (c_RoomTables.IsChecked    ?? false) style |= 0x008;
            if (c_RoomMonitors.IsChecked  ?? false) style |= 0x010;
            if (c_RoomEyecandy.IsChecked  ?? false) style |= 0x020;
            if (c_RoomSmallroom.IsChecked ?? false) style |= 0x040;
            if (c_RoomAltroom.IsChecked   ?? false) style |= 0x080;
            if (c_RoomFog.IsChecked       ?? false) style |= 0x100;

            DB.Instance.executeCommand(
                "UPDATE `starbase_rooms` SET `type` = @t, `style` = @s, `fog_near` = @fn, `fog_far` = @ff, " +
                "`description` = @d, `fog_red` = @fr, `fog_green` = @fg, `fog_blue` = @fb " +
                "WHERE `room_id` = @id",
                new[] { "@t", "@s", "@fn", "@ff", "@d", "@fr", "@fg", "@fb", "@id" },
                new[]
                {
                    c_RoomType.SelectedIndex.ToString(),
                    style.ToString(),
                    NumOrZero(c_RoomFogNear.Text),
                    NumOrZero(c_RoomFogFar.Text),
                    c_RoomDiscription.Text,
                    NumOrZero(c_RoomFogR.Text),
                    NumOrZero(c_RoomFogG.Text),
                    NumOrZero(c_RoomFogB.Text),
                    _selectedNode.Id.ToString()
                });
        }

        void SaveTerminal()
        {
            if (_selectedNode == null || _selectedNode.Kind != NodeKind.Terminal) return;
            DB.Instance.executeCommand(
                "UPDATE `starbase_terminals` SET `location` = @l, `type` = @t, `attribute` = @a, `description` = @d " +
                "WHERE `terminal_id` = @id",
                new[] { "@l", "@t", "@a", "@d", "@id" },
                new[]
                {
                    c_TerminalLocation.SelectedIndex.ToString(),
                    c_TerminalType.SelectedIndex.ToString(),
                    NumOrZero(c_TerminalAttribute.Text),
                    c_TerminalDisc.Text,
                    _selectedNode.Id.ToString()
                });
        }

        void SaveNpc()
        {
            if (_selectedNode == null || _selectedNode.Kind != NodeKind.Npc) return;

            int boothIdx = c_NPCBoothType.SelectedIndex - 1;
            DB.Instance.executeCommand(
                "UPDATE `starbase_npcs` SET `first_name` = @fn, `last_name` = @ln, `description` = @d, " +
                "`faction_id` = @f, `location` = @l, `talk_tree_handle` = @tt WHERE `npc_Id` = @id",
                new[] { "@fn", "@ln", "@d", "@f", "@l", "@tt", "@id" },
                new[]
                {
                    c_NPCFirstName.Text,
                    c_NPCLastName.Text,
                    c_NPCDiscription.Text,
                    (c_NPCFaction.SelectedIndex + 1).ToString(),
                    c_NPCLocation.SelectedIndex.ToString(),
                    m_TalkTreeData ?? "",
                    _selectedNode.Id.ToString()
                });

            int gidIdx = c_VenderGroupBox.SelectedIndex;
            int gid = (gidIdx >= 0 && gidIdx < m_VenderGroups.Count) ? m_VenderGroups[gidIdx] : -1;
            DB.Instance.executeCommand(
                "UPDATE `starbase_vendors` SET `level` = @lvl, `booth_type` = @bt, `groupid` = @g " +
                "WHERE `vendor_id` = @id",
                new[] { "@lvl", "@bt", "@g", "@id" },
                new[]
                {
                    c_NPCLevel.SelectedIndex.ToString(),
                    boothIdx.ToString(),
                    gid.ToString(),
                    _selectedNode.Id.ToString()
                });
        }

        static string NumOrZero(string s) => string.IsNullOrEmpty(s) ? "0" : s;

        // ----- context menu actions -----

        void OnAddRoom(object sender, RoutedEventArgs e)
        {
            if (m_CurrentStationID <= 0) return;
            try
            {
                DB.Instance.executeCommand(
                    "INSERT INTO `starbase_rooms` (`type`, `style`, `fog_near`, `fog_far`, `description`, `starbase_id`) " +
                    "VALUES ('0', '0', '0', '0', 'Enter Description', @sb)",
                    new[] { "@sb" }, new[] { m_CurrentStationID.ToString() });
                LoadStationTree(m_CurrentStationName);
                c_Status.Text = "added room";
            }
            catch (Exception ex) { Error("SQL: " + ex.Message); }
        }

        void OnAddTerminal(object sender, RoutedEventArgs e)
        {
            int roomId = ResolveRoomIdForContextAction();
            if (roomId <= 0) return;
            try
            {
                int idx = (int)(DB.Instance.executeQuery(
                    "SELECT COUNT(*) AS n FROM `starbase_terminals` WHERE `room_id` = @r",
                    new[] { "@r" }, new[] { roomId.ToString() })?.Rows[0]?["n"] ?? 0);
                DB.Instance.executeCommand(
                    "INSERT INTO `starbase_terminals` (`location`, `type`, `attribute`, `description`, `room_id`, `terminal_index`) " +
                    "VALUES ('0', '0', '0', 'Enter Description', @r, @i)",
                    new[] { "@r", "@i" }, new[] { roomId.ToString(), idx.ToString() });
                LoadStationTree(m_CurrentStationName);
                c_Status.Text = "added terminal";
            }
            catch (Exception ex) { Error("SQL: " + ex.Message); }
        }

        void OnAddNpc(object sender, RoutedEventArgs e)
        {
            int roomId = ResolveRoomIdForContextAction();
            if (roomId <= 0) return;
            try
            {
                int idx = (int)(DB.Instance.executeQuery(
                    "SELECT COUNT(*) AS n FROM `starbase_npcs` WHERE `room_id` = @r",
                    new[] { "@r" }, new[] { roomId.ToString() })?.Rows[0]?["n"] ?? 0);
                DB.Instance.executeCommand(
                    "INSERT INTO `starbase_npcs` (`first_name`, `last_name`, `description`, `faction_id`, `location`, `room_id`, `npc_index`, `talk_tree_handle`) " +
                    "VALUES ('New', 'NPC', 'Enter Description', '1', '0', @r, @i, '')",
                    new[] { "@r", "@i" }, new[] { roomId.ToString(), idx.ToString() });
                LoadStationTree(m_CurrentStationName);
                c_Status.Text = "added NPC";
            }
            catch (Exception ex) { Error("SQL: " + ex.Message); }
        }

        int ResolveRoomIdForContextAction()
        {
            if (_selectedNode == null) return -1;
            return _selectedNode.Kind switch
            {
                NodeKind.Room => _selectedNode.Id,
                NodeKind.TerminalsFolder => _selectedNode.Id,
                NodeKind.NpcsFolder => _selectedNode.Id,
                _ => -1
            };
        }

        void OnDeleteNode(object sender, RoutedEventArgs e)
        {
            if (_selectedNode == null) return;
            try
            {
                switch (_selectedNode.Kind)
                {
                    case NodeKind.Station:
                        DB.Instance.executeCommand("DELETE FROM `starbases` WHERE `starbase_id` = @id",
                            new[] { "@id" }, new[] { _selectedNode.Id.ToString() });
                        LoadStarbasesIntoCombo();
                        _roots.Clear();
                        m_CurrentStationID = 0;
                        m_CurrentStationName = null;
                        break;
                    case NodeKind.Room:
                        DB.Instance.executeCommand("DELETE FROM `starbase_rooms`     WHERE `room_id`     = @id",
                            new[] { "@id" }, new[] { _selectedNode.Id.ToString() });
                        DB.Instance.executeCommand("DELETE FROM `starbase_terminals` WHERE `room_id`     = @id",
                            new[] { "@id" }, new[] { _selectedNode.Id.ToString() });
                        DB.Instance.executeCommand("DELETE FROM `starbase_npcs`      WHERE `room_id`     = @id",
                            new[] { "@id" }, new[] { _selectedNode.Id.ToString() });
                        LoadStationTree(m_CurrentStationName);
                        break;
                    case NodeKind.Terminal:
                        DB.Instance.executeCommand("DELETE FROM `starbase_terminals` WHERE `terminal_id` = @id",
                            new[] { "@id" }, new[] { _selectedNode.Id.ToString() });
                        LoadStationTree(m_CurrentStationName);
                        break;
                    case NodeKind.Npc:
                        DB.Instance.executeCommand("DELETE FROM `starbase_npcs`              WHERE `npc_Id`               = @id",
                            new[] { "@id" }, new[] { _selectedNode.Id.ToString() });
                        DB.Instance.executeCommand("DELETE FROM `starbase_vendors`           WHERE `vendor_id`            = @id",
                            new[] { "@id" }, new[] { _selectedNode.Id.ToString() });
                        DB.Instance.executeCommand("DELETE FROM `starbase_npc_avatar_templates` WHERE `avatar_template_id` = @id",
                            new[] { "@id" }, new[] { _selectedNode.Id.ToString() });
                        LoadStationTree(m_CurrentStationName);
                        break;
                }
                c_Status.Text = "deleted";
            }
            catch (Exception ex) { Error("SQL: " + ex.Message); }
        }

        // ----- misc actions -----

        async void OnFindObject(object sender, RoutedEventArgs e)
        {
            var dlg = new FindObjectWindow();
            await dlg.ShowDialog(this);
            if (dlg.m_Ok)
            {
                c_StationObjectID.Text = dlg.GetStationID().ToString();
                c_StationSectorID.Text = dlg.GetSectorID().ToString();
            }
        }

        async void OnAddAvatar(object sender, RoutedEventArgs e)
        {
            if (_selectedNode == null || _selectedNode.Kind != NodeKind.Npc)
            {
                Error("Select an NPC first.");
                return;
            }
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Avatar Template",
                AllowMultiple = false
            });
            if (files.Count == 0) return;

            try
            {
                using var stream = File.OpenRead(files[0].Path.LocalPath);
                var avatar = new LoadAvatar(stream);
                // Persist the raw blob — store the file bytes in avatar_version
                // (which is the BLOB column the original schema used).
                byte[] bytes = File.ReadAllBytes(files[0].Path.LocalPath);
                string hex = Convert.ToHexString(bytes);

                DB.Instance.executeCommand(
                    "REPLACE INTO `starbase_npc_avatar_templates` (`avatar_template_id`, `avatar_version`) VALUES (@id, UNHEX(@h))",
                    new[] { "@id", "@h" },
                    new[] { _selectedNode.Id.ToString(), hex });

                c_Status.Text = $"avatar set: type={avatar.avatarType}, race={avatar.race}, prof={avatar.profession}";
            }
            catch (Exception ex) { Error("Avatar: " + ex.Message); }
        }

        void OnEditTalkTree(object sender, RoutedEventArgs e)
        {
            try
            {
                var psi = new ProcessStartInfo("dotnet", "run --project ../talktreeeditor-avalonia/")
                {
                    UseShellExecute = false
                };
                Process.Start(psi);
                c_Status.Text = "launched talktreeeditor-avalonia";
            }
            catch (Exception ex) { Error("Launch failed: " + ex.Message); }
        }

        static void Error(string msg)
        {
            try { MessageBoxManager.GetMessageBoxStandard("Station Tools", msg, MsBox.Avalonia.Enums.ButtonEnum.Ok, MsBoxIcon.Error).ShowAsync(); }
            catch { Console.Error.WriteLine("STATION ERROR: " + msg); }
        }
    }
}
