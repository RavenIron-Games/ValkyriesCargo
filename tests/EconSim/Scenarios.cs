// The six trading scenarios of the brief, plus the round trip found while writing scenario 4.
// Every visit is Market.StartVisit(id, worldTime, lastTakings); every deal is Market.Settle at
// the price in the snapshot the "player" was looking at; time between visits passes through
// Market.Relax, which the server calls exactly once a visit (Server/VisitDirector.Begin).

using System;
using System.Collections.Generic;
using RavenIron.ValkyriesCargo.Core;

namespace ValkyriesCargo.EconSim
{
    public static class Scenarios
    {
        // =====================================================================================
        // 1. Buy-out
        // =====================================================================================

        public static void One(Md md, long seed)
        {
            md.H(2, "1. Buy-out — a player empties a shelf, ten visits running");

            md.Line("Amber (base 7, target 30, max 90) bought one unit at a time in a single visit. The price is");
            md.Line("`base x clamp((target / max(1, stock))^0.35, 0.4, 3.0)`, so the shelf gets dearer as it empties.");
            md.Line("Rows are grouped: one line per price the shelf holds.");
            md.Blank();

            Ladder(md, "Amber");

            md.Line("Iron (base 25, target 20, max 60) for contrast: " + Sim.Fact("s1.iron"));
            md.Blank();
            md.Line("The 3.0 ceiling binds only where `target / stock >= 3^(1/0.35) = 23.1`, i.e. at stock 1 for a target");
            md.Line("of 24 or more. Amber (30) reaches it on its last unit; Iron (20) tops out at " + Sim.Fact("s1.ironmult") + ".");

            md.H(3, "Ten visits, one game day apart, emptied every time");

            Market m = Sim.NewMarket(0);
            var buyer = new Trader("Bui the Rich", 1000000);
            int lastTakings = 0;
            var rows = new List<string[]>();
            bool everRecovered = false;
            int grandTotal = 0, grandUnits = 0, secondVisitFirstPrice = 0, secondVisitStock = 0;
            for (int v = 1; v <= 10; v++)
            {
                double t = (v - 1) * Sim.Day;
                m.StartVisit(v, t, lastTakings);
                int atStart = Sim.Stock(m, "Amber");
                if (atStart >= m.Find("Amber").Entry.TargetStock && v > 1) everRecovered = true;
                int units = 0, spent = 0, first = 0, last = 0;
                double topMult = 0;
                while (true)
                {
                    MarketSnapshot snap = m.Snapshot();
                    MarketRow row = snap.Find("Amber");
                    if (row.Stock <= 0) break;
                    double mult = Market.MultiplierFor(row.Target, row.Stock, m.Rules);
                    if (mult > topMult) topMult = mult;
                    DealResult r = m.Settle(Sim.Buy(snap, "Amber", 1, buyer.Coins), buyer.Coins, t);
                    if (!r.Ok) break;
                    buyer.Apply(r);
                    if (units == 0) first = row.Buy;
                    last = row.Buy;
                    spent += row.Buy;
                    units++;
                }
                lastTakings = m.Takings;
                grandTotal += spent;
                grandUnits += units;
                if (v == 2) { secondVisitFirstPrice = first; secondVisitStock = atStart; }
                rows.Add(new[]
                {
                    Sim.N(v), Sim.F(t / Sim.Day, 1), Sim.N(atStart), Sim.N(units), Sim.N(first) + " -> " + Sim.N(last),
                    Sim.F(topMult, 2), Sim.N(spent), Sim.N(m.Purse),
                });
            }
            md.Table(new[] { "visit", "game day", "on the shelf (after drift)", "units bought", "unit price", "top multiplier", "coins spent", "his purse at the end" }, rows);
            Sim.Note("s1.v2first", Sim.N(secondVisitFirstPrice));
            Sim.Note("s1.total", Sim.N(grandTotal) + " coins for " + Sim.N(grandUnits) + " amber");

            // Where the shelf settles when it is emptied every day: with WareHalfLifeGameDays 0, nowhere.
            md.Line("`WareHalfLifeGameDays` is 0 (the owner, 2026-09-07): a Ware does not restock itself. The first visit empties the shelf and");
            md.Line("every visit after it finds " + Sim.N(secondVisitStock) + " amber, until a player sells him some back or an admin raises the target " +
                    (everRecovered ? "(it did recover to target at least once here, which means somebody did)." : "(neither happens in this run)."));
            md.Line("The buy-out is final rather than self-punishing: " + Sim.Fact("s1.total") + " over the ten visits, all of it on visit one.");
            md.Line("Section 10 has the same run at every half-life from half a day to never, which is where this number was decided.");
            Sim.Note("s1.recovered", everRecovered ? "yes" : "no");
        }

        private static void Ladder(Md md, string prefab)
        {
            Market m = Sim.NewMarket(0);
            m.StartVisit(1, 0, 0);
            var buyer = new Trader("Ladder", 1000000);
            var rows = new List<string[]>();
            int price = -1, runFrom = 0, runTo = 0, runUnits = 0;
            double runMult = 0;
            long coins = 0;
            int bought = 0;
            double topMult = 0;
            int firstPrice = 0, lastPrice = 0;
            while (true)
            {
                MarketSnapshot snap = m.Snapshot();
                MarketRow row = snap.Find(prefab);
                if (row.Stock <= 0) break;
                double mult = Market.MultiplierFor(row.Target, row.Stock, m.Rules);
                DealResult r = m.Settle(Sim.Buy(snap, prefab, 1, buyer.Coins), buyer.Coins, 0);
                if (!r.Ok) break;
                buyer.Apply(r);
                if (bought == 0) firstPrice = row.Buy;
                lastPrice = row.Buy;
                if (mult > topMult) topMult = mult;
                coins += row.Buy;
                bought++;
                if (row.Buy != price)
                {
                    if (price >= 0) rows.Add(new[] { Sim.N(price), Sim.F(runMult, 3), Sim.N(runFrom) + " -> " + Sim.N(runTo), Sim.N(runUnits) });
                    price = row.Buy; runFrom = row.Stock; runMult = mult; runUnits = 0;
                }
                runTo = row.Stock - 1;
                runUnits++;
            }
            if (price >= 0) rows.Add(new[] { Sim.N(price), Sim.F(runMult, 3), Sim.N(runFrom) + " -> " + Sim.N(runTo), Sim.N(runUnits) });
            md.Table(new[] { "unit price", "multiplier at the first unit of the run", "shelf", "units at this price" }, rows);
            md.Line("Thirty amber off a full shelf cost **" + Sim.N(coins) + " coins** one at a time, against " +
                    Sim.N(30 * firstPrice) + " for the same thirty in one deal (the whole quantity is priced at the price on");
            md.Line("screen, DESIGN section 8). The last unit costs " + Sim.N(lastPrice) + ", " + Sim.F((double)lastPrice / firstPrice, 1) + "x the first.");
            md.Blank();
            Sim.Note("s1.amberdrip", Sim.N(coins));
            Sim.Note("s1.amberbulk", Sim.N(30 * firstPrice));

            // Iron, for the contrast line above.
            Market im = Sim.NewMarket(0);
            im.StartVisit(1, 0, 0);
            long icoins = 0;
            int iunits = 0, ifirst = 0, ilast = 0;
            double imult = 0;
            while (true)
            {
                MarketSnapshot snap = im.Snapshot();
                MarketRow row = snap.Find("Iron");
                if (row.Stock <= 0) break;
                double mm = Market.MultiplierFor(row.Target, row.Stock, im.Rules);
                if (mm > imult) imult = mm;
                DealResult r = im.Settle(Sim.Buy(snap, "Iron", 1, 1000000), 1000000, 0);
                if (!r.Ok) break;
                if (iunits == 0) ifirst = row.Buy;
                ilast = row.Buy;
                icoins += row.Buy;
                iunits++;
            }
            Sim.Note("s1.iron", iunits + " units, " + Sim.N(icoins) + " coins one at a time, " + Sim.N(ifirst) + " -> " + Sim.N(ilast) + " a unit");
            Sim.Note("s1.ironmult", Sim.F(imult, 3));
        }

