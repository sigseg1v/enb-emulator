namespace MobEditorAvalonia
{
    // Top-level type so DataGrid x:DataType resolves cleanly under
    // AvaloniaUseCompiledBindingsByDefault.
    public sealed class MobRow
    {
        public int    MobID { get; set; }
        public string Name  { get; set; }
        public int    Level { get; set; }
    }
}
