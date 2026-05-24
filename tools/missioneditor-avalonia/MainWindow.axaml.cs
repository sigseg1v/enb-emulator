using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Xml;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using CommonTools;
using CommonTools.Database;
using CommonTools.Gui;
using MissionEditorAvalonia.Database;
using MissionEditorAvalonia.Nodes;
using MsBox.Avalonia;
using MsBoxIcon = MsBox.Avalonia.Enums.Icon;

namespace MissionEditorAvalonia
{
    // Avalonia port of tools/missioneditor/Gui/FrmMission.cs + TabMission.cs +
    // TabStages.cs. The original was three separate WinForms classes (a Form
    // with two embedded UserControls); this collapses them into a single
    // Window with a TabControl, which works better with Avalonia's compiled
    // bindings and reduces cross-control plumbing.
    //
    // Dropped vs. the original:
    //   * Cursor changes (WaitCursor) — Avalonia handles this differently
    //     and the original status text already conveys progress.
    //   * The MessageBox.Show("onConditionSelected") debug calls from the
    //     original TabMission/TabStages — they were obviously left over
    //     from development.
    //   * Embedded FrmTalkTree — replaced by Process.Start of the sibling
    //     talktreeeditor-avalonia project (same pattern as station-tools).
    //     Round-trip back into the stage's talk tree is not yet wired
    //     because talktreeeditor-avalonia does not return its result.
    public partial class MainWindow : Window
    {
        enum State { View, Edit, Add }

        Mission m_mission;
        Stage m_stage;
        State m_state;
        string m_currentMissionId;
        bool m_fieldChangesMuted;

        DlgConditionsWindow m_dlgConditions;
        DlgStagesWindow m_dlgStages;
        DlgCompletionsWindow m_dlgCompletions;
        DlgRewardsWindow m_dlgRewards;
        DlgReportWindow m_dlgReport;
        DlgSearch m_dlgSearch;
        DlgEditXml m_dlgEditXml;

        public MainWindow()
        {
            InitializeComponent();
            Title = Title + " " + LoginData.ApplicationVersion;

            m_mission = new Mission();
            m_fieldChangesMuted = false;

            m_dlgConditions = new DlgConditionsWindow();
            m_dlgStages = new DlgStagesWindow();
            m_dlgCompletions = new DlgCompletionsWindow();
            m_dlgRewards = new DlgRewardsWindow();
            m_dlgReport = new DlgReportWindow();
            m_dlgEditXml = new DlgEditXml();

            try
            {
                DataConfiguration.init();
                m_dlgSearch = new DlgSearch();
                m_dlgSearch.configure(Net7.Tables.missions);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("MainWindow init DB failed: " + ex.Message);
            }

            Enumeration.AddSortedByName<MissionType>(c_TypeCbo);

            new TableButtonHandler(c_ConditionsTbl, c_ConditionsAddBtn, c_ConditionsRemoveBtn, c_ConditionsEditBtn, c_ConditionUpBtn, c_ConditionDownBtn);
            c_ConditionUpBtn.Click += OnConditionReordered;
            c_ConditionDownBtn.Click += OnConditionReordered;

            new TableButtonHandler(c_StagesTbl, c_StagesAddBtn, c_StagesRemoveBtn, null, c_StageUpBtn, c_StageDownBtn);
            c_StageUpBtn.Click += OnStageReordered;
            c_StageDownBtn.Click += OnStageReordered;

            new TableButtonHandler(c_CompletionsTbl, c_CompletionsAddBtn, c_CompletionsRemoveBtn, c_CompletionsEditBtn, c_CompletionUpBtn, c_CompletionDownBtn);
            c_CompletionUpBtn.Click += OnCompletionReordered;
            c_CompletionDownBtn.Click += OnCompletionReordered;

            new TableButtonHandler(c_RewardsTbl, c_RewardsAddBtn, c_RewardsRemoveBtn, c_RewardsEditBtn, c_RewardUpBtn, c_RewardDownBtn);
            c_RewardUpBtn.Click += OnRewardReordered;
            c_RewardDownBtn.Click += OnRewardReordered;

            c_StagesTbl.SelectionChanged += OnStageTableSelected;

            c_KeyTxt.TextChanged += OnFieldChanged;
            c_NameTxt.TextChanged += OnFieldChanged;
            c_SummaryTxt.TextChanged += OnFieldChanged;
            c_TimeTxt.TextChanged += OnTimeChanged;
            c_ForfeitableChk.IsCheckedChanged += OnForfeitableChanged;
            c_TypeCbo.SelectionChanged += OnTypeChanged;
            c_DescriptionTxt.TextChanged += OnDescriptionChanged;

            Opened += (_, _) =>
            {
                try
                {
                    string firstId = Database.Database.getFirstMissionId();
                    if (firstId != null) loadMission(firstId);
                }
                catch (Exception ex) { Console.Error.WriteLine("initial load failed: " + ex.Message); }
            };

            setState(State.View);
        }

