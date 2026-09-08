using System;
using System.Collections.Generic;
using RavenIron.ValkyriesCargo.Core;

namespace RavenIron.ValkyriesCargo.Client.Terminal
{
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
    /// There is no pay mode (removed 2026-09-08, the playtest's item 5): the tray is always what the player
    /// gets and what they give, and the one balance line says who pays whom the difference. Since the same
    /// evening the GET side is a list too (the owner's ask: more than one ware per deal): one line per ware,
    /// up to MaxWantedLines, the price their sum; `Wanted` is the first of them, kept for the code and the
    /// harness that grew up with one.
    /// </summary>
    public sealed class TrayModel
    {
        public const int MaxOfferedLines = 8;
        public const int MaxWantedLines = 8;

        /// <summary>The window's own refusals, beside the server's DealReason tokens.</summary>
        public const string MissingItems = "missing_items";
        /// <summary>`Server.EnableBarter` is off and the tray holds goods beside a ware: coins only for his wares there.</summary>
        public const string BarterOff = "barter_off";

        private readonly List<TrayLine> _wanted = new List<TrayLine>();
        private readonly List<TrayLine> _offered = new List<TrayLine>();

        /// <summary>Every ware staged to buy, in the order they were staged.</summary>
        public IReadOnlyList<TrayLine> Wants => _wanted;
        /// <summary>The first wanted line, or null when nothing is staged to buy.</summary>
        public TrayLine Wanted => _wanted.Count > 0 ? _wanted[0] : null;
        public IReadOnlyList<TrayLine> Offered => _offered;
        /// <summary>The footer line: his last words, or why a confirm was stopped.</summary>
        public string Message = "";
        public bool IsEmpty => _wanted.Count == 0 && _offered.Count == 0;
        public bool AnyAmber
        {
            get
            {
                foreach (TrayLine l in _wanted) if (l.Amber) return true;
                foreach (TrayLine l in _offered) if (l.Amber) return true;
                return false;
            }
        }

        // ---- staging -----------------------------------------------------------------------------------

        /// <summary>
        /// Pick a ware to buy: one line per ware, up to MaxWantedLines, each clamped to his stock. False for a
        /// Want, a bare shelf, a nonsense count, or a tray with no room for another ware.
        /// </summary>
        public bool StageBuy(MarketRow row, int count)
        {
            if (row == null || row.Kind != EntryKind.Ware || count < 1 || row.Stock < 1) return false;
            TrayLine line = FindWanted(row.Prefab);
            if (line == null)
            {
                if (_wanted.Count >= MaxWantedLines) return false;
                line = new TrayLine { Prefab = row.Prefab, UnitPriceSeen = row.Buy, UnitPriceNow = row.Buy };
                _wanted.Add(line);
            }
            line.Count = Math.Min(line.Count + count, row.Stock);
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
            TrayLine want = FindWanted(prefab);
            if (want != null)
            {
                want.Count -= count;
                if (want.Count < 1) _wanted.Remove(want);
                return;
            }
            TrayLine line = FindOffered(prefab);
            if (line == null) return;
            line.Count -= count;
            if (line.Count < 1) _offered.Remove(line);
        }

        /// <summary>
        /// The count box (2026-09-08, the playtest's item 4): set a staged line to exactly `count`, clamped the
        /// way staging clamps (his stock for a wanted line; what the player carries and the room on his shelf
        /// for an offer), never below 1. Returns the count the line ended at; 0 when there is no such line, or
        /// the row is gone (the line is dropped then, as Refresh would). A count below 1 leaves the line as it
        /// is: the box is empty while the player types, and the x button is how a line goes.
        /// </summary>
        public int SetCount(MarketSnapshot m, string prefab, int count, Func<string, int> has)
        {
            if (m == null || string.IsNullOrEmpty(prefab)) return 0;
            MarketRow r = m.Find(prefab);
            TrayLine want = FindWanted(prefab);
            if (want != null)
            {
                if (r == null || r.Kind != EntryKind.Ware) { _wanted.Remove(want); return 0; }
                if (count >= 1) want.Count = Math.Max(1, Math.Min(count, r.Stock));
                return want.Count;
            }
            TrayLine line = FindOffered(prefab);
            if (line == null) return 0;
            if (r == null) { _offered.Remove(line); return 0; }
            if (count >= 1)
            {
                int cap = Math.Min(has != null ? has(prefab) : int.MaxValue, r.Max - r.Stock);
                line.Count = Math.Max(1, Math.Min(count, cap));
            }
            return line.Count;
        }

