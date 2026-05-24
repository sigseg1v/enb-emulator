using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace LaunchNet7Avalonia.Patching
{
    // Cross-platform replacement for the original IniUtility.cs which
    // P/Invoked kernel32's GetPrivateProfileString / WritePrivateProfileString.
    // The format we need to handle is the EnB client's ini files
    // (Network.ini, Auth.ini, rg_regdata.ini): conventional Windows INI
    // syntax with [Section] headers and Key=Value pairs, '#' or ';'
    // line comments, blank lines tolerated.
    //
    // Read-side returns null when the key is absent (mirrors the kernel32
    // contract). Write-side preserves the file's existing ordering and
    // any comments it can; an absent section is appended; an absent key
    // is appended to its section.
    public static class IniFile
    {
        public static string GetValue(string filename, string section, string key)
        {
            if (!File.Exists(filename)) return null;

            string currentSection = "";
            foreach (var rawLine in File.ReadAllLines(filename))
            {
                var line = rawLine.Trim();
                if (line.Length == 0) continue;
                if (line.StartsWith(";") || line.StartsWith("#")) continue;

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    currentSection = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }

                if (!string.Equals(currentSection, section, StringComparison.OrdinalIgnoreCase))
                    continue;

                int eq = line.IndexOf('=');
                if (eq < 0) continue;

                var k = line.Substring(0, eq).Trim();
                if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                    return line.Substring(eq + 1).Trim();
            }
            return null;
        }

        public static void SetValue(string filename, string section, string key, string value)
        {
            var lines = File.Exists(filename)
                ? new List<string>(File.ReadAllLines(filename))
                : new List<string>();

            // Locate section bounds. End is exclusive (the line index where
            // the next [Section] header sits, or lines.Count).
            int sectionStart = -1;
            int sectionEnd   = lines.Count;
            for (int i = 0; i < lines.Count; i++)
            {
                var t = lines[i].Trim();
                if (t.StartsWith("[") && t.EndsWith("]"))
                {
                    var name = t.Substring(1, t.Length - 2).Trim();
                    if (sectionStart < 0 &&
                        string.Equals(name, section, StringComparison.OrdinalIgnoreCase))
                    {
                        sectionStart = i;
                    }
                    else if (sectionStart >= 0)
                    {
                        sectionEnd = i;
                        break;
                    }
                }
            }

            if (sectionStart < 0)
            {
                if (lines.Count > 0 && lines[lines.Count - 1].Trim().Length > 0)
                    lines.Add("");
                lines.Add("[" + section + "]");
                lines.Add(key + "=" + value);
                File.WriteAllLines(filename, lines);
                return;
            }

            // Replace existing key within the section, or append at end.
            for (int i = sectionStart + 1; i < sectionEnd; i++)
            {
                var t = lines[i];
                int eq = t.IndexOf('=');
                if (eq < 0) continue;
                var k = t.Substring(0, eq).Trim();
                if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                {
                    lines[i] = key + "=" + value;
                    File.WriteAllLines(filename, lines);
                    return;
                }
            }

            // Append within section (before sectionEnd).
            int insertAt = sectionEnd;
            while (insertAt > sectionStart + 1 && lines[insertAt - 1].Trim().Length == 0)
                insertAt--;
            lines.Insert(insertAt, key + "=" + value);
            File.WriteAllLines(filename, lines);
        }
    }
}
