using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RavenIron.ValkyriesCargo.Core
{
    /// <summary>
    /// The one place the wire strings are cut up. Fields are separated by ';', list items by
    /// '|', sub-fields by ':'. Prefab names and reason codes never contain any of the three
    /// (Catalogue.IsPrefabName guarantees it for prefabs), and the only free-text field on
    /// any message, a nested MarketState, is always LAST and read as "the rest of the string".
    /// PURE: no game or Unity dependency.
    /// </summary>
    public static class Wire
    {
        public const char Field = ';';
        public const char Item  = '|';
        public const char Sub   = ':';

        public static string Int(int v)     => v.ToString(CultureInfo.InvariantCulture);
        public static string Long(long v)   => v.ToString(CultureInfo.InvariantCulture);
        public static string Float(float v) => v.ToString("R", CultureInfo.InvariantCulture);
        public static string Double(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        public static bool TryInt(string s, out int v) =>
            int.TryParse(s == null ? "" : s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v);

        public static bool TryLong(string s, out long v) =>
            long.TryParse(s == null ? "" : s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v);

        public static bool TryFloat(string s, out float v)
        {
            bool ok = float.TryParse(s == null ? "" : s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
            return ok && !float.IsNaN(v) && !float.IsInfinity(v);
        }

        public static bool TryDouble(string s, out double v)
        {
            bool ok = double.TryParse(s == null ? "" : s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v);
            return ok && !double.IsNaN(v) && !double.IsInfinity(v);
        }

        public static bool TryBool(string s, out bool v)
        {
            string t = s == null ? "" : s.Trim();
            if (t == "1" || string.Equals(t, "true", StringComparison.OrdinalIgnoreCase)) { v = true; return true; }
            if (t == "0" || string.Equals(t, "false", StringComparison.OrdinalIgnoreCase)) { v = false; return true; }
            v = false;
            return false;
        }

        /// <summary>Split on the field separator into exactly <paramref name="n"/> parts, the last one taking the rest.</summary>
        public static string[] Fields(string s, int n) => (s ?? "").Split(new[] { Field }, n, StringSplitOptions.None);

        /// <summary>Split a list field; an empty field is an empty list, not one empty item.</summary>
        public static string[] Items(string s) =>
            string.IsNullOrEmpty(s) ? new string[0] : s.Split(new[] { Item }, StringSplitOptions.RemoveEmptyEntries);

        public static string[] Subs(string s) => (s ?? "").Split(Sub);

        /// <summary>A prefab name or reason code is safe on the wire when it carries none of the three separators.</summary>
        public static bool IsToken(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            return s.IndexOf(Field) < 0 && s.IndexOf(Item) < 0 && s.IndexOf(Sub) < 0;
        }

        public static string Join(char sep, IList<string> parts)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < parts.Count; i++)
            {
                if (i > 0) sb.Append(sep);
                sb.Append(parts[i]);
            }
            return sb.ToString();
        }

        public static void Report(List<string> problems, string text)
        {
            if (problems != null) problems.Add(text);
        }
    }
}