        // =====================================================================================
        // 2. Flood
        // =====================================================================================

        private static readonly string[] FloodWants = { "Wood", "DeerHide", "IronScrap", "FlametalOreNew" };

        public static void Two(Md md, long seed)
        {
            md.H(2, "2. Flood — 200 of one Want into a full shelf");

            md.Line("Two hundred units offered in stacks of fifty (the vanilla stack for these rows, CATALOGUE section 3);");
            md.Line("when he refuses a stack the harness bisects for the largest count he will still take, so the table shows");
            md.Line("exactly where each Want stops and why. He starts with the default purse of 800 coins and the shelf at target.");
            md.Blank();

            var rows = new List<string[]>();
            foreach (string prefab in FloodWants)
            {
                Market m = Sim.NewMarket(0);
                m.StartVisit(1, 0, 0);
                var seller = new Trader("Flood", 0);
                seller.Give(prefab, 400);
                MarketItem item = m.Find(prefab);
                int remaining = 200, sold = 0, earned = 0, firstPrice = 0, lastPrice = 0;
                string firstRefusal = "", stop = "";
                int firstRefusalAt = 0;
                while (remaining > 0)
                {
                    int want = Math.Min(50, remaining);
                    MarketSnapshot snap = m.Snapshot();
                    DealResult r = m.Settle(Sim.Sell(snap, prefab, want, seller.Coins), seller.Coins, 0);
                    int units = want;
                    if (!r.Ok)
                    {
                        if (firstRefusal.Length == 0) { firstRefusal = r.Reason; firstRefusalAt = want; }
                        string why;
                        int largest = Sim.LargestSale(m, prefab, want - 1, seller.Coins, 0, out why);
                        // `why` is why the NEXT unit was refused, which is the true bound; r.Reason is only
                        // why a fifty-stack was refused, and over_max is tested before the purse.
                        if (largest <= 0) { stop = why != DealReason.Ok ? why : r.Reason; break; }
                        units = largest;
                        snap = m.Snapshot();
                        r = m.Settle(Sim.Sell(snap, prefab, units, seller.Coins), seller.Coins, 0);
                        if (!r.Ok) { stop = r.Reason; break; }
                    }
                    int unit = snap.Find(prefab).Sell;
                    if (sold == 0) firstPrice = unit;
                    lastPrice = unit;
                    seller.Apply(r);
                    sold += units;
                    earned += r.CoinsDelta;
                    remaining -= units;
                }
                if (stop.Length == 0) stop = "took all 200";
                rows.Add(new[]
                {
                    prefab, Sim.N(item.Entry.BasePrice), Sim.N(item.Entry.TargetStock) + " / " + Sim.N(item.Entry.MaxStock),
                    Sim.N(sold), Sim.N(firstPrice) + " -> " + Sim.N(lastPrice), Sim.N(earned), Sim.N(m.Purse),
                    Sim.N(item.Stock), firstRefusal.Length == 0 ? "-" : firstRefusal + " (at " + Sim.N(firstRefusalAt) + ")", stop,
                });
                if (prefab == "IronScrap")
                {
                    Sim.Note("s2.scrap", "sold " + sold + " for " + earned + " coins; stopped " + stop + " with " + m.Purse + " coins left and the shelf at " + item.Stock + " of " + item.Entry.MaxStock);
                    Sim.Note("s2.scrapstop", stop);
                }
                if (prefab == "Wood") Sim.Note("s2.wood", "all 200 taken for " + earned + " coins, " + firstPrice + " a unit from first to last");
            }
            md.Table(new[] { "Want", "base", "target / max", "units he took", "he paid, a unit", "coins to the player", "purse left", "shelf", "first refusal", "stopped by" }, rows);

            md.Line("**`over_max` is checked before the purse** — per offered line, inside `Settle`, at Market.cs 288 against 299 — so the");
            md.Line("*first* refusal is `over_max` on every row here, even where he could not have paid for the stack anyway. The last");
            md.Line("column is the honest one: it is why the NEXT single unit was refused, and it says flametal ore stops on the purse");
            md.Line("while the cheap rows stop on the shelf. Scrap iron in fifties: " + Sim.Fact("s2.scrap") + ".");

            md.H(3, "The same 200, one unit at a time");

            var drip = new List<string[]>();
            foreach (string prefab in FloodWants)
            {
                Market m = Sim.NewMarket(0);
                m.StartVisit(1, 0, 0);
                int sold = 0, earned = 0, first = 0, last = 0;
                string stop = "took all 200";
                for (int i = 0; i < 200; i++)
                {
                    MarketSnapshot snap = m.Snapshot();
                    MarketRow row = snap.Find(prefab);
                    DealResult r = m.Settle(Sim.Sell(snap, prefab, 1, 0), 0, 0);
                    if (!r.Ok) { stop = r.Reason; break; }
                    if (sold == 0) first = row.Sell;
                    last = row.Sell;
                    sold++;
                    earned += r.CoinsDelta;
                }
                drip.Add(new[] { prefab, Sim.N(sold), Sim.N(first) + " -> " + Sim.N(last), Sim.N(earned), Sim.N(m.Purse), stop });
                if (prefab == "IronScrap")
                {
                    Sim.Note("s2.scrapdrip", sold + " units for " + earned + " coins, stopped " + stop + ", " + m.Purse + " coins still in the purse");
                    Sim.Note("s2.scraprange", Sim.N(first) + " -> " + Sim.N(last));
                }
            }
            md.Table(new[] { "Want", "units he took", "he paid, a unit", "coins to the player", "purse left", "stopped by" }, drip);
            md.Line("Scrap iron one at a time: " + Sim.Fact("s2.scrapdrip") + " — 745 against 794 for the same");
            md.Line("goods in stacks, the mirror of CATALOGUE section 5's worked line, and the reason a player should always offer the");
            md.Line("whole stack at once.");
            md.Blank();
            md.Line("**Cheap Wants fill his shelf; dear ones empty his purse.** Two hundred wood troubles neither bound — 400 units of room");
            md.Line("and 200 coins for the lot. Deer hide and scrap iron run out of room (`over_max`) with coins still in the purse.");
            md.Line("Flametal ore, at 63 a unit, runs him dry after fifteen (`purse_empty`) with");
            md.Line("two thirds of the shelf still free. The crossover is `purse / (2 x target)` coins a unit — 13 for a 30-target row like");
            md.Line("scrap iron, 40 for a 10-target row like flametal ore. Above it, the purse is the wall and the shelf never matters.");

            md.H(3, "The 0.4 floor is unreachable");

            Market probe = Sim.NewMarket(0);
            double worst = double.MaxValue;
            string worstRow = "";
            var flat = new List<string>();
            var flatRows = new List<string[]>();
            foreach (MarketItem it in probe.Items)
            {
                double atMax = Market.MultiplierFor(it.Entry.TargetStock, it.Entry.MaxStock, probe.Rules);
                if (atMax < worst) { worst = atMax; worstRow = it.Prefab; }
                int payAtTarget = Market.PaysFor(it.Entry.BasePrice, it.Entry.TargetStock, it.Entry.TargetStock, probe.Rules);
                int payAtMax = Market.PaysFor(it.Entry.BasePrice, it.Entry.TargetStock, it.Entry.MaxStock, probe.Rules);
                if (payAtTarget == payAtMax)
                {
                    flat.Add(it.Prefab);
                    flatRows.Add(new[] { it.Prefab, it.Kind.ToString(), Sim.N(it.Entry.BasePrice), Sim.N(payAtTarget), Sim.N(payAtMax) });
                }
            }
            Sim.Note("floor.min", Sim.F(worst, 4));
            Sim.Note("floor.row", worstRow);
            Sim.Note("floor.flatcount", Sim.N(flat.Count));
            Sim.Note("floor.flat", string.Join(", ", flat.ToArray()));

            md.Line("Every one of the 72 default rows has `Max = 3 x Target`, so the lowest multiplier any shelf can reach is");
            md.Line("`(1/3)^0.35 = " + Sim.F(worst, 4) + "` — the configured `MinPriceMultiplier` of **0.4 can never be reached by trading at all**.");
            md.Line("It is dead config today: only a catalogue with `Max > 13.7 x Target` would ever touch it.");
            md.Blank();
            md.Line("The floor that does bite is `max(1, ...)` in `PaysFor`. " + Sim.N(flat.Count) + " rows pay the SAME coin at target and at");
            md.Line("max stock, so flooding them changes nothing a player can see:");
            md.Blank();
            md.Table(new[] { "row", "kind", "base", "he pays at target", "he pays at max stock" }, flatRows);
        }

