using System;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommonTools;
using CommonTools.Database;
using CommonTools.Gui;

namespace DataImportAvalonia
{
    // Avalonia port of tools/dataimport/DataImport.cs. The form is
    // tiny — a Tables ComboBox, a file-path TextBox + Browse button,
    // and Import/Close. The actual work is in DB.Instance.importValues
    // (in commontools-avalonia/Database/DB.cs), so this class just
    // wires controls to that call.
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // Title carries the editor version, same as the WinForms ctor did.
            Title = Title + " " + LoginData.ApplicationVersion;

            guiFileBtn.Click   += async (_, _) => await OnBrowse();
            guiImportBtn.Click += (_, _) => OnImport();
            guiCloseBtn.Click  += (_, _) => Close();

            Opened += (_, _) =>
            {
                Enumeration.AddSortedByName<Net7.Tables>(guiTableCbo);
                if (guiTableCbo.ItemCount > 0)
                    guiTableCbo.SelectedIndex = 0;
            };
        }

        async System.Threading.Tasks.Task OnBrowse()
        {
            var sp = StorageProvider;
            if (sp == null) return;
            var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select values file",
                AllowMultiple = false
            });
            if (files.Count > 0)
            {
                var path = files[0].TryGetLocalPath();
                if (!string.IsNullOrEmpty(path)) guiFileTxt.Text = path;
            }
        }

        void OnImport()
        {
            if (guiTableCbo.SelectedItem is Net7.Tables table)
                DB.Instance.importValues(table, guiFileTxt.Text);
        }
    }
}
