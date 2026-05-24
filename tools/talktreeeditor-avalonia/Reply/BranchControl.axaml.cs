using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace TalkTreeEditorAvalonia.Reply
{
    // Port of tools/talktreeeditor/Reply/Branch.cs. The original held a
    // back-reference to FrmTalkTree so the Goto button could re-select
    // another tree node; we expose `GotoRequested` as an event instead so
    // the embedder doesn't need to be the typed parent form.
    public partial class BranchControl : UserControl
    {
        TreeItem _treeItem;
        bool _muteEvents;

        public event Action<string> GotoRequested;

        public BranchControl() { InitializeComponent(); }

        public void SetTreeItem(TreeItem item)
        {
            _treeItem = item;
            var talk = (TalkNode)item.Tag;
            _muteEvents = true;
            c_GotoTxt.Text  = talk.id;
            c_ReplyTxt.Text = talk.text;
            _muteEvents = false;
        }

        void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_muteEvents || _treeItem == null) return;
            var talk = (TalkNode)_treeItem.Tag;
            talk.id   = c_GotoTxt.Text  ?? "";
            talk.text = c_ReplyTxt.Text ?? "";
            _treeItem.Text = talk.ToString();
        }

        void OnGotoClick(object sender, RoutedEventArgs e) =>
            GotoRequested?.Invoke(c_GotoTxt.Text ?? "");
    }
}
