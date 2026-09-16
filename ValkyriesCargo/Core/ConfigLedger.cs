using System;
using System.Collections.Generic;
using System.Globalization;

namespace RavenIron.ValkyriesCargo.Core
{
    /// <summary>
    /// The config migration's decisions, PURE and off-game (2026-09-16, the owner: "build it on a branch
    /// with the overrides and schema version backfilled"). Shape from Wu'barrk's WingsoftheValkyrie
    /// `ConfigMigration.cs` (the family's, alongside TortalPortal and Fatty): a stamped layout version, a
    /// rebase table keyed by the version it produces, and the rule that a stored value still equal to an
    /// OLD default belongs to the mod and moves to the new one, while anything else is an admin's and is
    /// kept. Wings only ever rebases a value onto a new DEFAULT; this mod's one migration goes further -
    /// version 2 retires `Server.Catalogue` entirely in favour of `Server.CatalogueOverrides` (what an
    /// admin actually changed, layered on the shipped catalogue in code - `Core/CatalogueOverrides.cs`),
    /// so a shipped default can grow forever without a stored line shadowing it again.
    ///
    /// Version numbers (backfilled, not literal history - nothing before this cut ever stamped a version):
    ///   0 = any unstamped file: every rc1 through 0.1.3 file, whatever it carries.
    ///   1 = the 0.1.1 catalogue (101 rows) - a version this mod never actually stamped, kept as a rung
    ///       on the ladder so a hand-migrated or future intermediate file has somewhere to land.
    ///   2 = the 0.1.4 overrides model. Current.
    /// </summary>
    public static class ConfigLedger
    {
        public const string MetaSection = "Meta";
        public const string VersionKey = "ConfigVersion";
        public const int CurrentVersion = 2;

        private const string CatalogueSection = "Server";
        private const string CatalogueKey = "Catalogue";
        private static readonly string CatalogueSlot = Slot(CatalogueSection, CatalogueKey);

        /// <summary>One slot's every old default, from Wings' shape. A stored value equal to ANY of them is the mod's own old default, not admin work.</summary>
        public sealed class Rebase
        {
            public string Section;
            public string Key;
            public string[] OldDefaults;
        }

        /// <summary>Keyed by the version the rebase produces: <c>Rebases[n]</c> takes a file at n-1 up to n. Version 2 is not here - it is the catalogue transform, always run as its own step.</summary>
        private static readonly Dictionary<int, Rebase[]> Rebases = new Dictionary<int, Rebase[]>
        {
            { 1, new[]
                {
                    new Rebase { Section = CatalogueSection, Key = CatalogueKey, OldDefaults = new[] { Catalogue.LegacyDefaultLine72 } },
                }
            },
        };

        /// <summary>One slot whose stored value was NOT an old default - real admin work, kept and named in the log.</summary>
        public struct KeptSlot
        {
            public string Slot;
            public string Value;
        }

        /// <summary>What version 2 does to a stored `Server.Catalogue` line: replace it with derived `Server.CatalogueOverrides`.</summary>
        public sealed class CatalogueTransformResult
        {
            /// <summary>The raw stored `Server.Catalogue` value, or null when the file had none.</summary>
            public string StoredLine;
            /// <summary>The derived overrides string (see `CatalogueOverrides.Derive`); "" when the stored line was not customised.</summary>
            public string Overrides;
            /// <summary>`CatalogueOverrides.Describe(Overrides, shipped)` at the CURRENT shipped catalogue, for the log.</summary>
            public string Summary;
            /// <summary>Row count of the current shipped catalogue, for the boot line's "moved to the shipped N rows".</summary>
            public int ShippedCount;
            /// <summary>Rows of the stored line that failed to parse (from `CatalogueOverrides.Derive`'s own parse of it), so a bad row is named next to the boot line instead of vanishing silently.</summary>
            public List<string> Problems = new List<string>();
            /// <summary>The stored line, trimmed, is ordinally identical to the CURRENT shipped line - an
            /// up-to-date file nobody customised, not text matching the version-1 rebase table's LEGACY
            /// default (which is the only thing `ResetToDefault`/`Kept` distinguish). Lets <see cref="Describe"/>
            /// tell "nothing to say" from "genuinely could not be explained".</summary>
            public bool AlreadyCurrentShippedLine;
        }

        /// <summary>The whole plan a file's snapshot produces. Nothing to do plans an empty one (<see cref="CatalogueTransform"/> null).</summary>
        public sealed class MigrationPlan
        {
            public int FromVersion;
            public int ToVersion;
            public List<string> ResetToDefault = new List<string>();
            public List<KeptSlot> Kept = new List<KeptSlot>();
            public CatalogueTransformResult CatalogueTransform;
        }

        private static string Slot(string section, string key) => section + "::" + key;

        /// <summary>
        /// BepInEx config files are plain INI: `[Section]` headers, `#` comments, blank lines, and
        /// `Key = value` where the value may itself contain `=` or `,`. Keyed "Section::Key", ordinal
        /// ignore-case; the last duplicate wins. Values trimmed. Never throws.
        /// </summary>
        public static Dictionary<string, string> ParseIni(IEnumerable<string> lines)
        {
            var into = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (lines == null) return into;

            string section = "";
            foreach (string rawLine in lines)
            {
                if (rawLine == null) continue;
                string line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                if (line[0] == '[' && line[line.Length - 1] == ']')
                {
                    section = line.Substring(1, line.Length - 2).Trim();
                    continue;
                }

                int eq = line.IndexOf('=');
                if (eq <= 0) continue;

                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();
                if (key.Length == 0) continue;

                into[Slot(section, key)] = value;
            }
            return into;
        }