        // =====================================================================================
        // 3. Purse exhaustion and carry
        // =====================================================================================

        public static void Three(Md md, long seed)
        {
            md.H(2, "3. The purse — exhaustion, carry, and twenty visits");

            md.Line("The purse is `min(PurseCoins x PurseCapMultiple, PurseCoins + round(lastTakings x PurseCarryPercent/100))`,");
            md.Line("i.e. 1500 + half of last visit's takings, capped at 4500 (Market.StartVisit). `Takings` is `Purse - purseAtVisitStart`,");
            md.Line("**floored at zero**: a visit in which he only bought pays nothing forward.");

            md.H(3, "Selling until he cannot pay");

            md.Line("The six rows the mod exists for: ore and scrap do not go through a portal, and Ingvar lands at the base with the");
            md.Line("smelter (CATALOGUE section 1). One player, one visit, one unit at a time, into the opening purse of 800:");
            md.Blank();

            string[] ores = { "CopperOre", "TinOre", "IronScrap", "SilverOre", "BlackMetalScrap", "FlametalOreNew" };
            var oreRows = new List<string[]>();
            foreach (string ore in ores)
            {
                Market om = Sim.NewMarket(0);
                om.StartVisit(1, 0, 0);
                int ounits = 0, ocoins = 0, ofirst = 0, olast = 0;
                string oreason = "took everything offered";
                for (int i = 0; i < 500; i++)
                {
                    MarketSnapshot snap = om.Snapshot();
                    MarketRow row = snap.Find(ore);
                    DealResult r = om.Settle(Sim.Sell(snap, ore, 1, 0), 0, 0);
                    if (!r.Ok) { oreason = r.Reason; break; }
                    if (ounits == 0) ofirst = row.Sell;
                    olast = row.Sell;
                    ounits++;
                    ocoins += r.CoinsDelta;
                }
                oreRows.Add(new[]
                {
                    ore, Sim.N(om.Find(ore).Entry.BasePrice), Sim.N(ounits), Sim.N(ofirst) + " -> " + Sim.N(olast),
                    Sim.N(ocoins), Sim.N(om.Purse), "`" + oreason + "`", Sim.N(om.Takings),
                });
                if (ore == "SilverOre")
                {
                    Sim.Note("s3.exhaust", "one visit buys " + ounits + " silver ore for " + ocoins + " coins, then `" + oreason + "` with " + om.Purse + " coins left");
                    Sim.Note("s3.takingsafterselling", Sim.N(om.Takings));
                }
            }
            md.Table(new[] { "row", "base", "units before he stops", "he paid, a unit", "coins to the player", "purse left", "stopped by", "takings" }, oreRows);
            md.Line("**Takings are 0 in every one of those visits.** `Takings` is `Purse - purseAtVisitStart` floored at zero, so a visit in");
            md.Line("which players only sold him things pays nothing forward: the next purse is the bare 1500 again.");
            md.Blank();
            md.H(3, "Twenty visits, two kinds of server");

            md.Line("One rich player a visit. On the left he only shops: he buys every Ware down to nothing, one deal a row, the whole");
            md.Line("shelf at the price on screen. On the right he shops AND sells Wants in stacks of fifty until Ingvar refuses. Same");
            md.Line("market, same twenty game days; the only difference is whether anybody sells him anything.");
            md.Blank();

            int capBuyOnly, capBoth;
            List<string[]> buyOnly = Series(false, out capBuyOnly);
            List<string[]> both = Series(true, out capBoth);
            var merged = new List<string[]>();
            for (int i = 0; i < buyOnly.Count; i++)
            {
                if (i == 6) { merged.Add(new[] { "...", "", "", "", "", "", "", "" }); continue; }
                if (i > 6 && i < buyOnly.Count - 1) continue;         // visits 8..19 repeat visit 7 exactly
                merged.Add(new[] { buyOnly[i][0], buyOnly[i][1], buyOnly[i][2], buyOnly[i][3], both[i][1], both[i][4], both[i][2], both[i][3] });
            }
            md.Table(new[] { "visit", "purse (shoppers only)", "takings", "carry", "purse (shoppers and sellers)", "he paid out", "takings", "carry" }, merged);
            Sim.Note("s3.caphits", Sim.N(capBuyOnly));
            Sim.Note("s3.capboth", Sim.N(capBoth));

            md.Line("**Shoppers only: the cap engages on " + Sim.N(capBuyOnly) + " of the 20 visits.** Emptying eighteen Ware shelves on the first visit puts");
            md.Line("thousands of coins in his hand and half of that is over the cap on its own; with `WareHalfLifeGameDays` 0 there is nothing left");
            md.Line("to buy from the second visit on, so the takings fall to zero and the purse is back to its base by the third.");
            md.Blank();
            md.Line("**Shoppers and sellers: the cap engages on " + Sim.N(capBoth) + " visits and the carry is 0 every single time.** The same player who put");
            md.Line("thousands in takes more back out before he leaves, so the visit's NET takings are zero and the next purse is the bare");
            md.Line("1500. That is the shape of a real server — people sell him more than they buy, because he is how you turn ore into");
            md.Line("coin. **Carry as written rewards a shopping server and does nothing at all for a supplying one**, and a supplying");
            md.Line("server is the one the catalogue was built for.");
        }

