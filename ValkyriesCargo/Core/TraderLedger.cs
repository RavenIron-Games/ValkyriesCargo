using System;
using System.Collections.Generic;

namespace RavenIron.ValkyriesCargo.Core
{
    /// <summary>One player's running trade totals this session, for barrkbot_cargo_traders.json.</summary>
    public sealed class TraderRow
    {
        public string Name = "";
        public long CoinsSpent;
        public long CoinsEarned;
        public int DealsSettled;
        public long ItemsBought;
        public long ItemsSold;
    }

    /// <summary>
    /// Per-player trade totals, in memory, reset every session (BARRKBOT_CONTRACT.md): coins spent,
    /// coins earned, deals settled, items bought, items sold, keyed by the player's platform id (the
    /// same key OwedLedger uses -- stable across reconnects where the peer uid is not). New counters:
    /// nothing accumulated these before. A row exists only once that player has had a deal settle; an
    /// empty ledger means nobody has traded yet, never that nobody is online. PURE: the caller (the
    /// server, where deals settle) supplies the platform id, the display name and the accepted result;
    /// this only accumulates.
    /// </summary>
    public sealed class TraderLedger
    {
        private readonly Dictionary<string, TraderRow> _rows = new Dictionary<string, TraderRow>(StringComparer.Ordinal);

        public int Count => _rows.Count;
        public IReadOnlyDictionary<string, TraderRow> Rows => _rows;

        /// <summary>
        /// Fold one accepted deal into its player's row (a new row if this is their first). Ignored: no
        /// key, no result, or a refused result -- only a settled deal moves these counters. Items added
        /// to the player (ItemsToAdd) are what they bought; items removed (ItemsToRemove) are what they
        /// sold. CoinsDelta's sign is to the player (Deal.cs): negative is coins paid (spent), positive
        /// is coins received (earned); a barter deal with CoinsDelta 0 moves neither coins field, which
        /// is correct -- nothing changed hands.
        /// </summary>
        public void Record(string playerKey, string playerName, DealResult r)
        {
            if (string.IsNullOrEmpty(playerKey) || r == null || !r.Ok) return;

            TraderRow row;
            if (!_rows.TryGetValue(playerKey, out row))
            {
                row = new TraderRow();
                _rows[playerKey] = row;
            }
            if (!string.IsNullOrEmpty(playerName)) row.Name = playerName;

            row.DealsSettled++;
            if (r.ItemsToAdd != null)
                for (int i = 0; i < r.ItemsToAdd.Count; i++)
                    if (r.ItemsToAdd[i] != null) row.ItemsBought += Math.Max(0, r.ItemsToAdd[i].Count);
            if (r.ItemsToRemove != null)
                for (int i = 0; i < r.ItemsToRemove.Count; i++)
                    if (r.ItemsToRemove[i] != null) row.ItemsSold += Math.Max(0, r.ItemsToRemove[i].Count);

            if (r.CoinsDelta < 0) row.CoinsSpent += -r.CoinsDelta;
            else if (r.CoinsDelta > 0) row.CoinsEarned += r.CoinsDelta;
        }

        /// <summary>A fresh session (or a fresh world): forget every trader. Never called for a restart mid-session -- see BARRKBOT_CONTRACT.md.</summary>
        public void Clear() => _rows.Clear();
    }
}
