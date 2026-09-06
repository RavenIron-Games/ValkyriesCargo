using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RavenIron.ValkyriesCargo.Core;
using RavenIron.ValkyriesCargo.Net;

namespace ValkyriesCargo.Tests
{
    /// <summary>
    /// Off-game harness for the pure-logic core. No test framework by design — a console
    /// program returning a nonzero exit code is enough, and adds no dependency to keep
    /// current.
    ///
    /// What it is for: the Catalogue parser that reads the backing config, and the
    /// integrity of the default item list against the shipped game data. Silent failures
    /// here — a misspelled prefab or a base price that drifts below its anchor — corrupt
    /// a world quietly if undetected.
    /// </summary>
    public static class Program
    {
        private static int _passed;
        private static int _failed;

        public static int Main()
        {
            Console.WriteLine("ValkyriesCargo — core tests\n");

            CatalogueDefaultTests();
            CatalogueValidationTests();
            CatalogueEdgeCaseTests();
            WireTests();
            MarketSnapshotTests();
            VisitSnapshotTests();
            DealTests();
            DealInboxTests();
            DemoMarketTests();
            CargoRpcTests();

            Console.WriteLine($"\n{_passed} passed, {_failed} failed.");
            return _failed == 0 ? 0 : 1;
        }

        // ---- harness -------------------------------------------------------------------

        private static void Check(bool condition, string what)
        {
            if (condition) { _passed++; return; }
            _failed++;
            Console.WriteLine($"  FAIL  {what}");
        }

        private static void Equal<T>(T expected, T actual, string what)
        {
            bool ok = EqualityComparer<T>.Default.Equals(expected, actual);
            if (ok) { _passed++; return; }
            _failed++;
            Console.WriteLine($"  FAIL  {what}\n          expected [{expected}]\n          actual   [{actual}]");
        }

        private static Exception Throws<TEx>(Action act, string what) where TEx : Exception
        {
            try { act(); }
            catch (TEx ex) { _passed++; return ex; }
            catch (Exception ex)
            {
                _failed++;
                Console.WriteLine($"  FAIL  {what}\n          expected {typeof(TEx).Name}, got {ex.GetType().Name}: {ex.Message}");
                return ex;
            }
            _failed++;
            Console.WriteLine($"  FAIL  {what}\n          expected {typeof(TEx).Name}, nothing was thrown");
            return null;
        }

        private static void Section(string name) => Console.WriteLine(name);

        // ---- helpers -------------------------------------------------------------------

        private static string FindItemsFile()
        {
            string start = AppContext.BaseDirectory;
            string dir = start;
            for (int depth = 0; depth < 10; depth++)
            {
                string candidate = Path.Combine(dir, "docs", "data", "items-valheim-2026-07-31.tsv");
                if (File.Exists(candidate))
                    return candidate;
                string parent = Path.GetDirectoryName(dir);
                if (parent == null || parent == dir)
                    break;
                dir = parent;
            }
            throw new FileNotFoundException(
                $"Cannot find items-valheim-2026-07-31.tsv by walking up from {start}");
        }

        private static Dictionary<string, (int stack, int value)> LoadItemsTable()
        {
            var result = new Dictionary<string, (int, int)>();
            string path = FindItemsFile();

            var lines = File.ReadAllLines(path);
            if (lines.Length < 1)
                throw new FormatException("Items file is empty");

            // Skip header (line 0: prefab, display, type, stack, weight, value, teleportable, token)
            for (int i = 1; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                string[] fields = line.Split('\t');
                if (fields.Length < 6)
                    continue;

                string prefab = fields[0];
                if (!int.TryParse(fields[3], out int stack) || !int.TryParse(fields[5], out int value))
                    continue;

                result[prefab] = (stack, value);
            }

            return result;
        }

        // ---- tests -------------------------------------------------------------------

        private static void CatalogueDefaultTests()
        {
            Section("Catalogue.DefaultLine");

            var problems = new List<string>();
            var cat = Catalogue.Parse(Catalogue.DefaultLine, problems);

            Equal(0, problems.Count, "DefaultLine parses with zero problems");
            Equal(72, cat.Count, "DefaultLine has exactly 72 entries");

            int wareCount = cat.CountOf(EntryKind.Ware);
            int wantCount = cat.CountOf(EntryKind.Want);
            Equal(18, wareCount, "exactly 18 Ware entries");
            Equal(54, wantCount, "exactly 54 Want entries");
        }

