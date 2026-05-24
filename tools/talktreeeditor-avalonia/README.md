# talktreeeditor-avalonia

Cross-platform Avalonia port of `tools/talktreeeditor/` (the original Net-7 WinForms editor for NPC conversation trees).

Built on **.NET 10** + **Avalonia 11.2.3**. Targets `net10.0` (no `-windows` suffix), so it runs on Linux without WINE.

## What this is

A standalone **non-DB** editor. The original was launched modally from `missioneditor` and `sector-editor` with an XML conversation string on the command line, and returned the modified XML when the user clicked Ok. The Avalonia port preserves that public contract — `SetConversation(string)` + `GetConversation(out string)` — and additionally accepts the XML as `argv[0]` for ad-hoc invocation.

The UI is a two-pane window:

- **Left**: `TreeView` of NPC dialogue nodes. Each top-level node represents a thing the NPC says; its children represent the player's possible replies (Branch / Trade / Flags).
- **Right**: editor for the currently-selected NPC node — an "NPC says" multi-line text box plus **4 reply rows**, each a type-combo and one of three swappable reply panels (`BranchControl` / `TradeControl` / `FlagControl`). XML / Back / Ok / Cancel buttons sit at the bottom.

## Mapping from the original

| Original `tools/talktreeeditor/` | Port |
|---|---|
| `GUI/FrmTalkTree.cs` (`Form` with `TreeView` + 4 fixed `Panel` rows) | `MainWindow.axaml` + `MainWindow.axaml.cs` |
| `TalkNode.cs` (`parentNode` was `TreeNode`) | `TalkNode.cs` (`parentNode` is `TreeItem` — see below) |
| `Reply/Branch.cs` (`UserControl`) | `Reply/BranchControl.axaml{,.cs}` |
| `Reply/Trade.cs` (`UserControl`) | `Reply/TradeControl.axaml{,.cs}` |
| `Reply/Flag.cs` (`UserControl`) | `Reply/FlagControl.axaml{,.cs}` |
| Implicit `WinForms.TreeNode` model | Explicit `TreeItem.cs` data model (`Tag` / `Children` / `Parent` / `FirstChild` / `NextSibling`) bound via `HierarchicalDataTemplate` |
| `Login` Form | **None.** This editor has no DB access. |
| `MessageBox.Show(...)` | `MsBox.Avalonia` |
| `FrmEditXml` for raw XML editing | reused from `commontools-avalonia` (`CommonTools.Gui.DlgEditXml`) |

## TreeView model — the biggest mechanical change

WinForms `TreeView` mutates `TreeNode` objects directly: `treeNode.Nodes.Add(...)`, `treeNode.Tag = ...`, `treeNode.Text = ...`. Avalonia's `TreeView` is `HierarchicalDataTemplate`-driven — it binds to a data model and the UI tracks the model's `ObservableCollection` changes.

So the port introduces `TreeItem.cs` as a stand-in:

```csharp
public sealed class TreeItem : INotifyPropertyChanged
{
    public string Text { get; set; }          // raises PropertyChanged
    public object Tag { get; set; }           // holds the TalkNode
    public TreeItem Parent { get; set; }
    public ObservableCollection<TreeItem> Children { get; }
    public TreeItem FirstChild => ...;        // mirrors TreeNode.FirstNode
    public TreeItem NextSibling => ...;       // mirrors TreeNode.NextNode
}
```

`TalkNode.parentNode` was a `TreeNode` reference in the original; in the port it's a `TreeItem` reference. Everything else in `TalkNode` (the `id` / `text` / `type` fields, `ToString()` for tree-row formatting) ports verbatim.

The `MainWindow.axaml` template uses compiled bindings, so the `TreeDataTemplate` declares `DataType="local:TreeItem"`:

```xml
<TreeView.ItemTemplate>
  <TreeDataTemplate DataType="local:TreeItem" ItemsSource="{Binding Children}">
    <TextBlock Text="{Binding Text}" />
  </TreeDataTemplate>
</TreeView.ItemTemplate>
```

## Reply rows — back-reference removal

The original `Reply/Branch.cs` and `Reply/Flag.cs` accepted a `FrmTalkTree` back-reference so they could:

- `Branch` — call `frmTalkTree.SelectedNode = ...` when the user clicks **Goto** (jump the tree's selection to the target NPC node).
- `Flag` — read `frmTalkTree.getStages()` to populate the goto-stage combo when the selected flag is `Mission_Goto_Stage`.

To avoid coupling the UserControls to the typed parent window:

- `BranchControl` exposes `event Action<string> GotoRequested` and `MainWindow` subscribes.
- `FlagControl` exposes `void SetStagesProvider(Func<List<CodeValue>>)` and `MainWindow` passes a lambda capturing `_stages`.

Functionally identical; just no compile-time dependency on the parent form.

## 4 reply rows — programmatic creation

The original used a `Panel` array (`m_replyTypeCbo`, `m_replyBranchPnl`, `m_replyTradePnl`, `m_replyFlagPnl`, each length 4) wired up at design time. The port creates them at construction time in `MainWindow` so the AXAML stays small:

```csharp
for (int i = 0; i < REPLY_ROW_COUNT; ++i)
{
    var typeCbo = new ComboBox { ... };
    var branch  = new BranchControl { IsVisible = false };
    var trade   = new TradeControl  { IsVisible = false };
    var flag    = new FlagControl   { IsVisible = false };
    // ... wire events with captured index ...
    c_ReplyStack.Children.Add(rowPanel);
}
```

The `_replyTypeNames` array intentionally **omits** `Trade` (matching the original — see `tools/talktreeeditor/Reply/Flag.cs:23`). `Trade` survives in `TalkNodeTypes` and in load/save so existing XML still round-trips, but it can't be picked from the type combo anymore.

## Validation

`Validate()` ports verbatim from `FrmTalkTree.cs:661`:

- node IDs must be unique
- the first node must have id "1"
- the first node cannot have a Trade reply
- every NPC node must have non-empty text and at least one reply
- every Branch must target an existing node and have non-empty text
- `Mission_Goto_Stage` flags must reference a known stage (or the `-2` repeatable-mission sentinel)
- every node must be reachable from node 1 via branches

## Building & running

```bash
dotnet build                                    # from this directory
dotnet run -- --smoke                           # headless smoke test (no DB, no XML needed)
dotnet run -- '<Tree id="1"><Text>hi</Text>...' # interactive with an initial conversation
dotnet run                                      # interactive with an empty conversation
```

Smoke output:

```
main     OK: 940x450 "Talk Tree"
branch   OK: ctor
trade    OK: ctor
flag     OK: ctor
smoke OK: all 4 talktreeeditor-avalonia controls instantiated
```

## License

CC BY-NC-SA 3.0 — see project root `LICENSES/Net7`. Original Net-7 Entertainment headers in ported source files are preserved unchanged.
