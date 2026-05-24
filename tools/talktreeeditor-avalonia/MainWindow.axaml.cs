using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Xml;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CommonTools;
using CommonTools.Database;
using CommonTools.Gui;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using MsBoxIcon = MsBox.Avalonia.Enums.Icon;
using TalkTreeEditorAvalonia.Reply;

namespace TalkTreeEditorAvalonia
{
    // Avalonia port of tools/talktreeeditor/GUI/FrmTalkTree.cs. The original
    // was a modal dialog invoked from missioneditor/sector-editor with an
    // initial conversation XML string; here it's a standalone Window with
    // the same Ok/Cancel semantics (Ok stores the modified XML on the
    // window and closes; Cancel just closes).
    //
    // The biggest mechanical change: WinForms.TreeView holds TreeNode
    // objects with .Tag/.Nodes/.Parent/.FirstNode/.NextNode. Avalonia's
    // TreeView is HierarchicalDataTemplate-driven, so we hold the data
    // in our own TreeItem class and bind ItemsSource to its Children.
    public partial class MainWindow : Window
    {
        readonly ObservableCollection<TreeItem> _roots = new();
        readonly string[] _replyTypeNames = new[]
        {
            TalkNodeTypes.None.ToString(),
            TalkNodeTypes.Branch.ToString(),
            // Trade is intentionally absent — original switched to a Flag
            // equivalent. See tools/talktreeeditor/Reply/Flag.cs:23.
            TalkNodeTypes.Flags.ToString(),
        };

        // Per-reply UI: 4 horizontally-laid rows, each with a Type combo
        // and the three possible reply panels (BranchControl / TradeControl /
        // FlagControl) toggled by visibility.
        sealed class ReplyRow
        {
            public ComboBox       TypeField;
            public BranchControl  Branch;
            public TradeControl   Trade;
            public FlagControl    Flag;
            public TreeItem       TreeItem;
        }
        const int REPLY_ROW_COUNT = 4;
        readonly List<ReplyRow> _replies = new();

        string _conversation = "";
        bool _madeSelection;
        bool _muteEvents;
        TreeItem _currentItem;
        readonly Stack<TreeItem> _previousItems = new();
        List<CodeValue> _stages;
        DlgEditXml _dlgEditXml;

        public MainWindow()
        {
            InitializeComponent();
            c_TalkTree.ItemsSource = _roots;

            for (int i = 0; i < REPLY_ROW_COUNT; ++i)
            {
                var typeCbo = new ComboBox
                {
                    Width = 100,
                    ItemsSource = _replyTypeNames,
                    SelectedIndex = 0,
                };
                var branch = new BranchControl { IsVisible = false };
                var trade  = new TradeControl  { IsVisible = false };
                var flag   = new FlagControl   { IsVisible = false };

                branch.GotoRequested += OnNodeGoto;
                flag.SetStagesProvider(() => _stages);

                var rowPanel = new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing     = 4,
                };
                rowPanel.Children.Add(typeCbo);
                rowPanel.Children.Add(branch);
                rowPanel.Children.Add(trade);
                rowPanel.Children.Add(flag);
                c_ReplyStack.Children.Add(rowPanel);

                var row = new ReplyRow
                {
                    TypeField = typeCbo,
                    Branch    = branch,
                    Trade     = trade,
                    Flag      = flag,
                };
                int captured = i;
                typeCbo.SelectionChanged += (_, _) => OnReplyTypeSelected(captured);
                _replies.Add(row);
            }

            Opened += (_, _) => LoadConversation(_conversation);
        }

        // ----- public API kept matching tools/talktreeeditor/GUI/FrmTalkTree.cs -----

        public void SetStages(List<CodeValue> stages) => _stages = stages;
        public List<CodeValue> GetStages() => _stages;

        public void SetConversation(string conversation) => _conversation = conversation ?? "";

        public bool GetConversation(out string conversation)
        {
            conversation = _conversation;
            return _madeSelection;
        }

        // ----- load/save (verbatim from FrmTalkTree.cs) -----

        public void LoadConversation(string conversation)
        {
            _roots.Clear();
            if (string.IsNullOrEmpty(conversation))
            {
                AddNodeText("1", "");
            }
            else
            {
                try
                {
                    var xmlDocument = new XmlDocument();
                    xmlDocument.Load(new StringReader("<Chat>" + conversation + "</Chat>"));
                    AddNodes(xmlDocument.DocumentElement);
                }
                catch (XmlException xmlEx) { ShowMessage(xmlEx.Message); }
                catch (Exception ex)       { ShowMessage(ex.Message); }
            }
            if (_roots.Count != 0)
            {
                // Selecting via TreeView.SelectedItem doesn't expand
                // collapsed ancestors automatically in Avalonia 11.x —
                // we don't have nested NPC nodes (only one level deep
                // for branches/flags), so this is fine.
                c_TalkTree.SelectedItem = _roots[0];
            }
        }

