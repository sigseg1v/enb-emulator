using Avalonia.Controls;

namespace TalkTreeEditorAvalonia.Reply
{
    public partial class TradeControl : UserControl
    {
        TreeItem _treeItem;
        bool _muteEvents;

        public TradeControl() { InitializeComponent(); }

        public void SetTreeItem(TreeItem item)
        {
            _treeItem = item;
            var talk = (TalkNode)item.Tag;
            _muteEvents = true;
            c_QuantityTxt.Text = talk.id;
            _muteEvents = false;
        }

        void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_muteEvents || _treeItem == null) return;
            var talk = (TalkNode)_treeItem.Tag;
            talk.id = c_QuantityTxt.Text ?? "";
            _treeItem.Text = talk.ToString();
        }
    }
}