        // ----- state -----

        void setState(State state)
        {
            m_state = state;
            bool view = state == State.View;
            c_AddBtn.IsEnabled = view;
            c_DeleteBtn.IsEnabled = view;
            c_SaveBtn.IsEnabled = !view;
            c_CancelBtn.IsEnabled = !view;
            c_EditBtn.IsEnabled = view;
            c_SearchBtn.IsEnabled = view;
            c_SearchTxt.IsEnabled = view;
        }

        void setMuteFieldChanges(bool mute, out bool wasMuted)
        {
            wasMuted = m_fieldChangesMuted;
            m_fieldChangesMuted = mute;
        }

        void onChanged()
        {
            if (!m_fieldChangesMuted && m_state == State.View) setState(State.Edit);
        }

        // ----- loading -----

        public void loadMission(string missionId)
        {
            bool wasMuted;
            setMuteFieldChanges(true, out wasMuted);
            if (missionId != null)
            {
                c_StatusLbl.Text = "Retrieving mission " + missionId;
                try { m_mission = Database.Database.getMission(missionId); }
                catch (Exception ex) { Console.Error.WriteLine("getMission failed: " + ex.Message); m_mission = null; }
            }
            if (m_mission == null)
            {
                Show("The mission id '" + missionId + "' does not exist");
            }
            else
            {
                try
                {
                    DateTime start = DateTime.Now;
                    c_StatusLbl.Text = "Parsing mission " + m_mission.getId();
                    m_mission.parseXml();
                    string error;
                    bool dataIsValid = DataConfiguration.validate(out error);
                    TimeSpan span = DateTime.Now - start;
                    c_StatusLbl.Text = span.TotalMilliseconds + " milliseconds";
                    populateMissionTab();
                    populateStagesTab();
                    if (!dataIsValid) Show(error);
                    m_currentMissionId = m_mission.getId();
                    c_SearchTxt.Text = m_currentMissionId;
                }
                catch (XmlException xmlEx) { Show(xmlEx.Message); }
                catch (Exception ex) { Show(ex.Message); }
            }
            setMuteFieldChanges(wasMuted, out wasMuted);
        }

        void populateMissionTab()
        {
            c_IdTxt.Text = m_mission.getId();
            c_TypeCbo.SelectedItem = m_mission.getType();
            c_KeyTxt.Text = m_mission.getKey();
            c_NameTxt.Text = m_mission.getName();
            c_SummaryTxt.Text = m_mission.getSummary();
            c_TimeTxt.Text = m_mission.getAllowedTime().ToString();
            c_ForfeitableChk.IsChecked = m_mission.isForfeitable();

            c_ConditionsTbl.Items.Clear();
            if (m_mission.hasConditions())
            {
                foreach (var cond in m_mission.getConditions())
                {
                    c_ConditionsTbl.Items.Add(new ConditionRow(cond));
                }
                c_ConditionsTbl.SelectedIndex = 0;
            }

            c_StagesTbl.Items.Clear();
            if (m_mission.hasStages())
            {
                foreach (var stage in m_mission.getStages())
                {
                    c_StagesTbl.Items.Add(new StageRow(stage));
                }
                c_StagesTbl.SelectedIndex = 0;
            }
        }

        void populateStagesTab()
        {
            c_StagesCbo.Items.Clear();
            c_DescriptionTxt.Text = "";
            c_CompletionsTbl.Items.Clear();
            c_RewardsTbl.Items.Clear();
            if (m_mission.hasStages())
            {
                foreach (var stage in m_mission.getStages()) c_StagesCbo.Items.Add(stage);
                selectStage(m_mission.getStages()[0]);
            }
        }

