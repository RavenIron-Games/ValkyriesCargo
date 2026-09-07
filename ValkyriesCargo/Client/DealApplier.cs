using System;
using RavenIron.ValkyriesCargo.Core;
using UnityEngine;

namespace RavenIron.ValkyriesCargo.Client
{
    /// <summary>
    /// The only code that touches the player's inventory for a deal (design 3.4, guarantee 5): after the
    /// server's answer, and only by ItemsToAdd, ItemsToRemove and CoinsDelta. Prefab names cross the wire;
    /// vanilla's inventory counts and removes by the item's SHARED name (`m_shared.m_name`, a "$item_..."
    /// token), so each prefab is resolved through ObjectDB first. Adds use `Inventory.AddItem(GameObject,
    /// amount)`, which caps one call at a stack, so a big count is added stack by stack. The terminal and
    /// the redelivery path both come through here; nothing else in the mod writes an inventory.
    /// </summary>
    public static class DealApplier
    {
        public const string CoinsPrefab = "Coins";

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
            return null;
        }

        /// <summary>
        /// Apply: removals first (goods, then coins), then additions (goods, then coins). Call CanApply first;
        /// a failure mid-way is logged and returns false, but what was removed stays removed, which is why
        /// the check exists. Returns true when every line landed.
        /// </summary>
        public static bool Apply(DealResult r)
        {
            string why = CanApply(r);
            if (why != null) { ValkyriesCargo.Log.LogWarning("deal apply refused: " + why); return false; }
            Inventory inv = Player.m_localPlayer.GetInventory();
            bool ok = true;
            try
            {
                foreach (DealLine line in r.ItemsToRemove) inv.RemoveItem(SharedName(line.Prefab), line.Count);
                if (r.CoinsDelta < 0) inv.RemoveItem(SharedName(CoinsPrefab), -r.CoinsDelta);
                foreach (DealLine line in r.ItemsToAdd) ok &= AddStacks(inv, Prefab(line.Prefab), line.Count);
                if (r.CoinsDelta > 0) ok &= AddStacks(inv, Prefab(CoinsPrefab), r.CoinsDelta);
            }
            catch (Exception ex)
            {
                ValkyriesCargo.Log.LogError("deal apply threw: " + ex);
                return false;
            }
            if (!ok) ValkyriesCargo.Log.LogWarning("deal apply: not every line landed (delivery " + r.DeliveryId + ")");
            return ok;
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

        public static int Count(Inventory inv, string prefabName)
        {
            string shared = SharedName(prefabName);
            return shared == null ? 0 : inv.CountItems(shared);
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