        private static void CatalogueValidationTests()
        {
            Section("Catalogue validation");

            Dictionary<string, (int stack, int value)> items = null;
            string itemsPath = null;
            try
            {
                itemsPath = FindItemsFile();
                items = LoadItemsTable();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  FAIL  Could not load items table: {ex.Message}");
                _failed += 6;
                return;
            }

            var problems = new List<string>();
            var cat = Catalogue.Parse(Catalogue.DefaultLine, problems);

            // Test 1: Every prefab exists in the item table with stack >= 1
            int missingPrefabs = 0;
            foreach (var entry in cat.Entries)
            {
                if (!items.TryGetValue(entry.Prefab, out var item))
                {
                    Console.WriteLine($"  FAIL  Prefab '{entry.Prefab}' not found in items table");
                    missingPrefabs++;
                }
                else if (item.stack < 1)
                {
                    Console.WriteLine($"  FAIL  Prefab '{entry.Prefab}' has stack {item.stack}, need >= 1");
                    missingPrefabs++;
                }
            }
            if (missingPrefabs == 0)
                _passed++;
            else
                _failed++;

            // Test 2: Ware anchors — for every Ware whose value > 0, BasePrice * 0.7 ≈ value (within 1 coin rounding)
            int badAnchors = 0;
            foreach (var entry in cat.Entries)
            {
                if (entry.Kind != EntryKind.Ware)
                    continue;

                if (!items.TryGetValue(entry.Prefab, out var item) || item.value <= 0)
                    continue;

                // The design rounds value / 0.7 to get BasePrice. At target stock, he pays BasePrice * 0.7.
                // Check that he pays approximately the vanilla value (within 1 coin of rounding error).
                double paysAtTarget = entry.BasePrice * 0.7;
                if (Math.Abs(paysAtTarget - item.value) > 1.0)
                {
                    Console.WriteLine($"  FAIL  Ware '{entry.Prefab}' has vanilla value {item.value}: " +
                        $"Ingvar pays {paysAtTarget} at target stock (BasePrice {entry.BasePrice} * 0.7), " +
                        $"off by {Math.Abs(paysAtTarget - item.value)}");
                    badAnchors++;
                }
            }
            if (badAnchors == 0)
                _passed++;
            else
                _failed++;

            // Test 3: Max >= Target > 0 and Base >= 1
            int badRanges = 0;
            foreach (var entry in cat.Entries)
            {
                if (entry.BasePrice < 1)
                {
                    Console.WriteLine($"  FAIL  '{entry.Prefab}' has BasePrice {entry.BasePrice}, need >= 1");
                    badRanges++;
                }
                if (entry.TargetStock < 1)
                {
                    Console.WriteLine($"  FAIL  '{entry.Prefab}' has TargetStock {entry.TargetStock}, need >= 1");
                    badRanges++;
                }
                if (entry.MaxStock < entry.TargetStock)
                {
                    Console.WriteLine($"  FAIL  '{entry.Prefab}' has Max {entry.MaxStock} < Target {entry.TargetStock}");
                    badRanges++;
                }
            }
            if (badRanges == 0)
                _passed++;
            else
                _failed++;
        }

        private static void CatalogueEdgeCaseTests()
        {
            Section("Catalogue edge cases");

            // Test 1: Entry with 4 fields is reported and skipped
            var problems = new List<string>();
            var cat = Catalogue.Parse("Iron:25:20:60", problems);
            Check(problems.Count >= 1 && problems[0].Contains("Iron"),
                  "entry with 4 fields is reported");
            Check(cat.Find("Iron") == null,
                  "entry with 4 fields is skipped");

            // Test 2: Kind "ware" lower-case is accepted
            problems.Clear();
            cat = Catalogue.Parse("Test1:10:5:15:ware", problems);
            Check(problems.Count == 0, "lowercase 'ware' parses without problems");
            var entry = cat.Find("Test1");
            Check(entry != null && entry.Kind == EntryKind.Ware, "lowercase 'ware' is recognized");

            // Test 3: Duplicate prefab keeps first and reports one problem
            problems.Clear();
            cat = Catalogue.Parse("Test2:10:5:15:Ware, Test2:20:10:30:Want", problems);
            Check(problems.Count == 1, "duplicate prefab produces one problem");
            entry = cat.Find("Test2");
            Check(entry != null && entry.BasePrice == 10 && entry.Kind == EntryKind.Ware,
                  "duplicate keeps the first entry");

            // Test 4: Max < Target is reported
            problems.Clear();
            cat = Catalogue.Parse("BadMax:10:20:15:Want", problems);
            Check(problems.Count >= 1, "Max < Target is reported");
            Check(cat.Find("BadMax") == null, "Max < Target entry is skipped");

            // Test 5: Base 0 is reported
            problems.Clear();
            cat = Catalogue.Parse("BadBase:0:5:15:Want", problems);
            Check(problems.Count >= 1, "Base 0 is reported");
            Check(cat.Find("BadBase") == null, "Base 0 entry is skipped");

            // Test 6: Whitespace and trailing commas are tolerated
            problems.Clear();
            cat = Catalogue.Parse("  Iron:25:20:60:Ware  ,  Silver:40:12:36:Ware  , ", problems);
            Check(problems.Count == 0, "whitespace and trailing commas are tolerated");
            Check(cat.Count == 2, "correct entries are parsed");

            // Test 7: Null or empty line gives an empty catalogue with no problems
            problems.Clear();
            cat = Catalogue.Parse(null, problems);
            Check(cat.Count == 0 && problems.Count == 0, "null line produces empty catalogue with no problems");

            problems.Clear();
            cat = Catalogue.Parse("", problems);
            Check(cat.Count == 0 && problems.Count == 0, "empty line produces empty catalogue with no problems");

            // Test 8: IsPrefabName tests
            Check(Catalogue.IsPrefabName("RoundLog"), "IsPrefabName accepts 'RoundLog'");
            Check(Catalogue.IsPrefabName("FlametalNew"), "IsPrefabName accepts 'FlametalNew'");
            Check(Catalogue.IsPrefabName("Arrow_Fire"), "IsPrefabName accepts 'Arrow_Fire'");
            Check(!Catalogue.IsPrefabName("Bad Name"), "IsPrefabName rejects 'Bad Name'");
            Check(!Catalogue.IsPrefabName("a:b"), "IsPrefabName rejects 'a:b'");
            Check(!Catalogue.IsPrefabName(""), "IsPrefabName rejects empty string");
        }