        void selectStage(Stage stage)
        {
            bool wasMuted;
            setMuteFieldChanges(true, out wasMuted);
            m_stage = stage;
            c_StagesCbo.SelectedItem = stage;
            c_DescriptionTxt.IsEnabled = stage.getId().CompareTo("0") != 0;
            c_DescriptionTxt.Text = stage.getDescription();
            c_CompletionsTbl.Items.Clear();
            if (stage.hasCompletions())
            {
                foreach (var c in stage.getCompletions()) c_CompletionsTbl.Items.Add(new CompletionRow(c));
                c_CompletionsTbl.SelectedIndex = 0;
            }
            c_RewardsTbl.Items.Clear();
            if (stage.hasRewards())
            {
                foreach (var r in stage.getRewards()) c_RewardsTbl.Items.Add(new RewardRow(r));
                c_RewardsTbl.SelectedIndex = 0;
            }
            setMuteFieldChanges(wasMuted, out wasMuted);
        }

        // ----- toolbar handlers -----

        void OnRecordAdd(object sender, RoutedEventArgs e)
        {
            setState(State.Add);
            if (m_mission == null) m_mission = new Mission();
            m_mission.clear();
            try { m_mission.setId(Database.Database.getNextMissionId()); }
            catch (Exception ex) { Console.Error.WriteLine("getNextMissionId failed: " + ex.Message); m_mission.setId(""); }
            m_mission.setName("");
            bool wasMuted;
            setMuteFieldChanges(true, out wasMuted);
            populateMissionTab();
            populateStagesTab();
            setMuteFieldChanges(wasMuted, out wasMuted);
            c_TabControl.SelectedIndex = 0;
        }

        async void OnRecordDelete(object sender, RoutedEventArgs e)
        {
            var box = MessageBoxManager.GetMessageBoxStandard(
                "Deletion Confirmation",
                "Do you really want to delete this record?",
                MsBox.Avalonia.Enums.ButtonEnum.YesNo,
                MsBoxIcon.Question);
            var result = await box.ShowWindowDialogAsync(this);
            if (result == MsBox.Avalonia.Enums.ButtonResult.Yes)
            {
                try
                {
                    Database.Database.deleteMission(m_mission);
                    loadMission(Database.Database.getFirstMissionId());
                }
                catch (Exception ex) { Show(ex.Message); }
            }
        }

        void OnRecordSave(object sender, RoutedEventArgs e)
        {
            m_mission.addFullValidations();
            string error;
            if (DataConfiguration.validate(out error))
            {
                try
                {
                    if (m_state == State.Add)
                    {
                        Database.Database.setMission(m_mission, true);
                        populateMissionTab();
                        m_currentMissionId = m_mission.getId();
                        c_SearchTxt.Text = m_currentMissionId;
                    }
                    else
                    {
                        Database.Database.setMission(m_mission, false);
                    }
                    setState(State.View);
                }
                catch (Exception ex) { Show(ex.Message); }
            }
            else
            {
                Show(error);
            }
        }

        void OnRecordUndo(object sender, RoutedEventArgs e)
        {
            setState(State.View);
            loadMission(m_currentMissionId);
        }

        async void OnRecordEdit(object sender, RoutedEventArgs e)
        {
            m_dlgEditXml.setXml(m_mission.getXML());
            await m_dlgEditXml.ShowDialog(this);
            string xml;
            if (m_dlgEditXml.getValues(out xml))
            {
                setState(State.Edit);
                m_mission.clear();
                m_mission.setXml(xml);
                loadMission(null);
            }
        }

        async void OnRecordSearch(object sender, RoutedEventArgs e)
        {
            if (m_dlgSearch == null) { Show("Search is unavailable (DB init failed)"); return; }
            await m_dlgSearch.ShowDialog(this);
            string selected = m_dlgSearch.getSelectedId();
            if (selected.Length != 0)
            {
                c_TabControl.SelectedIndex = 0;
                c_SearchTxt.Text = selected;
                loadMission(selected);
            }
        }

