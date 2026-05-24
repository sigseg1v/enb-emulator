using System;
using System.Collections.Generic;
using Avalonia.Controls;
using CommonTools.Database;

namespace CommonTools
{
    public static class Enumeration
    {
        // Avalonia ComboBox has no .Items.Add — uses ItemsSource. The WinForms
        // version mutated cbo.Items directly; here we build a list and assign.
        public static void AddSortedByName<T>(ComboBox cbo)
        {
            Type enumType = typeof(T);
            if (!enumType.IsEnum)
            {
                throw new InvalidOperationException("This type is not an enumeration");
            }
            String[] enumNames = Enum.GetNames(enumType);
            Array.Sort(enumNames);
            var items = new List<object>();
            T enumValue;
            foreach (String enumName in enumNames)
            {
                if (TryParse<T>(enumName, out enumValue))
                {
                    items.Add(enumValue);
                }
            }
            cbo.ItemsSource = items;
        }

        public static ColumnData.ColumnDataInfo[] ToColumnDataInfo<T>(Boolean sortByName)
        {
            Type enumType = typeof(T);
            if (!enumType.IsEnum)
            {
                throw new InvalidOperationException("This type is not an enumeration");
            }
            String[] enumNames = Enum.GetNames(enumType);
            if (sortByName)
            {
                Array.Sort(enumNames);
            }
            List<ColumnData.ColumnDataInfo> listColumnDataInfo = new List<ColumnData.ColumnDataInfo>();
            Enum enumValue;
            ColumnData.ColumnDataInfo columnDataInfo;
            foreach (String enumName in enumNames)
            {
                enumValue = (Enum)Enum.Parse(enumType, enumName);
                {
                    columnDataInfo = new ColumnData.ColumnDataInfo();
                    columnDataInfo.DataType = ColumnData.GetDataType(enumValue);
                    columnDataInfo.TypeIsString = ColumnData.IsDataTypeString(enumValue);
                    columnDataInfo.Name = ColumnData.GetName(enumValue);
                    listColumnDataInfo.Add(columnDataInfo);
                }
            }
            return listColumnDataInfo.ToArray();
        }

        public static Boolean TryParse<T>(String value, out T enumValue)
        {
            Type enumType = typeof(T);
            if (!enumType.IsEnum)
            {
                throw new InvalidOperationException("This type is not an enumeration");
            }
            enumValue = (T)Enum.GetValues(enumType).GetValue(0);

            if (String.IsNullOrEmpty(value))
            {
                return false;
            }

            try
            {
                enumValue = (T)Enum.Parse(enumType, value);
            }
            catch (System.ArgumentException)
            {
                return false;
            }
            return true;
        }

        public static String GetString<T>(Object value)
        {
            T enumValue;
            if (TryParse<T>(value.ToString(), out enumValue))
            {
                return enumValue.ToString();
            }
            return "";
        }
    }

    public enum CompletionType
    {
        Talk_To_Npc = 1,
        Give_Item = 2,
        Receive_Item = 3,
        Fight_Mob = 4,
        Current_Sector = 5,
        Nearest_Nav = 6,
        Obtain_Items = 8,
        Use_Skill_On_Mob_Type = 10,
        Use_Skill_On_Object = 11,
        Possess_Item = 13,
        Give_Credits = 14,
        Arrive_At = 15,
        Nav_Message = 17,
        Proximity_To_Space_Npc = 18,
        Talk_Space_Npc = 19
    }

    public enum ConditionType
    {
        Overall_Level = 1,
        Combat_Level = 2,
        Explore_Level = 3,
        Trade_Level = 4,
        Race = 5,
        Profession = 6,
        Hull_Level = 7,
        Faction_Required = 8,
        Item_Required = 9,
        Mission_Required = 10
    }

    public enum MissionType
    {
        Npc = 0,
        Sector = 1
    }


    public enum Professions
    {
        Warrior = 0,
        Trader = 1,
        Explorer = 2
    }

    public enum TalkTreeFlag
    {
        Trade = 1,
        Postpone_Mission,
        Drop_Mission,
        More,
        Mission_Goto_Stage,
        Mission_Completed
    }

    public enum Races
    {
        Terran = 0,
        Jenquai = 1,
        Progen = 2
    }

    public enum RewardType
    {
        Credits = 1,
        Explore_XP = 2,
        Combat_XP = 3,
        Trade_XP = 4,
        Faction = 5,
        Item_ID = 6,
        Hull_Upgrade = 7,
        Run_Script = 8,
        Award_Skill = 9,
        Advance_Mission = 10
    }
}
