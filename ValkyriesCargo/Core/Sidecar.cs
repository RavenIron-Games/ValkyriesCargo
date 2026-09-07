using System;
using System.Collections.Generic;
using System.Text;

namespace RavenIron.ValkyriesCargo.Core
{
    /// <summary>
    /// The world sidecar's text (design 3.5): a `format` line, then tagged rows that belong to four
    /// owners: the market (`stock`, `purse`, `purseStart`, `coined`, `visit`, `seq`), the scheduler (`cool`,
    /// `coolbase`), the running visit (`session`) and the owed ledger (`owed`). This splits a file into
    /// those four bundles and composes one back; each owner parses its own rows. Tab-separated,
    /// invariant culture, no BOM. The file I/O (.tmp/.bak, quarantine) is the store's business. PURE.
    /// </summary>
    public sealed class Sidecar
    {
        public const int FormatVersion = 1;
        public const string FormatTag = "format";

        public int Format = FormatVersion;
        public string MarketRows = "";
        public string CooldownRows = "";
        public string SessionRow = "";
        public List<string> OwedRows = new List<string>();
        public int UnknownRows;

        public bool FormatMatches => Format == FormatVersion;

        /// <summary>
        /// A file written by a NEWER build of this mod. P10b's "refuse rather than corrupt": this is not
        /// junk, it is somebody's market, written by a version that knows rows we do not. Renaming it to
        /// `.corrupt` and starting fresh would be the loudest possible way to lose a purse, and it is
        /// what a plain "format mismatch" branch does. A newer format is REFUSED, not quarantined.
        /// </summary>
        public bool FormatIsNewer => Format > FormatVersion;

        /// <summary>An older format (or none): ours to upgrade or to quarantine. Today it quarantines, as before.</summary>
        public bool FormatIsOlder => Format < FormatVersion;

        /// <summary>
        /// The format number a file claims, without parsing anything else: the first `format` row's
        /// value, or 0 when there is none and -1 when there is one that does not parse. Pure, allocates
        /// nothing but the lines it walks, and stops at the first answer - the store calls it before it
        /// decides whether it may touch the file at all.
        /// </summary>
        public static int PeekFormat(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#') continue;
                int tab = line.IndexOf('\t');
                if (tab < 0 || line.Substring(0, tab) != FormatTag) continue;
                int v;
                return Wire.TryInt(line.Substring(tab + 1), out v) ? v : -1;
            }
            return 0;
        }

        /// <summary>True when a file claims a format this build does not know how to read yet.</summary>
        public static bool IsNewerFormat(string text) => PeekFormat(text) > FormatVersion;

        /// <summary>Route every row to its owner. Never throws. A missing or foreign format line is reported.</summary>
        public static Sidecar Split(string text, List<string> problems)
        {
            var sc = new Sidecar { Format = 0 };
            if (string.IsNullOrEmpty(text)) { Wire.Report(problems, "sidecar: empty"); return sc; }
            var market = new List<string>();
            var cool = new List<string>();
            bool formatSeen = false;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#') continue;
                int tab = line.IndexOf('\t');
                string tag = tab < 0 ? line : line.Substring(0, tab);
                switch (tag)
                {
                    case FormatTag:
                    {
                        int v;
                        if (tab < 0 || !Wire.TryInt(line.Substring(tab + 1), out v)) { Wire.Report(problems, "sidecar: format line did not parse: " + line); continue; }
                        sc.Format = v;
                        formatSeen = true;
                        if (v != FormatVersion) Wire.Report(problems, "sidecar: format " + Wire.Int(v) + " is not " + Wire.Int(FormatVersion));
                        continue;
                    }
                    case "stock": case "purse": case "purseStart": case "coined": case "visit": case "seq":
                        market.Add(line); continue;
                    case "cool": case "coolbase":
                        cool.Add(line); continue;
                    case "session":
                        if (sc.SessionRow.Length > 0) Wire.Report(problems, "sidecar: a second session row ignored");
                        else sc.SessionRow = line;
                        continue;
                    case "owed":
                        sc.OwedRows.Add(line); continue;
                    default:
                        sc.UnknownRows++;
                        Wire.Report(problems, "sidecar: unknown row ignored: " + (line.Length > 60 ? line.Substring(0, 60) + "..." : line));
                        continue;
                }
            }
            if (!formatSeen) Wire.Report(problems, "sidecar: no format line");
            sc.MarketRows = string.Join("\n", market.ToArray());
            sc.CooldownRows = string.Join("\n", cool.ToArray());
            return sc;
        }

        /// <summary>The file text: format line, a comment, then the bundles in a fixed order. Ends with a newline.</summary>
        public static string Compose(string marketRows, string cooldownRows, string sessionRow, IList<string> owedRows)
        {
            var sb = new StringBuilder(4096);
            sb.Append(FormatTag).Append('\t').Append(Wire.Int(FormatVersion)).Append('\n');
            sb.Append("# Valkyrie's Cargo world sidecar. Tabs; invariant culture. Rows: stock purse purseStart coined visit seq cool coolbase session owed.\n");
            Append(sb, marketRows);
            Append(sb, cooldownRows);
            if (!string.IsNullOrEmpty(sessionRow)) sb.Append(sessionRow).Append('\n');
            if (owedRows != null) foreach (string row in owedRows) if (!string.IsNullOrEmpty(row)) sb.Append(row).Append('\n');
            return sb.ToString();
        }

        private static void Append(StringBuilder sb, string rows)
        {
            if (string.IsNullOrEmpty(rows)) return;
            foreach (string raw in rows.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length > 0) sb.Append(line).Append('\n');
            }
        }
    }
}