        void OnSearchKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                c_TabControl.SelectedIndex = 0;
                loadMission(c_SearchTxt.Text);
            }
        }

        async void OnRecordReport(object sender, RoutedEventArgs e)
        {
            try { m_dlgReport.set(m_mission.getReport()); }
            catch (Exception ex) { Show(ex.Message); return; }
            await m_dlgReport.ShowDialog(this);
        }

        // ----- mission tab field handlers -----

        void OnFieldChanged(object sender, TextChangedEventArgs e)
        {
            if (m_fieldChangesMuted || m_mission == null) return;
            if (sender == c_KeyTxt) m_mission.setKey(c_KeyTxt.Text ?? "");
            else if (sender == c_NameTxt) m_mission.setName(c_NameTxt.Text ?? "");
            else if (sender == c_SummaryTxt) m_mission.setSummary(c_SummaryTxt.Text ?? "");
            onChanged();
        }

        void OnTimeChanged(object sender, TextChangedEventArgs e)
        {
            if (m_fieldChangesMuted || m_mission == null) return;
            Int32 time;
            if (!Int32.TryParse(c_TimeTxt.Text ?? "", out time)) time = 0;
            m_mission.setAllowedTime(time);
            onChanged();
        }

        void OnForfeitableChanged(object sender, RoutedEventArgs e)
        {
            if (m_fieldChangesMuted || m_mission == null) return;
            m_mission.setForfeitable(c_ForfeitableChk.IsChecked ?? false);
            onChanged();
        }

        void OnTypeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (m_fieldChangesMuted || m_mission == null) return;
            if (c_TypeCbo.SelectedItem is MissionType t) m_mission.setType(t);
            onChanged();
        }

        async void OnKeySearch(object sender, RoutedEventArgs e)
        {
            if (c_TypeCbo.SelectedItem is not MissionType t) return;
            string code = "";
            try
            {
                switch (t)
                {
                    case MissionType.Npc:    code = await DataConfiguration.search(DataConfiguration.DataType.npc, this); break;
                    case MissionType.Sector: code = await DataConfiguration.search(DataConfiguration.DataType.sector, this); break;
                }
            }
            catch (Exception ex) { Show(ex.Message); return; }
            if (!string.IsNullOrEmpty(code)) c_KeyTxt.Text = code;
        }

        // ----- conditions -----

        async void OnConditionAdd(object sender, RoutedEventArgs e)
        {
            await m_dlgConditions.ShowDialog(this);
            Condition cond;
            if (m_dlgConditions.getValues(out cond))
            {
                onChanged();
                m_mission.addCondition(cond);
                c_ConditionsTbl.Items.Add(new ConditionRow(cond));
            }
        }

        async void OnConditionEdit(object sender, RoutedEventArgs e)
        {
            if (c_ConditionsTbl.SelectedItem is not ConditionRow row) return;
            m_dlgConditions.editCondition(row.Condition);
            await m_dlgConditions.ShowDialog(this);
            Condition cond;
            if (m_dlgConditions.getValues(out cond))
            {
                onChanged();
                int idx = c_ConditionsTbl.SelectedIndex;
                m_mission.removeCondition(row.Condition);
                m_mission.addCondition(cond);
                c_ConditionsTbl.Items[idx] = new ConditionRow(cond);
            }
        }

        void OnConditionRemove(object sender, RoutedEventArgs e)
        {
            if (c_ConditionsTbl.SelectedItem is not ConditionRow row) return;
            onChanged();
            m_mission.removeCondition(row.Condition);
            c_ConditionsTbl.Items.Remove(row);
        }

        void OnConditionReordered(object sender, RoutedEventArgs e)
        {
            m_mission.clearConditions();
            foreach (var item in c_ConditionsTbl.Items)
            {
                if (item is ConditionRow row) m_mission.addCondition(row.Condition);
            }
            onChanged();
        }

        // ----- stages (mission tab) -----

        async void OnStageAdd(object sender, RoutedEventArgs e)
        {
            await addStage(c_StagesTbl.ItemCount != 0);
        }

        async System.Threading.Tasks.Task addStage(bool showDialog)
        {
            int stageId = 0;
            if (c_StagesTbl.ItemCount != 0)
            {
                var last = c_StagesTbl.Items[c_StagesTbl.ItemCount - 1] as StageRow;
                stageId = Int32.Parse(last.Stage.getId()) + 1;
            }

            Stage stage = null;
            bool add = true;
            if (showDialog)
            {
                m_dlgStages.setId(stageId);
                await m_dlgStages.ShowDialog(this);
                add = m_dlgStages.getValues(out stage);
            }
            else
            {
                stage = new Stage();
                stage.setId(stageId.ToString());
            }

            if (add && stage != null)
            {
                onChanged();
                m_mission.addStage(stage);
                var row = new StageRow(stage);
                c_StagesTbl.Items.Add(row);
                c_StagesTbl.SelectedItem = row;
                populateStagesTab();
            }
        }

        void OnStageRemove(object sender, RoutedEventArgs e)
        {
            if (c_StagesTbl.SelectedItem is not StageRow row) return;
            int idx = c_StagesTbl.SelectedIndex;
            m_mission.removeStage(row.Stage);
            c_StagesTbl.Items.Remove(row);
            if (idx >= c_StagesTbl.ItemCount) idx--;
            if (idx >= 0)
            {
                c_StagesTbl.SelectedIndex = idx;
                OnStageReordered(null, null);
            }
            else
            {
                onChanged();
                populateStagesTab();
            }
        }

        void OnStageReordered(object sender, RoutedEventArgs e)
        {
            m_mission.clearStages();
            int idx = 0;
            foreach (var item in c_StagesTbl.Items)
            {
                if (item is StageRow row)
                {
                    row.Stage.setId(idx.ToString());
                    m_mission.addStage(row.Stage);
                    idx++;
                }
            }
            onChanged();
            populateStagesTab();
        }

        void OnStageTableSelected(object sender, SelectionChangedEventArgs e)
        {
            if (c_StagesTbl.SelectedItem is StageRow row) selectStage(row.Stage);
        }

        // ----- stages tab -----

        async void OnAddStage(object sender, RoutedEventArgs e)
        {
            await addStage(false);
        }

        void OnStageSelected(object sender, SelectionChangedEventArgs e)
        {
            if (c_StagesCbo.SelectedItem is Stage stage) selectStage(stage);
        }

        void OnDescriptionChanged(object sender, TextChangedEventArgs e)
        {
            if (m_fieldChangesMuted || m_stage == null) return;
            m_stage.setDescription(c_DescriptionTxt.Text ?? "");
            // Refresh the row in the stage table so the description column updates
            for (int i = 0; i < c_StagesTbl.ItemCount; i++)
            {
                if (c_StagesTbl.Items[i] is StageRow row && row.Stage == m_stage)
                {
                    c_StagesTbl.Items[i] = new StageRow(m_stage);
                    break;
                }
            }
            onChanged();
        }

        // ----- completions -----

        async void OnCompletionAdd(object sender, RoutedEventArgs e)
        {
            if (m_stage == null) return;
            await m_dlgCompletions.ShowDialog(this);
            Completion completion;
            if (m_dlgCompletions.getValues(out completion))
            {
                onChanged();
                m_stage.addCompletion(completion);
                c_CompletionsTbl.Items.Add(new CompletionRow(completion));
                if (m_stage.getId() == "0" && completion.getCompletionType() == CompletionType.Talk_To_Npc)
                {
                    m_mission.setType(MissionType.Npc);
                    m_mission.setKey(completion.getValue());
                    c_TypeCbo.SelectedItem = MissionType.Npc;
                    c_KeyTxt.Text = completion.getValue();
                }
            }
        }

        async void OnCompletionEdit(object sender, RoutedEventArgs e)
        {
            if (c_CompletionsTbl.SelectedItem is not CompletionRow row) return;
            m_dlgCompletions.editCompletion(row.Completion);
            await m_dlgCompletions.ShowDialog(this);
            Completion completion;
            if (m_dlgCompletions.getValues(out completion))
            {
                onChanged();
                int idx = c_CompletionsTbl.SelectedIndex;
                m_stage.removeCompletion(row.Completion);
                m_stage.addCompletion(completion);
                c_CompletionsTbl.Items[idx] = new CompletionRow(completion);
            }
        }

        void OnCompletionRemove(object sender, RoutedEventArgs e)
        {
            if (c_CompletionsTbl.SelectedItem is not CompletionRow row) return;
            onChanged();
            m_stage.removeCompletion(row.Completion);
            c_CompletionsTbl.Items.Remove(row);
        }

        void OnCompletionReordered(object sender, RoutedEventArgs e)
        {
            m_stage.clearCompletions();
            foreach (var item in c_CompletionsTbl.Items)
            {
                if (item is CompletionRow row) m_stage.addCompletion(row.Completion);
            }
            onChanged();
        }

        // ----- rewards -----

        async void OnRewardsAdd(object sender, RoutedEventArgs e)
        {
            if (m_stage == null) return;
            await m_dlgRewards.ShowDialog(this);
            Reward reward;
            if (m_dlgRewards.getValues(out reward))
            {
                onChanged();
                m_stage.addReward(reward);
                c_RewardsTbl.Items.Add(new RewardRow(reward));
            }
        }

        async void OnRewardEdit(object sender, RoutedEventArgs e)
        {
            if (c_RewardsTbl.SelectedItem is not RewardRow row) return;
            m_dlgRewards.editReward(row.Reward);
            await m_dlgRewards.ShowDialog(this);
            Reward reward;
            if (m_dlgRewards.getValues(out reward))
            {
                onChanged();
                int idx = c_RewardsTbl.SelectedIndex;
                m_stage.removeReward(row.Reward);
                m_stage.addReward(reward);
                c_RewardsTbl.Items[idx] = new RewardRow(reward);
            }
        }

        void OnRewardsRemove(object sender, RoutedEventArgs e)
        {
            if (c_RewardsTbl.SelectedItem is not RewardRow row) return;
            onChanged();
            m_stage.removeReward(row.Reward);
            c_RewardsTbl.Items.Remove(row);
        }

        void OnRewardReordered(object sender, RoutedEventArgs e)
        {
            m_stage.clearRewards();
            foreach (var item in c_RewardsTbl.Items)
            {
                if (item is RewardRow row) m_stage.addReward(row.Reward);
            }
            onChanged();
        }

        // ----- talk tree -----

        void OnTalkTree(object sender, RoutedEventArgs e)
        {
            if (m_stage == null) { Show("Select a stage first"); return; }
            // talktreeeditor-avalonia is a separate process; it accepts XML
            // via args[0] but does not return the edited XML. Round-tripping
            // the result back into the stage requires either a temp file
            // contract or stdin/stdout, neither of which is wired up yet.
            // TODO: wire round-trip; for now this opens the editor stand-alone.
            try
            {
                string conversation = "";
                if (m_stage.hasTalkTrees())
                {
                    var sw = new StringWriter();
                    m_stage.getTalkTreesXML(sw);
                    conversation = sw.ToString();
                }
                var psi = new ProcessStartInfo("dotnet", "run --project ../talktreeeditor-avalonia/")
                {
                    UseShellExecute = false
                };
                Process.Start(psi);
                c_StatusLbl.Text = "launched talktreeeditor-avalonia (round-trip not wired)";
                _ = conversation;
            }
            catch (Exception ex) { Show("Launch failed: " + ex.Message); }
        }

        // ----- helpers -----

        async void Show(string msg)
        {
            try
            {
                var box = MessageBoxManager.GetMessageBoxStandard("Mission Editor", msg, MsBox.Avalonia.Enums.ButtonEnum.Ok, MsBoxIcon.Warning);
                await box.ShowWindowDialogAsync(this);
            }
            catch { Console.Error.WriteLine("MISSION: " + msg); }
        }

        // ----- row VMs -----

        class ConditionRow
        {
            public Condition Condition { get; }
            public ConditionRow(Condition c) { Condition = c; }
            public override string ToString() => Condition.getConditionType() + " | " + Condition.getFormattedValue();
        }

        class StageRow
        {
            public Stage Stage { get; }
            public StageRow(Stage s) { Stage = s; }
            public override string ToString() => Stage.getId() + " | " + Stage.getDescription();
        }

        class CompletionRow
        {
            public Completion Completion { get; }
            public CompletionRow(Completion c) { Completion = c; }
            public override string ToString() => Completion.getCompletionType() + " | " + Completion.getFormattedValue();
        }

        class RewardRow
        {
            public Reward Reward { get; }
            public RewardRow(Reward r) { Reward = r; }
            public override string ToString() => Reward.getRewardType() + " | " + Reward.getFormattedValue();
        }
    }
}
