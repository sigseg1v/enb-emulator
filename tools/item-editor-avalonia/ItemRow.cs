using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ItemEditorAvalonia
{
    // Top-level type so DataGrid x:DataType resolves cleanly under
    // AvaloniaUseCompiledBindingsByDefault. Implements INotifyPropertyChanged
    // so an edit in the detail panel (name/level) updates the grid row in place.
    public sealed class ItemRow : INotifyPropertyChanged
    {
        long _itemID;
        string _name;
        int _level;
        int _category;

        public long ItemID { get => _itemID; set => Set(ref _itemID, value); }
        public string Name { get => _name; set => Set(ref _name, value); }
        public int Level { get => _level; set => Set(ref _level, value); }
        public int Category { get => _category; set => Set(ref _category, value); }

        public event PropertyChangedEventHandler PropertyChanged;

        void Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