        void AddNodes(XmlNode xmlNode)
        {
            if (!xmlNode.HasChildNodes)
            {
                ShowMessage("Invalid XML structure for: " + xmlNode.Name
                          + "\nShould not have a node without children");
                return;
            }
            foreach (XmlNode xNode in xmlNode.ChildNodes)
            {
                if (xNode.Name == "Chat") continue;
                if (xNode.Name == "Tree")
                {
                    string id = xNode.Attributes[0].Value;
                    TreeItem textItem = null;
                    foreach (XmlNode treeChild in xNode.ChildNodes)
                    {
                        if (treeChild.Name == "Text")
                        {
                            string text = treeChild.FirstChild.Value;
                            textItem = AddNodeText(id, text);
                        }
                        else if (treeChild.Name == "Trade")
                        {
                            // Auto-convert to Flags, same as the original
                            AddNodeFlag(textItem, "", ((int)TalkTreeFlag.Trade).ToString());
                        }
                        else if (treeChild.Name == "Branch")
                        {
                            string bid   = treeChild.Attributes[0].Value;
                            string btext = treeChild.FirstChild.Value;
                            AddNodeBranch(textItem, bid, btext);
                        }
                        else if (treeChild.Name == "Flags")
                        {
                            string fid   = treeChild.Attributes.Count != 0 ? treeChild.Attributes[0].Value : "";
                            string ftext = treeChild.FirstChild == null ? "" : treeChild.FirstChild.Value;
                            AddNodeFlag(textItem, fid, ftext);
                        }
                        else
                        {
                            ShowMessage("Unexpected child node of Tree: '" + treeChild.Name + "'");
                        }
                    }
                }
                else
                {
                    ShowMessage("Unexpected node: " + xNode.Name);
                }
            }
        }

        TreeItem AddNodeText(string id, string text)
        {
            var talk = new TalkNode(null, TalkNodeTypes.None, id, text);
            var item = new TreeItem { Tag = talk, Text = talk.ToString() };
            _roots.Add(item);
            return item;
        }

        void AddNodeBranch(TreeItem parent, string id, string text)
        {
            var talk = new TalkNode(parent, TalkNodeTypes.Branch, id, text);
            var item = new TreeItem { Tag = talk, Text = talk.ToString(), Parent = parent };
            parent.Children.Add(item);
        }

        void AddNodeFlag(TreeItem parent, string id, string text)
        {
            var talk = new TalkNode(parent, TalkNodeTypes.Flags, id, text);
            var item = new TreeItem { Tag = talk, Text = talk.ToString(), Parent = parent };
            parent.Children.Add(item);
        }

        // Kept for parity with the original even though we never enter
        // Trade type via the type combo (it's stripped from _replyTypeNames).
        void AddNodeTrade(TreeItem parent, string id)
        {
            var talk = new TalkNode(parent, TalkNodeTypes.Trade, id, "");
            var item = new TreeItem { Tag = talk, Text = talk.ToString(), Parent = parent };
            parent.Children.Insert(0, item);
        }

        void SaveConversation()
        {
            using var sw = new StringWriter();
            foreach (var item in _roots)
            {
                var talk = (TalkNode)item.Tag;
                sw.WriteLine(Xml.tagStart() + XmlTag.TALKTREE + Xml.attribute(XmlAttributes.TREENODEID, talk.id) + Xml.tagEnd());
                sw.WriteLine(Xml.tag(XmlTag.TEXT, talk.text));
                SaveItem(sw, item);
                sw.WriteLine(Xml.tagEnd(XmlTag.TALKTREE));
            }
            _conversation = sw.ToString();
        }

        void SaveItem(StringWriter sw, TreeItem treeItem)
        {
            var talk = (TalkNode)treeItem.Tag;
            if (!talk.type.Equals(TalkNodeTypes.None))
            {
                sw.Write(Xml.tagStart() + talk.type.ToString());
                if (talk.type.Equals(TalkNodeTypes.Branch))
                    sw.Write(Xml.attribute(XmlAttributes.TREENODEID, talk.id));
                else if (talk.type.Equals(TalkNodeTypes.Flags) && talk.id.Length != 0)
                    sw.Write(Xml.attribute(XmlAttributes.DATA, talk.id));
                sw.WriteLine(Xml.tagEnd() + talk.text + Xml.tagEnd(talk.type.ToString()));
            }
            foreach (var child in treeItem.Children)
                SaveItem(sw, child);
        }

