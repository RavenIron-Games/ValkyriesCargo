// The simulation's plumbing: a deterministic generator, a Markdown builder, a trader with a
// purse and a bag, and the four ways a deal is built. Nothing here knows a price: every
// number a "player" sees comes out of Market.Snapshot(), the way the terminal gets it.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using RavenIron.ValkyriesCargo.Core;

namespace ValkyriesCargo.EconSim
{
    /// <summary>
    /// xorshift64*, written out here rather than taken from System.Random: the framework's
    /// generator changed algorithm between .NET Framework and .NET Core, and this report must
    /// be byte-identical on any runtime, today and in a year.
    /// </summary>
    public sealed class Rng
    {
        private ulong _s;

        public Rng(long seed) { _s = (ulong)seed; if (_s == 0) _s = 0x9E3779B97F4A7C15UL; }

        public ulong Next64()
        {
            _s ^= _s >> 12;
            _s ^= _s << 25;
            _s ^= _s >> 27;
            return _s * 2685821657736338717UL;
        }

        /// <summary>[lo, hi).</summary>
        public int Next(int lo, int hi) => hi <= lo ? lo : lo + (int)(Next64() % (ulong)(hi - lo));

        public double NextDouble() => (Next64() >> 11) * (1.0 / 9007199254740992.0);

        public T Pick<T>(IList<T> xs) => xs[Next(0, xs.Count)];

        public bool Chance(double p) => NextDouble() < p;

        /// <summary>Index into weights, chosen proportionally. Weights must be positive.</summary>
        public int Weighted(IList<int> weights)
        {
            long total = 0;
            for (int i = 0; i < weights.Count; i++) total += weights[i];
            long roll = (long)(Next64() % (ulong)total);
            for (int i = 0; i < weights.Count; i++)
            {
                roll -= weights[i];
                if (roll < 0) return i;
            }
            return weights.Count - 1;
        }
    }

    /// <summary>A Markdown document, built line by line. '\n' only, so the file is the same on every machine.</summary>
    public sealed class Md
    {
        private readonly StringBuilder _sb = new StringBuilder();

        public void Line(string s) { _sb.Append(s); _sb.Append('\n'); }
        public void Blank() { _sb.Append('\n'); }

        public void H(int level, string text)
        {
            Blank();
            _sb.Append(new string('#', level)).Append(' ').Append(text).Append('\n');
            Blank();
        }

        public void Table(string[] head, IList<string[]> rows)
        {
            Line("| " + string.Join(" | ", head) + " |");
            var dashes = new string[head.Length];
            for (int i = 0; i < head.Length; i++) dashes[i] = "---";
            Line("|" + string.Join("|", dashes) + "|");
            for (int i = 0; i < rows.Count; i++) Line("| " + string.Join(" | ", rows[i]) + " |");
            Blank();
        }

        /// <summary>The text, with runs of blank lines collapsed so the sections join cleanly.</summary>
        public string Render()
        {
            string s = _sb.ToString().Replace("\r\n", "\n");
            while (s.Contains("\n\n\n")) s = s.Replace("\n\n\n", "\n\n");
            return s.TrimStart('\n');
        }
    }

    /// <summary>One trading agent: coins and an inventory. Only a DealResult moves either.</summary>
    public sealed class Trader
    {
        public readonly string Name;
        public int Coins;
        public int CoinsSpent;
        public int CoinsEarned;
        public readonly Dictionary<string, int> Bag = new Dictionary<string, int>(StringComparer.Ordinal);
        /// <summary>Set when a result would have taken goods the trader did not carry (the server never checks).</summary>
        public int ShortSales;

        public Trader(string name, int coins) { Name = name; Coins = coins; }

        public int Have(string prefab) { int n; return Bag.TryGetValue(prefab, out n) ? n : 0; }

        public void Give(string prefab, int count)
        {
            int n;
            Bag.TryGetValue(prefab, out n);
            Bag[prefab] = n + count;
        }

        /// <summary>The client's half of a deal: exactly CoinsDelta, ItemsToAdd and ItemsToRemove (Client/DealApplier.cs).</summary>
        public bool Apply(DealResult r)
        {
            if (r == null || !r.Ok) return false;
            Coins += r.CoinsDelta;
            if (r.CoinsDelta < 0) CoinsSpent += -r.CoinsDelta; else CoinsEarned += r.CoinsDelta;
            for (int i = 0; i < r.ItemsToRemove.Count; i++)
            {
                DealLine l = r.ItemsToRemove[i];
                if (Have(l.Prefab) < l.Count) ShortSales++;
                Give(l.Prefab, -l.Count);
            }
            for (int i = 0; i < r.ItemsToAdd.Count; i++) Give(r.ItemsToAdd[i].Prefab, r.ItemsToAdd[i].Count);
            return true;
        }
    }

    public static class Sim
    {
        /// <summary>1800 s: MarketRules' own constant, verified against the live EnvMan on StormTest 2026-09-06.</summary>
        public const double Day = MarketRules.DefaultSecondsPerGameDay;

        /// <summary>A wire token with no '-', so delivery ids read sim-visit-seq.</summary>
        public const string Salt = "sim";

        private static long _nonce;

        /// <summary>Deals are numbered, not randomised: Deal.NewNonce() would make the run irreproducible.</summary>
        public static long NextNonce() => ++_nonce;

        public static Catalogue DefaultCatalogue() => Catalogue.Parse(Catalogue.DefaultLine, null);

