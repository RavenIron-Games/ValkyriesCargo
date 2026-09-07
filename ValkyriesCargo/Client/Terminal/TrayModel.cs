using System;
using System.Collections.Generic;
using RavenIron.ValkyriesCargo.Core;

namespace RavenIron.ValkyriesCargo.Client.Terminal
{
    public enum PayMode { Coins, Barter }

    /// <summary>One staged line: what the player staged it at, and what it costs now.</summary>
    public sealed class TrayLine
    {
        public string Prefab = "";
        public int Count;
        /// <summary>The unit price on screen when the line was staged (or last confirmed).</summary>
        public int UnitPriceSeen;
        /// <summary>The unit price in the latest snapshot; what the tray shows and what a confirm sends.</summary>
        public int UnitPriceNow;
        /// <summary>True while the price has moved since it was staged: the line draws amber (design 3.4, Reconfirm).</summary>
        public bool Amber => UnitPriceNow != UnitPriceSeen;
        public long ValueNow => (long)Count * UnitPriceNow;
    }

    /// <summary>
    /// The staging tray behind the Cargo Terminal (design 3.4): what the player has picked to buy and
    /// to offer, at the numbers on screen, and the one Deal it becomes. The tray never computes a price;
    /// it copies them from the MarketSnapshot it is refreshed with, and it sends the unit price the
    /// player is looking at NOW (WORKSPLIT §2). PURE: the window feeds it snapshots, counts and answers.
    /// </summary>
    public sealed class TrayModel
    {
        public const int MaxOfferedLines = 8;

        private readonly List<TrayLine> _offered = new List<TrayLine>();

        public TrayLine Wanted { get; private set; }
        public IReadOnlyList<TrayLine> Offered => _offered;
        public PayMode Mode = PayMode.Coins;
        /// <summary>The footer line: his last words, or why a confirm was stopped.</summary>
        public string Message = "";
        public bool IsEmpty => Wanted == null && _offered.Count == 0;
        public bool AnyAmber
        {
            get
            {
                if (Wanted != null && Wanted.Amber) return true;
                foreach (TrayLine l in _offered) if (l.Amber) return true;
                return false;
            }
        }

        // ---- staging -----------------------------------------------------------------------------------

        /// <summary>Pick a ware to buy: one wanted line per deal (the Deal primitive), clamped to his stock. False for a Want, a bare shelf, or a nonsense count.</summary>
        public bool StageBuy(MarketRow row, int count)
        {
            if (row == null || row.Kind != EntryKind.Ware || count < 1 || row.Stock < 1) return false;
            if (Wanted != null && Wanted.Prefab == row.Prefab) Wanted.Count += count;
            else Wanted = new TrayLine { Prefab = row.Prefab, Count = count, UnitPriceSeen = row.Buy, UnitPriceNow = row.Buy };
            if (Wanted.Count > row.Stock) Wanted.Count = row.Stock;
            return true;
        }

        /// <summary>Offer goods: clamped to what the player carries and to the room on his shelf. False when either is zero.</summary>
        public bool StageOffer(MarketRow row, int count, int has)
        {
            if (row == null || count < 1 || has < 1) return false;
            int room = row.Max - row.Stock;
            if (room < 1) return false;
            TrayLine line = FindOffered(row.Prefab);
            if (line == null)
            {
                if (_offered.Count >= MaxOfferedLines) return false;
                line = new TrayLine { Prefab = row.Prefab, UnitPriceSeen = row.Sell, UnitPriceNow = row.Sell };
                _offered.Add(line);
            }
            line.Count = Math.Min(Math.Min(line.Count + count, has), room);
            return true;
        }

        /// <summary>Take some or all of a line back out of the tray.</summary>
        public void Unstage(string prefab, int count)
        {
            if (count < 1) return;
            if (Wanted != null && Wanted.Prefab == prefab)
            {
                Wanted.Count -= count;
                if (Wanted.Count < 1) Wanted = null;
                return;
            }
            TrayLine line = FindOffered(prefab);
            if (line == null) return;
            line.Count -= count;
            if (line.Count < 1) _offered.Remove(line);
        }

        public void Clear()
        {
            Wanted = null;
            _offered.Clear();
        }

        public TrayLine FindOffered(string prefab)
        {
            foreach (TrayLine l in _offered) if (l.Prefab == prefab) return l;
            return null;
        }

        // ---- the numbers on screen -----------------------------------------------------------------------

        /// <summary>Copy the latest prices onto the lines (amber where they moved) and drop lines whose row vanished. Call after every MarketChanged.</summary>
        public void Refresh(MarketSnapshot m)
        {
            if (m == null) return;
            if (Wanted != null)
            {
                MarketRow r = m.Find(Wanted.Prefab);
                if (r == null || r.Kind != EntryKind.Ware) Wanted = null; else Wanted.UnitPriceNow = r.Buy;
            }
            for (int i = _offered.Count - 1; i >= 0; i--)
            {
                MarketRow r = m.Find(_offered[i].Prefab);
                if (r == null) _offered.RemoveAt(i); else _offered[i].UnitPriceNow = r.Sell;
            }
        }

        /// <summary>What he charges for the wanted line, now.</summary>
        public long Price => Wanted != null ? Wanted.ValueNow : 0;

        /// <summary>What he pays for the offered goods, now.</summary>
        public long OfferedValue
        {
            get { long v = 0; foreach (TrayLine l in _offered) v += l.ValueNow; return v; }
        }

