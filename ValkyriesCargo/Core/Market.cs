using System;
using System.Collections.Generic;

namespace RavenIron.ValkyriesCargo.Core
{
    /// <summary>
    /// The numbers the economy runs on (design 3.5). Bound from Server.* config on the game side; the game
    /// day is read from EnvMan at boot. Doubles, not floats: a float Spread of 0.7 widens to 0.699999988 and
    /// rounds 25 × 0.7 a coin short on one runtime and not another. A coin is what `price_changed` compares.
    /// </summary>
    public sealed class MarketRules
    {
        /// <summary>What the scene's EnvMan is expected to say (30 real minutes); its COMPILED default is 1200.</summary>
        public const double DefaultSecondsPerGameDay = 1800.0;
        public const int MaxPurseCoins = 1000000;
        public const int MaxPurseCapMultiple = 100;

        public double Elasticity = 0.35;        // exponent of (target / stock)
        public double MinMultiplier = 0.4;      // floor when flooded
        public double MaxMultiplier = 3.0;      // ceiling when out
        public double Spread = 0.7;             // what he pays as a fraction of what he charges
        // The Fair Market Act (2026-09-07, docs/DECISIONS-WUBARRK.md §2): MaxMultiplier x Spread = 2.1 > 1,
        // so an unclamped Ware pays MORE to buy back than it charged to sell, and a shelf bought out and sold
        // straight back pumps the purse for free (docs/ECONOMY-SIM.md §9). On: PaysFor caps a Ware's buy-back
        // multiplier at 1.0. Off: the pre-fix number, for an owner who wants it back.
        public bool FairMarketAct = true;
        public double HalfLifeGameDays = 1.0;   // stock drifts back to target with this half-life
        public double SecondsPerGameDay = DefaultSecondsPerGameDay;   // EnvMan.instance.m_dayLengthSec, read once at boot
        /// <summary>
        /// The pure core's own baseline, and NOT what ships: `ModConfig.FillMarketRules` overwrites this
        /// from `Server.PurseCoins` (1500) before any market the game builds ever sees it. Deliberately
        /// left at a round 800 so the harness's mechanics tests -- the carry arithmetic, the purse_empty
        /// refusals -- read against a fixed number instead of being rewritten every time the balance moves.
        /// A test that says "60 scrap iron at 15 is 900 and the purse holds 800" is about the refusal, not
        /// about the shipped purse.
        /// </summary>
        public int PurseCoins = 800;
        public int PurseCarryPercent = 50;
        public int PurseCapMultiple = 3;

        public static MarketRules Default => new MarketRules();

        /// <summary>Clamp every number into the range the config declares; report what was clamped. Idempotent.</summary>
        public void Sanitize(List<string> problems)
        {
            Elasticity = Clamp(Elasticity, 0.05, 1.5, "Elasticity", problems);
            MinMultiplier = Clamp(MinMultiplier, 0.05, 1.0, "MinMultiplier", problems);
            MaxMultiplier = Clamp(MaxMultiplier, 1.0, 10.0, "MaxMultiplier", problems);
            Spread = Clamp(Spread, 0.1, 1.0, "Spread", problems);
            HalfLifeGameDays = Clamp(HalfLifeGameDays, 0.1, 30.0, "HalfLifeGameDays", problems);
            SecondsPerGameDay = Clamp(SecondsPerGameDay, 60.0, 86400.0, "SecondsPerGameDay", problems);
            if (PurseCoins < 0) { PurseCoins = 0; Wire.Report(problems, "PurseCoins clamped to 0"); }
            if (PurseCoins > MaxPurseCoins) { PurseCoins = MaxPurseCoins; Wire.Report(problems, "PurseCoins clamped to " + Wire.Int(MaxPurseCoins)); }
            if (PurseCarryPercent < 0 || PurseCarryPercent > 100) { PurseCarryPercent = Math.Max(0, Math.Min(100, PurseCarryPercent)); Wire.Report(problems, "PurseCarryPercent clamped to 0..100"); }
            if (PurseCapMultiple < 1) { PurseCapMultiple = 1; Wire.Report(problems, "PurseCapMultiple clamped to 1"); }
            if (PurseCapMultiple > MaxPurseCapMultiple) { PurseCapMultiple = MaxPurseCapMultiple; Wire.Report(problems, "PurseCapMultiple clamped to " + Wire.Int(MaxPurseCapMultiple)); }
        }