        private static void WireTests()
        {
            Section("Wire parsing");

            // TryInt accepts invariant culture
            Check(Wire.TryInt("42", out int i) && i == 42, "TryInt parses '42'");
            Check(Wire.TryInt("-99", out i) && i == -99, "TryInt parses '-99'");
            Check(Wire.TryInt("  123  ", out i) && i == 123, "TryInt trims whitespace");
            Check(!Wire.TryInt("abc", out i), "TryInt rejects 'abc'");
            Check(!Wire.TryInt(null, out i), "TryInt rejects null");
            Check(!Wire.TryInt("", out i), "TryInt rejects empty string");

            // TryLong
            Check(Wire.TryLong("9223372036854775807", out long l) && l == long.MaxValue, "TryLong parses max long");
            Check(Wire.TryLong("-1", out l) && l == -1, "TryLong parses '-1'");
            Check(!Wire.TryLong("99999999999999999999", out l), "TryLong rejects overflow");

            // TryFloat accepts invariant culture, rejects NaN/Infinity
            Check(Wire.TryFloat("3.14", out float f) && Math.Abs(f - 3.14f) < 0.01f, "TryFloat parses '3.14'");
            Check(!Wire.TryFloat("NaN", out f), "TryFloat rejects NaN");
            Check(!Wire.TryFloat("Infinity", out f), "TryFloat rejects Infinity");
            Check(!Wire.TryFloat("-Infinity", out f), "TryFloat rejects -Infinity");

            // TryDouble
            Check(Wire.TryDouble("2.718", out double d) && Math.Abs(d - 2.718) < 0.01, "TryDouble parses '2.718'");
            Check(!Wire.TryDouble("NaN", out d), "TryDouble rejects NaN");
            Check(!Wire.TryDouble("Infinity", out d), "TryDouble rejects Infinity");

            // TryBool
            Check(Wire.TryBool("1", out bool b) && b == true, "TryBool parses '1' as true");
            Check(Wire.TryBool("true", out b) && b == true, "TryBool parses 'true'");
            Check(Wire.TryBool("TRUE", out b) && b == true, "TryBool parses 'TRUE' (case insensitive)");
            Check(Wire.TryBool("0", out b) && b == false, "TryBool parses '0' as false");
            Check(Wire.TryBool("false", out b) && b == false, "TryBool parses 'false'");
            Check(!Wire.TryBool("maybe", out b), "TryBool rejects 'maybe'");

            // IsToken rejects separators
            Check(Wire.IsToken("Iron"), "IsToken accepts 'Iron'");
            Check(!Wire.IsToken(""), "IsToken rejects empty string");
            Check(!Wire.IsToken(null), "IsToken rejects null");
            Check(!Wire.IsToken("bad;token"), "IsToken rejects strings with ';'");
            Check(!Wire.IsToken("bad|token"), "IsToken rejects strings with '|'");
            Check(!Wire.IsToken("bad:token"), "IsToken rejects strings with ':'");

            // Items and Fields
            string[] items = Wire.Items("a|b|c");
            Check(items.Length == 3 && items[0] == "a" && items[1] == "b" && items[2] == "c",
                  "Items splits 'a|b|c' into 3 parts");
            items = Wire.Items("");
            Check(items.Length == 0, "Items('') returns empty array");
            items = Wire.Items("single");
            Check(items.Length == 1 && items[0] == "single", "Items('single') returns one item");

            string[] fields = Wire.Fields("a;b;c;rest of string", 4);
            Check(fields.Length == 4 && fields[0] == "a" && fields[3] == "rest of string",
                  "Fields splits into exactly n parts, last takes the rest");
        }

