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
    public partial class DlgConditionsWindow : Window
    {
        bool m_madeSelection;
        Condition m_condition;

        public DlgConditionsWindow()
        {
            InitializeComponent();

            CommonTools.Enumeration.AddSortedByName<CommonTools.ConditionType>(c_TypeCbo);
            c_TypeCbo.SelectionChanged += OnTypeSelected;
            if (c_TypeCbo.ItemCount > 0) c_TypeCbo.SelectedIndex = 0;

            Opened += (_, _) =>
            {
                m_madeSelection = false;
                if (m_condition == null && c_TypeCbo.ItemCount > 0) c_TypeCbo.SelectedIndex = 0;
            };
        }

        public void editCondition(Condition condition)
        {
            m_condition = condition;
            c_TypeCbo.SelectedItem = m_condition.getConditionType();
            switch (m_condition.getConditionType())
            {
                case ConditionType.Overall_Level:
                case ConditionType.Combat_Level:
                case ConditionType.Explore_Level:
                case ConditionType.Trade_Level:
                case ConditionType.Hull_Level:
                    c_AmountTxt.Text = m_condition.getValue();
                    break;
                case ConditionType.Faction_Required:
                case ConditionType.Item_Required:
                    c_CodeTxt.Text = m_condition.getValue();
                    c_AmountTxt.Text = m_condition.getFlag();
                    break;
                case ConditionType.Profession:
                    if (Int32.TryParse(m_condition.getValue(), out int p))
                        c_ValueCbo.SelectedItem = (Professions)p;
                    break;
                case ConditionType.Race:
                    if (Int32.TryParse(m_condition.getValue(), out int r))
                        c_ValueCbo.SelectedItem = (Races)r;
                    break;
                case ConditionType.Mission_Required:
                    c_CodeTxt.Text = m_condition.getValue();
                    break;
            }
        }

        DataConfiguration.DataType m_codeDataType;

        void OnTypeSelected(object sender, SelectionChangedEventArgs e)
        {
            c_ValueCbo.ItemsSource = null;
            c_ValueCbo.IsEnabled = false;
            c_CodeTxt.IsEnabled = false;
            c_CodeSearchBtn.IsEnabled = false;
            c_AmountTxt.IsEnabled = false;

            if (c_TypeCbo.SelectedItem is not ConditionType conditionType) return;
            switch (conditionType)
            {
                case ConditionType.Overall_Level:
                case ConditionType.Combat_Level:
                case ConditionType.Explore_Level:
                case ConditionType.Trade_Level:
                case ConditionType.Hull_Level:
                    c_AmountTxt.IsEnabled = true;
                    break;
                case ConditionType.Faction_Required:
                    c_CodeTxt.IsEnabled = true;
                    c_CodeSearchBtn.IsEnabled = true;
                    c_AmountTxt.IsEnabled = true;
                    m_codeDataType = DataConfiguration.DataType.faction;
                    break;
                case ConditionType.Item_Required:
                    c_CodeTxt.IsEnabled = true;
                    c_CodeSearchBtn.IsEnabled = true;
                    c_AmountTxt.IsEnabled = true;
                    m_codeDataType = DataConfiguration.DataType.item;
                    break;
                case ConditionType.Profession:
                    c_ValueCbo.IsEnabled = true;
                    CommonTools.Enumeration.AddSortedByName<Professions>(c_ValueCbo);
                    c_ValueCbo.SelectedIndex = 0;
                    break;
                case ConditionType.Race:
                    c_ValueCbo.IsEnabled = true;
                    CommonTools.Enumeration.AddSortedByName<Races>(c_ValueCbo);
                    c_ValueCbo.SelectedIndex = 0;
                    break;
                case ConditionType.Mission_Required:
                    c_CodeTxt.IsEnabled = true;
                    c_CodeSearchBtn.IsEnabled = true;
                    m_codeDataType = DataConfiguration.DataType.mission;
                    break;
            }
        }

        async void OnCodeSearch(object sender, RoutedEventArgs e)
        {
            string id = await DataConfiguration.search(m_codeDataType, this);
            if (!string.IsNullOrEmpty(id))
            {
                c_CodeTxt.Text = id;
                try
                {
                    c_CodeDescriptionTxt.Text = DataConfiguration.getDescription(m_codeDataType, id);
                }
                catch { c_CodeDescriptionTxt.Text = id; }
            }
        }

        async void OnOk(object sender, RoutedEventArgs e)
        {
            m_condition = new Condition();
            if (c_TypeCbo.SelectedItem is not ConditionType conditionType) return;
            m_condition.setConditionType(conditionType);
            switch (conditionType)
            {
                case ConditionType.Overall_Level:
                case ConditionType.Combat_Level:
                case ConditionType.Explore_Level:
                case ConditionType.Trade_Level:
                case ConditionType.Hull_Level:
                    m_condition.setValue(c_AmountTxt.Text ?? "");
                    break;
                case ConditionType.Faction_Required:
                case ConditionType.Item_Required:
                    m_condition.setValue(c_AmountTxt.Text ?? "");
                    m_condition.setFlag(c_CodeTxt.Text ?? "");
                    break;
                case ConditionType.Profession:
                    if (c_ValueCbo.SelectedItem is Professions p)
                        m_condition.setValue(((int)p).ToString());
                    break;
                case ConditionType.Race:
                    if (c_ValueCbo.SelectedItem is Races r)
                        m_condition.setValue(((int)r).ToString());
                    break;
                case ConditionType.Mission_Required:
                    m_condition.setValue(c_CodeTxt.Text ?? "");
                    break;
            }

            string error;
            m_condition.addValidations();
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

        public bool getValues(out Condition condition)
        {
            condition = m_condition;
            return m_madeSelection;
        }
    }
}
