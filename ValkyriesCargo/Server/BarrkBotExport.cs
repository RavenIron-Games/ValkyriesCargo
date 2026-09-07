using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using RavenIron.ValkyriesCargo.Config;
using RavenIron.ValkyriesCargo.Core;

namespace RavenIron.ValkyriesCargo.Server
{
    /// <summary>
    /// Publishes this mod's live state as JSON for BarrkBOT (BARRKBOT_CONTRACT.md at the repo root; the
    /// authoritative contract is WindowsDEV/Discord-BarrkBOT/docs/MOD_EXPORT_CONTRACT.md v4).
    /// Server-authoritative like everything else here: the server is the one machine the market, the
    /// trader ledger and the visit history are ever complete on, so there is no client-side counting and
    /// no merge step. Three files under BepInEx/config/ValkyriesCargo/, each a keyed record collection
    /// with its own v4 rollover (Core/BarrkRollover.cs) once its rows outgrow the contract's row-width
    /// budget: barrkbot_cargo_market.json (the catalogue, always current -- 72 rows at the shipped prices
    /// do not fit one part, so this one splits from a fresh boot), barrkbot_cargo_traders.json (per
    /// player, new counters -- nothing accumulated these before this file), barrkbot_cargo_visits.json
    /// (one row per visit that has ended this session, newest first).
    ///
    /// Never the source of truth for anything (docs/DECISIONS-WUBARRK.md #4): the world sidecar is, and
    /// Core/SidecarThenMirror.cs is what makes that a property of the CALLER (VisitDirector.Tick), not of
    /// this file. This file's own job is just "never let a throw here reach the caller", a second,
    /// independent line of defence on top of that ordering (house rule 3: every patch body is its own
    /// try/catch, logging at most three times).
    /// </summary>
    public static class BarrkBotExport
    {
        public const int SchemaVersion = 4;

        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private static int _throws;

        public static int Writes { get; private set; }
        public static int Failures { get; private set; }
        public static DateTime LastWriteUtc { get; private set; }
        public static string LastError { get; private set; } = "";

        /// <summary>BepInEx/config/ValkyriesCargo -- depth 1 below the contract's required root, well inside its 3-folder cap.</summary>
        public static string ExportDir => System.IO.Path.Combine(Paths.ConfigPath, "ValkyriesCargo");

        /// <summary>
        /// Write all three files now. Gated on Server.BarrkBotExport (synced+locked, default on). Never
        /// throws: a failure is counted and logged (at most three times) and the previous files stand,
        /// exactly like the world sidecar's own MarketStore.Save.
        /// </summary>
        public static void Write(VisitDirector d)
        {
            if (!ModConfig.BarrkBotExport.Value || d == null) return;
            try
            {
                string dir = ExportDir;
                Directory.CreateDirectory(dir);
                DateTime now = DateTime.UtcNow;

                WriteMarket(d, dir, now);
                WriteTraders(d, dir, now);
                WriteVisits(d, dir, now);

                Writes++;
                LastWriteUtc = now;
            }
            catch (Exception ex)
            {
                Failures++;
                LastError = ex.GetType().Name + ": " + ex.Message;
                if (_throws++ < 3) ValkyriesCargo.Log.LogError("barrkbot export failed (non-fatal, the previous files stand): " + ex);
            }
        }

        // ---- barrkbot_cargo_market.json --------------------------------------------------------------

        private static void WriteMarket(VisitDirector d, string dir, DateTime now)
        {
            List<MarketExportRow> rows = BarrkExport.MarketRows(d.Market);
            int wares = 0, wants = 0;
            foreach (MarketExportRow r in rows) { if (r.Purchasable) wares++; else wants++; }

            var rowPairs = new List<KeyValuePair<string, object>>(rows.Count);
            foreach (MarketExportRow r in rows)
            {
                rowPairs.Add(new KeyValuePair<string, object>(r.Prefab, new Dictionary<string, object>
                {
                    ["kind"] = r.Kind,
                    ["purchasable"] = r.Purchasable,
                    ["stock"] = r.Stock,
                    ["target_stock"] = r.TargetStock,
                    ["max_stock"] = r.MaxStock,
                    ["buy_price"] = r.BuyPrice,
                    ["sell_price"] = r.SellPrice,
                    ["trend"] = r.Trend,
                    ["updated_at"] = Iso(now),
                }));
            }

            var totals = new Dictionary<string, object>
            {
                ["catalogue_entries"] = rows.Count,
                ["wares"] = wares,
                ["wants"] = wants,
                ["purse_coins"] = d.Market.Purse,
                ["active_visit_id"] = d.Session.Active ? (object)d.Session.VisitId : null,
            };

            var leaders = new Dictionary<string, List<LeaderEntry>>
            {
                ["buy_price"] = BarrkExport.MarketLeaders(rows, r => r.BuyPrice),
                ["sell_price"] = BarrkExport.MarketLeaders(rows, r => r.SellPrice),
                ["stock"] = BarrkExport.MarketLeaders(rows, r => r.Stock),
            };

            const string notes =
                "kind is Ware (he sells it and buys it back) or Want (he only buys). purchasable=false rows " +
                "(Wants) still carry buy_price -- it drives the trend arrow the same way for both kinds -- " +
                "but Ingvar does not sell a Want; only a Ware can actually be bought. sell_price is what he " +
                "pays right now for one unit; buy_price is what he charges right now for one unit; both move " +
                "with stock on the same supply-and-demand curve and must never be averaged or summed together. " +
                "trend is -1 (priced below its base), 0 (at base) or 1 (above base). Every catalogue entry " +
                "always has a row here -- unlike traders and visits below, the market is not session-scoped: " +
                "stock and purse persist across a restart in the world's own save file, and this file is a " +
                "live mirror of that, refreshed on the export cadence, never the source of truth.";

            WriteCollection(dir, "barrkbot_cargo_market", "market", rowPairs, totals, notes, leaders, now, null);
        }

