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
        /// A null/empty stored line derives "". A stored row that does not parse is reported in
        /// <paramref name="problems"/> (when given) and ignored - the caller (`ConfigLedger.Plan`) carries
        /// those onto the boot line, so they are never silently dropped. Never throws.
        /// </summary>
        public static string Derive(string storedLine, IList<string> historicalDefaults, string currentShippedLine, List<string> problems = null)
        {
            if (string.IsNullOrEmpty(storedLine)) return "";

            Catalogue stored = Catalogue.Parse(storedLine, problems);
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
        /// The historical default this stored line descends from. Neither a raw match count nor a
        /// proportion of the candidate's own rows can tell "a 0.1.0 file" from "a 0.1.1 file with rows
        /// deleted": a newer default is usually a superset of an older one (the 29 food rows never
        /// dropped anything), so a customised line that has lost some of the newer rows still matches
        /// every row the OLDER default has, and a plain proportion score then hands the older, smaller
        /// default a perfect 1.000 against the newer default's damaged score - undoing the admin's own
        /// deletions on every migration (found live: a 0.1.3 file with four food rows removed by hand
        /// scored the 72-row 0.1.0 default 1.000 against 97/101, so the admin's deletions silently came
        /// back). Gate on EVIDENCE instead, then score: a candidate is eligible only if the stored line
        /// still carries AT LEAST ONE of the rows THAT CANDIDATE introduced over the previous one in the
        /// list, unchanged (the 101-row default introduces the 29 food rows; a Wonderland 72-row line
        /// carries zero of them and is ineligible for it; any line that has ever seen so much as one food
        /// row is eligible for it, however many of the rest an admin later deleted). A "half of what was
        /// introduced" gate (round 1 of this fix) still reverts routine deletions: a small default that
        /// adds three rows is undone by deleting two of them, which an admin trying to prune a bloated
        /// shelf does on the very next update - the review that caught it walked exactly that case. One
        /// surviving row of a candidate's own is proof the admin's file has been through that default at
        /// least once; zero is the only signal that a candidate was never reached at all. Among eligible
        /// candidates, pick the most raw matches; ties go to the LAST (newest). If nothing is eligible (a
        /// stored line unrelated to either default), fall back to the newest.
        /// </summary>
        private static string PickBase(Catalogue stored, IList<string> historicalDefaults)
        {
            if (historicalDefaults == null || historicalDefaults.Count == 0) return Catalogue.DefaultLine;

            string best = null;
            int bestMatches = -1;
            Catalogue previous = null;

            for (int i = 0; i < historicalDefaults.Count; i++)
            {
                Catalogue candidate = Catalogue.Parse(historicalDefaults[i], null);

                int introduced = 0;
                int introducedPresent = 0;
                foreach (CatalogueEntry e in candidate.Entries)
                {
                    CatalogueEntry carriedOver = previous != null ? FindCI(previous, e.Prefab) : null;
                    if (carriedOver != null && RowsIdentical(e, carriedOver)) continue; // not new to this candidate

                    introduced++;
                    CatalogueEntry s = FindCI(stored, e.Prefab);
                    if (s != null && RowsIdentical(s, e)) introducedPresent++;
                }
                bool eligible = introduced == 0 || introducedPresent >= 1;

                if (eligible)
                {
                    int matches = 0;
                    foreach (CatalogueEntry e in stored.Entries)
                    {
                        CatalogueEntry c = FindCI(candidate, e.Prefab);
                        if (c != null && RowsIdentical(e, c)) matches++;
                    }
                    if (matches >= bestMatches) { bestMatches = matches; best = historicalDefaults[i]; }
                }

                previous = candidate;
            }

            return best ?? historicalDefaults[historicalDefaults.Count - 1];
        }

        // ---- the console verbs (2026-09-16: `cargo catalogue add|remove` edit the overrides, not the line) ----

        /// <summary>
        /// `overrides` with `entryText` (one `Prefab:Base:Target:Max:Kind`) upserted: replacing an existing
        /// override of the same prefab in place (an edit keeps its position), replacing a `-Prefab` removal
        /// of the same prefab in place (undoing it), or appended. When the entry is IDENTICAL to
        /// <paramref name="shipped"/>'s own row for that prefab, it is a no-op: any existing token for it
        /// is dropped rather than storing a redundant override, and `report` says so (pass null to skip
        /// this check). Null, with the reason in `report`, when the entry does not parse or is not exactly
        /// one entry. PURE, never throws.
        /// </summary>
        public static string Upsert(string overrides, string entryText, Catalogue shipped, out string report)
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

            CatalogueEntry shippedRow = FindCI(shipped, e.Prefab);
            if (shippedRow != null && RowsIdentical(e, shippedRow))
            {
                // Exactly the shipped row: no override is needed. Drop whatever was there (an edit or an
                // undone removal) rather than store a redundant upsert that matches the shipped default.
                if (at >= 0)
                {
                    tokens.RemoveAt(at);
                    report = "that already matches the shipped value now; the earlier override for " + e.Prefab + " is dropped";
                }
                else
                {
                    report = "that is already the shipped value, nothing changed";
                }
                return string.Join(", ", tokens.ToArray());
            }

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
