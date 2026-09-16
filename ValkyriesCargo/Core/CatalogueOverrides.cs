using System;
using System.Collections.Generic;
using System.Globalization;

namespace RavenIron.ValkyriesCargo.Core
{
    /// <summary>
    /// The config migration (2026-09-16): the shipped catalogue stays code; the cfg holds only what an
    /// admin actually changed. `Server.CatalogueOverrides` replaces `Server.Catalogue` so a shipped
    /// default can grow (the 29 food rows, the next one) without a stored line shadowing it forever
    /// (`ConfigLedger` is the migration that gets an existing server there).
    ///
    /// Format: the catalogue's own row syntax `Prefab:Base:Target:Max:Kind` for a row added or changed,
    /// and `-Prefab` for a shipped row removed, comma-separated. Empty = the shipped catalogue as is.
    /// PURE, never throws: a bad token is reported in `problems` (when given) and skipped.
    /// </summary>
    public static class CatalogueOverrides
    {
        // ---- applying the overrides onto the shipped catalogue -------------------------------------

        /// <summary>
        /// Shipped rows in shipped order, each upsert applied in override order (a changed row keeps its
        /// shipped position; a new row appends), then removals. A removal naming a prefab that is
        /// neither shipped nor overridden is reported and skipped; a row that fails <see cref="Catalogue.Parse"/>
        /// is reported and skipped (the parser's own message). Never throws.
        /// </summary>
        public static Catalogue Apply(Catalogue shipped, string overrides, List<string> problems)
        {
            var list = new List<CatalogueEntry>();
            if (shipped != null)
                foreach (CatalogueEntry e in shipped.Entries)
                    if (e != null && !string.IsNullOrEmpty(e.Prefab))
                        list.Add(Clone(e));

            var removals = new List<string>();
            foreach (string tok in SplitTokens(overrides))
            {
                if (tok.Length > 0 && tok[0] == '-')
                {
                    removals.Add(tok.Substring(1).Trim());
                    continue;
                }

                Catalogue one = Catalogue.Parse(tok, problems);
                if (one.Count != 1) continue; // Parse already reported why (0 fields, or more than one entry in a token)

                CatalogueEntry e = one.Entries[0];
                int at = IndexOf(list, e.Prefab);
                if (at >= 0) list[at] = e; else list.Add(e);
            }

            foreach (string prefab in removals)
            {
                int at = IndexOf(list, prefab);
                if (at >= 0) list.RemoveAt(at);
                else Report(problems, "-" + prefab + ": '" + prefab + "' is neither shipped nor overridden, nothing to remove");
            }

            return Catalogue.Parse(Catalogue.Compose(list), null);
        }

        // ---- deriving overrides from a stored catalogue line (the migration) -----------------------

        /// <summary>
        /// What a stored `Server.Catalogue` line (0.1.0-0.1.3) becomes as overrides on the CURRENT shipped
        /// catalogue. Picks the BASE default (from <paramref name="historicalDefaults"/>, oldest first) with
        /// the most rows identical to the stored line - prefab, the three numbers and the kind all matching;
        /// ties go to the LAST in the list, the newest. Overrides are every stored row absent from or
        /// differing from that base (as upserts, in stored order) plus `-Prefab` for every base row absent
        /// from the stored line. Then pruned: an upsert identical to the CURRENT shipped row is dropped (a
        /// no-op), and a removal of a prefab the current shipped catalogue does not carry is dropped (moot).
        /// A null/empty stored line derives "". A stored row that does not parse is ignored (the boot log's
        /// parse problems already cover it). Never throws.
        /// </summary>
        public static string Derive(string storedLine, IList<string> historicalDefaults, string currentShippedLine)
        {
            if (string.IsNullOrEmpty(storedLine)) return "";

            Catalogue stored = Catalogue.Parse(storedLine, null);
            if (stored.Count == 0) return "";

            string baseLine = PickBase(stored, historicalDefaults);
            Catalogue baseCat = Catalogue.Parse(baseLine, null);

            var tokens = new List<string>();
            foreach (CatalogueEntry e in stored.Entries)
            {
                CatalogueEntry b = FindCI(baseCat, e.Prefab);
                if (b == null || !RowsIdentical(e, b)) tokens.Add(e.ToString());
            }
            foreach (CatalogueEntry b in baseCat.Entries)
            {
                if (FindCI(stored, b.Prefab) == null) tokens.Add("-" + b.Prefab);
            }

            // Prune against the CURRENT shipped catalogue: a no-op upsert, or a removal of something
            // not shipped any more, carries nothing forward.
            Catalogue shipped = Catalogue.Parse(currentShippedLine, null);
            var pruned = new List<string>();
            foreach (string tok in tokens)
            {
                if (tok.Length > 0 && tok[0] == '-')
                {
                    string prefab = tok.Substring(1).Trim();
                    if (FindCI(shipped, prefab) != null) pruned.Add(tok);
                    continue;
                }

                Catalogue one = Catalogue.Parse(tok, null);
                if (one.Count != 1) continue;
                CatalogueEntry e = one.Entries[0];
                CatalogueEntry s = FindCI(shipped, e.Prefab);
                if (s == null || !RowsIdentical(e, s)) pruned.Add(tok);
            }

            return string.Join(", ", pruned.ToArray());
        }

