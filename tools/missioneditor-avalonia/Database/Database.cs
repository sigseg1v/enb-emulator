using System;
using System.Data;
using System.IO;
using System.Text;
using System.Xml;
using CommonTools.Database;
using MissionEditorAvalonia.Nodes;

namespace MissionEditorAvalonia.Database
{
    // Ported verbatim from tools/missioneditor/Database/Database.cs.
    // Already uses parameterised SQL; no changes needed beyond namespaces.
    public class Database
    {
        public const String LOG_FILE = "missions.xml";

        public static String getFirstMissionId()
        {
            String query = DB.SELECT
                         + ColumnData.GetName(Net7.Table_missions._mission_id)
                         + DB.FROM
                         + Net7.Tables.missions.ToString()
                         + DB.LIMIT1;
            DataTable dataTable = DB.Instance.executeQuery(query, null, null);

            return dataTable.Rows.Count == 0 ? null : ColumnData.GetString(dataTable.Rows[0], Net7.Table_missions._mission_id);
        }

        public static Mission getMission(String id)
        {
            Mission mission = null;
            String parameter = "id";
            String query = DB.SELECT
                         + "*"
                         + DB.FROM
                         + Net7.Tables.missions
                         + DB.WHERE
                         + ColumnData.GetName(Net7.Table_missions._mission_id)
                         + DB.EQUALS
                         + DB.QueryParameterCharacter
                         + parameter;
            DataTable dataTable = DB.Instance.executeQuery(query, new String[] { parameter }, new String[] { id });
            if (dataTable.Rows.Count == 1)
            {
                mission = new Mission();
                mission.setId(id);
                mission.setXml(ColumnData.GetString(dataTable.Rows[0], Net7.Table_missions._mission_XML));
                mission.setName(ColumnData.GetString(dataTable.Rows[0], Net7.Table_missions._mission_name));
                String type = ColumnData.GetString(dataTable.Rows[0], Net7.Table_missions._mission_type);
                CommonTools.MissionType missionType;
                CommonTools.Enumeration.TryParse<CommonTools.MissionType>(type, out missionType);
                mission.setType(missionType);
                mission.setKey(ColumnData.GetString(dataTable.Rows[0], Net7.Table_missions._mission_key));
            }
            return mission;
        }

        public static String getNextMissionId()
        {
            String query = "SELECT MAX("
                         + ColumnData.GetName(Net7.Table_missions._mission_id)
                         + ") FROM "
                         + Net7.Tables.missions;
            DataTable dataTable = DB.Instance.executeQuery(query, null, null);
            Int32 nextId = 1;
            if (dataTable.Rows.Count == 1 && dataTable.Rows[0] != null)
            {
                DataRow dataRow = dataTable.Rows[0];
                Int32.TryParse(dataRow[0].ToString(), out nextId);
                ++nextId;
            }
            return nextId.ToString();
        }

        public static void setMission(Mission mission, Boolean newMission)
        {
            String query;
            String[] parameters = new String[] { "id", "xml", "name", "key", "type" };
            String[] values = new String[] { mission.getId(), mission.getXML(), mission.getName(), mission.getKey(), ((int)mission.getType()).ToString() };
            if (newMission)
            {
                writeXmlLog(mission, LogAction.Add);
                query = "INSERT INTO "
                      + Net7.Tables.missions
                      + " ( "
                      + ColumnData.GetName(Net7.Table_missions._mission_XML) + ","
                      + ColumnData.GetName(Net7.Table_missions._mission_name) + ","
                      + ColumnData.GetName(Net7.Table_missions._mission_key) + ","
                      + ColumnData.GetName(Net7.Table_missions._mission_type)
                      + " )"
                      + " VALUES ("
                      + DB.QueryParameterCharacter + parameters[1] + ","
                      + DB.QueryParameterCharacter + parameters[2] + ","
                      + DB.QueryParameterCharacter + parameters[3] + ","
                      + DB.QueryParameterCharacter + parameters[4]
                      + "); "
                      + DB.SELECT
                      + ColumnData.GetName(Net7.Table_missions._mission_id)
                      + DB.FROM
                      + Net7.Tables.missions
                      + DB.WHERE
                      + ColumnData.GetName(Net7.Table_missions._mission_id)
                      + " IS NULL;";
                DataTable dataTable = DB.Instance.executeQuery(query, parameters, values);

                mission.setId(ColumnData.GetString(dataTable.Rows[0], Net7.Table_missions._mission_id));
                values[0] = mission.getId();
                values[1] = mission.getXML();
                query = "UPDATE "
                      + Net7.Tables.missions
                      + " SET "
                      + ColumnData.GetName(Net7.Table_missions._mission_XML)
                      + DB.EQUALS
                      + DB.QueryParameterCharacter + parameters[1]
                      + DB.WHERE
                      + ColumnData.GetName(Net7.Table_missions._mission_id)
                      + DB.EQUALS
                      + DB.QueryParameterCharacter + parameters[0];
            }
            else
            {
                writeXmlLog(mission, LogAction.Edit);
                query = "UPDATE "
                      + Net7.Tables.missions
                      + " SET "
                      + ColumnData.GetName(Net7.Table_missions._mission_XML)
                      + DB.EQUALS
                      + DB.QueryParameterCharacter + parameters[1] + ","
                      + ColumnData.GetName(Net7.Table_missions._mission_name)
                      + DB.EQUALS
                      + DB.QueryParameterCharacter + parameters[2] + ","
                      + ColumnData.GetName(Net7.Table_missions._mission_key)
                      + DB.EQUALS
                      + DB.QueryParameterCharacter + parameters[3] + ","
                      + ColumnData.GetName(Net7.Table_missions._mission_type)
                      + DB.EQUALS
                      + DB.QueryParameterCharacter + parameters[4]
                      + DB.WHERE
                      + ColumnData.GetName(Net7.Table_missions._mission_id)
                      + DB.EQUALS
                      + DB.QueryParameterCharacter + parameters[0];
            }
            DB.Instance.executeQuery(query, parameters, values);
        }

        public static void deleteMission(Mission mission)
        {
            writeXmlLog(mission, LogAction.Delete);
            String parameter = "id";
            String query = "DELETE FROM "
                         + Net7.Tables.missions
                          + DB.WHERE
                          + ColumnData.GetName(Net7.Table_missions._mission_id)
                          + DB.EQUALS
                          + DB.QueryParameterCharacter + parameter;
            DB.Instance.executeQuery(query, new String[] { parameter }, new String[] { mission.getId() });
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
