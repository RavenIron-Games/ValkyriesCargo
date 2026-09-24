using System;
using RavenIron.ValkyriesCargo.Core;
using UnityEngine;

namespace RavenIron.ValkyriesCargo.Client
{
    /// <summary>
    /// The only code that touches the player's inventory for a deal (design 3.4, guarantee 5): after the
    /// server's answer, and only by ItemsToAdd, ItemsToRemove and CoinsDelta. Prefab names cross the wire.
    /// Vanilla's `CountItems` / `RemoveItem(string, ...)` key on the item's SHARED token (`m_shared.m_name`,
    /// "$item_..."), and two prefabs can carry one token (`FishRaw` and `FishAnglerRaw` are both
    /// `$item_fish_raw`; the rule-2 review of PR #85, 2026-09-15) - counted that way, forty raw fish would
    /// sell at the anglerfish price. So a stack is matched by the prefab it knows it came from
    /// (`ItemData.m_dropPrefab`, which the engine sets on pickup, on load and on every add) and by the
    /// token only for a stack that does not know; removal goes stack by stack through
    /// `Inventory.RemoveItem(ItemData, amount)`. Adds use `Inventory.AddItem(GameObject, amount)`, which
    /// caps one call at a stack, so a big count is added stack by stack. The terminal and the redelivery
    /// path both come through here; nothing else in the mod writes an inventory.
    /// </summary>
    public static class DealApplier
    {
        public const string CoinsPrefab = PackTransaction.CoinsPrefab;

        /// <summary>
        /// The DeliveryId of the last result this applier wrote into a pack IN FULL. The transport acks on
        /// it, because nothing else can answer "did it land": CargoRpc marks the inbox BEFORE handing the
        /// result to the caller, so the inbox says "applied" for a delivery the pack then refused, and an
        /// ack on that clears the server's owed row for good. Null while the last apply did not land.
        /// </summary>
        public static string LastApplied { get; private set; }

        /// <summary>Null when the whole result can be applied now; else the DealReason-style token that stops it.</summary>
        public static string CanApply(DealResult r)
        {
            if (r == null || !r.Ok) return DealReason.Malformed;
            Player p = Player.m_localPlayer;
            if (p == null) return "no_player";
            Inventory inv = p.GetInventory();
            if (inv == null) return "no_player";

            if (r.CoinsDelta < 0 && Count(inv, CoinsPrefab) < -r.CoinsDelta) return DealReason.CoinsShort;
            foreach (DealLine line in r.ItemsToRemove)
            {
                GameObject prefab = Prefab(line.Prefab);
                if (prefab == null) return DealReason.UnknownItem;
                if (Count(inv, line.Prefab) < line.Count) return "missing_items";
            }
            foreach (DealLine line in r.ItemsToAdd)
            {
                GameObject prefab = Prefab(line.Prefab);
                if (prefab == null) return DealReason.UnknownItem;
                if (!inv.CanAddItem(prefab, line.Count)) return DealReason.InventoryFull;
            }
            if (r.CoinsDelta > 0)
            {
                GameObject coins = Prefab(CoinsPrefab);
                if (coins == null || !inv.CanAddItem(coins, r.CoinsDelta)) return DealReason.InventoryFull;
            }
            // Each line above was asked alone, against the same free slots: two wares that each fit alone
            // passed with one slot free (review 2026-09-24, finding 1). The whole deal, run in order on a
            // throwaway copy of the pack, answers for all of them together.
            if (!FitsTogether(inv, r)) return DealReason.InventoryFull;
            return null;
        }

        private static int _copyThrows;

        /// <summary>
        /// Run the whole result on a copy of the pack (the same width and height, loaded from the pack's own
        /// `Save`), exactly the way <see cref="Apply"/> will run it on the real one. A copy that cannot be made
        /// answers yes: Apply is all or nothing by itself, so the worst case is a refusal after the server said yes.
        /// </summary>
        private static bool FitsTogether(Inventory inv, DealResult r)
        {
            Inventory copy;
            try
            {
                var pkg = new ZPackage();
                inv.Save(pkg);
                copy = new Inventory("VCargo_dryrun", null, inv.GetWidth(), inv.GetHeight());
                copy.Load(new ZPackage(pkg.GetArray()));
            }
            catch (Exception ex)
            {
                if (_copyThrows++ < 3) ValkyriesCargo.Log.LogWarning("deal pre-check: copying the pack threw, so the lines were checked one by one: " + ex.Message);
                return true;
            }
            int undoShort;
            Exception error;
            return PackTransaction.Apply(new InventoryPack(copy), r, out undoShort, out error);
        }

