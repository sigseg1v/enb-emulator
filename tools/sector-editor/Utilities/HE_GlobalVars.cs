using System;

namespace N7.Utilities
{
    // The constant string tables the original WinForms editor exposed to its
    // PropertyGrid TypeConverter classes (TypesConverter, NavTypeConverter, etc.)
    // The converter classes themselves don't carry over — Avalonia has no
    // PropertyGrid — but the constants are still the source of truth for what
    // values the data model accepts, and the eventual Tier 12e custom forms
    // will bind ComboBoxes to them directly.
    internal class HE_GlobalVars
    {
        internal static string[] _ListofTypes = new string[] { "Mobs", "Planets", "Stargates", "Starbases", "Decorations", "Harvestables" };
        internal static string[] _ListofFactions = new string[0];
        internal static string[] _ListofFieldTypes = new string[] { "Random", "Ring", "Donut", "Cylinder", "Sphere", "Gas Cloud Clump" };
        internal static string[] _ListofNavTypes = new string[] { "0", "1", "2" };
        internal static string[] _ListofLevels = new string[] { "1", "2", "3", "4", "5", "6", "7", "8", "9" };
        internal static string[] _listofSectorTypes = new string[] { "Space Sector", "Rocky Planet Surface", "Gas Giant Surface" };
    }
}