        // ---- barrkbot_cargo_traders.json -------------------------------------------------------------

        private static void WriteTraders(VisitDirector d, string dir, DateTime now)
        {
            List<TraderExportRow> rows = BarrkExport.TraderRows(d.Traders);

            long coinsSpent = 0, coinsEarned = 0, itemsBought = 0, itemsSold = 0;
            int dealsSettled = 0;
            var rowPairs = new List<KeyValuePair<string, object>>(rows.Count);
            foreach (TraderExportRow r in rows)
            {
                coinsSpent += r.CoinsSpent; coinsEarned += r.CoinsEarned;
                itemsBought += r.ItemsBought; itemsSold += r.ItemsSold; dealsSettled += r.DealsSettled;
                rowPairs.Add(new KeyValuePair<string, object>(r.PlayerKey, new Dictionary<string, object>
                {
                    ["name"] = r.Name,
                    ["coins_spent"] = r.CoinsSpent,
                    ["coins_earned"] = r.CoinsEarned,
                    ["deals_settled"] = r.DealsSettled,
                    ["items_bought"] = r.ItemsBought,
                    ["items_sold"] = r.ItemsSold,
                }));
            }

            var totals = new Dictionary<string, object>
            {
                ["distinct_traders"] = rows.Count,
                ["total_coins_spent"] = coinsSpent,
                ["total_coins_earned"] = coinsEarned,
                ["total_deals_settled"] = dealsSettled,
                ["total_items_bought"] = itemsBought,
                ["total_items_sold"] = itemsSold,
            };

            var leaders = new Dictionary<string, List<LeaderEntry>>
            {
                ["coins_spent"] = BarrkExport.TraderLeaders(rows, r => r.CoinsSpent),
                ["coins_earned"] = BarrkExport.TraderLeaders(rows, r => r.CoinsEarned),
                ["deals_settled"] = BarrkExport.TraderLeaders(rows, r => r.DealsSettled),
                ["items_bought"] = BarrkExport.TraderLeaders(rows, r => r.ItemsBought),
                ["items_sold"] = BarrkExport.TraderLeaders(rows, r => r.ItemsSold),
            };

            const string notes =
                "A player appears here only after their first settled deal this session; nobody trading yet " +
                "is an empty map, not zeros for everyone. New counters -- nothing was accumulated before " +
                "this file existed. Never compare coins to items: coins_spent/coins_earned are money, " +
                "items_bought/items_sold are unit counts of different goods at different prices. Never " +
                "compare deals to units either: one settled deal can move many units, so deals_settled is " +
                "usually far smaller than either items total -- it counts transactions, they count units, " +
                "and the two must never be compared as if they measured the same thing. coins_spent minus " +
                "coins_earned IS meaningful, unlike the pairs above: it is a player's net coins paid to " +
                "Ingvar this session (negative means they are net ahead). Resets when the server restarts.";

            WriteCollection(dir, "barrkbot_cargo_traders", "traders", rowPairs, totals, notes, leaders, now, d.SessionStartedUtc);
        }

        // ---- barrkbot_cargo_visits.json --------------------------------------------------------------

        private static void WriteVisits(VisitDirector d, string dir, DateTime now)
        {
            List<VisitExportRow> rows = BarrkExport.VisitRows(d.VisitHistory);

            int totalTakings = 0;
            var rowPairs = new List<KeyValuePair<string, object>>(rows.Count);
            foreach (VisitExportRow r in rows)
            {
                totalTakings += r.Takings;
                rowPairs.Add(new KeyValuePair<string, object>(r.VisitId.ToString(CultureInfo.InvariantCulture), new Dictionary<string, object>
                {
                    ["pilot"] = r.Pilot,
                    ["started_at"] = Iso(r.StartedAtUtc),
                    ["ended_at"] = Iso(r.EndedAtUtc),
                    ["duration_seconds"] = Math.Round(r.DurationSeconds, 1),
                    ["takings_coins"] = r.Takings,
                    ["ended_reason"] = r.EndedReason,
                }));
            }

            var totals = new Dictionary<string, object>
            {
                ["visits_this_session"] = rows.Count,
                ["total_takings_coins"] = totalTakings,
            };

            var leaders = new Dictionary<string, List<LeaderEntry>>
            {
                ["takings_coins"] = BarrkExport.VisitLeaders(rows, r => r.Takings),
                ["duration_seconds"] = BarrkExport.VisitLeaders(rows, r => r.DurationSeconds),
            };

            const string notes =
                "One row per visit that has ENDED this session, newest first; a visit still running has no " +
                "row yet (see barrkbot_cargo_market.json's active_visit_id for that one). started_at is " +
                "derived (ended_at minus duration_seconds), not stamped independently, so a night slept " +
                "through mid-visit can shift it slightly -- duration_seconds itself is exact, the visit's " +
                "own world clock. takings_coins is never negative and 0 is a genuine 'nobody bought or sold " +
                "enough to move the purse', not missing data. Resets when the server restarts.";

            WriteCollection(dir, "barrkbot_cargo_visits", "visits", rowPairs, totals, notes, leaders, now, d.SessionStartedUtc);
        }

