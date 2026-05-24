using Avalonia.Controls;
using Avalonia.Interactivity;

namespace FactionEditorAvalonia
{
    public partial class AboutBox : Window
    {
        public AboutBox()
        {
            InitializeComponent();
        }

        void OnOkClick(object sender, RoutedEventArgs e) => Close();
    }
}