        private static void MarketSnapshotTests()
        {
            Section("MarketSnapshot parsing");

            var problems = new List<string>();

            // Parse demo
            problems.Clear();
            MarketSnapshot snap = MarketSnapshot.Parse(MarketSnapshot.Demo, problems);
            Check(problems.Count == 0, "Demo parses with zero problems");
            Check(snap.Count == 72, "Demo has 72 rows");
            Check(snap.VisitId == 1, "Demo VisitId is 1");
            Check(snap.Purse == 800, "Demo Purse is 800");
            foreach (MarketRow row in snap.Rows)
                Check(row.Stock == row.Target && row.Buy > 0 && row.Sell == (int)Math.Round(row.Buy * 0.7f),
                      $"Row {row.Prefab}: stock==target, buy>0, sell==round(buy*0.7)");

            // Round trip
            string encoded = snap.Encode();
            problems.Clear();
            MarketSnapshot reparsed = MarketSnapshot.Parse(encoded, problems);
            Equal(snap.Encode(), reparsed.Encode(), "Encode(Parse(Demo)) == Demo");

            // Empty string
            problems.Clear();
            snap = MarketSnapshot.Parse("", problems);
            Check(problems.Count == 0 && snap.Count == 0, "Empty string parses to empty snapshot with no problems");

            // Version problem
            problems.Clear();
            snap = MarketSnapshot.Parse("v2;1;800;rows", problems);
            Check(problems.Count > 0 && problems[0].Contains("format"), "v2 format reports a problem");
            Check(snap.Count == 0, "v2 format gives an empty snapshot");

            // Row with 7 fields (wrong)
            problems.Clear();
            snap = MarketSnapshot.Parse("v1;1;800;Iron:Ware:10:10:20:38:26:26:extra", problems);
            Check(problems.Count > 0, "Row with 9 fields is reported");
            Check(snap.Count == 0, "Row with 9 fields is skipped");

            // Duplicate prefab
            problems.Clear();
            snap = MarketSnapshot.Parse("v1;1;800;Iron:Ware:10:10:20:38:26:0|Iron:Ware:5:5:10:19:13:0", problems);
            Check(problems.Count > 0 && problems[0].Contains("duplicate"), "Duplicate prefab is reported");
            Check(snap.Count == 1, "Duplicate prefab: first is kept");
            MarketRow ironRow = snap.Find("Iron");
            Check(ironRow != null && ironRow.Stock == 10, "First Iron entry (stock 10) is kept");

            // Find returns null for unknown
            Check(snap.Find("Unknown") == null, "Find returns null for unknown prefab");
        }

        private static void VisitSnapshotTests()
        {
            Section("VisitSnapshot parsing");

            var problems = new List<string>();

            // Parse demo
            problems.Clear();
            VisitSnapshot v = VisitSnapshot.Parse(VisitSnapshot.Demo, problems);
            Check(problems.Count == 0, "Demo parses with zero problems");
            Check(v.VisitId == 1, "Demo VisitId is 1");
            Check(v.Phase == VisitPhase.Trading, "Demo Phase is Trading");

            // Round trip
            string encoded = v.Encode();
            problems.Clear();
            VisitSnapshot v2 = VisitSnapshot.Parse(encoded, problems);
            Equal(v.Encode(), v2.Encode(), "Encode(Parse(Demo)) == Demo");

            // Phase case insensitivity
            problems.Clear();
            v = VisitSnapshot.Parse("v1;1;flying;1;;1:1;0;30;0;300;800;7", problems);
            Check(v.Phase == VisitPhase.Flying && problems.Count == 0, "Phase 'flying' lowercase parses");

            problems.Clear();
            v = VisitSnapshot.Parse("v1;1;TRADING;1;;1:1;0;30;0;300;800;7", problems);
            Check(v.Phase == VisitPhase.Trading && problems.Count == 0, "Phase 'TRADING' uppercase parses");

            // Unknown phase
            problems.Clear();
            v = VisitSnapshot.Parse("v1;1;unknown;1;;1:1;0;30;0;300;800;7", problems);
            Check(v.Phase == VisitPhase.None && problems.Count > 0, "Unknown phase reports and yields Phase None");

            // Remaining is never negative
            v = VisitSnapshot.Parse(VisitSnapshot.Demo, null);
            Check(v.Remaining(0) >= 0, "Remaining at worldTime 0 is non-negative");
            Check(v.Remaining(500) == 0, "Remaining past endWorldTime is clamped to 0");

            // Empty string
            problems.Clear();
            v = VisitSnapshot.Parse("", problems);
            Check(problems.Count == 0 && v.Phase == VisitPhase.None, "Empty string gives Phase None with no problems");

            // Wrong field count
            problems.Clear();
            v = VisitSnapshot.Parse("v1;1;trading;1;;1:1;0;30;0;300;800;7;extra;extra", problems);
            Check(problems.Count > 0, "More than 12 fields is reported");
        }

