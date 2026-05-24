using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MobEditorAvalonia
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