        /// <summary>
        /// The historical default with the most rows identical to <paramref name="stored"/>; ties go to
        /// the newest (last). Scored as a PROPORTION of the candidate's own rows, not a raw count: a newer
        /// default is usually a superset of an older one (the 29 food rows never dropped anything), so by
        /// raw count it can never score lower than the older one it contains - a customised 72-row line
        /// would otherwise always "tie" the 101-row default (both match the same 69 untouched rows) and
        /// the tie-break would hand it the wrong base. Measuring what fraction of EACH candidate survives
        /// in the stored line lets the closer, smaller default win outright instead.
        /// </summary>
        private static string PickBase(Catalogue stored, IList<string> historicalDefaults)
        {
            string best = historicalDefaults != null && historicalDefaults.Count > 0 ? historicalDefaults[0] : Catalogue.DefaultLine;
            double bestScore = -1;
            if (historicalDefaults == null) return best;

            for (int i = 0; i < historicalDefaults.Count; i++)
            {
                Catalogue candidate = Catalogue.Parse(historicalDefaults[i], null);
                int matches = 0;
                foreach (CatalogueEntry e in stored.Entries)
                {
                    CatalogueEntry c = FindCI(candidate, e.Prefab);
                    if (c != null && RowsIdentical(e, c)) matches++;
                }
                double score = candidate.Count > 0 ? (double)matches / candidate.Count : 0.0;
                if (score >= bestScore) { bestScore = score; best = historicalDefaults[i]; }
            }
            return best;
        }

        // ---- the console verbs (2026-09-16: `cargo catalogue add|remove` edit the overrides, not the line) ----

        /// <summary>
        /// `overrides` with `entryText` (one `Prefab:Base:Target:Max:Kind`) upserted: replacing an existing
        /// override of the same prefab in place (an edit keeps its position), replacing a `-Prefab` removal
        /// of the same prefab in place (undoing it), or appended. Null, with the reason in `report`, when
        /// the entry does not parse or is not exactly one entry. PURE, never throws.
        /// </summary>
        public static string Upsert(string overrides, string entryText, out string report)
        {
            var problems = new List<string>();
            Catalogue one = Catalogue.Parse(entryText, problems);
            if (problems.Count > 0) { report = problems[0]; return null; }
            if (one.Count != 1)
            {
                report = "expected exactly one Prefab:Base:Target:Max:Kind entry, got " + one.Count.ToString(CultureInfo.InvariantCulture);
                return null;
            }

            CatalogueEntry e = one.Entries[0];
            var tokens = new List<string>(SplitTokens(overrides));
            int at = IndexOfToken(tokens, e.Prefab);
            string entryLine = e.ToString();

            if (at >= 0)
            {
                bool wasRemoval = tokens[at].Length > 0 && tokens[at][0] == '-';
                tokens[at] = entryLine;
                report = wasRemoval
                    ? "added " + e.Prefab + " back to the overrides: " + Catalogue.Describe(e) + " (undoing the earlier removal)"
                    : "changed " + e.Prefab + " in the overrides: " + Catalogue.Describe(e);
            }
            else
            {
                tokens.Add(entryLine);
                report = "added " + e.Prefab + " to the overrides: " + Catalogue.Describe(e);
            }
            return string.Join(", ", tokens.ToArray());
        }

