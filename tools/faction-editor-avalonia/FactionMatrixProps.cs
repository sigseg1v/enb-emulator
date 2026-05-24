namespace FactionEditorAvalonia
{
    // Verbatim port of tools/faction-editor/FactionMatrixProps.cs, minus
    // the WinForms PropertyGrid attributes (CategoryAttribute /
    // DescriptionAttribute / BrowsableAttribute / ReadOnlyAttribute) —
    // Avalonia has no PropertyGrid control. The descriptions migrate
    // into the Avalonia UI's tooltips/labels instead.
    public sealed class FactionMatrixProps
    {
        public int  ID { get; set; }
        public int  FactionID { get; set; }
        public int  FactionEntryID { get; set; }
        public int  BaseValue { get; set; }
        public int  CurrentValue { get; set; }   // read-only: current live-server value
        public bool RewardFaction { get; set; }
    }
}
