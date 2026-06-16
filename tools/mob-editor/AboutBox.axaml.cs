using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MobEditor
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