        /// <summary>The stamped `Meta.ConfigVersion`, or 0 when absent or unparseable (a pre-migration file, by definition).</summary>
        public static int ReadVersion(Dictionary<string, string> snapshot)
        {
            string raw;
            if (snapshot != null && snapshot.TryGetValue(Slot(MetaSection, VersionKey), out raw) &&
                int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int version))
            {
                return version;
            }
            return 0;
        }

        /// <summary>
        /// What migrating `snapshot` from `fileVersion` to <see cref="CurrentVersion"/> does. A missing
        /// snapshot (fresh install) or a file already current plans nothing. Steps apply in version order
        /// and a later step sees the earlier one's result: version 1's rebase runs on the raw stored
        /// `Server.Catalogue` value, and version 2's transform reads that SAME raw value (not what version
        /// 1 would reset it to) - which is safe because a value version 1 resets IS one of the historical
        /// defaults, and `CatalogueOverrides.Derive` on a value that already equals a historical default
        /// derives no overrides on its own. Never throws.
        /// </summary>
        public static MigrationPlan Plan(Dictionary<string, string> snapshot, int fileVersion, string currentShippedLine, IList<string> historicalDefaults)
        {
            var plan = new MigrationPlan { FromVersion = fileVersion, ToVersion = CurrentVersion };
            if (snapshot == null || snapshot.Count == 0) return plan;
            if (fileVersion >= CurrentVersion) return plan;

            for (int version = fileVersion + 1; version <= CurrentVersion; version++)
            {
                if (version == 2)
                {
                    string stored;
                    snapshot.TryGetValue(CatalogueSlot, out stored);
                    var catProblems = new List<string>();
                    string overrides = CatalogueOverrides.Derive(stored, historicalDefaults, currentShippedLine, catProblems);
                    Catalogue shipped = Catalogue.Parse(currentShippedLine, null);
                    plan.CatalogueTransform = new CatalogueTransformResult
                    {
                        StoredLine = stored,
                        Overrides = overrides,
                        Summary = CatalogueOverrides.Describe(overrides, shipped),
                        ShippedCount = shipped.Count,
                        Problems = catProblems,
                        AlreadyCurrentShippedLine = stored != null && currentShippedLine != null &&
                            string.Equals(stored.Trim(), currentShippedLine.Trim(), StringComparison.Ordinal),
                    };
                    continue;
                }

                Rebase[] steps;
                if (!Rebases.TryGetValue(version, out steps)) continue;

                foreach (Rebase r in steps)
                {
                    string slot = Slot(r.Section, r.Key);
                    string stored;
                    if (!snapshot.TryGetValue(slot, out stored)) continue;

                    bool wasOldDefault = false;
                    foreach (string oldDefault in r.OldDefaults)
                    {
                        if (string.Equals(stored.Trim(), oldDefault, StringComparison.Ordinal)) { wasOldDefault = true; break; }
                    }

                    if (wasOldDefault) plan.ResetToDefault.Add(slot);
                    else plan.Kept.Add(new KeptSlot { Slot = slot, Value = stored });
                }
            }
            return plan;
        }

        /// <summary>
        /// One boot line: `"config: version 0 -> 2: Catalogue was the 0.1.0 default, moved to the shipped
        /// 101 rows; overrides: none"` or `"... Catalogue was customised, kept as 3 override(s): Ruby
        /// changed, FlametalNew added, Honey removed"`. Never throws.
        /// </summary>
        public static string Describe(MigrationPlan plan)
        {
            if (plan == null) return "config: nothing to migrate";

            string clause;
            CatalogueTransformResult t = plan.CatalogueTransform;
            if (t == null)
            {
                clause = "nothing to migrate";
            }
            else
            {
                bool wasReset = plan.ResetToDefault.Contains(CatalogueSlot);
                string keptValue = null;
                foreach (KeptSlot k in plan.Kept)
                {
                    if (string.Equals(k.Slot, CatalogueSlot, StringComparison.OrdinalIgnoreCase)) { keptValue = k.Value; break; }
                }

                if (wasReset)
                    clause = "Catalogue was the 0.1.0 default, moved to the shipped " + t.ShippedCount.ToString(CultureInfo.InvariantCulture) + " rows; overrides: none";
                else if (keptValue != null && t.AlreadyCurrentShippedLine)
                    clause = "Catalogue already matched the shipped " + t.ShippedCount.ToString(CultureInfo.InvariantCulture) + " rows; overrides: none";
                else if (keptValue != null && t.Overrides.Length == 0)
                    clause = "Catalogue differed from every shipped default but nothing of it survives as an override";
                else if (keptValue != null)
                    clause = "Catalogue was customised, kept as " + t.Summary;
                else
                    clause = t.Overrides.Length == 0
                        ? "Catalogue matches the shipped default; overrides: none"
                        : "Catalogue carries " + t.Summary;
            }

            return "config: version " + plan.FromVersion.ToString(CultureInfo.InvariantCulture) + " -> " +
                   plan.ToVersion.ToString(CultureInfo.InvariantCulture) + ": " + clause;
        }
    }
}
