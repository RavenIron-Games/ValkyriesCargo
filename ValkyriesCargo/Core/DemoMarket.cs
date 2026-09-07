using System;

namespace RavenIron.ValkyriesCargo.Core
{
    /// <summary>
    /// The stand-in for the server behind `cargo terminal demo`: the REAL Market with the default
    /// catalogue and rules, settling deals exactly as the server will, plus one knob (Tick) that moves
    /// a line's stock so the terminal's price_changed handling can be seen without a second player.
    /// A demo that shares the server's code cannot drift from it. PURE.
    /// </summary>
    public sealed class DemoMarket
    {
        public const string Salt = "demo";

        private readonly Market _core;
        private double _worldTime;

        public DemoMarket(Market core) { _core = core ?? new Market(null, MarketRules.Default, 0, Salt); }

        public static DemoMarket Default()
        {
            var core = new Market(Catalogue.Parse(Catalogue.DefaultLine, null), DemoRules(), 0, Salt);
            core.StartVisit(1, 0, 0);
            return new DemoMarket(core);
        }

        /// <summary>
        /// The demo's own rules: the core baseline with a one-game-day half-life on BOTH kinds, so `Advance`
        /// visibly moves a ticked row back and the walk stays price-driven. The shipped knobs (a Ware never
        /// drifts, a Want at three days; 2026-09-07) are the server's economy, not what a demo of the window
        /// needs to show, and nothing here reaches a real market.
        /// </summary>
        private static MarketRules DemoRules()
        {
            var r = MarketRules.Default;
            r.WareHalfLifeGameDays = 1.0;
            r.WantHalfLifeGameDays = 1.0;
            return r;
        }

        /// <summary>The underlying market, for tests and for the demo console.</summary>
        public Market Core => _core;

        /// <summary>
        /// What the terminal renders: a FRESH snapshot (72 new rows) on every read, as the contract's
        /// "snapshots are values" demands. Not for a draw path: CargoRpc.Market is the cached one.
        /// </summary>
        public MarketSnapshot Market => _core.Snapshot();

        /// <summary>
        /// Move one line's stock, one unit at a time, until the number the terminal shows for it (Buy for
        /// a Ware, Sell for a Want) actually changes; scarcer = fewer on the shelf = pricier. A quarter-target
        /// step moved nothing on 42 of the 72 default rows (every base-1 row rounds back to the same coin),
        /// so the walk is price-driven. False only for an unknown prefab or a row already at the bound it is
        /// walking toward, in which case the stock is left where it was.
        /// </summary>
        public bool Tick(string prefab, bool scarcer)
        {
            int before, after;
            return Tick(prefab, scarcer, out before, out after);
        }

        public bool Tick(string prefab, bool scarcer, out int shownBefore, out int shownAfter)
        {
            shownBefore = shownAfter = 0;
            MarketItem it = _core.Find(prefab);
            if (it == null) return false;
            shownBefore = shownAfter = Shown(it);
            int original = it.Stock;
            int step = scarcer ? -1 : 1;
            while (true)
            {
                int next = it.Stock + step;
                if (next < 0 || next > it.Entry.MaxStock) { it.Stock = original; return false; }
                it.Stock = next;
                int now = Shown(it);
                if (now != shownBefore) { shownAfter = now; it.UpdatedWorldTime = _worldTime; return true; }
            }
        }

        private int Shown(MarketItem it) => it.Kind == EntryKind.Ware ? _core.Charge(it) : _core.Pays(it);

        /// <summary>Advance the demo's world clock (seconds) so drift can be shown; relaxes stock.</summary>
        public void Advance(double seconds) { _worldTime += Math.Max(0, seconds); _core.Relax(_worldTime); }

        public DealResult Settle(Deal d, int playerCoins) => _core.Settle(d, playerCoins, _worldTime);
    }
}
