using System;
using CommonTools;

namespace TalkTreeEditorAvalonia
{
    // Ported verbatim in shape from tools/talktreeeditor/TalkNode.cs. The
    // only change is that `parentNode` was a WinForms.TreeNode reference —
    // here it's our TreeItem stand-in (see TreeItem.cs).
    public enum TalkNodeTypes { None, Branch, Trade, Flags }

    public class TalkNode
    {
        public TreeItem parentNode;
        public TalkNodeTypes type;
        public string id;
        public string text;

        public TalkNode()
        {
            parentNode = null;
            type = TalkNodeTypes.None;
            id = "";
            text = "";
        }

        public TalkNode(TreeItem parentNode, TalkNodeTypes type, string id, string text)
        {
            this.parentNode = parentNode;
            this.type = type;
            this.id = id;
            this.text = text;
        }

        public override string ToString()
        {
            if (parentNode == null)
            {
                return id
                     + ": "
                     + ((text.Length > 80) ? text.Substring(0, 80) : text);
            }
            switch (type)
            {
                case TalkNodeTypes.Branch:
                    return id + ") " + ((text.Length > 80) ? text.Substring(0, 80) : text);
                case TalkNodeTypes.Trade:
                    return "*Old Trade, automatically removed upon save*";
                case TalkNodeTypes.Flags:
                    return Enumeration.GetString<TalkTreeFlag>(text)
                         + ((id.Length == 0) ? "" : ": " + id);
            }
            return "";
        }
    }
}
