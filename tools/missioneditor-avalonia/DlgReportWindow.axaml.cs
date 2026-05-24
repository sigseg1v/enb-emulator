using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace MissionEditorAvalonia
{
    // Renders the HTML report as plain text (Avalonia has no built-in HtmlBrowser);
    // matches the WinForms DlgReport's role of being a viewer only.
    public partial class DlgReportWindow : Window
    {
        public DlgReportWindow() => InitializeComponent();

        public void set(string html) => c_ReportTxt.Text = html;

        void OnClose(object sender, RoutedEventArgs e) => Close();
    }
}
