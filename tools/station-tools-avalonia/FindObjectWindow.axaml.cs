using System;
using System.Data;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CommonTools.Database;

namespace StationToolsAvalonia
{
    // Avalonia port of tools/station-tools/FindObject.cs — modal starbase
    // picker. Loads sector_objects ⋈ sector_objects_starbases ⋈ sectors
    // and exposes (m_Ok, m_StationID, m_SectorID) after close.
    public partial class FindObjectWindow : Window
    {
        public bool m_Ok;
        int m_StationID;
        int m_SectorID;

        public FindObjectWindow()
        {
            InitializeComponent();
            m_Ok = false;
            try { LoadList(); }
            catch (Exception ex) { Console.Error.WriteLine("FindObject DB load failed: " + ex.Message); }
        }

        void LoadList()
        {
            const string sql =
                "SELECT `sector_objects`.`sector_object_id` AS StarbaseID, " +
                "       `sector_objects`.`sector_id` AS SectorID, " +
                "       `sectors`.`name` AS Sector, " +
                "       `sector_objects`.`name` AS StarbaseName " +
                "FROM `sector_objects_starbases` " +
                "INNER JOIN `sector_objects` ON `sector_objects_starbases`.`starbase_id` = `sector_objects`.`sector_object_id` " +
                "INNER JOIN `sectors` ON `sector_objects`.`sector_id` = `sectors`.`sector_id`";

            var dt = DB.Instance.executeQuery(sql, new string[0], new string[0]);
            c_StationList.ItemsSource = dt?.DefaultView;
        }

        public int GetSectorID() => m_SectorID;
        public int GetStationID() => m_StationID;

        void OnOk(object sender, RoutedEventArgs e)
        {
            if (c_StationList.SelectedItem is DataRowView drv)
            {
                m_StationID = Convert.ToInt32(drv.Row["StarbaseID"]);
                m_SectorID  = Convert.ToInt32(drv.Row["SectorID"]);
                m_Ok = true;
            }
            Close();
        }

        void OnCancel(object sender, RoutedEventArgs e) => Close();
    }
}
