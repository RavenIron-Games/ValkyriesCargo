// Scenarios 7 and 8: the assertions. Every one of these is a claim about the SHIPPING
// Market.Settle, and the run exits non-zero if one fails, so tools\run-econsim.ps1 is a test
// as well as a report generator.

using System;
using System.Collections.Generic;
using System.Reflection;
using RavenIron.ValkyriesCargo.Core;

namespace ValkyriesCargo.EconSim
{
    public static class Checks
    {
        public static bool AllPassed = true;

        private static readonly List<string[]> Rows = new List<string[]>();
        private static readonly List<string> Answers = new List<string>();

        /// <summary>Every token DealReason declares, read off the contract class itself.</summary>
        public static readonly HashSet<string> Tokens = ReadTokens();

        private static HashSet<string> ReadTokens()
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (FieldInfo f in typeof(DealReason).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (f.IsLiteral && f.FieldType == typeof(string)) set.Add((string)f.GetRawConstantValue());
            return set;
        }

        private static string Outcome(Func<DealResult> act)
        {
            try
            {
                DealResult r = act();
                if (r == null) return "(null)";
                if (r.Ok) return DealReason.Ok;
                return Tokens.Contains(r.Reason) ? r.Reason : "NOT A TOKEN: " + r.Reason;
            }
            catch (Exception e)
            {
                return "threw " + e.GetType().Name;
            }
        }

        private static void Expect(string what, string expected, Func<DealResult> act)
        {
            string got = Outcome(act);
            Answers.Add(got);
            bool pass = string.Equals(got, expected, StringComparison.Ordinal);
            if (!pass) AllPassed = false;
            Rows.Add(new[] { what, "`" + expected + "`", "`" + got + "`", pass ? "PASS" : "**FAIL**" });
        }

        private static void ExpectTrue(string what, string expected, Func<bool> act)
        {
            bool pass;
            string got;
            try { pass = act(); got = pass ? expected : "no"; }
            catch (Exception e) { pass = false; got = "threw " + e.GetType().Name; }
            if (!pass) AllPassed = false;
            Rows.Add(new[] { what, expected, got, pass ? "PASS" : "**FAIL**" });
        }

        // =====================================================================================
        // 7. Edge sweep
        // =====================================================================================

