using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using CommonTools;
using MissionEditorAvalonia.Database;
using MissionEditorAvalonia.Nodes;
using MsBox.Avalonia;
using MsBoxIcon = MsBox.Avalonia.Enums.Icon;

namespace MissionEditorAvalonia
{
    public partial class DlgCompletionsWindow : Window
    {
        bool m_madeSelection;
        Completion m_completion;
        DataConfiguration.DataType m_valueDataType;
        DataConfiguration.DataType m_dataDataType;

        public DlgCompletionsWindow()
        {
            InitializeComponent();

            CommonTools.Enumeration.AddSortedByName<CompletionType>(c_TypeCbo);
            c_TypeCbo.SelectionChanged += OnTypeSelected;
            if (c_TypeCbo.ItemCount > 0) c_TypeCbo.SelectedIndex = 0;

            Opened += (_, _) => m_madeSelection = false;
        }

        public void editCompletion(Completion completion)
        {
            m_completion = completion;
            c_TypeCbo.SelectedItem = m_completion.getCompletionType();
        }

        void OnTypeSelected(object sender, SelectionChangedEventArgs e)
        {
            c_ValueTxt.IsEnabled = false;
            c_ValueSearchBtn.IsEnabled = false;
            c_DataTxt.IsEnabled = false;
            c_DataSearchBtn.IsEnabled = false;
            c_AmountTxt.IsEnabled = false;

            if (c_TypeCbo.SelectedItem is not CompletionType completionType) return;
            switch (completionType)
            {
                case CompletionType.Arrive_At:
                case CompletionType.Proximity_To_Space_Npc:
                case CompletionType.Talk_Space_Npc:
                case CompletionType.Nearest_Nav:
                    c_ValueTxt.IsEnabled = true; c_ValueSearchBtn.IsEnabled = true;
                    m_valueDataType = DataConfiguration.DataType.sector_object;
                    break;
                case CompletionType.Nav_Message:
                    c_ValueTxt.IsEnabled = true; c_ValueSearchBtn.IsEnabled = true;
                    m_valueDataType = DataConfiguration.DataType.sector_object;
                    c_AmountTxt.IsEnabled = true;
                    break;
                case CompletionType.Fight_Mob:
                    c_ValueTxt.IsEnabled = true; c_ValueSearchBtn.IsEnabled = true;
                    m_valueDataType = DataConfiguration.DataType.mob;
                    c_AmountTxt.IsEnabled = true;
                    break;
                case CompletionType.Give_Credits:
                    c_AmountTxt.IsEnabled = true;
                    break;
                case CompletionType.Give_Item:
                    c_ValueTxt.IsEnabled = true; c_ValueSearchBtn.IsEnabled = true;
                    m_valueDataType = DataConfiguration.DataType.item;
                    c_AmountTxt.IsEnabled = true;
                    break;
                case CompletionType.Obtain_Items:
                    c_ValueTxt.IsEnabled = true; c_ValueSearchBtn.IsEnabled = true;
                    m_valueDataType = DataConfiguration.DataType.item;
                    break;
                case CompletionType.Possess_Item:
                    c_ValueTxt.IsEnabled = true; c_ValueSearchBtn.IsEnabled = true;
                    m_valueDataType = DataConfiguration.DataType.item;
                    c_AmountTxt.IsEnabled = true;
                    break;
                case CompletionType.Receive_Item:
                    c_AmountTxt.IsEnabled = true;
                    break;
                case CompletionType.Current_Sector:
                    c_ValueTxt.IsEnabled = true; c_ValueSearchBtn.IsEnabled = true;
                    m_valueDataType = DataConfiguration.DataType.sector;
                    break;
                case CompletionType.Talk_To_Npc:
                    c_ValueTxt.IsEnabled = true; c_ValueSearchBtn.IsEnabled = true;
                    m_valueDataType = DataConfiguration.DataType.npc;
                    break;
                case CompletionType.Use_Skill_On_Mob_Type:
                    c_ValueTxt.IsEnabled = true; c_ValueSearchBtn.IsEnabled = true;
                    m_valueDataType = DataConfiguration.DataType.mob;
                    c_DataTxt.IsEnabled = true; c_DataSearchBtn.IsEnabled = true;
                    m_dataDataType = DataConfiguration.DataType.skill;
                    break;
                case CompletionType.Use_Skill_On_Object:
                    c_ValueTxt.IsEnabled = true; c_ValueSearchBtn.IsEnabled = true;
                    m_valueDataType = DataConfiguration.DataType.sector_object;
                    c_DataTxt.IsEnabled = true; c_DataSearchBtn.IsEnabled = true;
                    m_dataDataType = DataConfiguration.DataType.skill;
                    break;
            }
        }

        async void OnValueSearch(object sender, RoutedEventArgs e)
        {
            string id = await DataConfiguration.search(m_valueDataType, this);
            if (!string.IsNullOrEmpty(id))
            {
                c_ValueTxt.Text = id;
                try { c_ValueDescriptionTxt.Text = DataConfiguration.getDescription(m_valueDataType, id); }
                catch { c_ValueDescriptionTxt.Text = id; }
            }
        }

        async void OnDataSearch(object sender, RoutedEventArgs e)
        {
            string id = await DataConfiguration.search(m_dataDataType, this);
            if (!string.IsNullOrEmpty(id))
            {
                c_DataTxt.Text = id;
                try { c_DataDescriptionTxt.Text = DataConfiguration.getDescription(m_dataDataType, id); }
                catch { c_DataDescriptionTxt.Text = id; }
            }
        }

        async void OnOk(object sender, RoutedEventArgs e)
        {
            m_completion = new Completion();
            if (c_TypeCbo.SelectedItem is not CompletionType completionType) return;
            m_completion.setCompletionType(completionType);

            string value = c_ValueTxt.IsEnabled ? (c_ValueTxt.Text ?? "") : "";
            string data  = c_DataTxt.IsEnabled  ? (c_DataTxt.Text  ?? "") : "";
            Int32  count = -1;
            if (c_AmountTxt.IsEnabled && !Int32.TryParse(c_AmountTxt.Text, out count)) count = -1;

            switch (completionType)
            {
                case CompletionType.Arrive_At:
                case CompletionType.Proximity_To_Space_Npc:
                case CompletionType.Talk_Space_Npc:
                case CompletionType.Nearest_Nav:
                case CompletionType.Obtain_Items:
                case CompletionType.Current_Sector:
                case CompletionType.Talk_To_Npc:
                    m_completion.setValue(value);
                    break;
                case CompletionType.Nav_Message:
                case CompletionType.Fight_Mob:
                case CompletionType.Give_Item:
                case CompletionType.Possess_Item:
                    m_completion.setValue(value);
                    m_completion.setCount(count);
                    break;
                case CompletionType.Give_Credits:
                case CompletionType.Receive_Item:
                    m_completion.setValue(count.ToString());
                    break;
                case CompletionType.Use_Skill_On_Mob_Type:
                case CompletionType.Use_Skill_On_Object:
                    m_completion.setValue(value);
                    m_completion.setData(data);
                    break;
            }

            string error;
            m_completion.addValidations();
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

        public bool getValues(out Completion completion)
        {
            completion = m_completion;
            return m_madeSelection;
        }
    }
}