        /// <summary>Positive: the player pays this many coins. Negative: he pays the player. The "you pay" line.</summary>
        public long Net => Price - OfferedValue;

        /// <summary>The coins the deal puts on the table: what the player pays, never negative.</summary>
        public int CoinsOffered => (int)Math.Min(int.MaxValue, Math.Max(0, Net));

        // ---- before sending ------------------------------------------------------------------------------

        /// <summary>
        /// The refusals the server would give, caught here first, in the server's own order and words
        /// (DealReason tokens): null when the deal can go. `has` answers how many of a prefab the player carries.
        /// The inventory-room check is the window's (DealApplier.CanApply); the tray cannot see an inventory.
        /// </summary>
        public string Validate(MarketSnapshot m, int coins, Func<string, int> has)
        {
            if (IsEmpty) return DealReason.EmptyDeal;
            if (m == null) return DealReason.NotConnected;
            if (Wanted != null)
            {
                MarketRow r = m.Find(Wanted.Prefab);
                if (r == null || r.Kind != EntryKind.Ware) return DealReason.UnknownItem;
                if (r.Stock < Wanted.Count) return DealReason.SoldOut;
            }
            foreach (TrayLine l in _offered)
            {
                MarketRow r = m.Find(l.Prefab);
                if (r == null) return DealReason.UnknownItem;
                if (has != null && has(l.Prefab) < l.Count) return "missing_items";
                if (r.Stock + l.Count > r.Max) return DealReason.OverMax;
            }
            if (Net > 0 && coins < Net) return DealReason.CoinsShort;
            return null;
        }

        /// <summary>The words for a stopped confirm.</summary>
        public static string Words(string reason)
        {
            switch (reason)
            {
                case null: return "";
                case DealReason.EmptyDeal: return "Nothing in the tray. Click a ware to buy it, or one of your goods to offer it.";
                case "missing_items": return "You no longer carry that many.";
                default: return Lines.Refusal(reason);
            }
        }

        /// <summary>
        /// The Deal the tray describes, at the prices the player is looking at NOW (that is the reconfirm:
        /// an amber line is accepted at its new price by confirming). Marks every line seen at that price.
        /// </summary>
        public Deal Build(int visitId)
        {
            var d = new Deal { VisitId = visitId, Nonce = Deal.NewNonce() };
            if (Wanted != null)
            {
                Wanted.UnitPriceSeen = Wanted.UnitPriceNow;
                d.Wanted = new DealLine { Prefab = Wanted.Prefab, Count = Wanted.Count, UnitPriceSeen = Wanted.UnitPriceNow };
            }
            foreach (TrayLine l in _offered)
            {
                l.UnitPriceSeen = l.UnitPriceNow;
                d.Offered.Add(new DealLine { Prefab = l.Prefab, Count = l.Count, UnitPriceSeen = l.UnitPriceNow });
            }
            d.CoinsOffered = CoinsOffered;
            return d;
        }

        /// <summary>
        /// Barter's assist (design 3.4): fill the offered side from the player's goods, highest of his prices first,
        /// until it covers the wanted line; the change comes back in coins. Returns how many lines were added or grown.
        /// </summary>
        public int AutoFill(MarketSnapshot m, Func<string, int> has)
        {
            if (m == null || Wanted == null || has == null) return 0;
            var rows = new List<MarketRow>();
            foreach (MarketRow r in m.Rows)
            {
                if (r.Sell < 1 || r.Max - r.Stock < 1) continue;
                if (Wanted != null && r.Prefab == Wanted.Prefab) continue;
                if (has(r.Prefab) < 1) continue;
                rows.Add(r);
            }
            rows.Sort((a, b) => b.Sell != a.Sell ? b.Sell.CompareTo(a.Sell) : string.CompareOrdinal(a.Prefab, b.Prefab));
            int touched = 0;
            foreach (MarketRow r in rows)
            {
                long shortfall = Price - OfferedValue;
                if (shortfall <= 0) break;
                TrayLine existing = FindOffered(r.Prefab);
                int already = existing != null ? existing.Count : 0;
                int want = (int)Math.Min(int.MaxValue, (shortfall + r.Sell - 1) / r.Sell);
                int limit = Math.Min(has(r.Prefab), r.Max - r.Stock) - already;
                int add = Math.Min(want, limit);
                if (add < 1) continue;
                if (StageOffer(r, add, has(r.Prefab))) touched++;
            }
            return touched;
        }

        // ---- after the answer ----------------------------------------------------------------------------

        /// <summary>
        /// The server answered. Ok: the tray empties and he speaks. price_changed: the window has already
        /// published NewMarketState; the lines go amber against it and stay for one more confirm. Anything
        /// else: his refusal, the tray kept. Returns true when the deal went through.
        /// </summary>
        public bool Answer(DealResult r, MarketSnapshot latest)
        {
            if (r == null) { Message = Lines.Refusal(DealReason.Malformed); return false; }
            if (r.Ok)
            {
                bool bought = r.ItemsToAdd.Count > 0;
                Message = bought ? Lines.BuyFor(r.Nonce) : Lines.SellFor(r.Nonce);
                Clear();
                return true;
            }
            if (r.Reason == DealReason.PriceChanged)
            {
                Refresh(latest);
                Message = Lines.PriceChanged;
                return false;
            }
            Message = Lines.Refusal(r.Reason);
            return false;
        }
    }
}