        // ----- event handlers -----

        void OnTreeSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _muteEvents = true;
            if (c_TalkTree.SelectedItem is TreeItem item)
            {
                var talk = (TalkNode)item.Tag;
                if (talk.parentNode != null)
                {
                    // A reply child was selected; re-select the NPC parent.
                    c_TalkTree.SelectedItem = talk.parentNode;
                }
                else
                {
                    _currentItem = item;
                    c_NpcText.Text = talk.text;

                    TreeItem child = item.FirstChild;
                    for (int i = 0; i < _replies.Count; ++i)
                    {
                        TreeItem current = child;
                        TalkNodeTypes type;
                        if (child == null)
                        {
                            type = TalkNodeTypes.None;
                        }
                        else
                        {
                            var ctalk = (TalkNode)child.Tag;
                            type = ctalk.type;
                            child = child.NextSibling;
                        }
                        _replies[i].TreeItem = current;
                        _replies[i].TypeField.SelectedItem = type.ToString();
                    }
                    SetFields(-1);
                }
            }
            _muteEvents = false;
        }

        void OnReplyTypeSelected(int childIndex)
        {
            if (_muteEvents) return;
            var row = _replies[childIndex];
            if (row.TypeField.SelectedItem is not string typeName) return;
            if (!Enum.TryParse<TalkNodeTypes>(typeName, out var newType)) return;

            TalkNodeTypes currentType = row.TreeItem == null
                ? TalkNodeTypes.None
                : ((TalkNode)row.TreeItem.Tag).type;
            if (currentType.Equals(newType)) return;

            if (newType == TalkNodeTypes.None)
            {
                // Delete this sub-node.
                if (row.TreeItem != null && _currentItem != null)
                {
                    _currentItem.Children.Remove(row.TreeItem);
                    row.TreeItem = null;
                }
                SetFields(-1);
                RefreshSelection();
                return;
            }

            if (newType == TalkNodeTypes.Trade && !CanSetNodeToTrade(_currentItem))
            {
                _muteEvents = true;
                row.TypeField.SelectedItem = currentType.ToString();
                _muteEvents = false;
                SetFields(childIndex);
                return;
            }

            if (currentType == TalkNodeTypes.None)
            {
                if (newType == TalkNodeTypes.Trade)  AddNodeTrade (_currentItem, "");
                if (newType == TalkNodeTypes.Branch) AddNodeBranch(_currentItem, "", "");
                if (newType == TalkNodeTypes.Flags)  AddNodeFlag  (_currentItem, "", "");
                row.TreeItem = newType == TalkNodeTypes.Trade
                             ? _currentItem.Children[0]
                             : _currentItem.Children[_currentItem.Children.Count - 1];
            }
            else
            {
                _currentItem.Children.Remove(row.TreeItem);
                if (newType == TalkNodeTypes.Trade)  AddNodeTrade (_currentItem, "");
                if (newType == TalkNodeTypes.Branch) AddNodeBranch(_currentItem, "", "");
                if (newType == TalkNodeTypes.Flags)  AddNodeFlag  (_currentItem, "", "");
                row.TreeItem = newType == TalkNodeTypes.Trade
                             ? _currentItem.Children[0]
                             : _currentItem.Children[_currentItem.Children.Count - 1];
            }
            RefreshSelection();
            SetFields(childIndex);
        }

        bool CanSetNodeToTrade(TreeItem item)
        {
            if (item != null
                && item.Children.Count != 0
                && ((TalkNode)item.Children[0].Tag).type.Equals(TalkNodeTypes.Trade))
            {
                ShowMessage("This node already contains a Trade sub-node");
                return false;
            }
            return true;
        }

        void RefreshSelection()
        {
            var sel = c_TalkTree.SelectedItem;
            c_TalkTree.SelectedItem = null;
            c_TalkTree.SelectedItem = sel;
        }

        void SetFields(int childIndex)
        {
            for (int i = 0; i < _replies.Count; ++i)
            {
                if (childIndex != -1 && childIndex != i) continue;
                var row = _replies[i];
                row.Branch.IsVisible = false;
                row.Trade.IsVisible  = false;
                row.Flag.IsVisible   = false;
                if (row.TypeField.SelectedItem is not string typeName) continue;
                if (!Enum.TryParse<TalkNodeTypes>(typeName, out var type)) continue;
                switch (type)
                {
                    case TalkNodeTypes.Branch:
                        if (row.TreeItem != null) row.Branch.SetTreeItem(row.TreeItem);
                        row.Branch.IsVisible = true;
                        break;
                    case TalkNodeTypes.Trade:
                        if (row.TreeItem != null) row.Trade.SetTreeItem(row.TreeItem);
                        row.Trade.IsVisible = true;
                        break;
                    case TalkNodeTypes.Flags:
                        if (row.TreeItem != null)
                        {
                            row.Flag.SetTreeItem(row.TreeItem);
                            row.Flag.UpdateData();
                        }
                        row.Flag.IsVisible = true;
                        break;
                }
            }
        }

        void OnNodeAdd(object sender, RoutedEventArgs e)
        {
            int id = _roots.Count + 1;
            var talk = new TalkNode(null, TalkNodeTypes.None, id.ToString(), "");
            var item = new TreeItem { Tag = talk, Text = talk.ToString() };
            _roots.Add(item);
            c_TalkTree.SelectedItem = item;
        }

        void OnNodeRemove(object sender, RoutedEventArgs e)
        {
            if (c_TalkTree.SelectedItem is not TreeItem item) return;
            var talk = (TalkNode)item.Tag;
            if (talk.id == "1") return;
            _roots.Remove(item);
            if (_roots.Count != 0) c_TalkTree.SelectedItem = _roots[0];
        }

        void OnNpcTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_muteEvents || _currentItem == null) return;
            var talk = (TalkNode)_currentItem.Tag;
            talk.text = c_NpcText.Text ?? "";
            _currentItem.Text = talk.ToString();
        }

        public void OnNodeGoto(string gotoNodeId)
        {
            foreach (var node in _roots)
            {
                var talk = (TalkNode)node.Tag;
                if (talk != null
                    && talk.id != null
                    && talk.id.Equals(gotoNodeId)
                    && talk.type.Equals(TalkNodeTypes.None))
                {
                    if (_previousItems.Count == 0) c_PreviousBtn.IsEnabled = true;
                    if (c_TalkTree.SelectedItem is TreeItem cur) _previousItems.Push(cur);
                    c_TalkTree.SelectedItem = node;
                    break;
                }
            }
        }

        void OnNodePrevious(object sender, RoutedEventArgs e)
        {
            if (_previousItems.Count == 0) return;
            var prev = _previousItems.Pop();
            if (_previousItems.Count == 0) c_PreviousBtn.IsEnabled = false;
            c_TalkTree.SelectedItem = prev;
        }

        async void OnOk(object sender, RoutedEventArgs e)
        {
            if (!Validate(out string error))
            {
                await MessageBoxManager.GetMessageBoxStandard(
                    "Validation Error", error, ButtonEnum.Ok, MsBoxIcon.Error).ShowAsync();
                return;
            }
            _madeSelection = true;
            SaveConversation();
            Close();
        }

        void OnCancel(object sender, RoutedEventArgs e) => Close();

        async void OnEditXml(object sender, RoutedEventArgs e)
        {
            if (_dlgEditXml == null) _dlgEditXml = new DlgEditXml();
            string conversation = _conversation;
            SaveConversation();
            _dlgEditXml.setXml(_conversation);
            SetConversation(conversation);
            await _dlgEditXml.ShowDialog(this);
            if (_dlgEditXml.getValues(out string updated))
                LoadConversation(updated);
        }

        // ----- validation (ported verbatim from FrmTalkTree.cs:661) -----

        sealed class CheckBranch
        {
            public string NpcTreeNodeId;
            public string BranchToNodeId;
            public CheckBranch(string npc, string id) { NpcTreeNodeId = npc; BranchToNodeId = id; }
        }

        public bool Validate(out string error)
        {
            TalkNode npcTalkNode;
            TalkNode replyTalkNode;
            int nodeId = 1;
            error = "";
            var idList = new List<string>();
            var branchList = new List<CheckBranch>();
            TalkTreeFlag talkTreeFlag = TalkTreeFlag.More;

            foreach (var npcTreeNode in _roots)
            {
                npcTalkNode = (TalkNode)npcTreeNode.Tag;

                if (idList.Contains(npcTalkNode.id))
                {
                    error = "The node id '" + npcTalkNode.id + "' is present more than once";
                    return false;
                }
                idList.Add(npcTalkNode.id);

                if (nodeId == 1)
                {
                    if (!npcTalkNode.id.Equals("1"))
                    {
                        error = "The first node has an id of '" + npcTalkNode.id + "' but was expected to have the id '1'";
                        return false;
                    }
                    foreach (var replyTreeNode in npcTreeNode.Children)
                    {
                        replyTalkNode = (TalkNode)replyTreeNode.Tag;
                        if (replyTalkNode.type.Equals(TalkNodeTypes.Flags))
                        {
                            if (!Enumeration.TryParse<TalkTreeFlag>(replyTalkNode.text, out talkTreeFlag))
                                talkTreeFlag = TalkTreeFlag.More;
                        }
                        if (replyTalkNode.type.Equals(TalkNodeTypes.Trade)
                            || (replyTalkNode.type.Equals(TalkNodeTypes.Flags) && talkTreeFlag.Equals(TalkTreeFlag.Trade)))
                        {
                            error = "The first node cannot specify a trade action";
                            return false;
                        }
                    }
                }

                if (npcTalkNode.text.Length == 0)
                {
                    error = "The node '" + npcTalkNode.id + "' does not specify any NPC text";
                    return false;
                }
                if (npcTreeNode.Children.Count == 0)
                {
                    error = "The node '" + npcTalkNode.id + "' does not specify any reply";
                    return false;
                }

                foreach (var childTreeNode in npcTreeNode.Children)
                {
                    replyTalkNode = (TalkNode)childTreeNode.Tag;
                    switch (replyTalkNode.type)
                    {
                        case TalkNodeTypes.None: break;
                        case TalkNodeTypes.Branch:
                            if (npcTalkNode.id.Equals(replyTalkNode.id))
                            {
                                error = "Cannot branch to the same/current node (Node: " + npcTalkNode.id + ")";
                                return false;
                            }
                            branchList.Add(new CheckBranch(npcTalkNode.id, replyTalkNode.id));
                            if (replyTalkNode.text.Length == 0)
                            {
                                error = "A branch does not specify any text (Node: " + npcTalkNode.id + ")";
                                return false;
                            }
                            break;
                        case TalkNodeTypes.Trade: break;
                        case TalkNodeTypes.Flags:
                            if (!Enumeration.TryParse<TalkTreeFlag>(replyTalkNode.text, out talkTreeFlag))
                            {
                                error = "The flag value '" + replyTalkNode.text + "' cannot be converted to a TalkTreeFlag value (Node: " + npcTalkNode.id + ")";
                                return false;
                            }
                            if (talkTreeFlag == TalkTreeFlag.Mission_Goto_Stage)
                            {
                                if (replyTalkNode.id.Equals("0"))
                                {
                                    error = "Cannot go back to stage 0 (Node: " + npcTalkNode.id + ")";
                                    return false;
                                }
                                else if (replyTalkNode.id.Equals("-2"))
                                {
                                    // Repeatable mission sentinel; not an error
                                }
                                else
                                {
                                    error = "The " + TalkTreeFlag.Mission_Goto_Stage + " flag points to an invalid stage ID of '" + replyTalkNode.id + "' (Node: " + npcTalkNode.id + ")";
                                    if (_stages != null)
                                    {
                                        foreach (var stage in _stages)
                                        {
                                            if (stage.code.ToString().Equals(replyTalkNode.id))
                                            {
                                                error = "";
                                                break;
                                            }
                                        }
                                    }
                                    if (error.Length != 0) return false;
                                }
                            }
                            break;
                    }
                    ++nodeId;
                }
            }

            foreach (var checkGoto in branchList)
            {
                if (!idList.Contains(checkGoto.BranchToNodeId))
                {
                    error = "The branch directs to an invalid node id '" + checkGoto.BranchToNodeId + "' (Node: " + checkGoto.NpcTreeNodeId + ")";
                    return false;
                }
            }

            branchList.Add(new CheckBranch("1", "1"));
            foreach (var checkBranch in branchList)
                idList.Remove(checkBranch.BranchToNodeId);
            if (idList.Count != 0)
            {
                error = "Node " + idList[0] + " cannot be accessed";
                return false;
            }
            return true;
        }

        // ----- helpers -----

        async void ShowMessage(string message) =>
            await MessageBoxManager.GetMessageBoxStandard(
                "Talk Tree Editor", message, ButtonEnum.Ok, MsBoxIcon.Info).ShowAsync();
    }
}