        /// <summary>
        /// The rules THIS MOD SHIPS, not `MarketRules.Default`. The two are not the same: the core's own
        /// baseline keeps a round 800 purse so the harness's mechanics tests read against a fixed number,
        /// while `Server.PurseCoins` ships 1500. This report exists to review the SHIPPED economy, and
        /// running it on a purse nobody plays with would make finding 4 -- which is about the purse --
        /// a review of the wrong number. Mirrored by hand from `Config/ModConfig.cs`, because EconSim
        /// compiles Core alone and cannot reach BepInEx's config types.
        /// </summary>
        public static MarketRules Shipped
        {
            get
            {
                var r = MarketRules.Default;
                r.PurseCoins = 1500;          // Server.PurseCoins
                r.PurseCarryPercent = 50;     // Server.PurseCarryPercent, now measured on the GROSS
                r.WareHalfLifeGameDays = 0;   // Server.WareHalfLifeGameDays: never (the owner, 2026-09-07; scenario 10)
                r.WantHalfLifeGameDays = 3;   // Server.WantHalfLifeGameDays
                return r;
            }
        }

        public static Market NewMarket(double worldTime = 0) =>
            new Market(DefaultCatalogue(), Shipped, worldTime, Salt);

        public static Market NewMarket(MarketRules rules, double worldTime = 0) =>
            new Market(DefaultCatalogue(), rules, worldTime, Salt);

        // ---- deals, built from what the client can see -------------------------------------

        public static Deal Buy(MarketSnapshot snap, string prefab, int count, int coins)
        {
            MarketRow row = snap.Find(prefab);
            return new Deal
            {
                VisitId = snap.VisitId,
                Nonce = NextNonce(),
                CoinsOffered = coins,
                Wanted = new DealLine { Prefab = prefab, Count = count, UnitPriceSeen = row != null ? row.Buy : 0 },
            };
        }

        public static Deal Sell(MarketSnapshot snap, string prefab, int count, int coins)
        {
            var d = new Deal { VisitId = snap.VisitId, Nonce = NextNonce(), CoinsOffered = coins };
            MarketRow row = snap.Find(prefab);
            d.Offered.Add(new DealLine { Prefab = prefab, Count = count, UnitPriceSeen = row != null ? row.Sell : 0 });
            return d;
        }

        public static Deal Barter(MarketSnapshot snap, string wanted, int count, IList<string> offeredPrefabs,
                                  IList<int> offeredCounts, int coins)
        {
            Deal d = Buy(snap, wanted, count, coins);
            for (int i = 0; i < offeredPrefabs.Count; i++)
            {
                MarketRow row = snap.Find(offeredPrefabs[i]);
                d.Offered.Add(new DealLine { Prefab = offeredPrefabs[i], Count = offeredCounts[i], UnitPriceSeen = row != null ? row.Sell : 0 });
            }
            return d;
        }

        // ---- reading the market ------------------------------------------------------------

        public static double Mult(Market m, string prefab)
        {
            MarketItem it = m.Find(prefab);
            return Market.MultiplierFor(it.Entry.TargetStock, it.Stock, m.Rules);
        }

        public static int Stock(Market m, string prefab) => m.Find(prefab).Stock;

        public static List<string> Prefabs(Market m, EntryKind kind)
        {
            var list = new List<string>();
            foreach (MarketItem it in m.Items) if (it.Kind == kind) list.Add(it.Prefab);
            return list;
        }

        /// <summary>
        /// A second market carrying this one's state, through the shipping sidecar rows. Used to probe
        /// "would he take this?" without settling it: an ACCEPTED deal mutates, so the probe cannot run
        /// on the real market.
        /// </summary>
        public static Market Clone(Market m)
        {
            var c = new Market(DefaultCatalogue(), m.Rules, 0, Salt);
            c.ApplyState(m.EncodeState(), null);
            return c;
        }

        /// <summary>
        /// The largest count of <paramref name="prefab"/> he will take in ONE deal right now, by bisection
        /// on a clone. 0 when he takes none; stopReason is why the next unit was refused.
        /// </summary>
        public static int LargestSale(Market m, string prefab, int want, int coins, double worldTime, out string stopReason)
        {
            string state = m.EncodeState();
            var probe = new Market(DefaultCatalogue(), m.Rules, 0, Salt);
            int lo = 0, hi = want;
            string reason = DealReason.Ok;
            while (lo < hi)
            {
                int mid = lo + (hi - lo + 1) / 2;
                probe.ApplyState(state, null);
                DealResult r = probe.Settle(Sell(probe.Snapshot(), prefab, mid, coins), coins, worldTime);
                if (r.Ok) lo = mid;
                else { hi = mid - 1; reason = r.Reason; }
            }
            stopReason = lo == want ? DealReason.Ok : reason;
            return lo;
        }

        // ---- formatting ----------------------------------------------------------------------

        public static string N(int v) => v.ToString(CultureInfo.InvariantCulture);
        public static string N(long v) => v.ToString(CultureInfo.InvariantCulture);
        public static string F(double v, int digits) => v.ToString("F" + N(digits), CultureInfo.InvariantCulture);
        public static string Pct(double v, int digits) => F(v * 100.0, digits) + "%";
        public static string Signed(int v) => (v > 0 ? "+" : "") + N(v);

        /// <summary>Facts the scenarios measure and the verdict quotes, so no number in the prose is typed by hand.</summary>
        public static readonly Dictionary<string, string> Findings = new Dictionary<string, string>(StringComparer.Ordinal);

        public static void Note(string key, string value) { Findings[key] = value; }

        public static string Fact(string key)
        {
            string v;
            return Findings.TryGetValue(key, out v) ? v : "(not measured)";
        }
    }
}