        private static void DealTests()
        {
            Section("Deal/DealLine encoding");

            var problems = new List<string>();

            // Buy deal (wanted only)
            Deal buy = new Deal { VisitId = 1, Nonce = 1, Wanted = new DealLine { Prefab = "Iron", Count = 5, UnitPriceSeen = 38 } };
            Check(buy.IsBuy, "IsBuy true for wanted only");
            Check(!buy.IsSell && !buy.IsBarter && !buy.IsEmpty, "IsBuy flags others false");
            string encoded = buy.Encode();
            problems.Clear();
            Deal reparsed = Deal.Parse(encoded, problems);
            Equal(buy.Encode(), reparsed.Encode(), "Buy deal round-trips byte-exact");

            // Sell deal (offered only)
            Deal sell = new Deal { VisitId = 1, Nonce = 2, Offered = new List<DealLine> { new DealLine { Prefab = "Flax", Count = 2, UnitPriceSeen = 4 } } };
            Check(sell.IsSell, "IsSell true for offered only");
            Check(!sell.IsBuy && !sell.IsBarter && !sell.IsEmpty, "IsSell flags others false");
            encoded = sell.Encode();
            problems.Clear();
            reparsed = Deal.Parse(encoded, problems);
            Equal(sell.Encode(), reparsed.Encode(), "Sell deal round-trips byte-exact");

            // Barter deal (both)
            Deal barter = new Deal
            {
                VisitId = 1, Nonce = 3,
                Wanted = new DealLine { Prefab = "Iron", Count = 3, UnitPriceSeen = 38 },
                Offered = new List<DealLine> { new DealLine { Prefab = "Flax", Count = 10, UnitPriceSeen = 4 } },
                CoinsOffered = 5
            };
            Check(barter.IsBarter, "IsBarter true for both");
            Check(!barter.IsBuy && !barter.IsSell && !barter.IsEmpty, "IsBarter flags others false");
            encoded = barter.Encode();
            problems.Clear();
            reparsed = Deal.Parse(encoded, problems);
            Equal(barter.Encode(), reparsed.Encode(), "Barter deal round-trips byte-exact");

            // OfferedValueSeen
            int offerValue = barter.OfferedValueSeen();
            Check(offerValue == 40, "OfferedValueSeen sums count * unit");

            // Count 0 is refused
            problems.Clear();
            Deal badCount = Deal.Parse("v1;1;0;Iron:0:38;;0", problems);
            Check(badCount == null && problems.Count > 0, "Deal line with count 0 is refused");

            // NewNonce is never 0 and differs
            long n1 = Deal.NewNonce();
            long n2 = Deal.NewNonce();
            Check(n1 != 0 && n2 != 0, "NewNonce is never 0");
            Check(n1 != n2, "Two NewNonce calls differ");
        }

        private static void DealInboxTests()
        {
            Section("DealInbox");

            DealInbox inbox = new DealInbox();

            // MarkApplied returns true first time
            Check(inbox.MarkApplied("d1"), "MarkApplied returns true first time");
            Check(!inbox.MarkApplied("d1"), "MarkApplied returns false the second time");

            // AlreadyApplied
            Check(inbox.AlreadyApplied("d1"), "AlreadyApplied true for existing id");
            Check(!inbox.AlreadyApplied("d2"), "AlreadyApplied false for new id");

            // Capacity 3 evicts oldest
            inbox = new DealInbox(3);
            Check(inbox.MarkApplied("d1"), "Add d1");
            Check(inbox.MarkApplied("d2"), "Add d2");
            Check(inbox.MarkApplied("d3"), "Add d3");
            Check(inbox.Count == 3, "Inbox has 3 items");
            Check(inbox.MarkApplied("d4"), "Add d4");
            Check(inbox.Count == 3, "Inbox still has 3 items (d1 evicted)");
            Check(!inbox.AlreadyApplied("d1"), "d1 (first) is gone");
            Check(inbox.AlreadyApplied("d2"), "d2 is still there");
            Check(inbox.AlreadyApplied("d3"), "d3 is still there");
            Check(inbox.AlreadyApplied("d4"), "d4 is there");

            // Encode/Parse round trip
            string encoded = inbox.Encode();
            DealInbox restored = DealInbox.Parse(encoded, 3);
            Check(restored.Count == inbox.Count, "Restored inbox has same count");
            for (int i = 0; i < inbox.Count; i++)
                Check(restored.Ids[i] == inbox.Ids[i], $"Restored inbox preserves order at position {i}");
        }