        // ---- the shared v4 envelope + rollover ---------------------------------------------------------

        /// <summary>
        /// Renders one collection's rows, paginating into "_2", "_3", ... parts when the whole roster does
        /// not fit BarrkRollover's row-width budget, shipping "&lt;collectionKey&gt;_leaders" in every part
        /// once there is more than one (the contract's "hard rule": a superlative must answer from any one
        /// part). Deletes any stale trailing parts left over from a roster that has since shrunk -- a dead
        /// file that still parses is worse than a missing one (BlightedHeart's contract).
        /// </summary>
        private static void WriteCollection(
            string dir, string baseName, string collectionKey,
            List<KeyValuePair<string, object>> rows,
            Dictionary<string, object> totals,
            string notes,
            Dictionary<string, List<LeaderEntry>> leaders,
            DateTime generatedAtUtc,
            DateTime? sessionStartedUtc)
        {
            var widths = new List<int>(rows.Count);
            foreach (KeyValuePair<string, object> row in rows)
            {
                // Core/Json.cs (the owner's decision 2026-09-07: no JSON library, no runtime dependency);
                // its compact form is shaped like Newtonsoft's, so the widths and the part boundaries stand.
                string rendered = Json.Write(new Dictionary<string, object> { [row.Key] = row.Value });
                widths.Add(rendered.Length);
            }

            List<List<int>> parts = BarrkRollover.Paginate(widths, BarrkRollover.DefaultRowBudgetChars);
            int partOf = parts.Count;

            for (int p = 0; p < partOf; p++)
            {
                var doc = new Dictionary<string, object>
                {
                    ["schema_version"] = SchemaVersion,
                    ["part"] = p + 1,
                    ["part_of"] = partOf,
                    ["generated_at"] = Iso(generatedAtUtc),
                    ["source"] = Source(),
                    ["intervals"] = new Dictionary<string, object> { ["write_seconds"] = (int)VisitDirector.ExportCadenceSeconds },
                };
                if (sessionStartedUtc.HasValue) doc["session_started_at"] = Iso(sessionStartedUtc.Value);
                doc[collectionKey + "_notes"] = notes;
                doc["totals"] = totals;
                if (partOf > 1 && leaders != null && leaders.Count > 0)
                {
                    var leadersDoc = new Dictionary<string, object>();
                    foreach (KeyValuePair<string, List<LeaderEntry>> kv in leaders) leadersDoc[kv.Key] = RenderLeaders(kv.Value);
                    doc[collectionKey + "_leaders"] = leadersDoc;
                }
                var collDoc = new Dictionary<string, object>();
                foreach (int idx in parts[p]) collDoc[rows[idx].Key] = rows[idx].Value;
                doc[collectionKey] = collDoc;

                string fileName = p == 0 ? baseName + ".json" : baseName + "_" + (p + 1).ToString(CultureInfo.InvariantCulture) + ".json";
                Save(System.IO.Path.Combine(dir, fileName), Json.Write(doc, indented: true));
            }

            CleanupStaleParts(dir, baseName, partOf);
        }

        private static List<object> RenderLeaders(List<LeaderEntry> entries)
        {
            var list = new List<object>(entries.Count);
            foreach (LeaderEntry e in entries) list.Add(new Dictionary<string, object> { ["name"] = e.Credit, ["value"] = e.Value });
            return list;
        }

        /// <summary>Remove any leftover "_N" part beyond what was just written (a roster that shrank back under one file's cap, or across several).</summary>
        private static void CleanupStaleParts(string dir, string baseName, int partOf)
        {
            for (int p = partOf + 1; p <= partOf + 10; p++)
            {
                string path = System.IO.Path.Combine(dir, baseName + "_" + p.ToString(CultureInfo.InvariantCulture) + ".json");
                if (File.Exists(path)) { try { File.Delete(path); } catch { /* next cycle tries again */ } }
            }
        }

        /// <summary>Written aside and swapped in, never in place, so a sweep never reads a half-written file (the contract's own atomicity rule; MarketStore.Save's discipline without the .bak rotation, which the sidecar does not need here).</summary>
        private static void Save(string path, string json)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, json, Utf8NoBom);
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }

        private static string Source() => ValkyriesCargo.PluginName + " " + ValkyriesCargo.PluginVersion;
        private static string Iso(DateTime utc) => utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    }
}
