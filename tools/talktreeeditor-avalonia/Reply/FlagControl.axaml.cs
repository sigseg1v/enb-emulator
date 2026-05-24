using System;
using System.Collections.Generic;
using Avalonia.Controls;
using CommonTools;
using CommonTools.Database;

namespace TalkTreeEditorAvalonia.Reply
{
    // Port of tools/talktreeeditor/Reply/Flag.cs. The original passed the
    // FrmTalkTree back-reference in to fetch m_stages. We accept a
    // delegate instead so the embedder type stays loose.
    public partial class FlagControl : UserControl
    {
        TreeItem _treeItem;
        bool _muteEvents;
        Func<List<CodeValue>> _stagesProvider;

        public FlagControl()
        {
            InitializeComponent();
            Enumeration.AddSortedByName<TalkTreeFlag>(c_TypeCbo);
            c_TypeCbo.SelectedIndex = 0;
        }

        public void SetStagesProvider(Func<List<CodeValue>> stagesProvider) =>
            _stagesProvider = stagesProvider;

        public void SetTreeItem(TreeItem item)
        {
            _treeItem = item;
            var talk = (TalkNode)item.Tag;
            _muteEvents = true;
            c_ValueTxt.Text = talk.id;
            if (Enumeration.TryParse<TalkTreeFlag>(talk.text, out var flag))
                c_TypeCbo.SelectedItem = flag;
            else
                c_TypeCbo.SelectedItem = TalkTreeFlag.More;
            _muteEvents = false;
        }

        // Forces a redraw after a SetTreeItem so the goto-stage combo
        // populates if the loaded value is Mission_Goto_Stage.
        public void UpdateData() => OnTypeSelected(null, null);

        void OnTypeSelected(object sender, SelectionChangedEventArgs e)
        {
            c_ValueTxt.IsVisible = false;
            c_ValueCbo.IsVisible = false;
            if (c_TypeCbo.SelectedItem is not TalkTreeFlag flag) return;
            switch (flag)
            {
                case TalkTreeFlag.Mission_Goto_Stage:
                    var stages = _stagesProvider?.Invoke();
                    var items = new List<CodeValue>();
                    if (stages != null) items.AddRange(stages);
                    items.Add(new CodeValue(-2, "Mission completed & is repeatable"));
                    c_ValueCbo.ItemsSource = items;
                    if (string.IsNullOrEmpty(c_ValueTxt.Text))
                    {
                        c_ValueCbo.SelectedIndex = 0;
                    }
                    else if (int.TryParse(c_ValueTxt.Text, out int code))
                    {
                        var key = new CodeValue(code);
                        foreach (var cv in items)
                            if (cv.Equals(key)) { c_ValueCbo.SelectedItem = cv; break; }
                    }
                    c_ValueCbo.IsVisible = true;
                    break;
                default:
                    c_ValueTxt.Text = "";
                    break;
            }

            if (_treeItem != null && !_muteEvents)
            {
                var talk = (TalkNode)_treeItem.Tag;
                talk.text = ((int)flag).ToString();
                talk.id   = c_ValueTxt.Text ?? "";
                _treeItem.Text = talk.ToString();
            }
        }

        void OnValueSelected(object sender, SelectionChangedEventArgs e)
        {
            if (_muteEvents) return;
            if (c_ValueCbo.SelectedItem is CodeValue cv)
                c_ValueTxt.Text = cv.code.ToString();
        }

        void OnValueTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_muteEvents || _treeItem == null) return;
            var talk = (TalkNode)_treeItem.Tag;
            talk.id = c_ValueTxt.Text ?? "";
            _treeItem.Text = talk.ToString();
        }
    }
}