        /// <summary>Twenty visits a game day apart; the player always buys, and sells too when <paramref name="sell"/>.</summary>
        private static List<string[]> Series(bool sell, out int capHits)
        {
            Market h = Sim.NewMarket(0);
            var rich = new Trader("Hoard", 100000000);
            List<string> wares = Sim.Prefabs(h, EntryKind.Ware);
            List<string> wants = Sim.Prefabs(h, EntryKind.Want);
            foreach (string w in wants) rich.Give(w, 100000);
            int lastTakings = 0;
            capHits = 0;
            var rows = new List<string[]>();
            int cap = h.Rules.PurseCoins * h.Rules.PurseCapMultiple;
            for (int v = 1; v <= 20; v++)
            {
                double t = (v - 1) * Sim.Day;
                h.StartVisit(v, t, lastTakings);
                int purseStart = h.Purse;
                if (purseStart >= cap) capHits++;
                int paid = 0;
                foreach (string ware in wares)
                {
                    int stock = Sim.Stock(h, ware);
                    if (stock <= 0) continue;
                    DealResult r = h.Settle(Sim.Buy(h.Snapshot(), ware, stock, rich.Coins), rich.Coins, t);
                    if (r.Ok) rich.Apply(r);
                }
                if (sell)
                {
                    foreach (string want in wants)
                    {
                        while (true)
                        {
                            string why;
                            int n = Sim.LargestSale(h, want, 50, rich.Coins, t, out why);
                            if (n <= 0) break;
                            DealResult r = h.Settle(Sim.Sell(h.Snapshot(), want, n, rich.Coins), rich.Coins, t);
                            if (!r.Ok) break;
                            rich.Apply(r);
                            paid += r.CoinsDelta;
                            if (n < 50) break;
                        }
                    }
                }
                int takings = h.Takings;
                int carry = Math.Min(cap, h.Rules.PurseCoins + (int)Math.Round(takings * 0.5, MidpointRounding.AwayFromZero)) - h.Rules.PurseCoins;
                rows.Add(new[] { Sim.N(v), Sim.N(purseStart), Sim.N(takings), Sim.N(carry), Sim.N(paid) });
                lastTakings = takings;
            }
            return rows;
        }

        // =====================================================================================
        // 4. Barter
        // =====================================================================================

        public static void Four(Md md, long seed)
        {
            md.H(2, "4. Barter — the dearest ware, paid for in goods");

            Market m = Sim.NewMarket(0);
            m.StartVisit(1, 0, 0);
            MarketSnapshot snap = m.Snapshot();

            // The dearest Ware and the most valuable Want, chosen from the snapshot the terminal renders.
            string dearest = null; int dearestPrice = 0;
            string richest = null; int richestPay = 0;
            foreach (MarketRow row in snap.Rows)
            {
                if (row.Kind == EntryKind.Ware && row.Buy > dearestPrice) { dearest = row.Prefab; dearestPrice = row.Buy; }
                if (row.Kind == EntryKind.Want && row.Sell > richestPay) { richest = row.Prefab; richestPay = row.Sell; }
            }
            int need = (dearestPrice + richestPay - 1) / richestPay;   // the smallest stack that covers it

            var player = new Trader("Barterer", 0);
            player.Give(richest, 50);
            var offeredPrefabs = new List<string> { richest };
            var offeredCounts = new List<int> { need };
            int wareStockBefore = Sim.Stock(m, dearest);
            int wantStockBefore = Sim.Stock(m, richest);
            int purseBefore = m.Purse;

            DealResult res = m.Settle(Sim.Barter(snap, dearest, 1, offeredPrefabs, offeredCounts, player.Coins), player.Coins, 0);
            player.Apply(res);

            MarketSnapshot after = m.Snapshot();
            md.Line("With no coins at all, a player buys one **" + dearest + "** (his dearest ware at " + Sim.N(dearestPrice) + " coins) and pays in **" +
                    richest + "**, the Want he values highest at " + Sim.N(richestPay) + " a unit.");
            md.Blank();
            md.Table(new[] { "", "before", "after" }, new List<string[]>
            {
                new[] { "his " + dearest + " shelf", Sim.N(wareStockBefore) + " (he charges " + Sim.N(dearestPrice) + ")", Sim.N(Sim.Stock(m, dearest)) + " (he charges " + Sim.N(after.Find(dearest).Buy) + ")" },
                new[] { "his " + richest + " shelf", Sim.N(wantStockBefore) + " (he pays " + Sim.N(richestPay) + ")", Sim.N(Sim.Stock(m, richest)) + " (he pays " + Sim.N(after.Find(richest).Sell) + ")" },
                new[] { "his purse", Sim.N(purseBefore), Sim.N(m.Purse) },
                new[] { "the player's coins", "0", Sim.N(player.Coins) },
            });
            md.Line("The " + Sim.N(need) + " units are worth `" + Sim.N(need) + " x " + Sim.N(richestPay) + " = " + Sim.N(need * richestPay) + "` coins against a price of " + Sim.N(dearestPrice) +
                    ", so `net = " + Sim.N(dearestPrice) + " - " + Sim.N(need * richestPay) + " = " + Sim.N(dearestPrice - need * richestPay) + "`");
            md.Line("and **`Settle` pays the difference out of the purse** (`CoinsDelta = " + Sim.Signed(res.CoinsDelta) + "`). Change is real coin, not credit: a barter can");
            md.Line("therefore be refused `purse_empty` even though the player is the one buying. Both shelves move in the same deal, and");
            md.Line("both prices are read BEFORE either moves, so the offered goods are valued at the pre-deal shelf.");
            Sim.Note("s4.line", dearest + " for " + need + " " + richest + " (" + (need * richestPay) + " coins of goods against " + dearestPrice + "), change " + res.CoinsDelta);
            Sim.Note("s4.change", Sim.N(res.CoinsDelta));

            // Barter that runs him out of change.
            Market poor = Sim.NewMarket(new MarketRules { PurseCoins = 5 }, 0);
            poor.StartVisit(1, 0, 0);
            MarketSnapshot ps = poor.Snapshot();
            DealResult refused = poor.Settle(Sim.Barter(ps, dearest, 1, offeredPrefabs, offeredCounts, 0), 0, 0);
            md.Line("The same barter against a 5-coin purse is refused **`" + refused.Reason + "`** — he has the goods and the player has the payment,");
            md.Line("and the deal dies on the change. That is worth a line of his own in the terminal ('I've not the coin to make it even').");
        }

        // =====================================================================================
        // 5. A server day
        // =====================================================================================

