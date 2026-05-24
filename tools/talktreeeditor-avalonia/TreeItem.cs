using System.Collections.ObjectModel;
using System.ComponentModel;

namespace TalkTreeEditorAvalonia
{
    // Stand-in for WinForms.TreeNode. The original editor's tree manipulation
    // (FrmTalkTree.cs) leans on TreeNode.Tag, .Nodes, .Parent, .FirstNode,
    // .NextNode, plus a SelectedNode setter on TreeView. Avalonia's TreeView
    // is HierarchicalDataTemplate-driven, so we hold the data ourselves.
    //
    // Display text is bindable so refreshing after a TalkNode mutation lights
    // up the TreeView without rebuilding the whole tree.
    public sealed class TreeItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        string _text = "";
        public string Text
        {
            get => _text;
            set
            {
                if (_text == value) return;
                _text = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
            }
        }

        public object Tag { get; set; }
        public TreeItem Parent { get; set; }
        public ObservableCollection<TreeItem> Children { get; } = new();

        public TreeItem FirstChild => Children.Count > 0 ? Children[0] : null;

        public TreeItem NextSibling
        {
            get
            {
                if (Parent == null) return null;
                int idx = Parent.Children.IndexOf(this);
                if (idx < 0 || idx + 1 >= Parent.Children.Count) return null;
                return Parent.Children[idx + 1];
            }
        }
    }
}