        /// <summary>
        /// `overrides` without `prefab`: a SHIPPED prefab gets a `-Prefab` token (and drops any upsert of
        /// it already there); an OVERRIDE-ONLY prefab just loses its row; a prefab that is neither is
        /// refused with the reason in `report`, null. PURE, never throws.
        /// </summary>
        public static string Remove(string overrides, string prefab, Catalogue shipped, out string report)
        {
            string name = (prefab ?? "").Trim();
            if (name.Length == 0) { report = "name the prefab to remove"; return null; }

            var tokens = new List<string>(SplitTokens(overrides));
            bool inShipped = FindCI(shipped, name) != null;
            int at = IndexOfToken(tokens, name);

            if (at >= 0)
            {
                bool alreadyRemoval = tokens[at].Length > 0 && tokens[at][0] == '-';
                if (alreadyRemoval) { report = name + " is already removed"; return null; }

                if (inShipped)
                {
                    tokens[at] = "-" + name;
                    report = "removed " + name + " (it was overridden; the shipped row is hidden too now)";
                }
                else
                {
                    tokens.RemoveAt(at);
                    report = "removed " + name + " (an override-only row; nothing shipped by that name)";
                }
                return string.Join(", ", tokens.ToArray());
            }

            if (inShipped)
            {
                tokens.Add("-" + name);
                report = "removed " + name + " (a shipped row; -" + name + " added to the overrides)";
                return string.Join(", ", tokens.ToArray());
            }

            report = name + " is not in the catalogue (not shipped, not overridden)";
            return null;
        }

        /// <summary>`"3 override(s): Ruby changed, FlametalNew added, Honey removed"`, or `"none"`. Never throws.</summary>
        public static string Describe(string overrides, Catalogue shipped)
        {
            var parts = new List<string>();
            foreach (string tok in SplitTokens(overrides))
            {
                if (tok.Length > 0 && tok[0] == '-')
                {
                    parts.Add(tok.Substring(1).Trim() + " removed");
                    continue;
                }
                Catalogue one = Catalogue.Parse(tok, null);
                if (one.Count != 1) continue;
                CatalogueEntry e = one.Entries[0];
                bool isShipped = FindCI(shipped, e.Prefab) != null;
                parts.Add(e.Prefab + (isShipped ? " changed" : " added"));
            }
            return parts.Count == 0 ? "none" : parts.Count.ToString(CultureInfo.InvariantCulture) + " override(s): " + string.Join(", ", parts.ToArray());
        }

        // ---- helpers ---------------------------------------------------------------------------------

        private static CatalogueEntry Clone(CatalogueEntry e) =>
            new CatalogueEntry { Prefab = e.Prefab, BasePrice = e.BasePrice, TargetStock = e.TargetStock, MaxStock = e.MaxStock, Kind = e.Kind };

        private static bool RowsIdentical(CatalogueEntry a, CatalogueEntry b) =>
            a != null && b != null && string.Equals(a.Prefab, b.Prefab, StringComparison.OrdinalIgnoreCase) &&
            a.BasePrice == b.BasePrice && a.TargetStock == b.TargetStock && a.MaxStock == b.MaxStock && a.Kind == b.Kind;

        private static CatalogueEntry FindCI(Catalogue cat, string prefab)
        {
            if (cat == null || string.IsNullOrEmpty(prefab)) return null;
            foreach (CatalogueEntry e in cat.Entries)
                if (e != null && string.Equals(e.Prefab, prefab, StringComparison.OrdinalIgnoreCase)) return e;
            return null;
        }

        private static int IndexOf(List<CatalogueEntry> list, string prefab)
        {
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i].Prefab, prefab, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        /// <summary>The index of the token (upsert or `-removal`) that names `prefab`, or -1.</summary>
        private static int IndexOfToken(List<string> tokens, string prefab)
        {
            for (int i = 0; i < tokens.Count; i++)
            {
                string tok = tokens[i];
                string name = tok.Length > 0 && tok[0] == '-' ? tok.Substring(1).Trim() : FirstField(tok);
                if (string.Equals(name, prefab, StringComparison.OrdinalIgnoreCase)) return i;
            }
            return -1;
        }

        private static string FirstField(string tok)
        {
            int c = tok.IndexOf(':');
            return c < 0 ? tok.Trim() : tok.Substring(0, c).Trim();
        }

        private static IEnumerable<string> SplitTokens(string overrides)
        {
            if (string.IsNullOrEmpty(overrides)) yield break;
            foreach (string part in overrides.Split(','))
            {
                string t = part.Trim();
                if (t.Length > 0) yield return t;
            }
        }

        private static void Report(List<string> problems, string text)
        {
            if (problems != null) problems.Add(text);
        }
    }
}
