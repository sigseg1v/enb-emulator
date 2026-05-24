using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using MissionEditorAvalonia.Database;
using MissionEditorAvalonia.Nodes;
using MsBox.Avalonia;
using MsBoxIcon = MsBox.Avalonia.Enums.Icon;

namespace MissionEditorAvalonia
{
    public partial class DlgStagesWindow : Window
    {
        bool m_madeSelection;
        Stage m_stage;

        public DlgStagesWindow()
        {
            InitializeComponent();
            Opened += (_, _) =>
            {
                c_DescriptionTxt.Text = "";
                m_madeSelection = false;
                c_DescriptionTxt.Focus();
            };
        }

        public void setId(int id) { c_IdTxt.Text = id.ToString(); }

        public void editStage(Stage stage) { m_stage = stage; }

        async void OnOk(object sender, RoutedEventArgs e)
        {
            m_stage = new Stage();
            m_stage.setId(c_IdTxt.Text ?? "");
            m_stage.setDescription(c_DescriptionTxt.Text ?? "");

            string error;
            m_stage.addValidations(Stage.ValidationType.FromDialog);
            if (DataConfiguration.validate(out error))
            {
                m_madeSelection = true;
                Close();
            }
            else
            {
                await MessageBoxManager.GetMessageBoxStandard("Validation", error, MsBox.Avalonia.Enums.ButtonEnum.Ok, MsBoxIcon.Warning).ShowWindowDialogAsync(this);
            }
        }

        void OnCancel(object sender, RoutedEventArgs e) => Close();

        public bool getValues(out Stage stage)
        {
            stage = m_stage;
            return m_madeSelection;
        }
    }
}