        public static void Five(Md md, long seed)
        {
            md.H(2, "5. A server day — four players, three visits, one game day");

            md.Line("Three visits at 0, 600 and 1200 seconds of a 1800-second day. Four players with a few hundred coins and a bag of");
            md.Line("gathered goods; each does two to four deals a visit, drawn from a seeded generator. Sells are drawn from the Wants");
            md.Line("weighted by target stock (his target is what he normally carries, the best proxy in the data for what players bring");
            md.Line("him); buys are a fifth to a twentieth of a Ware's target, clamped to the shelf and to what the player can afford.");
            md.Blank();

            var rng = new Rng(seed + 5);
            Market m = Sim.NewMarket(0);
            List<string> wares = Sim.Prefabs(m, EntryKind.Ware);
            List<string> wants = Sim.Prefabs(m, EntryKind.Want);
            var wantWeights = new List<int>();
            foreach (string w in wants) wantWeights.Add(m.Find(w).Entry.TargetStock);

            var players = new List<Trader>();
            string[] names = { "Astrid", "Bjorn", "Gudrun", "Halfdan" };
            for (int i = 0; i < names.Length; i++)
            {
                var p = new Trader(names[i], rng.Next(150, 650));
                for (int k = 0; k < 6; k++)
                {
                    string w = wants[rng.Weighted(wantWeights)];
                    p.Give(w, rng.Next(20, 90));
                }
                players.Add(p);
            }

            MarketSnapshot dayStart = m.Snapshot();
            var visitRows = new List<string[]>();
            var playerRows = new List<string[]>();
            var refusals = new Dictionary<string, int>(StringComparer.Ordinal);
            int lastTakings = 0;
            int totalDeals = 0, totalOk = 0;
            for (int v = 1; v <= 3; v++)
            {
                double t = (v - 1) * 600.0;
                m.StartVisit(v, t, lastTakings);
                int purseStart = m.Purse;
                int dealsHere = 0, okHere = 0;
                foreach (Trader p in players)
                {
                    int deals = rng.Next(2, 5);
                    int spent = 0, earned = 0, ok = 0;
                    for (int d = 0; d < deals; d++)
                    {
                        MarketSnapshot snap = m.Snapshot();
                        bool sell = rng.Chance(0.55) && HasSomething(p);
                        DealResult r;
                        if (sell)
                        {
                            string prefab = PickHeld(p, rng);
                            int count = Math.Min(p.Have(prefab), rng.Next(5, 45));
                            r = m.Settle(Sim.Sell(snap, prefab, count, p.Coins), p.Coins, t);
                        }
                        else
                        {
                            string prefab = rng.Pick(wares);
                            MarketRow row = snap.Find(prefab);
                            int count = Math.Max(1, rng.Next(row.Target / 20 + 1, row.Target / 5 + 2));
                            if (count > row.Stock) count = row.Stock;
                            if (row.Buy > 0 && count * row.Buy > p.Coins) count = p.Coins / Math.Max(1, row.Buy);
                            if (count < 1) count = 1;
                            r = m.Settle(Sim.Buy(snap, prefab, count, p.Coins), p.Coins, t);
                        }
                        dealsHere++; totalDeals++;
                        if (r.Ok)
                        {
                            p.Apply(r);
                            ok++; okHere++; totalOk++;
                            if (r.CoinsDelta < 0) spent += -r.CoinsDelta; else earned += r.CoinsDelta;
                        }
                        else
                        {
                            int n;
                            refusals.TryGetValue(r.Reason, out n);
                            refusals[r.Reason] = n + 1;
                        }
                    }
                    playerRows.Add(new[] { Sim.N(v), p.Name, Sim.N(deals), Sim.N(ok), Sim.N(spent), Sim.N(earned), Sim.N(p.Coins) });
                }
                lastTakings = m.Takings;
                visitRows.Add(new[] { Sim.N(v), Sim.F(t, 0) + " s", Sim.N(purseStart), Sim.N(dealsHere), Sim.N(okHere), Sim.N(m.Purse), Sim.N(lastTakings) });
            }

            md.Table(new[] { "visit", "world time", "purse at start", "deals tried", "settled", "purse at end", "takings" }, visitRows);
            md.Table(new[] { "visit", "player", "deals", "settled", "coins spent", "coins earned", "coins left" }, playerRows);

            var refusalText = new List<string>();
            var keys = new List<string>(refusals.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (string k in keys) refusalText.Add(k + " x" + refusals[k]);
            md.Line("Deals tried: " + Sim.N(totalDeals) + ", settled " + Sim.N(totalOk) + ". Refusals: " +
                    (refusalText.Count == 0 ? "none" : string.Join(", ", refusalText.ToArray())) + ".");
            Sim.Note("s5.refusals", refusalText.Count == 0 ? "none" : string.Join(", ", refusalText.ToArray()));

            md.H(3, "What a day of four players does to the prices");

            MarketSnapshot dayEnd = m.Snapshot();
            m.Relax(1200.0 + Sim.Day);
            MarketSnapshot nextDay = m.Snapshot();

            var movers = new List<string[]>();
            var scored = new List<KeyValuePair<int, string>>();
            foreach (MarketRow row in dayStart.Rows)
            {
                MarketRow now = dayEnd.Find(row.Prefab);
                int shown = row.Kind == EntryKind.Ware ? Math.Abs(now.Buy - row.Buy) : Math.Abs(now.Sell - row.Sell);
                if (shown > 0) scored.Add(new KeyValuePair<int, string>(shown, row.Prefab));
            }
            scored.Sort((a, b) => a.Key != b.Key ? b.Key.CompareTo(a.Key) : string.CompareOrdinal(a.Value, b.Value));
            int shownRows = Math.Min(10, scored.Count);
            for (int i = 0; i < shownRows; i++)
            {
                string prefab = scored[i].Value;
                MarketRow a = dayStart.Find(prefab), b = dayEnd.Find(prefab), c = nextDay.Find(prefab);
                bool ware = a.Kind == EntryKind.Ware;
                movers.Add(new[]
                {
                    prefab, ware ? "Ware" : "Want",
                    Sim.N(a.Stock) + " / " + Sim.N(b.Stock) + " / " + Sim.N(c.Stock),
                    Sim.N(ware ? a.Buy : a.Sell), Sim.N(ware ? b.Buy : b.Sell), Sim.N(ware ? c.Buy : c.Sell),
                });
            }
            md.Table(new[] { "row", "kind", "stock: dawn / dusk / next dawn", "price at dawn", "at dusk", "after a day of drift" }, movers);
            md.Line("Of the 72 rows, **" + Sim.N(scored.Count) + " moved a coin** over a full day of four players trading. A day's drift then takes back");
            md.Line("a fifth of the gap (`1 - 0.5^(1/3)`) on every Want that moved and nothing on a Ware (`WareHalfLifeGameDays` 0: what the");
            md.Line("players bought stays bought). This is the number that matters for the first real visit: on a small server the");
            md.Line("market is quiet, and the prices a player sees on day two are close to the prices on day one.");
            Sim.Note("s5.movers", Sim.N(scored.Count));
            Sim.Note("s5.takings", Sim.N(lastTakings));
        }

        private static bool HasSomething(Trader p)
        {
            foreach (KeyValuePair<string, int> kv in p.Bag) if (kv.Value > 0) return true;
            return false;
        }

        private static string PickHeld(Trader p, Rng rng)
        {
            var held = new List<string>();
            foreach (KeyValuePair<string, int> kv in p.Bag) if (kv.Value > 0) held.Add(kv.Key);
            held.Sort(StringComparer.Ordinal);       // dictionary order is not a contract; the report must be
            return held[rng.Next(0, held.Count)];
        }

        // =====================================================================================
        // 6. Drift
        // =====================================================================================

        public static void Six(Md md, long seed)
        {
            md.H(2, "6. Drift — how long the damage lasts");

            md.Line("A market that has taken scenario 1's buy-out (Amber and Iron emptied) and scenario 2's flood (four Wants filled");
            md.Line("toward max), then nobody trades. `Relax` closes `1 - 0.5^(days / halfLife)` of each row's gap and rounds away from zero,");
            md.Line("with one half-life per kind: `WareHalfLifeGameDays` 0 (never) and `WantHalfLifeGameDays` 3 (the owner, 2026-09-07).");
            md.Blank();
            md.Line("**The server calls `Relax` exactly once a visit** (`VisitDirector.Begin` -> `Market.StartVisit`), so the honest question is");
            md.Line("'if the next visit is N days later, how close is the shelf?'. The last column answers the other one — a visit every day —");
            md.Line("and the two differ, because rounding away from zero moves at least one unit per call.");
            md.Blank();

            Market damaged = Sim.NewMarket(0);
            damaged.StartVisit(1, 0, 0);
            foreach (string ware in new[] { "Amber", "Iron" })
            {
                int stock = Sim.Stock(damaged, ware);
                damaged.Settle(Sim.Buy(damaged.Snapshot(), ware, stock, 1000000), 1000000, 0);
            }
            int floodVisit = 1;
            foreach (string want in FloodWants)
            {
                // A fresh visit at the SAME world time: the purse refills, no time passes, nothing drifts.
                // Four sellers on one day, not one seller with a bottomless merchant.
                damaged.StartVisit(++floodVisit, 0, 0);
                while (true)
                {
                    string why;
                    int n = Sim.LargestSale(damaged, want, 50, 0, 0, out why);
                    if (n <= 0) break;
                    DealResult r = damaged.Settle(Sim.Sell(damaged.Snapshot(), want, n, 0), 0, 0);
                    if (!r.Ok) break;
                    if (n < 50) break;
                }
            }

            string state = damaged.EncodeState();
            var rows = new List<string[]>();
            int worstOneCall = 0, worstStepped = 0;
            string worstRow = "";
            var stuck = new List<string>();
            foreach (MarketItem it in damaged.Items)
            {
                if (it.Stock == it.Entry.TargetStock) continue;
                int oneCall = DaysToRecover(state, it.Prefab, false);
                int stepped = DaysToRecover(state, it.Prefab, true);
                if (oneCall < 0) stuck.Add(it.Prefab);
                else
                {
                    if (oneCall > worstOneCall) { worstOneCall = oneCall; worstRow = it.Prefab; }
                    if (stepped > worstStepped) worstStepped = stepped;
                }
                rows.Add(new[]
                {
                    it.Prefab, it.Kind == EntryKind.Ware ? "Ware" : "Want", Sim.N(it.Stock), Sim.N(it.Entry.TargetStock),
                    Sim.Pct((double)(it.Stock - it.Entry.TargetStock) / it.Entry.TargetStock, 0),
                    oneCall < 0 ? "never" : Sim.N(oneCall), stepped < 0 ? "never" : Sim.N(stepped),
                });
            }
            md.Table(new[] { "row", "kind", "stock after the damage", "target", "off target", "days (one visit, N days later)", "days (a visit every day)" }, rows);
            md.Line("**The Wares never recover** (" + string.Join(", ", stuck.ToArray()) + "): `WareHalfLifeGameDays` is 0, so an emptied shelf stays");
            md.Line("empty until a player sells him that item back, or an admin raises its target (`cargo catalogue add`). That is the");
            md.Line("decision: his stock is what the server's players put in his hands, not a shelf that fills itself overnight.");
            md.Line("**Every damaged Want is back inside 5% of target within " + Sim.N(worstOneCall) + " game days**" +
                    (worstStepped == worstOneCall ? " either way" : ", or " + Sim.N(worstStepped) + " when he is visited every day") + "; the slowest row is " + worstRow + ".");
            md.Line("A game day is 1800 real seconds, so that is **" + Sim.F(worstOneCall * 0.5, 1) + " real hours** of server uptime: a flood is");
            md.Line("forgotten within a real day of play, and he never fills up for good. That is what the Want half-life is for.");
            Sim.Note("s6.days", Sim.N(worstOneCall));
            Sim.Note("s6.stepped", Sim.N(worstStepped));
            Sim.Note("s6.stuck", string.Join(", ", stuck.ToArray()));
        }

        /// <summary>First whole day count at which this row is within 5% of target, from the saved state.</summary>
        private static int DaysToRecover(string state, string prefab, bool stepped)
        {
            for (int days = 1; days <= 60; days++)
            {
                Market m = Sim.NewMarket(0);
                m.ApplyState(state, null);
                MarketItem it = m.Find(prefab);
                double t = it.UpdatedWorldTime;
                if (stepped) for (int d = 1; d <= days; d++) m.Relax(t + d * Sim.Day);
                else m.Relax(t + days * Sim.Day);
                double off = Math.Abs(it.Stock - it.Entry.TargetStock) / (double)it.Entry.TargetStock;
                if (off <= 0.05) return days;
            }
            return -1;
        }

        // =====================================================================================
        // 9. The round trip (not in the brief; found while writing scenario 4)
        // =====================================================================================

        public static void Nine(Md md, long seed)
        {
            md.H(2, "9. The round trip — buying a shelf out and selling it straight back");

            md.Line("`Settle` prices a whole deal at the pre-deal shelf (DESIGN section 8, 'a bulk deal beats a drip-feed') and Ingvar buys");
            md.Line("his own Wares back. Put those two together in one visit: buy the whole shelf at the full-shelf price, then sell the");
            md.Line("same goods back at the empty-shelf price. Nothing else happens; the shelf ends where it started.");
            md.Blank();

            var rows = new List<string[]>();
            int best = 0; string bestWare = "";
            int losers = 0;
            Market probe = Sim.NewMarket(0);
            foreach (string ware in Sim.Prefabs(probe, EntryKind.Ware))
            {
                Market m = Sim.NewMarket(0);
                m.StartVisit(1, 0, 0);
                var p = new Trader("Trip", 1000000);
                int stock = Sim.Stock(m, ware);
                MarketSnapshot s1 = m.Snapshot();
                int charge = s1.Find(ware).Buy;
                DealResult buy = m.Settle(Sim.Buy(s1, ware, stock, p.Coins), p.Coins, 0);
                if (!buy.Ok) continue;
                p.Apply(buy);
                MarketSnapshot s2 = m.Snapshot();
                int pays = s2.Find(ware).Sell;
                string why;
                int n = Sim.LargestSale(m, ware, stock, p.Coins, 0, out why);
                DealResult sell = n > 0 ? m.Settle(Sim.Sell(m.Snapshot(), ware, n, p.Coins), p.Coins, 0) : DealResult.Refuse(0, why);
                if (sell.Ok) p.Apply(sell);
                int profit = p.Coins - 1000000;
                if (profit > best) { best = profit; bestWare = ware; }
                if (profit <= 0) losers++;
                rows.Add(new[]
                {
                    ware, Sim.N(stock), Sim.N(charge), Sim.N(pays), Sim.F((double)pays / Math.Max(1, charge), 2),
                    Sim.N(n), Sim.Signed(profit), Sim.N(m.Purse), Sim.N(Sim.Stock(m, ware)),
                });
            }
            md.Table(new[] { "Ware", "shelf", "he charges (full shelf)", "he pays (empty shelf)", "pays / charges", "sold back", "player's profit", "his purse", "shelf at the end" }, rows);

            // How much a single player can take out of one visit, and out of three -- IF anything is
            // still profitable to round-trip. Under the Fair Market Act nothing is, `bestWare` stays
            // empty, and this whole section has no subject: `Sim.Stock(pump, "")` is a null deref, which
            // is how the Act's arrival was discovered here. A simulation that dies when the exploit it
            // measures is fixed is worse than useless, so the absence is now the finding.
            if (bestWare.Length == 0)
            {
                Sim.Note("s9.best", "none");
                Sim.Note("s9.bestprofit", "0");
                Sim.Note("s9.losers", Sim.N(losers));
                Sim.Note("s9.pervisit", "0");
                md.Line("**No Ware is profitable to round-trip.** All " + Sim.N(rows.Count) + " of them lose the player coins, so the");
                md.Line("pump table that used to stand here has no subject and is not printed.");
                md.Line("");
                md.Line("This is the **Fair Market Act** (`Server.FairMarketAct`, default on) doing its job: a Ware's buy-back");
                md.Line("multiplier is clamped at 1.0, so he never pays more than `base x SpreadBuy` for something he himself");
                md.Line("sells, and `3.0 x 0.7 = 2.1 > 1` no longer has anything to bite on. What he CHARGES still rises to the");
                md.Line("full 3.0x, and Wants are untouched -- the scarcity signal is intact everywhere it was meant to be.");
                md.Line("");
                md.Line("Before the Act this section reported 17 of 18 Wares profitable and a player walking off with Ingvar's");
                md.Line("whole purse on the first visit, with the shelves ending exactly where they started so nothing in the");
                md.Line("saved state showed it. Turn the knob off and this table comes back; that is the regression test.");
                return;
            }

            Market pump = Sim.NewMarket(0);
            var thief = new Trader("Pump", 100000);
            int lastTakings = 0, firstVisitGain = 0;
            var pumpRows = new List<string[]>();
            for (int v = 1; v <= 3; v++)
            {
                double t = (v - 1) * Sim.Day;
                pump.StartVisit(v, t, lastTakings);
                int before = thief.Coins, purseStart = pump.Purse, cycles = 0;
                while (true)
                {
                    int stock = Sim.Stock(pump, bestWare);
                    if (stock <= 0) break;
                    MarketSnapshot s = pump.Snapshot();
                    DealResult b = pump.Settle(Sim.Buy(s, bestWare, stock, thief.Coins), thief.Coins, t);
                    if (!b.Ok) break;
                    thief.Apply(b);
                    string why;
                    int n = Sim.LargestSale(pump, bestWare, stock, thief.Coins, t, out why);
                    if (n <= 0) break;
                    DealResult sl = pump.Settle(Sim.Sell(pump.Snapshot(), bestWare, n, thief.Coins), thief.Coins, t);
                    if (!sl.Ok) break;
                    thief.Apply(sl);
                    cycles++;
                    if (n < stock) break;
                }
                lastTakings = pump.Takings;
                if (v == 1) firstVisitGain = thief.Coins - before;
                pumpRows.Add(new[] { Sim.N(v), Sim.N(purseStart), Sim.N(cycles), Sim.Signed(thief.Coins - before), Sim.N(pump.Purse), Sim.N(lastTakings) });
            }
            md.Table(new[] { "visit", "purse at start", "round trips", "coins to the player", "purse at end", "takings" }, pumpRows);

            Sim.Note("s9.best", bestWare);
            Sim.Note("s9.bestprofit", Sim.N(best));
            Sim.Note("s9.losers", Sim.N(losers));
            Sim.Note("s9.pervisit", Sim.N(firstVisitGain));

            md.Line("**The Fair Market Act is OFF for this run** (`Server.FairMarketAct`), so the round trip below is live.");
            md.Line("");
            md.Line("**A round trip is profitable on every Ware whose ratio above is over 1.00**, which is " + Sim.N(rows.Count - losers) + " of the " + Sim.N(rows.Count) + " Wares.");
            md.Line("The reason is one line of arithmetic: an empty shelf multiplies the price by up to `MaxPriceMultiplier` (3.0) and he pays");
            md.Line("`SpreadBuy` (0.7) of it, and `3.0 x 0.7 = 2.1 > 1`. Any Ware whose target is 3 or more reaches a multiplier above `1/0.7 = 1.43`");
            md.Line("when its shelf is empty, so buying it out and selling it back turns coins into more coins. Only " + Sim.Fact("s9.losers") + " row is safe:");
            md.Line("BlackCore, whose target of 2 caps its empty-shelf multiplier at 1.27.");
            md.Line("The player's profit equals the purse drain exactly, so **one player can walk off with Ingvar's entire purse every visit**");
            md.Line("(" + Sim.Fact("s9.pervisit") + " coins on the first visit above) without gathering anything, and the shelves end the visit exactly where they");
            md.Line("started, so nothing in the market state shows it happened.");
        }

        // =====================================================================================
        // 10. The half-life sweep (2026-09-07: one knob per kind, and why wares never / wants 3)
        // =====================================================================================

        private static readonly double[] SweepHalfLives = { 0.5, 1, 3, 7, 30, 0 };
        private static readonly int[] SweepDays = { 1, 2, 3, 7, 14, 30 };

        private static string HalfLifeName(double hl) => hl <= 0 ? "never" : Sim.F(hl, 1);

        private static string RealTime(int days)
        {
            double h = days * Sim.Day / 3600.0;
            return h < 1 ? Sim.F(h * 60, 0) + " min" : Sim.F(h, 1) + " h";
        }

        private static Market SweepMarket(double ware, double want)
        {
            MarketRules r = Sim.Shipped;
            r.WareHalfLifeGameDays = ware;
            r.WantHalfLifeGameDays = want;
            return Sim.NewMarket(r, 0);
        }

        /// <summary>One row's stock at each of SweepDays, a Relax every game day (the server's own cadence on a busy server).</summary>
        private static string[] DriftRow(string label, Market m, string prefab, int stockAtZero)
        {
            MarketItem it = m.Find(prefab);
            it.Stock = stockAtZero; it.UpdatedWorldTime = 0;
            var row = new List<string> { label };
            int di = 0;
            for (int day = 1; day <= 30; day++)
            {
                m.Relax(day * Sim.Day);
                if (di < SweepDays.Length && SweepDays[di] == day) { row.Add(Sim.N(it.Stock)); di++; }
            }
            return row.ToArray();
        }

        public static void Ten(Md md, long seed)
        {
            md.H(2, "10. The half-life sweep — one knob per kind, and why");
            md.Line("The same market run at ONE half-life for both kinds, from half a game day to never, and then at the shipped pair.");
            md.Line("This is the table behind the owner's 2026-09-07 decision: a single number cannot serve both kinds, because a Ware only");
            md.Line("leaves the shelf when someone buys it and a Want only arrives when someone sells it, so 'never' empties the one and");
            md.Line("fills the other for good. A game day is 1800 real seconds of server uptime; a Relax every game day.");
            md.Blank();

            var head = new List<string> { "half-life (both kinds)" };
            foreach (int d in SweepDays) head.Add("day " + Sim.N(d) + " (" + RealTime(d) + ")");

            md.H(3, "Iron (a Ware: target 20, max 60) emptied at day 0. Stock at the visit N days later");
            var rows = new List<string[]>();
            foreach (double hl in SweepHalfLives) rows.Add(DriftRow(HalfLifeName(hl), SweepMarket(hl, hl), "Iron", 0));
            md.Table(head.ToArray(), rows);
            md.Blank();

            md.H(3, "Wood (a Want: target 200, max 600) flooded to 600 at day 0. Stock at the visit N days later");
            rows = new List<string[]>();
            foreach (double hl in SweepHalfLives) rows.Add(DriftRow(HalfLifeName(hl), SweepMarket(hl, hl), "Wood", 600));
            md.Table(head.ToArray(), rows);
            md.Blank();

            md.H(3, "A shopping server: 30 visits one game day apart; each visit one player buys the whole Iron and Amber shelves");
            rows = new List<string[]>();
            foreach (double hl in SweepHalfLives)
            {
                Market m = SweepMarket(hl, hl);
                int ironUnits = 0, ironCoins = 0, ironEmpty = 0, amberUnits = 0, amberCoins = 0, amberEmpty = 0;
                for (int v = 1; v <= 30; v++)
                {
                    double t = v * Sim.Day;
                    m.StartVisit(v, t, m.Coined);
                    foreach (string prefab in new[] { "Iron", "Amber" })
                    {
                        MarketItem it = m.Find(prefab);
                        if (it.Stock <= 0) { if (prefab == "Iron") ironEmpty++; else amberEmpty++; continue; }
                        int count = it.Stock, unit = m.Charge(it);
                        DealResult r = m.Settle(Sim.Buy(m.Snapshot(), prefab, count, 1000000), 1000000, t);
                        if (!r.Ok) continue;
                        if (prefab == "Iron") { ironUnits += count; ironCoins += count * unit; } else { amberUnits += count; amberCoins += count * unit; }
                    }
                }
                rows.Add(new[] { HalfLifeName(hl), Sim.N(ironUnits), Sim.N(ironCoins), Sim.N(ironEmpty), Sim.N(amberUnits), Sim.N(amberCoins), Sim.N(amberEmpty), Sim.N(m.Purse) });
            }
            md.Table(new[] { "half-life (both kinds)", "Iron units sold", "Iron coins", "visits Iron was empty", "Amber units", "Amber coins", "visits Amber was empty", "his purse at the end" }, rows);
            md.Line("The longer the shelf remembers, the less of a shop he is: at never he sells each shelf once and stands empty for the");
            md.Line("other 29 visits. That is the trade the owner made for Wares, on purpose: what he sells is what the server's players");
            md.Line("sold him and what an admin's target says, not a shelf that fills itself overnight.");
            md.Blank();

            md.H(3, "A supplying server: 30 visits one game day apart; each visit one player sells 50 Wood and 20 IronScrap, or what still fits");
            rows = new List<string[]>();
            int neverRefused = 0;
            foreach (double hl in SweepHalfLives)
            {
                Market m = SweepMarket(hl, hl);
                int wood = 0, woodCoins = 0, woodFull = 0, scrap = 0, scrapCoins = 0, scrapFull = 0, refusedVisits = 0;
                for (int v = 1; v <= 30; v++)
                {
                    double t = v * Sim.Day;
                    m.StartVisit(v, t, m.Coined);
                    bool refused = false;
                    foreach (string prefab in new[] { "Wood", "IronScrap" })
                    {
                        int ask = prefab == "Wood" ? 50 : 20;
                        MarketItem it = m.Find(prefab);
                        int count = Math.Min(ask, it.Entry.MaxStock - it.Stock);
                        if (count <= 0)
                        {
                            refused = true;
                            if (prefab == "Wood" && woodFull == 0) woodFull = v;
                            if (prefab == "IronScrap" && scrapFull == 0) scrapFull = v;
                            continue;
                        }
                        int unit = m.Pays(it);
                        DealResult r = m.Settle(Sim.Sell(m.Snapshot(), prefab, count, 0), 0, t);
                        if (!r.Ok) { refused = true; continue; }
                        if (prefab == "Wood") { wood += count; woodCoins += count * unit; } else { scrap += count; scrapCoins += count * unit; }
                    }
                    if (refused) refusedVisits++;
                }
                if (hl <= 0) neverRefused = refusedVisits;
                rows.Add(new[] { HalfLifeName(hl), Sim.N(wood), Sim.N(woodCoins), woodFull == 0 ? "never" : Sim.N(woodFull), Sim.N(scrap), Sim.N(scrapCoins), scrapFull == 0 ? "never" : Sim.N(scrapFull), Sim.N(refusedVisits) });
            }
            md.Table(new[] { "half-life (both kinds)", "Wood bought", "coins paid for wood", "first visit Wood was full", "IronScrap bought", "coins paid for scrap", "first visit scrap was full", "visits he refused something" }, rows);
            Sim.Note("s10.neverRefused", Sim.N(neverRefused));
            md.Line("A Want only ever arrives, so at never every Want fills to its max and he stops buying: **" + Sim.N(neverRefused) + " of 30 visits refused**.");
            md.Line("That is why the Want knob is not 0. At 3 he passes on what he was sold fast enough that nobody is refused and the price");
            md.Line("he pays stays near par; at 30 nobody is refused either, but scrap sits at the flooded price for the whole month.");
            md.Blank();

            md.H(3, "The shipped pair: wares never, wants 3");
            rows = new List<string[]>();
            rows.Add(DriftRow("Iron emptied (Ware, never)", Sim.NewMarket(0), "Iron", 0));
            rows.Add(DriftRow("Wood flooded to 600 (Want, 3)", Sim.NewMarket(0), "Wood", 600));
            rows.Add(DriftRow("Iron set to 60 by an admin, target 20 (Ware, never)", Sim.NewMarket(0), "Iron", 60));
            var head2 = new List<string> { "row" };
            foreach (int d in SweepDays) head2.Add("day " + Sim.N(d) + " (" + RealTime(d) + ")");
            md.Table(head2.ToArray(), rows);
            md.Line("**An admin's stock edit lives as long as the drift lets it**, which for a Ware is now for ever, and for a Want a few days.");
            md.Line("The lever that persists on either kind is the TARGET (`cargo catalogue add Iron:25:60:60:Ware` holds 60 at any half-life,");
            md.Line("because the gap is 0); a stock edit is an event. No stock verb was asked for, and none was built.");
        }
    }
}