        private static double Clamp(double v, double lo, double hi, string name, List<string> problems)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) { Wire.Report(problems, name + " was not a number; using " + Wire.Double(lo)); return lo; }
            if (v < lo) { Wire.Report(problems, name + " clamped up to " + Wire.Double(lo)); return lo; }
            if (v > hi) { Wire.Report(problems, name + " clamped down to " + Wire.Double(hi)); return hi; }
            return v;
        }
    }

    /// <summary>One catalogue entry with its live stock.</summary>
    public sealed class MarketItem
    {
        public CatalogueEntry Entry;
        public int Stock;
        /// <summary>World time of the last stock change or relaxation; drift is measured from here.</summary>
        public double UpdatedWorldTime;

        public string Prefab => Entry.Prefab;
        public EntryKind Kind => Entry.Kind;
        public bool SoldOut => Stock <= 0;
        public bool Full => Stock >= Entry.MaxStock;
    }

    /// <summary>A bounded set of the nonces settled in this visit; a repeat is refused. Oldest evicted first.</summary>
    public sealed class NonceRing
    {
        public const int DefaultCapacity = 500;
        private readonly int _capacity;
        private readonly Queue<long> _order = new Queue<long>();
        private readonly HashSet<long> _set = new HashSet<long>();

        public NonceRing(int capacity = DefaultCapacity) { _capacity = Math.Max(1, capacity); }

        public int Count => _set.Count;
        public bool Contains(long nonce) => _set.Contains(nonce);

        /// <summary>True if new; false if already settled.</summary>
        public bool Add(long nonce)
        {
            if (!_set.Add(nonce)) return false;
            _order.Enqueue(nonce);
            while (_order.Count > _capacity) _set.Remove(_order.Dequeue());
            return true;
        }

        /// <summary>A refused deal does not spend its nonce.</summary>
        public void Forget(long nonce) { if (_set.Remove(nonce)) Rebuild(); }

        public void Clear() { _order.Clear(); _set.Clear(); }

        private void Rebuild()
        {
            var keep = new List<long>();
            while (_order.Count > 0) { long n = _order.Dequeue(); if (_set.Contains(n)) keep.Add(n); }
            for (int i = 0; i < keep.Count; i++) _order.Enqueue(keep[i]);
        }
    }

    /// <summary>
    /// Ingvar's market: the catalogue with live stock, a purse, the price curve, the drift between
    /// visits, and the settlement of deals in the server's refusal order. The one place a price is
    /// computed. PURE: the server owns an instance; the demo owns another; the terminal never sees this
    /// class, only the MarketSnapshot it emits.
    /// </summary>
    public sealed class Market
    {
        /// <summary>The delivery-id prefix when the caller gives none. The server passes the world's salt, the demo "demo".</summary>
        public const string DefaultSalt = "v";

        private readonly List<MarketItem> _items = new List<MarketItem>();
        private readonly Dictionary<string, MarketItem> _byPrefab = new Dictionary<string, MarketItem>(StringComparer.Ordinal);
        private readonly NonceRing _nonces = new NonceRing();
        private readonly string _salt;
        private int _deliverySeq;
        private int _purseAtVisitStart;
        private int _coinedThisVisit;

        public MarketRules Rules { get; }
        public int VisitId { get; private set; }
        public int Purse { get; private set; }
        public IReadOnlyList<MarketItem> Items => _items;
        public int Count => _items.Count;
        public NonceRing Nonces => _nonces;
        /// <summary>The prefix every delivery id of this market carries.</summary>
        public string Salt => _salt;
        /// <summary>The id the server gives the next visit: one past the last, and the last is persisted, so ids never repeat.</summary>
        public int NextVisitId => VisitId == int.MaxValue ? 1 : VisitId + 1;

        /// <summary>
        /// The rules are sanitized IN PLACE (the game side calls Sanitize(problems) first if it wants the report).
        /// The salt is a wire token that makes delivery ids unique across worlds; anything else falls back to "v".
        /// </summary>
        public Market(Catalogue catalogue, MarketRules rules, double worldTime, string salt = null)
        {
            Rules = rules ?? MarketRules.Default;
            Rules.Sanitize(null);
            _salt = Wire.IsToken(salt) && salt.IndexOf('-') < 0 && salt.IndexOf('\t') < 0 && salt.IndexOf('\n') < 0 ? salt : DefaultSalt;
            Purse = Rules.PurseCoins;
            if (catalogue == null) return;
            foreach (CatalogueEntry e in catalogue.Entries)
            {
                var item = new MarketItem { Entry = e, Stock = e.TargetStock, UpdatedWorldTime = worldTime };
                _items.Add(item);
                _byPrefab.Add(e.Prefab, item);
            }
        }

        public MarketItem Find(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return null;
            MarketItem it;
            return _byPrefab.TryGetValue(prefab, out it) ? it : null;
        }

        // ---- the curve ------------------------------------------------------------------

        /// <summary>clamp((target / max(1, stock))^elasticity, min, max). An empty shelf is priced as if one were left.</summary>
        public static double MultiplierFor(int target, int stock, MarketRules r)
        {
            double ratio = (double)Math.Max(1, target) / Math.Max(1, stock);
            double mult = Math.Pow(ratio, r.Elasticity);
            if (mult < r.MinMultiplier) mult = r.MinMultiplier;
            if (mult > r.MaxMultiplier) mult = r.MaxMultiplier;
            return mult;
        }

        /// <summary>What he charges: base × multiplier, rounded once, never below 1.</summary>
        public static int PriceFor(int basePrice, int target, int stock, MarketRules r) =>
            Math.Max(1, (int)Math.Round(basePrice * MultiplierFor(target, stock, r), MidpointRounding.AwayFromZero));

        /// <summary>
        /// What he pays: base × multiplier × spread, rounded ONCE, never below 1. Not the rounded charge times
        /// the spread: that squashes the spread on cheap goods (amber flooded: 3.34 → 3, not round(5 × 0.7) = 4).
        ///
        /// The Fair Market Act (2026-09-07, docs/DECISIONS-WUBARRK.md §2): for a Ware, with
        /// <see cref="MarketRules.FairMarketAct"/> on, the multiplier on THIS side only is capped at 1.0 before
        /// the spread is applied, so he never pays more than base × spread — the target-stock rate — for
        /// something he also sells. <see cref="MultiplierFor"/> and <see cref="PriceFor"/> (what he CHARGES)
        /// are untouched either way: an empty shelf still charges the full 3.0× going out. A Want is never
        /// clamped; he does not sell it back, so there is no round trip to protect it from.
        /// </summary>
        public static int PaysFor(int basePrice, int target, int stock, EntryKind kind, MarketRules r) =>
            PaysForCore(basePrice, target, stock, kind == EntryKind.Ware && r.FairMarketAct, r);

        /// <summary>
        /// The legacy 4-argument shape, kept working because CLAUDE.md's working agreement keeps an existing
        /// signature alive rather than break its callers. It does not know the row's kind, so it always takes
        /// the no-clamp path: the pre-Fair-Market-Act number, exactly. Every real trade reaches
        /// <see cref="Pays(MarketItem)"/>, which calls the 5-argument overload above and does carry the kind;
        /// prefer that one for anything new.
        /// </summary>
        public static int PaysFor(int basePrice, int target, int stock, MarketRules r) =>
            PaysForCore(basePrice, target, stock, clampToPar: false, r);

        private static int PaysForCore(int basePrice, int target, int stock, bool clampToPar, MarketRules r)
        {
            double mult = MultiplierFor(target, stock, r);
            if (clampToPar && mult > 1.0) mult = 1.0;
            return Math.Max(1, (int)Math.Round(basePrice * mult * r.Spread, MidpointRounding.AwayFromZero));
        }

        public int Charge(MarketItem it) => PriceFor(it.Entry.BasePrice, it.Entry.TargetStock, it.Stock, Rules);
        public int Pays(MarketItem it) => PaysFor(it.Entry.BasePrice, it.Entry.TargetStock, it.Stock, it.Entry.Kind, Rules);
        /// <summary>Derived from the charge against base for both kinds; monotone with Pays, so the arrow is right for a Want too.</summary>
        public int Trend(MarketItem it) { int c = Charge(it); return c > it.Entry.BasePrice ? 1 : c < it.Entry.BasePrice ? -1 : 0; }

        // ---- the visit ------------------------------------------------------------------

        /// <summary>
        /// A new visit: relax stock for the time elapsed, refill the purse (base + carry of last takings,
        /// capped), forget the nonce ring, number the visit. Read Takings BEFORE calling this: it resets the baseline.
        /// </summary>
        public void StartVisit(int visitId, double worldTime, int lastTakings)
        {
            Relax(worldTime);
            VisitId = visitId;
            long carry = (long)Math.Round(Math.Max(0, lastTakings) * (Rules.PurseCarryPercent / 100.0), MidpointRounding.AwayFromZero);
            long cap = (long)Rules.PurseCoins * Rules.PurseCapMultiple;
            Purse = (int)Math.Min(cap, Rules.PurseCoins + carry);
            _purseAtVisitStart = Purse;
            _coinedThisVisit = 0;
            _nonces.Clear();
            _deliverySeq = 0;
        }

        /// <summary>Coins the purse gained this visit (what players bought minus what he paid), never negative. This is what the visit log quotes.</summary>
        public int Takings => Math.Max(0, Purse - _purseAtVisitStart);

        /// <summary>
        /// Coins that came IN this visit, gross -- every deal where the player paid him, with nothing
        /// subtracted for the deals where he paid out. This, not `Takings`, is what the purse carry is
        /// measured on.
        ///
        /// Why: `Takings` is the NET, so a visit where players sell him as much as they buy carries
        /// nothing forward, and that is exactly the visit the catalogue was written for. Measured over
        /// twenty simulated visits the carry cap engaged 19 times for a shopping server and **0 times**
        /// for a supplying one, which saw a flat 800 for ever (`docs/ECONOMY-SIM.md`, "what looks off" 4).
        /// A busy visit should refill him whichever direction the goods went.
        /// </summary>
        public int Coined => _coinedThisVisit;

        /// <summary>
        /// He trades elsewhere between visits: each item's stock moves toward target by
        /// 1 − 0.5^(days / halfLife) of the gap, days measured in world time since its last change, rounded
        /// away from zero so a gap of one unit closes within a half-life. Never overshoots: |round(gap × f)| ≤ |gap|
        /// for f &lt; 1. An item whose share of the elapsed time is not yet a whole unit KEEPS its stamp, so calling
        /// this often never throws the time away; a clock that ran backwards moves nothing and keeps its stamp too.
        /// </summary>
        public void Relax(double worldTime)
        {
            if (Rules.HalfLifeGameDays <= 0 || Rules.SecondsPerGameDay <= 0) return;
            foreach (MarketItem it in _items)
            {
                double dt = worldTime - it.UpdatedWorldTime;
                if (dt <= 0) continue;
                int gap = it.Entry.TargetStock - it.Stock;
                if (gap == 0) { it.UpdatedWorldTime = worldTime; continue; }
                double days = dt / Rules.SecondsPerGameDay;
                double fraction = 1.0 - Math.Pow(0.5, days / Rules.HalfLifeGameDays);
                int move = (int)Math.Round(gap * fraction, MidpointRounding.AwayFromZero);
                if (move == 0) continue;
                it.Stock += move;
                it.UpdatedWorldTime = worldTime;
            }
        }

        // ---- the snapshot ----------------------------------------------------------------

        /// <summary>A fresh value every call (the contract's "snapshots are values"); cache it on the receiving side.</summary>
        public MarketSnapshot Snapshot()
        {
            var snap = new MarketSnapshot { VisitId = VisitId, Purse = Purse };
            foreach (MarketItem it in _items)
            {
                snap.Add(new MarketRow
                {
                    Prefab = it.Prefab, Kind = it.Kind, Stock = it.Stock, Target = it.Entry.TargetStock, Max = it.Entry.MaxStock,
                    Buy = Charge(it), Sell = Pays(it), Trend = Trend(it),
                });
            }
            return snap;
        }

        // ---- settlement -------------------------------------------------------------------

        /// <summary>
        /// Settle a deal in the server's order (design 3.4): malformed, empty, stale visit, duplicate nonce; the
        /// wanted line (unknown or not a ware, bad count, sold out, price changed); each offered line
        /// (unknown, bad count, over max, price changed); coins short; purse empty; then commit stock and
        /// purse and answer. A refusal does not spend the nonce. The whole quantity is priced at the moment
        /// of the deal (count × the unit the player saw); stock moves after. Never throws.
        /// </summary>
        public DealResult Settle(Deal d, int playerCoins, double worldTime)
        {
            if (d == null || d.Offered == null) return DealResult.Refuse(d == null ? 0 : d.Nonce, DealReason.Malformed);
            for (int i = 0; i < d.Offered.Count; i++) if (d.Offered[i] == null) return DealResult.Refuse(d.Nonce, DealReason.Malformed);
            if (d.IsEmpty) return DealResult.Refuse(d.Nonce, DealReason.EmptyDeal);
            if (d.VisitId != VisitId) return DealResult.Refuse(d.Nonce, DealReason.StaleVisit);
            if (!_nonces.Add(d.Nonce)) return DealResult.Refuse(d.Nonce, DealReason.Duplicate);

            MarketItem want = null;
            int wantCharge = 0;
            if (d.Wanted != null)
            {
                want = Find(d.Wanted.Prefab);
                if (want == null || want.Kind != EntryKind.Ware) return Refuse(d, DealReason.UnknownItem);
                if (d.Wanted.Count < 1) return Refuse(d, DealReason.BadCount);
                if (want.Stock < d.Wanted.Count) return Refuse(d, DealReason.SoldOut);
                wantCharge = Charge(want);
                if (wantCharge != d.Wanted.UnitPriceSeen) return Refuse(d, DealReason.PriceChanged, Snapshot().Encode());
            }

            long offeredValue = 0;
            var offered = new List<MarketItem>(d.Offered.Count);
            var offeredPays = new List<int>(d.Offered.Count);
            for (int i = 0; i < d.Offered.Count; i++)
            {
                DealLine line = d.Offered[i];
                MarketItem it = Find(line.Prefab);
                if (it == null) return Refuse(d, DealReason.UnknownItem);
                if (line.Count < 1) return Refuse(d, DealReason.BadCount);
                if ((long)it.Stock + line.Count > it.Entry.MaxStock) return Refuse(d, DealReason.OverMax);
                int pays = Pays(it);
                if (pays != line.UnitPriceSeen) return Refuse(d, DealReason.PriceChanged, Snapshot().Encode());
                offeredValue += (long)line.Count * pays;
                offered.Add(it);
                offeredPays.Add(pays);
            }

            long price = want != null ? (long)d.Wanted.Count * wantCharge : 0;
            long net = price - offeredValue;
            if (net > 0 && playerCoins < net) return Refuse(d, DealReason.CoinsShort);
            if (net < 0 && Purse < -net) return Refuse(d, DealReason.PurseEmpty);

            if (want != null) { want.Stock -= d.Wanted.Count; want.UpdatedWorldTime = worldTime; }
            for (int i = 0; i < offered.Count; i++) { offered[i].Stock += d.Offered[i].Count; offered[i].UpdatedWorldTime = worldTime; }
            Purse += (int)net;
            if (net > 0) _coinedThisVisit += (int)net;   // GROSS in; see Coined

            var r = new DealResult
            {
                Nonce = d.Nonce, Ok = true, Reason = DealReason.Ok,
                DeliveryId = _salt + "-" + Wire.Int(VisitId) + "-" + Wire.Int(++_deliverySeq),
                CoinsDelta = (int)(-net),
            };
            if (want != null) r.ItemsToAdd.Add(new DealLine { Prefab = want.Prefab, Count = d.Wanted.Count, UnitPriceSeen = wantCharge });
            for (int i = 0; i < offered.Count; i++)
                r.ItemsToRemove.Add(new DealLine { Prefab = offered[i].Prefab, Count = d.Offered[i].Count, UnitPriceSeen = offeredPays[i] });
            return r;
        }

        private DealResult Refuse(Deal d, string reason, string market = "")
        {
            _nonces.Forget(d.Nonce);
            return DealResult.Refuse(d.Nonce, reason, market);
        }

        // ---- persistence rows ----------------------------------------------------------------

        /// <summary>
        /// The rows the sidecar stores (design 3.5): one line per item "stock\tprefab\tn\tupdatedWorldTime",
        /// then "purse\tn", "purseStart\tn" (this visit's baseline, so Takings survives a restart), "visit\tn"
        /// (the current visit, so ids stay monotonic and a resumed visit keeps its number) and "seq\tn" (the
        /// delivery sequence, so a resumed visit never re-issues an id). Tab-separated, invariant culture; the
        /// file wrapper (format line, atomic write) is the persistence layer's business.
        /// </summary>
        public string EncodeState()
        {
            var lines = new List<string>(_items.Count + 4);
            foreach (MarketItem it in _items)
                lines.Add("stock\t" + it.Prefab + "\t" + Wire.Int(it.Stock) + "\t" + Wire.Double(it.UpdatedWorldTime));
            lines.Add("purse\t" + Wire.Int(Purse));
            lines.Add("purseStart\t" + Wire.Int(_purseAtVisitStart));
            lines.Add("coined\t" + Wire.Int(_coinedThisVisit));
            lines.Add("visit\t" + Wire.Int(VisitId));
            lines.Add("seq\t" + Wire.Int(_deliverySeq));
            return string.Join("\n", lines.ToArray());
        }

        /// <summary>
        /// Apply saved rows. Unknown prefabs (a catalogue edit) are reported and ignored; a missing item
        /// keeps target stock; bad numbers are reported and skipped. Never throws.
        /// </summary>
        public void ApplyState(string rows, List<string> problems)
        {
            if (string.IsNullOrEmpty(rows)) return;
            foreach (string raw in rows.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0) continue;
                string[] f = line.Split('\t');
                if (f.Length == 2 && (f[0] == "purse" || f[0] == "purseStart" || f[0] == "coined" || f[0] == "visit" || f[0] == "seq"))
                {
                    int n;
                    if (!Wire.TryInt(f[1], out n) || n < 0) { Wire.Report(problems, f[0] + " row did not parse: " + line); continue; }
                    if (f[0] == "purse") Purse = n;
                    else if (f[0] == "purseStart") _purseAtVisitStart = n;
                    else if (f[0] == "coined") _coinedThisVisit = n;
                    else if (f[0] == "visit") VisitId = n;
                    else _deliverySeq = n;
                    continue;
                }
                if (f[0] == "stock" && f.Length == 4)
                {
                    MarketItem it = Find(f[1]);
                    if (it == null) { Wire.Report(problems, "stock row for unknown prefab " + f[1] + " ignored"); continue; }
                    int s; double t;
                    if (!Wire.TryInt(f[2], out s) || s < 0 || !Wire.TryDouble(f[3], out t) || double.IsNaN(t) || double.IsInfinity(t)) { Wire.Report(problems, "stock row did not parse: " + line); continue; }
                    it.Stock = Math.Min(s, it.Entry.MaxStock);
                    it.UpdatedWorldTime = t;
                    continue;
                }
                Wire.Report(problems, "unknown row ignored: " + line);
            }
        }
    }
}
