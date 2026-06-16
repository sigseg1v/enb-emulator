using System;
using System.Data;
using Avalonia.Data;
using Avalonia.Data.Core.Plugins;

namespace CommonTools.Gui
{
    // Teaches Avalonia's binding system how to read a System.Data.DataRowView
    // by column name.
    //
    // Avalonia's DataGrid has no native DataTable/DataView support, and -- more
    // subtly -- its binding engine cannot resolve a DataRowView's columns at
    // all out of the box: neither the indexer form ("[col]") nor the
    // plain-name form ("col") resolves, because DataRowView exposes its columns
    // through ICustomTypeDescriptor / a string indexer that Avalonia's default
    // reflection accessor does not consult. The result is a grid that shows the
    // right column HEADERS but completely EMPTY cells (every binding silently
    // yields null).
    //
    // This plugin closes that gap: when the bound object is a DataRowView, a
    // "col" binding path reads/writes drv[col]. Register() installs it once at
    // the front of the accessor chain so it wins for DataRowView before the
    // default reflection plugin (which would otherwise match and return null).
    public sealed class DataRowViewAccessorPlugin : IPropertyAccessorPlugin
    {
        private static bool s_registered;

        // Idempotent: safe to call from every Bind()/grid setup path.
        public static void Register()
        {
            if (s_registered) return;
            s_registered = true;
            BindingPlugins.PropertyAccessors.Insert(0, new DataRowViewAccessorPlugin());
        }

        public bool Match(object obj, string propertyName) => obj is DataRowView;

        public IPropertyAccessor Start(WeakReference<object> reference, string propertyName)
        {
            reference.TryGetTarget(out var target);
            return new Accessor(target as DataRowView, propertyName);
        }

        private sealed class Accessor : IPropertyAccessor
        {
            private readonly DataRowView _drv;
            private readonly string _col;

            public Accessor(DataRowView drv, string col) { _drv = drv; _col = col; }

            public Type PropertyType => typeof(object);

            public object Value =>
                _drv != null && _drv.Row.Table.Columns.Contains(_col) ? _drv[_col] : null;

            public bool SetValue(object value, BindingPriority priority)
            {
                if (_drv == null || !_drv.Row.Table.Columns.Contains(_col)) return false;
                _drv[_col] = value ?? DBNull.Value;
                return true;
            }

            // Read-back is one-shot: the editor grids are populated then read on
            // selection; no in-grid live re-read of an externally mutated row is
            // needed. SetValue keeps two-way edits working for the editable grids.
            public void Subscribe(Action<object> listener) => listener(Value);
            public void Unsubscribe() { }
            public void Dispose() { }
        }
    }
}