        private static void DemoMarketTests()
        {
            Section("DemoMarket.Settle");

            DemoMarket market = DemoMarket.Default();
            int playerCoins = 840;

            // Get known prefabs from the market
            MarketRow ware1 = null, want1 = null;
            foreach (var row in market.Market.Rows)
            {
                if (row.Kind == EntryKind.Ware && ware1 == null) ware1 = row;
                if (row.Kind == EntryKind.Want && want1 == null) want1 = row;
                if (ware1 != null && want1 != null) break;
            }

            // Empty deal
            Deal empty = new Deal { VisitId = 1, Nonce = 1 };
            DealResult r = market.Settle(empty, playerCoins);
            Check(r.Reason == DealReason.EmptyDeal, "Empty deal returns empty_deal");

            // Wrong VisitId
            Deal wrongVisit = new Deal { VisitId = 999, Nonce = 2, Wanted = new DealLine { Prefab = ware1.Prefab, Count = 1, UnitPriceSeen = ware1.Buy } };
            r = market.Settle(wrongVisit, playerCoins);
            Check(r.Reason == DealReason.StaleVisit, "Wrong VisitId returns stale_visit");

            // Duplicate nonce
            Deal first = new Deal { VisitId = 1, Nonce = 100, Wanted = new DealLine { Prefab = ware1.Prefab, Count = 1, UnitPriceSeen = ware1.Buy } };
            r = market.Settle(first, playerCoins);
            Check(r.Ok, "First deal with nonce 100 succeeds");
            Deal dup = new Deal { VisitId = 1, Nonce = 100, Wanted = new DealLine { Prefab = ware1.Prefab, Count = 1, UnitPriceSeen = ware1.Buy } };
            r = market.Settle(dup, playerCoins);
            Check(r.Reason == DealReason.Duplicate, "Same nonce twice returns duplicate");

            // Refused deal's nonce may be reused
            Deal refused = new Deal { VisitId = 1, Nonce = 101, Wanted = new DealLine { Prefab = "Unknown", Count = 1, UnitPriceSeen = 0 } };
            r = market.Settle(refused, playerCoins);
            Check(!r.Ok && r.Reason == DealReason.UnknownItem, "Unknown item is refused");
            Deal reused = new Deal { VisitId = 1, Nonce = 101, Wanted = new DealLine { Prefab = ware1.Prefab, Count = 1, UnitPriceSeen = ware1.Buy } };
            r = market.Settle(reused, playerCoins);
            Check(r.Ok, "Refused nonce 101 may be reused and accepted");

            // Buying a Want-kind prefab (only Ware can be bought)
            Deal buyWant = new Deal { VisitId = 1, Nonce = 102, Wanted = new DealLine { Prefab = want1.Prefab, Count = 1, UnitPriceSeen = 0 } };
            r = market.Settle(buyWant, playerCoins);
            Check(r.Reason == DealReason.UnknownItem, "Buying a Want-kind prefab returns unknown_item");

            // More than stock: find a ware and try to buy more than stock
            int nonce = 103;
            foreach (var testRow in market.Market.Rows)
            {
                if (testRow.Kind == EntryKind.Ware && testRow.Stock > 0)
                {
                    Deal overStock = new Deal { VisitId = 1, Nonce = nonce, Wanted = new DealLine { Prefab = testRow.Prefab, Count = testRow.Stock + 1, UnitPriceSeen = testRow.Buy } };
                    r = market.Settle(overStock, playerCoins);
                    Check(r.Reason == DealReason.SoldOut, "Buying more than stock returns sold_out");
                    break;
                }
            }

            // Wrong unit price seen
            nonce++;
            Deal wrongPrice = new Deal { VisitId = 1, Nonce = nonce, Wanted = new DealLine { Prefab = ware1.Prefab, Count = 1, UnitPriceSeen = ware1.Buy + 50 } };
            r = market.Settle(wrongPrice, playerCoins);
            Check(r.Reason == DealReason.PriceChanged && r.NewMarketState.Length > 0, "Wrong UnitPriceSeen returns price_changed with NewMarketState");

            // Offering more than max
            nonce++;
            Deal overMax = new Deal { VisitId = 1, Nonce = nonce, Offered = new List<DealLine> { new DealLine { Prefab = want1.Prefab, Count = want1.Max + 10, UnitPriceSeen = want1.Sell } } };
            r = market.Settle(overMax, playerCoins);
            Check(r.Reason == DealReason.OverMax, "Offering more than max returns over_max");

            // coins_short: find a ware and buy enough to exceed the 10 coins we're giving
            nonce++;
            int coinShortQuantity = Math.Max(1, (10 / Math.Max(1, ware1.Buy)) + 5); // Enough to exceed 10 coins
            Deal coinShort = new Deal { VisitId = 1, Nonce = nonce, Wanted = new DealLine { Prefab = ware1.Prefab, Count = coinShortQuantity, UnitPriceSeen = ware1.Buy } };
            r = market.Settle(coinShort, 10);
            Check(r.Reason == DealReason.CoinsShort, "Buying too much with too few coins returns coins_short");

            // purse_empty: sell enough of a want to exceed the merchant's purse
            nonce++;
            bool purseEmptyTested = false;
            foreach (var testWant in market.Market.Rows)
            {
                if (testWant.Kind == EntryKind.Want && testWant.Sell > 0 && testWant.Max > 0)
                {
                    int testPurse = market.Market.Purse;
                    // To trigger purse_empty: offeredValue > purse
                    // offeredValue = quantity * sell_price
                    // So: quantity * sell_price > purse
                    //     quantity > purse / sell_price
                    long minQuantity = (long)testPurse / testWant.Sell + 1;
                    int tryQuantity = (int)Math.Min(testWant.Max, minQuantity);

                    // If we can fit the quantity within max and it would overflow purse, try it
                    if (tryQuantity > 0 && (long)tryQuantity * testWant.Sell > testPurse)
                    {
                        Deal purseOverflow = new Deal { VisitId = 1, Nonce = nonce, Offered = new List<DealLine> { new DealLine { Prefab = testWant.Prefab, Count = tryQuantity, UnitPriceSeen = testWant.Sell } } };
                        r = market.Settle(purseOverflow, 10000);
                        if (r.Reason == DealReason.PurseEmpty)
                        {
                            Check(true, "Selling to exceed purse returns purse_empty");
                            purseEmptyTested = true;
                            break;
                        }
                    }
                    nonce++;
                }
            }
            if (!purseEmptyTested)
                Check(false, "Selling to exceed purse returns purse_empty");

            // Accepted buy reduces stock and raises purse
            nonce++;
            Deal acceptBuy = new Deal { VisitId = 1, Nonce = nonce, Wanted = new DealLine { Prefab = ware1.Prefab, Count = 1, UnitPriceSeen = ware1.Buy } };
            int stockBefore = ware1.Stock;
            int purseBefore = market.Market.Purse;
            r = market.Settle(acceptBuy, 10000);
            Check(r.Ok, "Accept buy succeeds");
            Check(ware1.Stock == stockBefore - 1, "Stock decremented");
            Check(market.Market.Purse == purseBefore + ware1.Buy, "Purse raised by price");
            Check(r.CoinsDelta < 0, "CoinsDelta is negative for player");

            // Accepted sell raises stock and lowers purse
            nonce++;
            stockBefore = want1.Stock;
            purseBefore = market.Market.Purse;
            Deal acceptSell = new Deal { VisitId = 1, Nonce = nonce, Offered = new List<DealLine> { new DealLine { Prefab = want1.Prefab, Count = 1, UnitPriceSeen = want1.Sell } } };
            r = market.Settle(acceptSell, 10000);
            Check(r.Ok, "Accept sell succeeds");
            Check(want1.Stock == stockBefore + 1, "Stock incremented");
            Check(market.Market.Purse == purseBefore - want1.Sell, "Purse lowered by value");
            Check(r.CoinsDelta > 0, "CoinsDelta is positive for player");

            // Barter: net is price minus offered value
            nonce++;
            Deal barter = new Deal
            {
                VisitId = 1, Nonce = nonce,
                Wanted = new DealLine { Prefab = ware1.Prefab, Count = 1, UnitPriceSeen = ware1.Buy },
                Offered = new List<DealLine> { new DealLine { Prefab = want1.Prefab, Count = 1, UnitPriceSeen = want1.Sell } }
            };
            r = market.Settle(barter, 10000);
            Check(r.Ok && r.ItemsToAdd.Count > 0 && r.ItemsToRemove.Count > 0, "Barter accepted with both add and remove");

            // Tick changes Buy/Sell and sets Trend
            int buyBefore = ware1.Buy;
            int sellBefore = ware1.Sell;
            market.Tick(ware1.Prefab, true);
            Check(ware1.Buy > buyBefore && ware1.Sell > sellBefore && ware1.Trend == 1, "Tick up increases prices and sets Trend to 1");
            market.Tick(ware1.Prefab, false);
            Check(ware1.Trend == -1, "Tick down sets Trend to -1");
        }

