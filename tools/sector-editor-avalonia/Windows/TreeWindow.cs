// SPDX-License-Identifier: CC-BY-NC-SA-3.0
// Part of the Earth & Beyond emulator preservation project.
// Ported from N7.TreeWindow under Net-7 Entertainment's CC BY-NC-SA 3.0;
// preservation modifications inherit under ShareAlike.

using System.Collections.Generic;
using System.Data;

namespace SectorEditorAvalonia.Windows
{
    // Builds the system -> sector tree shown in MainWindow's left pane.
    // The WinForms original used System.Windows.Forms.TreeNode; the
    // Avalonia port returns the same data as a plain POCO tree the
    // MainWindow can bind to whatever it likes (TreeView / TreeDataGrid).
    public sealed class TreeWindow
    {
        public sealed class Node
        {
            public string Name { get; set; }
            public List<Node> Children { get; } = new List<Node>();
        }

        private readonly Node[] _parent;

        public TreeWindow(DataTable systems, DataTable sectors)
        {
            _parent = new Node[systems.Rows.Count];

            int i = 0;
            foreach (DataRow r in systems.Rows)
            {
                string name = r["name"].ToString();
                string id = r["system_id"].ToString();
                var tn1 = new Node { Name = name };

                foreach (DataRow r2 in sectors.Rows)
                {
                    string id2 = r2["system_id"].ToString();
                    if (id == id2)
                    {
                        tn1.Children.Add(new Node { Name = r2["name"].ToString() });
                    }
                }

                _parent[i] = tn1;
                i++;
            }
        }

        public Node[] setupInitialTree() => _parent;
    }
}
