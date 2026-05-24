namespace FactionEditorAvalonia
{
    // Lifted out of MainWindow so the AXAML compiled binding for
    // c_FactionGrid can target it via x:DataType. Avalonia's compiled
    // binding pipeline can't address nested types cleanly.
    public sealed class FactionRow
    {
        public int    FactionID { get; set; }
        public string Name      { get; set; }
    }
}
