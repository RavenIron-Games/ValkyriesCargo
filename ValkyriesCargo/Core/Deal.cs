using System;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace RavenIron.ValkyriesCargo.Core
{
    /// <summary>One item line of a deal: what, how many, and the unit price the player was looking at.</summary>
    public sealed class DealLine
    {
        public string Prefab;
        public int Count;
        /// <summary>The unit price shown when the player staged it. The server refuses with price_changed if it moved.</summary>
        public int UnitPriceSeen;

        public string Encode() => Prefab + Wire.Sub + Wire.Int(Count) + Wire.Sub + Wire.Int(UnitPriceSeen);

        public static DealLine Parse(string s, List<string> problems)
        {
            string[] f = Wire.Subs(s);
            if (f.Length != 3) { Wire.Report(problems, "deal line '" + s + "': expected prefab:count:unit"); return null; }
            if (!Catalogue.IsPrefabName(f[0])) { Wire.Report(problems, "deal line '" + s + "': bad prefab name"); return null; }
            int count, unit;
            if (!Wire.TryInt(f[1], out count) || count < 1) { Wire.Report(problems, "deal line '" + s + "': count must be at least 1"); return null; }
            if (!Wire.TryInt(f[2], out unit) || unit < 0) { Wire.Report(problems, "deal line '" + s + "': unit price must be 0 or more"); return null; }
            return new DealLine { Prefab = f[0], Count = count, UnitPriceSeen = unit };
        }

        public static string EncodeList(IList<DealLine> lines)
        {
            var parts = new List<string>();
            if (lines != null) for (int i = 0; i < lines.Count; i++) if (lines[i] != null) parts.Add(lines[i].Encode());
            return Wire.Join(Wire.Item, parts);
        }

        public static List<DealLine> ParseList(string s, List<string> problems)
        {
            var list = new List<DealLine>();
            foreach (string item in Wire.Items(s))
            {
                DealLine l = Parse(item, problems);
                if (l != null) list.Add(l);
            }
            return list;
        }
    }

    /// <summary>
    /// The one trade primitive (design 3.4): buy = Wanted + coins; sell = Offered only; barter =
    /// Wanted + Offered, change in coins. "v1;visitId;nonce;wanted;offered;coinsOffered" where wanted is
    /// one line or empty and offered is a '|' list. PURE.
    /// </summary>
    public sealed class Deal
    {
        public const int FormatVersion = 1;

        public int VisitId;
        public long Nonce;
        public DealLine Wanted;
        public List<DealLine> Offered = new List<DealLine>();
        public int CoinsOffered;

        public bool IsBuy => Wanted != null && Offered.Count == 0;
        public bool IsSell => Wanted == null && Offered.Count > 0;
        public bool IsBarter => Wanted != null && Offered.Count > 0;
        public bool IsEmpty => Wanted == null && Offered.Count == 0;

        /// <summary>The value of the offered goods at the prices the player saw.</summary>
        public int OfferedValueSeen()
        {
            long sum = 0;
            for (int i = 0; i < Offered.Count; i++) sum += (long)Offered[i].Count * Offered[i].UnitPriceSeen;
            return sum > int.MaxValue ? int.MaxValue : (int)sum;
        }

        public string Encode() =>
            "v" + Wire.Int(FormatVersion) + Wire.Field + Wire.Int(VisitId) + Wire.Field + Wire.Long(Nonce) + Wire.Field +
            (Wanted != null ? Wanted.Encode() : "") + Wire.Field + DealLine.EncodeList(Offered) + Wire.Field + Wire.Int(CoinsOffered);

        /// <summary>Never throws; returns null (and reports) when the message is not a deal at all.</summary>
        public static Deal Parse(string s, List<string> problems)
        {
            if (string.IsNullOrEmpty(s)) { Wire.Report(problems, "deal: empty"); return null; }
            string[] f = Wire.Fields(s, 6);
            if (f.Length != 6) { Wire.Report(problems, "deal: expected 6 fields, found " + f.Length); return null; }
            if (f[0] != "v" + Wire.Int(FormatVersion)) { Wire.Report(problems, "deal: format " + f[0] + " is not v" + FormatVersion + "; update the side that is behind"); return null; }
            var d = new Deal();
            if (!Wire.TryInt(f[1], out d.VisitId)) { Wire.Report(problems, "deal: visitId did not parse"); return null; }
            if (!Wire.TryLong(f[2], out d.Nonce)) { Wire.Report(problems, "deal: nonce did not parse"); return null; }
            if (f[3].Length > 0)
            {
                d.Wanted = DealLine.Parse(f[3], problems);
                if (d.Wanted == null) return null;
            }
            int before = problems != null ? problems.Count : 0;
            d.Offered = DealLine.ParseList(f[4], problems);
            if (problems != null && problems.Count > before) return null;
            if (!Wire.TryInt(f[5], out d.CoinsOffered) || d.CoinsOffered < 0) { Wire.Report(problems, "deal: coinsOffered must be 0 or more"); return null; }
            return d;
        }

        /// <summary>64 random bits from the platform generator. The server's ring refuses a repeat.</summary>
        public static long NewNonce()
        {
            var bytes = new byte[8];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            long n = BitConverter.ToInt64(bytes, 0);
            return n == 0 ? 1 : n;
        }
    }

    /// <summary>The reason codes a refusal carries. Tokens, never text; the terminal maps them to Ingvar's lines.</summary>
    public static class DealReason
    {
        public const string Ok            = "ok";
        public const string SoldOut       = "sold_out";
        public const string OverMax       = "over_max";
        public const string PurseEmpty    = "purse_empty";
        public const string CoinsShort    = "coins_short";
        public const string InventoryFull = "inventory_full";
        public const string VisitOver     = "visit_over";
        public const string UnknownItem   = "unknown_item";
        public const string BadCount      = "bad_count";
        public const string StaleVisit    = "stale_visit";
        public const string Duplicate     = "duplicate";
        public const string PriceChanged  = "price_changed";
        public const string EmptyDeal     = "empty_deal";
        public const string NotConnected  = "not_connected";
        public const string Malformed     = "malformed";
    }

    /// <summary>
    /// The server's answer. "v1;nonce;deliveryId;ok;reason;coinsDelta;add;remove;newMarketState" where
    /// newMarketState is the LAST field and may be empty or a whole MarketState (it carries separators,
    /// so it is read as the rest of the string). The client mutates its inventory only when Ok, and only
    /// by ItemsToAdd, ItemsToRemove and CoinsDelta. PURE.
    /// </summary>
    public sealed class DealResult
    {
        public const int FormatVersion = 1;

        public long Nonce;
        public string DeliveryId = "";
        public bool Ok;
        public string Reason = DealReason.Ok;
        /// <summary>Positive: coins to the player. Negative: coins from the player.</summary>
        public int CoinsDelta;
        public List<DealLine> ItemsToAdd = new List<DealLine>();
        public List<DealLine> ItemsToRemove = new List<DealLine>();
        /// <summary>Set with price_changed: the market as it is now, so the tray can redraw before the player confirms again.</summary>
        public string NewMarketState = "";

        public static DealResult Refuse(long nonce, string reason, string newMarketState = "") =>
            new DealResult { Nonce = nonce, Ok = false, Reason = reason ?? DealReason.Malformed, NewMarketState = newMarketState ?? "" };

        public string Encode() =>
            "v" + Wire.Int(FormatVersion) + Wire.Field + Wire.Long(Nonce) + Wire.Field + (DeliveryId ?? "") + Wire.Field +
            (Ok ? "1" : "0") + Wire.Field + (Reason ?? DealReason.Malformed) + Wire.Field + Wire.Int(CoinsDelta) + Wire.Field +
            DealLine.EncodeList(ItemsToAdd) + Wire.Field + DealLine.EncodeList(ItemsToRemove) + Wire.Field + (NewMarketState ?? "");

        public static DealResult Parse(string s, List<string> problems)
        {
            if (string.IsNullOrEmpty(s)) { Wire.Report(problems, "deal result: empty"); return null; }
            string[] f = Wire.Fields(s, 9);
            if (f.Length != 9) { Wire.Report(problems, "deal result: expected 9 fields, found " + f.Length); return null; }
            if (f[0] != "v" + Wire.Int(FormatVersion)) { Wire.Report(problems, "deal result: format " + f[0] + " is not v" + FormatVersion + "; update the side that is behind"); return null; }
            var r = new DealResult();
            if (!Wire.TryLong(f[1], out r.Nonce)) { Wire.Report(problems, "deal result: nonce did not parse"); return null; }
            r.DeliveryId = Wire.IsToken(f[2]) || f[2].Length == 0 ? f[2] : "";
            if (!Wire.TryBool(f[3], out r.Ok)) { Wire.Report(problems, "deal result: ok flag did not parse"); return null; }
            if (!Wire.IsToken(f[4])) { Wire.Report(problems, "deal result: reason is not a token"); return null; }
            r.Reason = f[4];
            if (!Wire.TryInt(f[5], out r.CoinsDelta)) { Wire.Report(problems, "deal result: coinsDelta did not parse"); return null; }
            int before = problems != null ? problems.Count : 0;
            r.ItemsToAdd = DealLine.ParseList(f[6], problems);
            r.ItemsToRemove = DealLine.ParseList(f[7], problems);
            if (problems != null && problems.Count > before) return null;
            r.NewMarketState = f[8] ?? "";
            if (r.Ok && r.DeliveryId.Length == 0) { Wire.Report(problems, "deal result: an accepted deal must carry a deliveryId"); return null; }
            return r;
        }
    }

    /// <summary>
    /// The client's memory of deliveries it has already applied, so a redelivered result (a lost ack, a
    /// claim at login) is recognised rather than applied twice. Bounded, oldest evicted first
    /// (VikingOS's TradeInbox rule, ported without Newtonsoft). Persisted by the client as one id per
    /// line; that file lives on the game side. PURE.
    /// </summary>
    public sealed class DealInbox
    {
        public const int DefaultCapacity = 500;

        private readonly int _capacity;
        private readonly List<string> _order = new List<string>();
        private readonly HashSet<string> _index = new HashSet<string>(StringComparer.Ordinal);

        public DealInbox(int capacity = DefaultCapacity) { _capacity = Math.Max(1, capacity); }

        public int Count => _order.Count;

        public bool AlreadyApplied(string deliveryId) =>
            !string.IsNullOrEmpty(deliveryId) && _index.Contains(deliveryId);

        /// <summary>True if this is the first time; false if it was already there (and nothing changed).</summary>
        public bool MarkApplied(string deliveryId)
        {
            if (string.IsNullOrEmpty(deliveryId) || !_index.Add(deliveryId)) return false;
            _order.Add(deliveryId);
            while (_order.Count > _capacity)
            {
                _index.Remove(_order[0]);
                _order.RemoveAt(0);
            }
            return true;
        }

        public IReadOnlyList<string> Ids => _order;

        public string Encode() => Wire.Join(Wire.Item, _order);

        public static DealInbox Parse(string s, int capacity = DefaultCapacity)
        {
            var box = new DealInbox(capacity);
            foreach (string id in Wire.Items(s)) box.MarkApplied(id.Trim());
            return box;
        }
    }
}