        /// <summary>
        /// Apply: removals first (goods, then coins), then additions (goods, then coins), ALL OR NOTHING
        /// (<see cref="PackTransaction"/>): a failure part-way takes back out whatever went in and puts back
        /// whatever came out, so a result that did not land leaves the pack as it was, and its redelivery
        /// (`cargo claim`) is the only copy. Call CanApply first. Returns true when every line landed.
        /// </summary>
        public static bool Apply(DealResult r)
        {
            LastApplied = null;
            string why = CanApply(r);
            if (why != null) { ValkyriesCargo.Log.LogWarning("deal apply refused: " + why); return false; }
            Player p = Player.m_localPlayer;
            Inventory inv = p != null ? p.GetInventory() : null;
            if (inv == null) { ValkyriesCargo.Log.LogWarning("deal apply refused: no_player"); return false; }

            int undoShort;
            Exception error;
            bool ok = PackTransaction.Apply(new InventoryPack(inv), r, out undoShort, out error);
            if (error != null) ValkyriesCargo.Log.LogError("deal apply threw: " + error);
            if (!ok)
            {
                // The pre-check makes this rare; when it happens, the pack goes back to what it was, and the
                // server still owes the delivery (it is not acked without an applied result).
                ValkyriesCargo.Log.LogWarning("deal apply: not every line landed (delivery " + r.DeliveryId + "); the pack was put back as it was" +
                                              (undoShort > 0 ? ", except " + undoShort + " unit(s) the undo could not move" : ""));
            }
            if (ok) LastApplied = r.DeliveryId;
            return ok;
        }

        /// <summary>
        /// A real inventory as the pure transaction sees it, by this class's own prefab rules. Every Add keeps a
        /// journal of exactly which stacks rose and by how much (vanilla merges into existing stacks first and
        /// then opens a slot), so TakeBack returns those units from those stacks, newest first, and the pack
        /// ends with the slots it started with. One instance per Apply.
        /// </summary>
        private sealed class InventoryPack : IPack
        {
            private sealed class Added { public string Prefab; public ItemDrop.ItemData Item; public int Units; }

            private readonly Inventory _inv;
            private readonly System.Collections.Generic.List<Added> _journal = new System.Collections.Generic.List<Added>();
            public InventoryPack(Inventory inv) { _inv = inv; }
            public int Count(string prefab) => DealApplier.Count(_inv, prefab);
            public int Remove(string prefab, int count) => DealApplier.Remove(_inv, prefab, count);

            public bool Add(string prefab, int count)
            {
                var before = new System.Collections.Generic.Dictionary<ItemDrop.ItemData, int>();
                foreach (ItemDrop.ItemData item in _inv.GetAllItems()) before[item] = item.m_stack;
                try { return AddStacks(_inv, Prefab(prefab), count); }
                finally
                {
                    foreach (ItemDrop.ItemData item in _inv.GetAllItems())
                    {
                        int was;
                        int rose = before.TryGetValue(item, out was) ? item.m_stack - was : item.m_stack;
                        if (rose > 0) _journal.Add(new Added { Prefab = prefab, Item = item, Units = rose });
                    }
                }
            }

            public int TakeBack(string prefab, int count)
            {
                int left = count;
                for (int i = _journal.Count - 1; i >= 0 && left > 0; i--)
                {
                    Added a = _journal[i];
                    if (a.Prefab != prefab || a.Units <= 0) continue;
                    int take = Math.Min(left, Math.Min(a.Units, a.Item.m_stack));
                    if (take <= 0 || !_inv.RemoveItem(a.Item, take)) continue;
                    a.Units -= take;
                    left -= take;
                }
                return count - left;
            }
        }

