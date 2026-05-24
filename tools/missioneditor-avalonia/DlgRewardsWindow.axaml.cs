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
    public partial class DlgRewardsWindow : Window
    {
        bool m_madeSelection;
        Reward m_reward;
        DataConfiguration.DataType m_codeDataType;

        public DlgRewardsWindow()
        {
            InitializeComponent();

            CommonTools.Enumeration.AddSortedByName<RewardType>(c_TypeCbo);
            c_TypeCbo.SelectionChanged += OnTypeSelected;
            if (c_TypeCbo.ItemCount > 0) c_TypeCbo.SelectedIndex = 0;

            Opened += (_, _) => m_madeSelection = false;
        }

        public void editReward(Reward reward) { m_reward = reward; }

        void OnTypeSelected(object sender, SelectionChangedEventArgs e)
        {
            c_AmountTxt.IsEnabled = false;
            c_CodeTxt.IsEnabled = false;
            c_CodeSearchBtn.IsEnabled = false;

            if (c_TypeCbo.SelectedItem is not RewardType rewardType) return;
            switch (rewardType)
            {
                case RewardType.Credits:
                case RewardType.Explore_XP:
                case RewardType.Combat_XP:
                case RewardType.Trade_XP:
                case RewardType.Hull_Upgrade:
                case RewardType.Run_Script:
                    c_AmountTxt.IsEnabled = true;
                    break;
                case RewardType.Award_Skill:
                    c_CodeTxt.IsEnabled = true; c_CodeSearchBtn.IsEnabled = true;
                    m_codeDataType = DataConfiguration.DataType.skill;
                    break;
                case RewardType.Faction:
                    c_CodeTxt.IsEnabled = true; c_CodeSearchBtn.IsEnabled = true;
                    c_AmountTxt.IsEnabled = true;
                    m_codeDataType = DataConfiguration.DataType.faction;
                    break;
                case RewardType.Item_ID:
                    c_CodeTxt.IsEnabled = true; c_CodeSearchBtn.IsEnabled = true;
                    m_codeDataType = DataConfiguration.DataType.item;
                    break;
                case RewardType.Advance_Mission:
                    c_CodeTxt.IsEnabled = true; c_CodeSearchBtn.IsEnabled = true;
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
                try { c_CodeDescriptionTxt.Text = DataConfiguration.getDescription(m_codeDataType, id); }
                catch { c_CodeDescriptionTxt.Text = id; }
            }
        }

        async void OnOk(object sender, RoutedEventArgs e)
        {
            m_reward = new Reward();
            if (c_TypeCbo.SelectedItem is not RewardType rewardType) return;
            m_reward.setRewardType(rewardType);
            switch (rewardType)
            {
                case RewardType.Award_Skill:
                case RewardType.Item_ID:
                case RewardType.Advance_Mission:
                    m_reward.setValue(c_CodeTxt.Text ?? "");
                    break;
                case RewardType.Credits:
                case RewardType.Explore_XP:
                case RewardType.Combat_XP:
                case RewardType.Trade_XP:
                case RewardType.Hull_Upgrade:
                case RewardType.Run_Script:
                    m_reward.setValue(c_AmountTxt.Text ?? "");
                    break;
                case RewardType.Faction:
                    m_reward.setValue(c_AmountTxt.Text ?? "");
                    m_reward.setFlag(c_CodeTxt.Text ?? "");
                    break;
            }

            string error;
            m_reward.addValidations();
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

        public bool getValues(out Reward reward)
        {
            reward = m_reward;
            return m_madeSelection;
        }
    }
}
