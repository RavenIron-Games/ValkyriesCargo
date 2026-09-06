using System;
using System.Collections.Generic;

namespace RavenIron.ValkyriesCargo.Core
{
    /// <summary>One line of the market as the client sees it. Buy = what the player pays, Sell = what Ingvar pays.</summary>
    public sealed class MarketRow
    {
        public string Prefab;
        public EntryKind Kind;
        public int Stock;
        public int Target;
        public int Max;
        public int Buy;
        public int Sell;
        /// <summary>-1 below base price, 0 at base, +1 above. The terminal's glyph, decided by the server.</summary>
        public int Trend;

        public bool SoldOut => Stock <= 0;
        public bool Full => Stock >= Max;

        public string Encode() =>
            Prefab + Wire.Sub + Kind + Wire.Sub + Wire.Int(Stock) + Wire.Sub + Wire.Int(Target) + Wire.Sub +
            Wire.Int(Max) + Wire.Sub + Wire.Int(Buy) + Wire.Sub + Wire.Int(Sell) + Wire.Sub + Wire.Int(Trend);

        public static MarketRow Parse(string s, List<string> problems)
        {
            string[] f = Wire.Subs(s);
            if (f.Length != 8) { Wire.Report(problems, "market row '" + s + "': expected 8 fields, found " + f.Length); return null; }
            if (!Catalogue.IsPrefabName(f[0])) { Wire.Report(problems, "market row '" + s + "': bad prefab name"); return null; }
            EntryKind kind;
            if (string.Equals(f[1], "Ware", StringComparison.OrdinalIgnoreCase)) kind = EntryKind.Ware;
            else if (string.Equals(f[1], "Want", StringComparison.OrdinalIgnoreCase)) kind = EntryKind.Want;
            else { Wire.Report(problems, "market row '" + s + "': kind must be Ware or Want"); return null; }
            int stock, target, max, buy, sell, trend;
            if (!Wire.TryInt(f[2], out stock) || !Wire.TryInt(f[3], out target) || !Wire.TryInt(f[4], out max) ||
                !Wire.TryInt(f[5], out buy) || !Wire.TryInt(f[6], out sell) || !Wire.TryInt(f[7], out trend))
            { Wire.Report(problems, "market row '" + s + "': a number did not parse"); return null; }
            if (stock < 0 || target < 1 || max < target || buy < 0 || sell < 0 || trend < -1 || trend > 1)
            { Wire.Report(problems, "market row '" + s + "': a number is out of range"); return null; }
            return new MarketRow { Prefab = f[0], Kind = kind, Stock = stock, Target = target, Max = max, Buy = buy, Sell = sell, Trend = trend };
        }
    }

    /// <summary>
    /// The MarketState channel, parsed. "v1;visitId;purse;row|row|...". The server writes it after
    /// every deal; every client renders it. The terminal never computes a price: these numbers
    /// are the only ones it shows. PURE.
    /// </summary>
    public sealed class MarketSnapshot
    {
        public const int FormatVersion = 1;

        public int VisitId;
        public int Purse;

        private readonly List<MarketRow> _rows = new List<MarketRow>();
        private readonly Dictionary<string, MarketRow> _byPrefab = new Dictionary<string, MarketRow>(StringComparer.Ordinal);

        public IReadOnlyList<MarketRow> Rows => _rows;
        public int Count => _rows.Count;

        public MarketRow Find(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return null;
            MarketRow r;
            return _byPrefab.TryGetValue(prefab, out r) ? r : null;
        }

        public bool Add(MarketRow row)
        {
            if (row == null || _byPrefab.ContainsKey(row.Prefab)) return false;
            _rows.Add(row);
            _byPrefab.Add(row.Prefab, row);
            return true;
        }

        public string Encode()
        {
            var parts = new List<string>(_rows.Count);
            for (int i = 0; i < _rows.Count; i++) parts.Add(_rows[i].Encode());
            return "v" + Wire.Int(FormatVersion) + Wire.Field + Wire.Int(VisitId) + Wire.Field + Wire.Int(Purse) + Wire.Field +
                   Wire.Join(Wire.Item, parts);
        }

        /// <summary>Never throws. An empty string is an empty snapshot with no problems (the channel before any visit).</summary>
        public static MarketSnapshot Parse(string s, List<string> problems)
        {
            var snap = new MarketSnapshot();
            if (string.IsNullOrEmpty(s)) return snap;
            string[] f = Wire.Fields(s, 4);
            if (f.Length < 3) { Wire.Report(problems, "market: expected v1;visitId;purse;rows"); return snap; }
            if (f[0] != "v" + Wire.Int(FormatVersion)) { Wire.Report(problems, "market: format " + f[0] + " is not v" + FormatVersion + "; update the side that is behind"); return snap; }
            if (!Wire.TryInt(f[1], out snap.VisitId)) { Wire.Report(problems, "market: visitId did not parse"); return snap; }
            if (!Wire.TryInt(f[2], out snap.Purse) || snap.Purse < 0) { Wire.Report(problems, "market: purse did not parse"); snap.Purse = 0; }
            if (f.Length < 4) return snap;
            foreach (string item in Wire.Items(f[3]))
            {
                MarketRow row = MarketRow.Parse(item, problems);
                if (row != null && !snap.Add(row)) Wire.Report(problems, "market row '" + item + "': duplicate prefab; first kept");
            }
            return snap;
        }

        /// <summary>
        /// Every catalogue entry at target stock, priced at base (trend 0), Ingvar paying
        /// round(base × spread). The shape a visit starts in on a fresh world.
        /// </summary>
        public static MarketSnapshot FromCatalogue(Catalogue cat, int visitId, int purse, float spread)
        {
            var snap = new MarketSnapshot { VisitId = visitId, Purse = purse };
            if (cat == null) return snap;
            foreach (CatalogueEntry e in cat.Entries)
            {
                snap.Add(new MarketRow
                {
                    Prefab = e.Prefab, Kind = e.Kind, Stock = e.TargetStock, Target = e.TargetStock, Max = e.MaxStock,
                    Buy = e.BasePrice, Sell = Math.Max(1, (int)Math.Round(e.BasePrice * spread)), Trend = 0,
                });
            }
            return snap;
        }

        /// <summary>The 72 defaults at target stock, purse 800, visit 1: what `cargo terminal demo` renders.</summary>
        public static readonly string Demo =
            FromCatalogue(Catalogue.Parse(Catalogue.DefaultLine, null), 1, 800, 0.7f).Encode();
    }
}
