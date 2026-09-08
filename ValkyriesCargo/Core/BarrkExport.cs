using System;
using System.Collections.Generic;

namespace RavenIron.ValkyriesCargo.Core
{
    /// <summary>One row of barrkbot_cargo_market.json, before it becomes JSON.</summary>
    public sealed class MarketExportRow
    {
        public string Prefab = "";
        /// <summary>"Ware" or "Want" -- Catalogue.EntryKind spelled out.</summary>
        public string Kind = "";
        /// <summary>True for a Ware: he sells it. Every row's buy_price is still shown (it drives the
        /// trend arrow, Market.cs's own comment: "the arrow is right for a Want too"), but only a Ware
        /// is actually purchasable -- this is the derived fact that says so, rather than leaving the
        /// reader to infer it from Kind.</summary>
        public bool Purchasable;
        public int Stock;
        public int TargetStock;
        public int MaxStock;
        public int BuyPrice;
        public int SellPrice;
        /// <summary>-1 below base price, 0 at it, 1 above it (Market.Trend).</summary>
        public int Trend;
    }

    /// <summary>One row of barrkbot_cargo_traders.json, before it becomes JSON.</summary>
    public sealed class TraderExportRow
    {
        public string PlayerKey = "";
        public string Name = "";
        public long CoinsSpent;
        public long CoinsEarned;
        public int DealsSettled;
        public long ItemsBought;
        public long ItemsSold;
    }

    /// <summary>One row of barrkbot_cargo_visits.json, before it becomes JSON.</summary>
    public sealed class VisitExportRow
    {
        public int VisitId;
        public string Pilot = "";
        public DateTime StartedAtUtc;
        public DateTime EndedAtUtc;
        public double DurationSeconds;
        public int Takings;
        public string EndedReason = "";
    }

    /// <summary>
    /// The BarrkBOT export's payload shaping (BARRKBOT_CONTRACT.md): turns the live Market, the
    /// session's TraderLedger and its VisitHistory into the rows the three barrkbot_cargo_*.json files
    /// carry, and the leaderboards the v4 rollover's "hard rule" requires when a collection's rows are
    /// split across parts. PURE: no JSON, no file I/O, no Unity -- Server/BarrkBotExport.cs renders
    /// these into JSON, measures their real rendered width and writes them; this file only decides what
    /// the numbers are, which is exactly the part a mutation can silently get backwards.
    /// </summary>
    public static class BarrkExport
    {
        // ---- rows -----------------------------------------------------------------------------------

        /// <summary>Every catalogue entry with its live stock and price, in the market's own order.</summary>
        public static List<MarketExportRow> MarketRows(Market market)
        {
            var rows = new List<MarketExportRow>();
            if (market == null) return rows;
            foreach (MarketItem it in market.Items)
            {
                rows.Add(new MarketExportRow
                {
                    Prefab = it.Prefab,
                    // The kind he TRADES it as right now: on the rotating shelf (2026-09-08) an entry is a Ware
                    // only while it is on this period's shelf, and the export says what a player would see.
                    Kind = market.KindOf(it) == EntryKind.Ware ? "Ware" : "Want",
                    Purchasable = market.KindOf(it) == EntryKind.Ware,
                    Stock = it.Stock,
                    TargetStock = it.Entry.TargetStock,
                    MaxStock = it.Entry.MaxStock,
                    BuyPrice = market.Charge(it),
                    SellPrice = market.Pays(it),
                    Trend = market.Trend(it),
                });
            }
            return rows;
        }

        /// <summary>Every player who has had at least one deal settle this session, in ledger order.</summary>
        public static List<TraderExportRow> TraderRows(TraderLedger ledger)
        {
            var rows = new List<TraderExportRow>();
            if (ledger == null) return rows;
            foreach (KeyValuePair<string, TraderRow> kv in ledger.Rows)
            {
                TraderRow t = kv.Value;
                rows.Add(new TraderExportRow
                {
                    PlayerKey = kv.Key,
                    Name = t.Name ?? "",
                    CoinsSpent = t.CoinsSpent,
                    CoinsEarned = t.CoinsEarned,
                    DealsSettled = t.DealsSettled,
                    ItemsBought = t.ItemsBought,
                    ItemsSold = t.ItemsSold,
                });
            }
            return rows;
        }

        /// <summary>Every visit that has ended this session, NEWEST first (history keeps them oldest first; members ask about the last one).</summary>
        public static List<VisitExportRow> VisitRows(VisitHistory history)
        {
            var rows = new List<VisitExportRow>();
            if (history == null) return rows;
            IReadOnlyList<VisitRecord> src = history.Rows;
            for (int i = src.Count - 1; i >= 0; i--)
            {
                VisitRecord v = src[i];
                rows.Add(new VisitExportRow
                {
                    VisitId = v.VisitId,
                    Pilot = v.PilotName ?? "",
                    StartedAtUtc = v.StartedAtUtc,
                    EndedAtUtc = v.EndedAtUtc,
                    DurationSeconds = v.DurationSeconds,
                    Takings = v.Takings,
                    EndedReason = v.EndedReason ?? "",
                });
            }
            return rows;
        }

        // ---- leaders (the v4 "hard rule": ranked over the WHOLE roster, before any split) -----------

        public static List<LeaderEntry> MarketLeaders(List<MarketExportRow> rows, Func<MarketExportRow, double> field)
        {
            var candidates = new List<LeaderEntry>();
            if (rows != null && field != null)
                foreach (MarketExportRow r in rows)
                    candidates.Add(new LeaderEntry { Credit = r.Prefab, Value = field(r) });
            return BarrkRollover.TopN(candidates);
        }

        public static List<LeaderEntry> TraderLeaders(List<TraderExportRow> rows, Func<TraderExportRow, double> field)
        {
            var candidates = new List<LeaderEntry>();
            if (rows != null && field != null)
                foreach (TraderExportRow r in rows)
                    candidates.Add(new LeaderEntry { Credit = r.Name, Value = field(r) });
            return BarrkRollover.TopN(candidates);
        }

        public static List<LeaderEntry> VisitLeaders(List<VisitExportRow> rows, Func<VisitExportRow, double> field)
        {
            var candidates = new List<LeaderEntry>();
            if (rows != null && field != null)
                foreach (VisitExportRow r in rows)
                    candidates.Add(new LeaderEntry { Credit = r.Pilot, Value = field(r) });
            return BarrkRollover.TopN(candidates);
        }
    }
}
