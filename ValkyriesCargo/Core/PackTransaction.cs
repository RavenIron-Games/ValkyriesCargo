using System;
using System.Collections.Generic;

namespace RavenIron.ValkyriesCargo.Core
{
    /// <summary>
    /// The three things a deal does to a pack, as the applier needs them. `DealApplier` implements it over a
    /// real `Inventory`; the harness implements it over a grid of slots. PURE.
    /// </summary>
    public interface IPack
    {
        /// <summary>How many of that prefab the pack holds (the applier's own counting rule).</summary>
        int Count(string prefab);
        /// <summary>Take up to `count` out; answer how many came out.</summary>
        int Remove(string prefab, int count);
        /// <summary>Put `count` in. False when not all of it went in; some of it may have.</summary>
        bool Add(string prefab, int count);
        /// <summary>
        /// The undo of <see cref="Add"/>: take back up to `count` of the units this pack's own Add calls put in
        /// for that prefab, newest first, from the very stacks they went into. Answer how many came out. Not
        /// <see cref="Remove"/>, which takes from the oldest stacks and can leave a new slot filled, so the
        /// removals would have nowhere to go back to (the fix review of 2026-09-24).
        /// </summary>
        int TakeBack(string prefab, int count);
    }

    /// <summary>
    /// A deal's result applied to a pack ALL OR NOTHING (review 2026-09-24, finding 1). Removals first
    /// (goods, then coins), then additions (goods, then coins), as before. What changed: the additions are
    /// remembered too, by how much each prefab's count actually rose, so a failure part-way takes them back
    /// out before it puts the removals back. Until 0.1.5 a deal with two wares could land its first ware,
    /// fail on the second, give the coins back and keep the first ware, and `cargo claim` then redelivered
    /// the same deal and landed the first ware again, for free, every time.
    ///
    /// The same run on a throwaway copy of the pack is the pre-check (`DealApplier.CanApply`), so every line
    /// is tested against the room the lines before it used, not each against the empty pack. PURE.
    /// </summary>
    public static class PackTransaction
    {
        public const string CoinsPrefab = "Coins";

        /// <summary>
        /// Apply every line of `r` to `pack`. True when every line landed. False: the pack is put back as it
        /// was (as far as the pack lets it: `undoShort` counts the units the undo itself could not move,
        /// which is 0 unless the pack changed under us), and the caller must not ack the delivery. `error` is
        /// what a line threw, for the log; null when nothing threw.
        /// </summary>
        public static bool Apply(IPack pack, DealResult r, out int undoShort, out Exception error)
        {
            undoShort = 0;
            error = null;
            if (pack == null || r == null || !r.Ok) return false;
            var removed = new List<DealLine>();
            var added = new List<DealLine>();
            bool ok;
            try { ok = Run(pack, r, removed, added); }
            catch (Exception ex) { ok = false; error = ex; }
            if (ok) return true;

            // Newest first: take back what went in, then put back what came out.
            for (int i = added.Count - 1; i >= 0; i--)
            {
                try { undoShort += added[i].Count - pack.TakeBack(added[i].Prefab, added[i].Count); }
                catch (Exception) { undoShort += added[i].Count; }
            }
            for (int i = removed.Count - 1; i >= 0; i--)
            {
                int before = SafeCount(pack, removed[i].Prefab);
                try { pack.Add(removed[i].Prefab, removed[i].Count); } catch (Exception) { }
                int back = SafeCount(pack, removed[i].Prefab) - before;
                if (back < removed[i].Count) undoShort += removed[i].Count - Math.Max(0, back);
            }
            return false;
        }

        private static bool Run(IPack pack, DealResult r, List<DealLine> removed, List<DealLine> added)
        {
            foreach (DealLine line in r.ItemsToRemove)
            {
                if (line == null || line.Count < 1) return false;
                int got = pack.Remove(line.Prefab, line.Count);
                if (got > 0) removed.Add(new DealLine { Prefab = line.Prefab, Count = got });
                if (got < line.Count) return false;
            }
            if (r.CoinsDelta < 0)
            {
                int got = pack.Remove(CoinsPrefab, -r.CoinsDelta);
                if (got > 0) removed.Add(new DealLine { Prefab = CoinsPrefab, Count = got });
                if (got < -r.CoinsDelta) return false;
            }
            foreach (DealLine line in r.ItemsToAdd)
            {
                if (line == null || line.Count < 1) return false;
                if (!AddCounted(pack, line.Prefab, line.Count, added)) return false;
            }
            if (r.CoinsDelta > 0 && !AddCounted(pack, CoinsPrefab, r.CoinsDelta, added)) return false;
            return true;
        }

        /// <summary>Add, and remember what actually went in by the count's rise, even when the add failed part-way.</summary>
        private static bool AddCounted(IPack pack, string prefab, int count, List<DealLine> added)
        {
            int before = pack.Count(prefab);
            bool ok = false;
            try { ok = pack.Add(prefab, count); }
            finally
            {
                int got = SafeCount(pack, prefab) - before;
                if (got > 0) added.Add(new DealLine { Prefab = prefab, Count = got });
            }
            return ok;
        }

        private static int SafeCount(IPack pack, string prefab)
        {
            try { return pack.Count(prefab); } catch (Exception) { return 0; }
        }
    }
}