        private static void CargoRpcTests()
        {
            Section("CargoRpc end to end");

            CargoRpc.ResetForTests();
            Check(!CargoRpc.Ready, "After reset, Ready is false");

            // Send with no transport returns not_connected
            Deal deal = new Deal { VisitId = 1, Nonce = Deal.NewNonce(), Wanted = new DealLine { Prefab = "Iron", Count = 1, UnitPriceSeen = 38 } };
            DealResult result = null;
            CargoRpc.Send(deal, r => result = r);
            Check(result != null && result.Reason == DealReason.NotConnected, "Send with no transport returns not_connected");

            // UseDemo(true) publishes Visit and Market events
            bool visitFired = false, marketFired = false;
            CargoRpc.VisitChanged += v => { visitFired = true; Check(v.Phase == VisitPhase.Trading, "Visit event has Trading phase"); };
            CargoRpc.MarketChanged += m => { marketFired = true; Check(m.Count == 72, "Market event has 72 rows"); };

            CargoRpc.UseDemo(true);
            Check(CargoRpc.Ready, "After UseDemo(true), Ready is true");
            Check(visitFired, "VisitChanged event fired");
            Check(marketFired, "MarketChanged event fired");
            Check(CargoRpc.IsDemo, "CargoRpc.IsDemo is true");

            // Send of a valid buy accepts and fires MarketChanged
            marketFired = false;
            int marketEventCount = 0;
            Action<MarketSnapshot> countMarket = m => marketEventCount++;
            CargoRpc.MarketChanged += countMarket;

            MarketRow iron = CargoRpc.Market.Find("Iron");
            int stockBefore = iron.Stock;
            Deal buy = new Deal { VisitId = 1, Nonce = Deal.NewNonce(), Wanted = new DealLine { Prefab = "Iron", Count = 1, UnitPriceSeen = iron.Buy } };
            result = null;
            CargoRpc.Send(buy, r => result = r);
            Check(result != null && result.Ok, "Valid buy accepted");
            Check(CargoRpc.Market.Find("Iron").Stock == stockBefore - 1, "Market stock reduced");
            Check(marketEventCount > 0, "MarketChanged event fired after deal");

            CargoRpc.MarketChanged -= countMarket;

            // Same DeliveryId offered twice is answered as duplicate
            result = null;
            DealResult prevResult = null;
            // Create a fake transport that returns the same result twice
            var fakeTransport = new FakeTransport(new DealResult { Ok = true, DeliveryId = "same-id", Reason = DealReason.Ok, Nonce = 999, CoinsDelta = -38 });
            CargoRpc.UseTransport(fakeTransport);

            Deal deal1 = new Deal { VisitId = 1, Nonce = 999, Wanted = new DealLine { Prefab = "Iron", Count = 1, UnitPriceSeen = 38 } };
            result = null;
            CargoRpc.Send(deal1, r => { result = r; prevResult = result; });
            Check(result != null && result.Ok, "First send with same DeliveryId accepted");

            Deal deal2 = new Deal { VisitId = 1, Nonce = 998, Wanted = new DealLine { Prefab = "Iron", Count = 1, UnitPriceSeen = 38 } };
            result = null;
            CargoRpc.Send(deal2, r => result = r);
            Check(result != null && !result.Ok && result.Reason == DealReason.Duplicate, "Second send with same DeliveryId answered as duplicate");

            // UseDemo(false) makes Ready false (need to reset to demo first)
            CargoRpc.UseDemo(true);  // Back to demo
            Check(CargoRpc.Ready, "After UseDemo(true), Ready is true");
            CargoRpc.UseDemo(false);
            Check(!CargoRpc.Ready, "After UseDemo(false), Ready is false");

            // EndSession clears subscribers but keeps inbox
            int inboxCountBefore = CargoRpc.Inbox.Count;
            visitFired = false;
            marketFired = false;
            Action<VisitSnapshot> testVisit = v => { visitFired = true; };
            Action<MarketSnapshot> testMarket = m => { marketFired = true; };
            CargoRpc.VisitChanged += testVisit;
            CargoRpc.MarketChanged += testMarket;

            CargoRpc.EndSession();
            Check(!visitFired && !marketFired, "Subscribers cleared and not fired");
            Check(CargoRpc.Inbox.Count == inboxCountBefore, "Inbox count preserved after EndSession");

            // ResetForTests clears inbox
            CargoRpc.ResetForTests();
            Check(CargoRpc.Inbox.Count == 0, "Inbox cleared after ResetForTests");
        }
    }

    /// <summary>Fake transport for testing duplicate delivery handling.</summary>
    public sealed class FakeTransport : RavenIron.ValkyriesCargo.Net.ICargoTransport
    {
        private readonly DealResult _result;
        public bool Ready => true;
        public FakeTransport(DealResult result) { _result = result; }
        public void Open(int visitId) { }
        public void Close(int visitId) { }
        public void Dismiss(int visitId) { }
        public void Send(RavenIron.ValkyriesCargo.Core.Deal deal, System.Action<RavenIron.ValkyriesCargo.Core.DealResult> onAnswer)
        {
            onAnswer(_result);
        }
    }
}