        /// <summary>
        /// The "all" button: for a wanted line, as many as he has AND the player can pay for out of their
        /// coins plus what the tray already offers, after the OTHER wanted lines are paid (never below 1, so a
        /// player with no coins still sees the coins_short refusal rather than an empty tray); for an offer,
        /// everything the player carries that fits on his shelf. Returns the count the line ended at.
        /// </summary>
        public int AllOf(MarketSnapshot m, string prefab, Func<string, int> has, int coins)
        {
            if (m == null || string.IsNullOrEmpty(prefab)) return 0;
            MarketRow r = m.Find(prefab);
            if (r == null) return 0;
            TrayLine want = FindWanted(prefab);
            if (want != null)
            {
                long unit = Math.Max(1, want.UnitPriceNow);
                long others = Price - want.ValueNow;
                long afford = (Math.Max(0, coins) + OfferedValue - others) / unit;
                int n = (int)Math.Max(1, Math.Min(r.Stock, Math.Min(int.MaxValue, afford)));
                return SetCount(m, prefab, n, has);
            }
            int all = Math.Min(has != null ? has(prefab) : 0, r.Max - r.Stock);
            return SetCount(m, prefab, Math.Max(1, all), has);
        }

        /// <summary>Take a whole line out (the x button).</summary>
        public void Remove(string prefab)
        {
            TrayLine want = FindWanted(prefab);
            if (want != null) { _wanted.Remove(want); return; }
            TrayLine line = FindOffered(prefab);
            if (line != null) _offered.Remove(line);
        }

        public void Clear()
        {
            _wanted.Clear();
            _offered.Clear();
        }

        public TrayLine FindWanted(string prefab)
        {
            foreach (TrayLine l in _wanted) if (l.Prefab == prefab) return l;
            return null;
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
            for (int i = _wanted.Count - 1; i >= 0; i--)
            {
                MarketRow r = m.Find(_wanted[i].Prefab);
                if (r == null || r.Kind != EntryKind.Ware) _wanted.RemoveAt(i); else _wanted[i].UnitPriceNow = r.Buy;
            }
            for (int i = _offered.Count - 1; i >= 0; i--)
            {
                MarketRow r = m.Find(_offered[i].Prefab);
                if (r == null) _offered.RemoveAt(i); else _offered[i].UnitPriceNow = r.Sell;
            }
        }

        /// <summary>What he charges for the wanted lines, now.</summary>
        public long Price
        {
            get { long v = 0; foreach (TrayLine l in _wanted) v += l.ValueNow; return v; }
        }

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
        /// `barter` false (the client's read of `Server.EnableBarter`) refuses goods beside a ware, here and
        /// only here: the server settles a barter either way (docs/CONFIG-SHAKEDOWN.md).
        /// </summary>
        public string Validate(MarketSnapshot m, int coins, Func<string, int> has, bool barter = true)
        {
            if (IsEmpty) return DealReason.EmptyDeal;
            if (m == null) return DealReason.NotConnected;
            if (!barter && _wanted.Count > 0 && _offered.Count > 0) return BarterOff;
            foreach (TrayLine l in _wanted)
            {
                MarketRow r = m.Find(l.Prefab);
                if (r == null || r.Kind != EntryKind.Ware) return DealReason.UnknownItem;
                if (r.Stock < l.Count) return DealReason.SoldOut;
            }
            foreach (TrayLine l in _offered)
            {
                MarketRow r = m.Find(l.Prefab);
                if (r == null) return DealReason.UnknownItem;
                if (has != null && has(l.Prefab) < l.Count) return MissingItems;
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
                case MissingItems: return "You no longer carry that many.";
                case BarterOff: return "Coins for my wares on this shore, friend. Sell me your goods in a deal of their own.";
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
            foreach (TrayLine l in _wanted)
            {
                l.UnitPriceSeen = l.UnitPriceNow;
                d.Wants.Add(new DealLine { Prefab = l.Prefab, Count = l.Count, UnitPriceSeen = l.UnitPriceNow });
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
        /// "Cover it with my goods" (design 3.4's barter assist): fill the offered side from the player's goods,
        /// highest of his prices first, until it covers the wanted lines; the change comes back in coins. Returns
        /// how many lines were added or grown.
        /// </summary>
        public int AutoFill(MarketSnapshot m, Func<string, int> has)
        {
            if (m == null || _wanted.Count == 0 || has == null) return 0;
            var rows = new List<MarketRow>();
            foreach (MarketRow r in m.Rows)
            {
                if (r.Sell < 1 || r.Max - r.Stock < 1) continue;
                if (FindWanted(r.Prefab) != null) continue;
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