        public static void Seven(Md md, long seed)
        {
            md.H(2, "7. Edge sweep — the refusal order, asserted");

            md.Line("`Settle` refuses in the server's order (DESIGN 3.4): malformed, empty, stale visit, duplicate nonce; the wanted");
            md.Line("line (unknown / not a Ware, bad count, sold out, price changed); each offered line (unknown, bad count, over max,");
            md.Line("price changed); coins short; purse empty. Each row below is one deal against a fresh market. Nothing throws.");
            md.Blank();

            // --- the wanted line -------------------------------------------------------------
            Expect("buy 3 BlackCore off a shelf of 2", DealReason.SoldOut, () =>
            {
                Market m = Fresh();
                return m.Settle(Sim.Buy(m.Snapshot(), "BlackCore", 3, 100000), 100000, 0);
            });

            Expect("buy 1 off an emptied shelf", DealReason.SoldOut, () =>
            {
                Market m = Fresh();
                m.Settle(Sim.Buy(m.Snapshot(), "BlackCore", 2, 100000), 100000, 0);
                return m.Settle(Sim.Buy(m.Snapshot(), "BlackCore", 1, 100000), 100000, 0);
            });

            Expect("buy a prefab that is not in the catalogue", DealReason.UnknownItem, () =>
            {
                Market m = Fresh();
                var d = Sim.Buy(m.Snapshot(), "Amber", 1, 100000);
                d.Wanted.Prefab = "Mistlands_Cheese";
                return m.Settle(d, 100000, 0);
            });

            Expect("buy a Want (he does not sell those)", DealReason.UnknownItem, () =>
            {
                Market m = Fresh();
                var d = Sim.Buy(m.Snapshot(), "Amber", 1, 100000);
                d.Wanted = new DealLine { Prefab = "Wood", Count = 1, UnitPriceSeen = 1 };
                return m.Settle(d, 100000, 0);
            });

            Expect("buy zero of something", DealReason.BadCount, () =>
            {
                Market m = Fresh();
                var d = Sim.Buy(m.Snapshot(), "Amber", 1, 100000);
                d.Wanted.Count = 0;
                return m.Settle(d, 100000, 0);
            });

            Expect("buy at yesterday's price (the snapshot went stale)", DealReason.PriceChanged, () =>
            {
                Market m = Fresh();
                MarketSnapshot stale = m.Snapshot();
                m.Settle(Sim.Buy(m.Snapshot(), "Amber", 20, 100000), 100000, 0);   // someone else moved the shelf
                return m.Settle(Sim.Buy(stale, "Amber", 1, 100000), 100000, 0);
            });

            Expect("...and it carries the new market with it", DealReason.PriceChanged, () =>
            {
                Market m = Fresh();
                MarketSnapshot stale = m.Snapshot();
                m.Settle(Sim.Buy(m.Snapshot(), "Amber", 20, 100000), 100000, 0);
                DealResult r = m.Settle(Sim.Buy(stale, "Amber", 1, 100000), 100000, 0);
                return r.NewMarketState.Length > 0 ? r : DealResult.Refuse(0, "no market state on price_changed");
            });

            // --- the offered lines ------------------------------------------------------------
            Expect("sell 61 scrap iron into a shelf with room for 60", DealReason.OverMax, () =>
            {
                Market m = Fresh();
                return m.Settle(Sim.Sell(m.Snapshot(), "IronScrap", 61, 0), 0, 0);
            });

            Expect("one unit past max, reached by selling", DealReason.OverMax, () =>
            {
                Market m = Fresh();
                m.Settle(Sim.Sell(m.Snapshot(), "TrophyDeer", 20, 0), 0, 0);   // 10 -> 30, his max
                return m.Settle(Sim.Sell(m.Snapshot(), "TrophyDeer", 1, 0), 0, 0);
            });

            Expect("sell at a stale price", DealReason.PriceChanged, () =>
            {
                Market m = Fresh();
                MarketSnapshot stale = m.Snapshot();
                m.Settle(Sim.Sell(m.Snapshot(), "IronScrap", 40, 0), 0, 0);   // 15 a unit down to 11
                return m.Settle(Sim.Sell(stale, "IronScrap", 1, 0), 0, 0);
            });

            Expect("sell a prefab that is not in the catalogue", DealReason.UnknownItem, () =>
            {
                Market m = Fresh();
                var d = Sim.Sell(m.Snapshot(), "Wood", 1, 0);
                d.Offered[0].Prefab = "Deerstalker";
                return m.Settle(d, 0, 0);
            });

            Expect("sell a negative count", DealReason.BadCount, () =>
            {
                Market m = Fresh();
                var d = Sim.Sell(m.Snapshot(), "Wood", 1, 0);
                d.Offered[0].Count = -5;
                return m.Settle(d, 0, 0);
            });

            Expect("sell int.MaxValue units (no overflow)", DealReason.OverMax, () =>
            {
                Market m = Fresh();
                var d = Sim.Sell(m.Snapshot(), "Wood", 1, 0);
                d.Offered[0].Count = int.MaxValue;
                return m.Settle(d, 0, 0);
            });

            Expect("buy int.MaxValue units", DealReason.SoldOut, () =>
            {
                Market m = Fresh();
                var d = Sim.Buy(m.Snapshot(), "Amber", 1, int.MaxValue);
                d.Wanted.Count = int.MaxValue;
                return m.Settle(d, int.MaxValue, 0);
            });

            // --- coins and purse ---------------------------------------------------------------
            Expect("a player with no coins buys one amber", DealReason.CoinsShort, () =>
            {
                Market m = Fresh();
                return m.Settle(Sim.Buy(m.Snapshot(), "Amber", 1, 0), 0, 0);
            });

            Expect("a player with no coins SELLS (allowed)", DealReason.Ok, () =>
            {
                Market m = Fresh();
                return m.Settle(Sim.Sell(m.Snapshot(), "Wood", 10, 0), 0, 0);
            });

            Expect("a one-coin purse takes the first unit", DealReason.Ok, () =>
            {
                Market m = Fresh(new MarketRules { PurseCoins = 1 });
                return m.Settle(Sim.Sell(m.Snapshot(), "Wood", 1, 0), 0, 0);
            });

            Expect("...and refuses the second", DealReason.PurseEmpty, () =>
            {
                Market m = Fresh(new MarketRules { PurseCoins = 1 });
                m.Settle(Sim.Sell(m.Snapshot(), "Wood", 1, 0), 0, 0);
                return m.Settle(Sim.Sell(m.Snapshot(), "Wood", 1, 0), 0, 0);
            });

            Expect("a barter whose change is more than the purse", DealReason.PurseEmpty, () =>
            {
                Market m = Fresh(new MarketRules { PurseCoins = 5 });
                var prefabs = new List<string> { "FlametalOreNew" };
                var counts = new List<int> { 5 };
                return m.Settle(Sim.Barter(m.Snapshot(), "BlackCore", 1, prefabs, counts, 0), 0, 0);
            });

            // --- the envelope --------------------------------------------------------------------
            Expect("a deal for a visit that has ended", DealReason.StaleVisit, () =>
            {
                Market m = Fresh();
                var d = Sim.Buy(m.Snapshot(), "Amber", 1, 100000);
                d.VisitId = m.VisitId + 1;
                return m.Settle(d, 100000, 0);
            });

            Expect("the same nonce twice", DealReason.Duplicate, () =>
            {
                Market m = Fresh();
                var d = Sim.Buy(m.Snapshot(), "Amber", 1, 100000);
                m.Settle(d, 100000, 0);
                var again = Sim.Buy(m.Snapshot(), "Amber", 1, 100000);
                again.Nonce = d.Nonce;
                return m.Settle(again, 100000, 0);
            });

            Expect("a deal with nothing in it", DealReason.EmptyDeal, () =>
            {
                Market m = Fresh();
                return m.Settle(new Deal { VisitId = m.VisitId, Nonce = Sim.NextNonce() }, 0, 0);
            });

            Expect("a null deal", DealReason.Malformed, () => Fresh().Settle(null, 0, 0));

            Expect("a deal with a null offered line", DealReason.Malformed, () =>
            {
                Market m = Fresh();
                var d = Sim.Sell(m.Snapshot(), "Wood", 1, 0);
                d.Offered.Add(null);
                return m.Settle(d, 0, 0);
            });

            Expect("a deal with a null Offered list", DealReason.Malformed, () =>
            {
                Market m = Fresh();
                var d = Sim.Buy(m.Snapshot(), "Amber", 1, 100000);
                d.Offered = null;
                return m.Settle(d, 100000, 0);
            });

            // --- what a refusal must NOT do --------------------------------------------------------
            ExpectTrue("a refused deal moves no stock and no purse", "unchanged", () =>
            {
                Market m = Fresh();
                string before = m.EncodeState();
                m.Settle(Sim.Buy(m.Snapshot(), "BlackCore", 99, 0), 0, 0);
                m.Settle(Sim.Sell(m.Snapshot(), "IronScrap", 999, 0), 0, 0);
                return m.EncodeState() == before;
            });

            ExpectTrue("a refused deal does not spend its nonce", "reusable", () =>
            {
                Market m = Fresh();
                var bad = Sim.Buy(m.Snapshot(), "BlackCore", 99, 100000);
                m.Settle(bad, 100000, 0);
                var good = Sim.Buy(m.Snapshot(), "BlackCore", 1, 100000);
                good.Nonce = bad.Nonce;
                return m.Settle(good, 100000, 0).Ok;
            });

            ExpectTrue("an accepted deal always carries a delivery id", "always", () =>
            {
                Market m = Fresh();
                DealResult r = m.Settle(Sim.Buy(m.Snapshot(), "Amber", 1, 100000), 100000, 0);
                return r.Ok && r.DeliveryId == Sim.Salt + "-1-1";
            });

            ExpectTrue("every answer above is one of the " + Sim.N(Tokens.Count) + " DealReason tokens", "always", () =>
            {
                for (int i = 0; i < Answers.Count; i++) if (!Tokens.Contains(Answers[i])) return false;
                return true;
            });

            md.Table(new[] { "deal", "expected", "answer", "" }, Rows);

            // The one that is not a failure but is worth knowing about.
            Market never = Sim.NewMarket(0);
            DealResult beforeAnyVisit = never.Settle(new Deal
            {
                VisitId = 0,
                Nonce = Sim.NextNonce(),
                Wanted = new DealLine { Prefab = "Amber", Count = 1, UnitPriceSeen = 7 },
                CoinsOffered = 100,
            }, 100, 0);
            Sim.Note("s7.visit0", beforeAnyVisit.Ok ? "settled (" + beforeAnyVisit.DeliveryId + ")" : beforeAnyVisit.Reason);

            md.Line("All " + Sim.N(Rows.Count) + " checks " + (AllPassed ? "**pass**" : "**DID NOT ALL PASS**") + ", and no input threw. Two answers are worth a second look:");
            md.Blank();
            md.Line("- **A Want asked for as a Ware answers `unknown_item`, not a kind of its own.** `Settle` folds 'not in the catalogue'");
            md.Line("  and 'in the catalogue, but he does not sell it' into one token (Market.cs 272), so the terminal cannot tell a player");
            md.Line("  'he only buys those' — it has to say 'he has never heard of it'. A `not_for_sale` token would cost one line.");
            md.Line("- **A market that has never started a visit settles a deal numbered 0**: `VisitId` is 0 until `StartVisit`, and a deal");
            md.Line("  carrying `visitId 0` passes the stale-visit gate (this run: " + Sim.Fact("s7.visit0") + "). The server never opens the deal RPC");
            md.Line("  outside a visit (`VisitDirector.Settle` refuses `visit_over` first), so it is unreachable today — but it is the pure");
            md.Line("  core's only unguarded door, and `VisitId` starting at -1 would shut it.");
        }

