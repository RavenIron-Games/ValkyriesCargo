using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RavenIron.ValkyriesCargo.Core
{
    /// <summary>
    /// A JSON WRITER — never a reader — for the BarrkBOT export (`Server/BarrkBotExport.cs`). PURE.
    ///
    /// Why it exists (the owner's decision 2026-09-07, closing `docs/TODO.md` §1): P12 had brought in
    /// Newtonsoft.Json for exactly two calls, one compact (the row widths the rollover paginates by) and
    /// one indented (the files), and with it a Thunderstore runtime dependency on every server and every
    /// client (`ValheimModding-JsonDotNET`), overriding the locked "BepInExPack only" row. Writing JSON is
    /// sixty lines; reading it is the part that needs a library, and nothing here reads any. So the
    /// dependency goes and this file takes its place. The output is shaped to match what Newtonsoft's
    /// `SerializeObject(value)` and `SerializeObject(value, Formatting.Indented)` produce for the value
    /// types the export builds, so the rollover's part boundaries and BarrkBOT's files do not move.
    ///
    /// What it renders: null; bool; string; the integer types; float, double and decimal (round-trip
    /// text, a `.0` appended to a whole double the way Newtonsoft does, NaN and the infinities as the
    /// quoted strings Newtonsoft writes by default); any `IDictionary` with string-convertible keys, in
    /// its own enumeration order; any other `IEnumerable` as an array; `DateTime` as ISO 8601; an enum as
    /// its integer. Anything else becomes its invariant `ToString()` as a string — this writer never
    /// throws on a type, because decision 4 says a throw in the mirror must never reach the sidecar's
    /// caller, and a surprising string in a bot file is the cheaper failure.
    ///
    /// Indented form: two spaces per level, `"key": value`, one element per line, `{}` and `[]` for empty
    /// containers, and the platform newline (what a `TextWriter` gives, hence what Newtonsoft gave).
    /// </summary>
    public static class Json
    {
        private const string Indent = "  ";

        /// <summary>Render `value` as JSON text. `indented` false is the compact form with no whitespace at all.</summary>
        public static string Write(object value, bool indented = false)
        {
            StringBuilder sb = new StringBuilder(indented ? 512 : 128);
            WriteValue(sb, value, indented, 0);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object value, bool indented, int depth)
        {
            if (value == null) { sb.Append("null"); return; }
            if (value is string s) { WriteString(sb, s); return; }
            if (value is bool b) { sb.Append(b ? "true" : "false"); return; }
            if (value is int i) { sb.Append(i.ToString(CultureInfo.InvariantCulture)); return; }
            if (value is long l) { sb.Append(l.ToString(CultureInfo.InvariantCulture)); return; }
            if (value is double d) { WriteDouble(sb, d); return; }
            if (value is float f) { WriteSingle(sb, f); return; }
            if (value is decimal m) { sb.Append(m.ToString(CultureInfo.InvariantCulture)); return; }
            if (value is short || value is byte || value is sbyte || value is ushort || value is uint || value is ulong)
            {
                sb.Append(((IFormattable)value).ToString(null, CultureInfo.InvariantCulture));
                return;
            }
            if (value is Enum) { sb.Append(Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)); return; }
            if (value is DateTime dt) { WriteString(sb, dt.ToString("yyyy-MM-dd'T'HH:mm:ss.FFFFFFFK", CultureInfo.InvariantCulture)); return; }
            if (value is IDictionary dict) { WriteObject(sb, dict, indented, depth); return; }
            if (value is IEnumerable seq) { WriteArray(sb, seq, indented, depth); return; }
            WriteString(sb, Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");
        }

        private static void WriteObject(StringBuilder sb, IDictionary dict, bool indented, int depth)
        {
            if (dict.Count == 0) { sb.Append("{}"); return; }
            sb.Append('{');
            bool first = true;
            foreach (DictionaryEntry e in dict)
            {
                if (!first) sb.Append(',');
                first = false;
                if (indented) NewLine(sb, depth + 1);
                WriteString(sb, e.Key == null ? "" : (e.Key as string ?? Convert.ToString(e.Key, CultureInfo.InvariantCulture) ?? ""));
                sb.Append(indented ? ": " : ":");
                WriteValue(sb, e.Value, indented, depth + 1);
            }
            if (indented) NewLine(sb, depth);
            sb.Append('}');
        }

        private static void WriteArray(StringBuilder sb, IEnumerable seq, bool indented, int depth)
        {
            sb.Append('[');
            bool first = true;
            foreach (object item in seq)
            {
                if (!first) sb.Append(',');
                first = false;
                if (indented) NewLine(sb, depth + 1);
                WriteValue(sb, item, indented, depth + 1);
            }
            if (first) { sb.Append(']'); return; }   // empty: "[]"
            if (indented) NewLine(sb, depth);
            sb.Append(']');
        }

        private static void NewLine(StringBuilder sb, int depth)
        {
            sb.Append(Environment.NewLine);
            for (int i = 0; i < depth; i++) sb.Append(Indent);
        }

        /// <summary>
        /// Newtonsoft's number text: round-trip ("R") in the invariant culture, and a whole double gets a
        /// `.0` so `120` the double never reads back as an integer. NaN and the infinities are not JSON
        /// numbers; Newtonsoft writes them as the strings "NaN", "Infinity", "-Infinity" by default.
        /// </summary>
        private static void WriteDouble(StringBuilder sb, double d)
        {
            if (double.IsNaN(d)) { sb.Append("\"NaN\""); return; }
            if (double.IsPositiveInfinity(d)) { sb.Append("\"Infinity\""); return; }
            if (double.IsNegativeInfinity(d)) { sb.Append("\"-Infinity\""); return; }
            string text = d.ToString("R", CultureInfo.InvariantCulture);
            sb.Append(text);
            if (IsWholeDigits(text)) sb.Append(".0");
        }

        /// <summary>A float is formatted as a float ("R" on the single), not through its double form, which is what Newtonsoft does too.</summary>
        private static void WriteSingle(StringBuilder sb, float f)
        {
            if (float.IsNaN(f)) { sb.Append("\"NaN\""); return; }
            if (float.IsPositiveInfinity(f)) { sb.Append("\"Infinity\""); return; }
            if (float.IsNegativeInfinity(f)) { sb.Append("\"-Infinity\""); return; }
            string text = f.ToString("R", CultureInfo.InvariantCulture);
            sb.Append(text);
            if (IsWholeDigits(text)) sb.Append(".0");
        }

        private static bool IsWholeDigits(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '.' || c == 'E' || c == 'e' || c == 'N' || c == 'I') return false;
            }
            return true;
        }

        /// <summary>
        /// The JSON string escapes, in Newtonsoft's default set: the quote, the backslash, the named
        /// controls, every other control as `\u00xx`, and the three line-terminator code points a
        /// JavaScript reader chokes on (U+0085, U+2028, U+2029). Nothing else; non-ASCII passes through.
        /// </summary>
        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ' || c == '\u0085' || c == '\u2028' || c == '\u2029')
                        {
                            sb.Append("\\u");
                            sb.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
