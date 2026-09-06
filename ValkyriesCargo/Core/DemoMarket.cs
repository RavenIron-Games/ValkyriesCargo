using System;
using System.Collections.Generic;

namespace RavenIron.ValkyriesCargo.Core
{
    /// <summary>
    /// A stand-in for the server so the terminal can be built and reviewed with no world: it holds
    /// a MarketSnapshot and settles Deals against it with the same refusals the real server will
    /// give, in the same order. It is deliberately NOT the real Market (Phase 1's pricing lives
    /// there): prices here only move by a fixed step so the price_changed path can be exercised
    /// (`Tick(prefab)` nudges one line). PURE.
    /// </summary>
    public sealed class DemoMarket
    {
        private readonly MarketSnapshot _market;
        private readonly HashSet<long> _nonces = new HashSet<long>();
        private int _deliveries;

        public DemoMarket(MarketSnapshot market) { _market = market ?? new MarketSnapshot(); }

        public static DemoMarket Default() => new DemoMarket(MarketSnapshot.Parse(MarketSnapshot.Demo, null));

        public MarketSnapshot Market => _market;

        /// <summary>Move one line's prices by 10% so the terminal's price_changed handling can be seen.</summary>
        public bool Tick(string prefab, bool up)
        {
            MarketRow r = _market.Find(prefab);
            if (r == null) return false;
            r.Buy = Math.Max(1, (int)Math.Round(r.Buy * (up ? 1.1 : 0.9)));
            r.Sell = Math.Max(1, (int)Math.Round(r.Sell * (up ? 1.1 : 0.9)));
            r.Trend = up ? 1 : -1;
            return true;
        }

        /// <summary>
        /// Settle a deal the way the server will: validate everything, then commit stock and purse,
        /// then answer. The order of refusals is the contract; the terminal maps each reason to a line.
        /// </summary>
        public DealResult Settle(Deal d, int playerCoins)
        {
            if (d == null) return DealResult.Refuse(0, DealReason.Malformed);
            if (d.IsEmpty) return DealResult.Refuse(d.Nonce, DealReason.EmptyDeal);
            if (d.VisitId != _market.VisitId) return DealResult.Refuse(d.Nonce, DealReason.StaleVisit);
            if (!_nonces.Add(d.Nonce)) return DealResult.Refuse(d.Nonce, DealReason.Duplicate);

            // Everything the player wants must exist, be a ware, be in stock, and cost what they saw.
            MarketRow want = null;
            if (d.Wanted != null)
            {
                want = _market.Find(d.Wanted.Prefab);
                if (want == null || want.Kind != EntryKind.Ware) return Refuse(d, DealReason.UnknownItem);
                if (d.Wanted.Count < 1) return Refuse(d, DealReason.BadCount);
                if (want.Stock < d.Wanted.Count) return Refuse(d, DealReason.SoldOut);
                if (want.Buy != d.Wanted.UnitPriceSeen) return Refuse(d, DealReason.PriceChanged, _market.Encode());
            }

            // Everything offered must be something he buys, fit under his max, and pay what they saw.
            long offeredValue = 0;
            var offeredRows = new List<MarketRow>();
            for (int i = 0; i < d.Offered.Count; i++)
            {
                DealLine line = d.Offered[i];
                MarketRow row = _market.Find(line.Prefab);
                if (row == null) return Refuse(d, DealReason.UnknownItem);
                if (line.Count < 1) return Refuse(d, DealReason.BadCount);
                if (row.Stock + line.Count > row.Max) return Refuse(d, DealReason.OverMax);
                if (row.Sell != line.UnitPriceSeen) return Refuse(d, DealReason.PriceChanged, _market.Encode());
                offeredValue += (long)line.Count * row.Sell;
                offeredRows.Add(row);
            }

            long price = want != null ? (long)d.Wanted.Count * want.Buy : 0;
            long net = price - offeredValue;            // > 0: player pays; < 0: Ingvar pays
            if (net > 0 && playerCoins < net) return Refuse(d, DealReason.CoinsShort);
            if (net < 0 && _market.Purse < -net) return Refuse(d, DealReason.PurseEmpty);

            // Commit.
            if (want != null) want.Stock -= d.Wanted.Count;
            for (int i = 0; i < d.Offered.Count; i++) offeredRows[i].Stock += d.Offered[i].Count;
            _market.Purse += (int)net;

            var r = new DealResult
            {
                Nonce = d.Nonce, Ok = true, Reason = DealReason.Ok,
                DeliveryId = "demo-" + Wire.Int(++_deliveries),
                CoinsDelta = (int)(-net),
            };
            if (want != null) r.ItemsToAdd.Add(new DealLine { Prefab = want.Prefab, Count = d.Wanted.Count, UnitPriceSeen = want.Buy });
            for (int i = 0; i < d.Offered.Count; i++)
                r.ItemsToRemove.Add(new DealLine { Prefab = d.Offered[i].Prefab, Count = d.Offered[i].Count, UnitPriceSeen = offeredRows[i].Sell });
            return r;
        }

        private DealResult Refuse(Deal d, string reason, string market = "")
        {
            // A refused nonce is not spent: the player may resend the same deal after fixing it.
            _nonces.Remove(d.Nonce);
            return DealResult.Refuse(d.Nonce, reason, market);
        }
    }
}