        private static Market Fresh(MarketRules rules = null)
        {
            Market m = rules == null ? Sim.NewMarket(0) : Sim.NewMarket(rules, 0);
            m.StartVisit(1, 0, 0);
            return m;
        }

        // =====================================================================================
        // 8. Bounds
        // =====================================================================================

        public static void Eight(Md md, long seed)
        {
            md.H(2, "8. Bounds — ten thousand random deals");

            var rng = new Rng(seed + 8);
            Market m = Sim.NewMarket(0);
            int visit = 1;
            double t = 0;
            m.StartVisit(visit, t, 0);

            List<string> prefabs = new List<string>();
            foreach (MarketItem it in m.Items) prefabs.Add(it.Prefab);
            string[] junk = { "", "Nothing", "Amber ", "wood", "Coins" };

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var reasons = new Dictionary<string, int>(StringComparer.Ordinal);
            int deals = 0, accepted = 0, threw = 0, badToken = 0, duplicateId = 0, invariant = 0;
            int lastTakings = 0;

            for (int i = 0; i < 10000; i++)
            {
                if (i > 0 && i % 500 == 0)
                {
                    lastTakings = m.Takings;
                    visit++;
                    t += Sim.Day * 0.5;
                    m.StartVisit(visit, t, lastTakings);
                }

                MarketSnapshot snap = m.Snapshot();
                var d = new Deal { VisitId = m.VisitId, Nonce = Sim.NextNonce(), CoinsOffered = rng.Next(0, 5000) };
                if (rng.Chance(0.1)) d.VisitId = rng.Next(0, 40);
                bool wants = rng.Chance(0.55);
                if (wants)
                {
                    string p = rng.Chance(0.05) ? junk[rng.Next(0, junk.Length)] : rng.Pick(prefabs);
                    MarketRow row = snap.Find(p);
                    int seen = row != null ? row.Buy : rng.Next(0, 100);
                    if (rng.Chance(0.15)) seen = rng.Next(0, 500);
                    d.Wanted = new DealLine { Prefab = p, Count = Count(rng), UnitPriceSeen = seen };
                }
                int lines = rng.Next(0, 3);
                for (int k = 0; k < lines; k++)
                {
                    string p = rng.Chance(0.05) ? junk[rng.Next(0, junk.Length)] : rng.Pick(prefabs);
                    MarketRow row = snap.Find(p);
                    int seen = row != null ? row.Sell : rng.Next(0, 100);
                    if (rng.Chance(0.15)) seen = rng.Next(0, 500);
                    d.Offered.Add(new DealLine { Prefab = p, Count = Count(rng), UnitPriceSeen = seen });
                }

                DealResult r;
                try { r = m.Settle(d, d.CoinsOffered, t); }
                catch (Exception) { threw++; continue; }
                deals++;

                string reason = r.Ok ? DealReason.Ok : r.Reason;
                if (!Tokens.Contains(reason)) badToken++;
                int n;
                reasons.TryGetValue(reason, out n);
                reasons[reason] = n + 1;
                if (r.Ok)
                {
                    accepted++;
                    if (!ids.Add(r.DeliveryId)) duplicateId++;
                }
                if (!Sound(m)) invariant++;
            }

            if (threw > 0 || badToken > 0 || duplicateId > 0 || invariant > 0) AllPassed = false;

            var counts = new List<string[]>();
            var keys = new List<string>(reasons.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (string k in keys) counts.Add(new[] { "`" + k + "`", Sim.N(reasons[k]), Sim.Pct((double)reasons[k] / deals, 1) });
            md.Line("A seeded generator builds buys, sells, barters and nonsense: prefabs from the catalogue and five that are not,");
            md.Line("counts from -5 to int.MaxValue, prices that are right, stale or invented, visit ids that are wrong one time in ten,");
            md.Line("and a new visit every 500 deals. What came back:");
            md.Blank();
            md.Table(new[] { "answer", "deals", "share" }, counts);

            md.Table(new[] { "invariant", "violations" }, new List<string[]>
            {
                new[] { "`Settle` threw", Sim.N(threw) },
                new[] { "an answer that is not a `DealReason` token", Sim.N(badToken) },
                new[] { "a repeated delivery id (" + Sim.N(ids.Count) + " issued)", Sim.N(duplicateId) },
                new[] { "stock outside 0..Max, purse negative, price below 1, or a multiplier that is not a number", Sim.N(invariant) },
            });
            md.Line((threw + badToken + duplicateId + invariant) == 0
                ? "**Ten thousand deals, " + Sim.N(accepted) + " of them settled, and not one violation.** The pure core holds its bounds under nonsense."
                : "**Violations found — see the counts above.**");
            Sim.Note("s8.accepted", Sim.N(accepted));
        }

        private static int Count(Rng rng)
        {
            double roll = rng.NextDouble();
            if (roll < 0.05) return rng.Next(-5, 1);
            if (roll < 0.08) return int.MaxValue;
            if (roll < 0.15) return rng.Next(100, 1000);
            return rng.Next(1, 60);
        }

        /// <summary>Every bound the market promises, checked over all 72 rows.</summary>
        private static bool Sound(Market m)
        {
            if (m.Purse < 0) return false;
            foreach (MarketItem it in m.Items)
            {
                if (it.Stock < 0 || it.Stock > it.Entry.MaxStock) return false;
                double mult = Market.MultiplierFor(it.Entry.TargetStock, it.Stock, m.Rules);
                if (double.IsNaN(mult) || double.IsInfinity(mult)) return false;
                if (mult < m.Rules.MinMultiplier || mult > m.Rules.MaxMultiplier) return false;
                if (m.Charge(it) < 1 || m.Pays(it) < 1) return false;
            }
            return true;
        }
    }
}
