using System;
using System.Collections.Generic;

namespace RavenIron.ValkyriesCargo.Core
{
    /// <summary>One ranked entry in a `*_leaders` block: a credited subject and its number.</summary>
    public sealed class LeaderEntry
    {
        public string Credit;
        public double Value;
    }

    /// <summary>
    /// The BarrkBOT export contract's v4 rollover (WindowsDEV/Discord-BarrkBOT/docs/MOD_EXPORT_CONTRACT.md
    /// section 3): split a collection's rows into numbered parts so no file ever again needs a manual
    /// split as it grows, and rank the whole roster before it is split, so a superlative is still
    /// answerable from any one part (the contract's "hard rule"). PURE: knows nothing about JSON or a
    /// row's own fields. The caller renders each row with the real serializer and hands back its length
    /// in characters, so the budget checked here is the exact one BarrkBOT measures, never a guess.
    /// </summary>
    public static class BarrkRollover
    {
        /// <summary>The contract's row-width budget per file: 2,600 characters of JSON.stringify(row).</summary>
        public const int DefaultRowBudgetChars = 2600;
        public const int DefaultLeaderCount = 3;

        /// <summary>
        /// Greedily fills ordered parts up to budgetChars each: a row is added to the current part
        /// unless doing so would exceed the budget, in which case a new part starts with that row. A
        /// row wider than the whole budget still gets a part of its own (never dropped, never split
        /// mid-row). Empty input yields one empty part, so an empty collection still writes "part 1 of
        /// 1" rather than nothing.
        /// </summary>
        public static List<List<int>> Paginate(IList<int> rowWidths, int budgetChars = DefaultRowBudgetChars)
        {
            int cap = Math.Max(1, budgetChars);
            var parts = new List<List<int>>();
            var current = new List<int>();
            int used = 0;
            if (rowWidths != null)
            {
                for (int i = 0; i < rowWidths.Count; i++)
                {
                    int w = Math.Max(0, rowWidths[i]);
                    if (current.Count > 0 && used + w > cap)
                    {
                        parts.Add(current);
                        current = new List<int>();
                        used = 0;
                    }
                    current.Add(i);
                    used += w;
                }
            }
            parts.Add(current);
            return parts;
        }

        /// <summary>
        /// The top N candidates by value, highest first. "Rankable" per the contract: a finite number
        /// whose value is greater than 0 -- null, absent, zero and negative entries are excluded, named
        /// as unmeasured rather than shown as a false last place. Ties keep the input's own order (a
        /// List&lt;T&gt;.Sort is not stable, so this breaks ties on the original index itself), which
        /// keeps the output deterministic for a deterministic input -- the harness depends on that.
        /// </summary>
        public static List<LeaderEntry> TopN(IEnumerable<LeaderEntry> candidates, int n = DefaultLeaderCount)
        {
            var kept = new List<LeaderEntry>();
            if (candidates != null)
            {
                foreach (LeaderEntry c in candidates)
                    if (c != null && !double.IsNaN(c.Value) && !double.IsInfinity(c.Value) && c.Value > 0)
                        kept.Add(c);
            }
            var indexed = new List<KeyValuePair<int, LeaderEntry>>(kept.Count);
            for (int i = 0; i < kept.Count; i++) indexed.Add(new KeyValuePair<int, LeaderEntry>(i, kept[i]));
            indexed.Sort((a, b) =>
            {
                int byValue = b.Value.Value.CompareTo(a.Value.Value);
                return byValue != 0 ? byValue : a.Key.CompareTo(b.Key);
            });
            int take = Math.Max(0, Math.Min(n, indexed.Count));
            var out_ = new List<LeaderEntry>(take);
            for (int i = 0; i < take; i++) out_.Add(indexed[i].Value);
            return out_;
        }
    }
}