        /// <summary>One line of words for the console or the HUD: "+2 Iron, -38 coins".</summary>
        public static string Describe(DealResult r)
        {
            if (r == null) return "";
            var parts = new System.Collections.Generic.List<string>();
            foreach (DealLine l in r.ItemsToAdd) parts.Add("+" + l.Count + " " + l.Prefab);
            foreach (DealLine l in r.ItemsToRemove) parts.Add("-" + l.Count + " " + l.Prefab);
            if (r.CoinsDelta != 0) parts.Add((r.CoinsDelta > 0 ? "+" : "") + r.CoinsDelta + " coins");
            return string.Join(", ", parts.ToArray());
        }

        public static GameObject Prefab(string prefabName)
        {
            if (string.IsNullOrEmpty(prefabName) || ObjectDB.instance == null) return null;
            GameObject go = ObjectDB.instance.GetItemPrefab(prefabName);
            return go != null && go.GetComponent<ItemDrop>() != null ? go : null;
        }

        /// <summary>The "$item_..." token vanilla's inventory keys on, or null for an unknown prefab.</summary>
        public static string SharedName(string prefabName)
        {
            GameObject go = Prefab(prefabName);
            if (go == null) return null;
            ItemDrop drop = go.GetComponent<ItemDrop>();
            return drop.m_itemData != null && drop.m_itemData.m_shared != null ? drop.m_itemData.m_shared.m_name : null;
        }

        /// <summary>
        /// How many of that PREFAB the pack holds: every stack that knows it came from this prefab, plus
        /// (for a stack that knows no prefab) the token match vanilla would have made. The world-level rule
        /// is vanilla's own `CountItems` rule, kept.
        /// </summary>
        public static int Count(Inventory inv, string prefabName)
        {
            string shared = SharedName(prefabName);
            if (shared == null || inv == null) return 0;
            int n = 0;
            foreach (ItemDrop.ItemData item in inv.GetAllItems())
                if (IsStackOf(item, prefabName, shared)) n += item.m_stack;
            return n;
        }

        /// <summary>The stack's own prefab decides; the token only when a stack has none.</summary>
        private static bool IsStackOf(ItemDrop.ItemData item, string prefabName, string shared)
        {
            if (item == null || item.m_shared == null || item.m_stack <= 0) return false;
            if (item.m_worldLevel < Game.m_worldLevel) return false;
            if (item.m_dropPrefab != null) return string.Equals(PrefabName(item.m_dropPrefab.name), prefabName, StringComparison.Ordinal);
            return item.m_shared.m_name == shared;
        }

        /// <summary>A prefab's name as the catalogue spells it: "FishRaw", never "FishRaw(Clone)".</summary>
        private static string PrefabName(string name)
        {
            if (name == null) return "";
            int at = name.IndexOf("(Clone)", StringComparison.Ordinal);
            return (at >= 0 ? name.Substring(0, at) : name).Trim();
        }

        /// <summary>
        /// Take up to `count` of that prefab out, stack by stack, and say how many came out. Less than
        /// `count` means the pack was short (CanApply counted, so only a race gets here) and Apply puts
        /// back what it took (and, since 0.1.5, takes back out what it added).
        /// </summary>
        private static int Remove(Inventory inv, string prefabName, int count)
        {
            string shared = SharedName(prefabName);
            if (shared == null || inv == null || count <= 0) return 0;
            int left = count;
            // A copy: RemoveItem edits the list it hands out.
            foreach (ItemDrop.ItemData item in new System.Collections.Generic.List<ItemDrop.ItemData>(inv.GetAllItems()))
            {
                if (left <= 0) break;
                if (!IsStackOf(item, prefabName, shared)) continue;
                int take = Math.Min(left, item.m_stack);
                if (!inv.RemoveItem(item, take)) break;
                left -= take;
            }
            return count - left;
        }

        private static bool AddStacks(Inventory inv, GameObject prefab, int count)
        {
            if (prefab == null || count <= 0) return false;
            ItemDrop drop = prefab.GetComponent<ItemDrop>();
            int stack = Math.Max(1, drop.m_itemData.m_shared.m_maxStackSize);
            int left = count;
            int guard = 0;
            while (left > 0 && guard++ < 1000)
            {
                int n = Math.Min(stack, left);
                if (!inv.AddItem(prefab, n)) return false;
                left -= n;
            }
            return left == 0;
        }
    }
}
