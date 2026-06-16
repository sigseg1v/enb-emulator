using System;
using System.Data;
using System.IO;
using System.Text;
using System.Xml;
using CommonTools.Database;
using MissionEditor.Nodes;

namespace MissionEditor.Database
{
    // Ported from tools/missioneditor/Database/Database.cs, then rewritten to
    // drop the old keyword-builder DSL in favour of plain literal
    // SQL with bound parameters (CLAUDE.md "no string-concat SQL"). The "mission_XML"
    // column has mixed case, so every identifier stays double-quoted or Postgres
    // folds it to lowercase and the lookup misses.
    public class Database
    {
        public const String LOG_FILE = "missions.xml";

        // Dapper maps each aliased column onto a constructor parameter by name
        // (case-insensitive). mission_type is smallint, mission_key is int.
        private sealed record MissionRow(string xml, string name, short type, int key);

        public static String getFirstMissionId()
        {
            int? id = DB.Instance.queryScalar<int?>("SELECT \"mission_id\" FROM missions LIMIT 1");
            return id?.ToString();
        }

        public static Mission getMission(String id)
        {
            if (!Int32.TryParse(id, out int missionId)) return null;

            MissionRow row = DB.Instance.queryRow<MissionRow>(
                "SELECT \"mission_XML\" AS xml, \"mission_name\" AS name, " +
                "\"mission_type\" AS type, \"mission_key\" AS key " +
                "FROM missions WHERE \"mission_id\" = @id",
                new { id = missionId });
            if (row == null) return null;

            var mission = new Mission();
            mission.setId(id);
            mission.setXml(row.xml);
            mission.setName(row.name);
            CommonTools.Enumeration.TryParse<CommonTools.MissionType>(row.type.ToString(), out var missionType);
            mission.setType(missionType);
            mission.setKey(row.key.ToString());
            return mission;
        }

        public static String getNextMissionId()
        {
            int? max = DB.Instance.queryScalar<int?>("SELECT MAX(\"mission_id\") FROM missions");
            return ((max ?? 0) + 1).ToString();
        }

        public static void setMission(Mission mission, Boolean newMission)
        {
            String[] parameters = new String[] { "id", "xml", "name", "key", "type" };
            String[] values = new String[] { mission.getId(), mission.getXML(), mission.getName(), mission.getKey(), ((int)mission.getType()).ToString() };
            if (newMission)
            {
                // mission_id is pre-allocated by getNextMissionId() before we get
                // here, so INSERT it explicitly. (The old path INSERTed without an
                // id then tried to recover it via "WHERE mission_id IS NULL", which
                // only worked against MySQL's NULL-on-missing-PK quirk and fails on
                // a Postgres NOT NULL primary key.)
                writeXmlLog(mission, LogAction.Add);
                DB.Instance.executeCommand(
                    "INSERT INTO missions (\"mission_id\", \"mission_XML\", \"mission_name\", \"mission_key\", \"mission_type\") " +
                    "VALUES (@id, @xml, @name, @key, @type)",
                    parameters, values);
            }
            else
            {
                writeXmlLog(mission, LogAction.Edit);
                DB.Instance.executeCommand(
                    "UPDATE missions SET \"mission_XML\" = @xml, \"mission_name\" = @name, " +
                    "\"mission_key\" = @key, \"mission_type\" = @type WHERE \"mission_id\" = @id",
                    parameters, values);
            }
        }

        public static void deleteMission(Mission mission)
        {
            writeXmlLog(mission, LogAction.Delete);
            DB.Instance.executeCommand(
                "DELETE FROM missions WHERE \"mission_id\" = @id",
                new String[] { "id" }, new String[] { mission.getId() });
        }

        private enum LogAction { Add, Delete, Edit };
        private enum LogXmlTag { Missions, Date, Time, Action, Mission, Id, Xml, Name, Type, Key };

        private static void writeXmlLog(Mission mission, LogAction logAction)
        {
            if (!File.Exists(LOG_FILE))
            {
                XmlTextWriter textWritter = new XmlTextWriter(LOG_FILE, Encoding.Unicode);
                textWritter.WriteStartElement(LogXmlTag.Missions.ToString());
                textWritter.WriteEndElement();
                textWritter.Close();
            }

            DateTime dateTime = DateTime.Now;
            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.Load(LOG_FILE);

            XmlElement subRoot = xmlDoc.CreateElement(LogXmlTag.Mission.ToString());
            subRoot.SetAttribute(LogXmlTag.Date.ToString(), dateTime.ToString("yyyy/MM/dd"));
            subRoot.SetAttribute(LogXmlTag.Time.ToString(), dateTime.ToString("HH:mm:ss"));
            subRoot.SetAttribute(LogXmlTag.Action.ToString(), logAction.ToString());
            subRoot.SetAttribute(LogXmlTag.Id.ToString(), mission.getId());
            subRoot.SetAttribute(LogXmlTag.Name.ToString(), mission.getName());
            subRoot.SetAttribute(LogXmlTag.Type.ToString(), mission.getType().ToString());
            subRoot.SetAttribute(LogXmlTag.Key.ToString(), mission.getKey());
            XmlText xmlTextMission = xmlDoc.CreateTextNode(mission.getXML());
            subRoot.AppendChild(xmlTextMission);
            xmlDoc.DocumentElement.AppendChild(subRoot);

            xmlDoc.Save(LOG_FILE);
        }
    }
}
