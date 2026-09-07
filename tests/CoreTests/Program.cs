using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using RavenIron.ValkyriesCargo.Core;
using RavenIron.ValkyriesCargo.Net;
using RavenIron.ValkyriesCargo.Client.Terminal;

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
            MarketRulesTests();
            PriceCurveTests();
            MarketConstructionTests();
            StartVisitTests();
            RelaxTests();
            MarketSettleTests();
            MarketStateTests();
            NonceRingTests();
            SchedulerTests();
            VisitClockTests();
            DemoMarketTests();
            DemoMarketKnobTests();
            MarketReviewFixTests();
            VisitSessionTests();
            SidecarTests();
            OwedLedgerTests();
            SessionRowTests();
            BodyMotionTests();
            TrayModelTests();
            CargoRpcTests();
            FlightPlanTests();
            MerchantPlanTests();
            KeysTests();
            TraderLedgerTests();
            VisitHistoryTests();
            BarrkRolloverTests();
            BarrkExportTests();
            SidecarThenMirrorTests();

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

        private static void CollectionsEqual(IList<string> expected, IList<string> actual, string what)
        {
            bool same = expected.Count == actual.Count;
            for (int i = 0; same && i < expected.Count; i++) same = expected[i] == actual[i];
            Check(same, what);
        }

        // ---- helpers -------------------------------------------------------------------

        /// <summary>A market on the shipped catalogue and the shipped rules: what the server actually runs.</summary>
        private static Market NewMarket(double worldTime)
        {
            return new Market(Catalogue.Parse(Catalogue.DefaultLine, null), MarketRules.Default, worldTime);
        }

        /// <summary>An otherwise-eligible candidate. Each scheduler test breaks exactly one thing about him.</summary>
        private static Candidate Player(long uid, string name, float x, float z)
        {
            return new Candidate
            {
                Uid = uid, Name = name, X = x, Y = 30f, Z = z,
                BaseValue = 5, Rested = true, Comfort = 6, Alive = true, Ready = true,
            };
        }

        /// <summary>A scripted random source: these values in order, then the last one for ever.</summary>
        private static Func<double> Rolls(params double[] values)
        {
            int i = 0;
            return () => values[Math.Min(i++, values.Length - 1)];
        }

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

            // Forget: a delivery the pack refused after Send had already marked it must be applicable again
            Check(inbox.Forget("d1"), "Forget returns true for a marked id");
            Check(!inbox.AlreadyApplied("d1"), "AlreadyApplied false after Forget");
            Check(inbox.MarkApplied("d1"), "MarkApplied true again after Forget");
            Check(!inbox.Forget("nope") && !inbox.Forget(""), "Forget false for an unknown or empty id");
            Check(inbox.Count == 1 && inbox.Ids[0] == "d1", "Forget keeps order and count consistent");

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

        private static void MarketRulesTests()
        {
            Section("MarketRules.Sanitize");

            var problems = new List<string>();
            MarketRules ok = MarketRules.Default;
            ok.Sanitize(problems);
            Equal(0, problems.Count, "the shipped defaults are all in range and report nothing");
            Check(ok.Elasticity == 0.35 && ok.MinMultiplier == 0.4 && ok.MaxMultiplier == 3 &&
                  ok.Spread == 0.7 && ok.HalfLifeGameDays == 1 &&
                  ok.PurseCoins == 800 && ok.PurseCarryPercent == 50 && ok.PurseCapMultiple == 3,
                  "an in-range value is left exactly as configured");

            // Every float below its floor is clamped up and named.
            problems.Clear();
            MarketRules low = new MarketRules
            {
                Elasticity = 0.001, MinMultiplier = 0.001, MaxMultiplier = 0.5,
                Spread = 0.01, HalfLifeGameDays = 0.01,
            };
            low.Sanitize(problems);
            Equal(0.05, low.Elasticity, "Elasticity clamped up to its floor 0.05");
            Equal(0.05, low.MinMultiplier, "MinMultiplier clamped up to its floor 0.05");
            Equal(1, low.MaxMultiplier, "MaxMultiplier clamped up to its floor 1");
            Equal(0.1, low.Spread, "Spread clamped up to its floor 0.1");
            Equal(0.1, low.HalfLifeGameDays, "HalfLifeGameDays clamped up to its floor 0.1");
            Equal(5, problems.Count, "five values out of range, five problems reported");
            Check(problems[0].Contains("Elasticity") && problems[0].Contains("clamped up"),
                  "the report names the value and says which way it moved");

            // Every float above its ceiling is clamped down and named.
            problems.Clear();
            MarketRules high = new MarketRules
            {
                Elasticity = 5, MinMultiplier = 2, MaxMultiplier = 50,
                Spread = 2, HalfLifeGameDays = 100,
            };
            high.Sanitize(problems);
            Equal(1.5, high.Elasticity, "Elasticity clamped down to its ceiling 1.5");
            Equal(1, high.MinMultiplier, "MinMultiplier clamped down to its ceiling 1");
            Equal(10, high.MaxMultiplier, "MaxMultiplier clamped down to its ceiling 10");
            Equal(1, high.Spread, "Spread clamped down to its ceiling 1");
            Equal(30, high.HalfLifeGameDays, "HalfLifeGameDays clamped down to its ceiling 30");
            Equal(5, problems.Count, "five ceilings, five problems reported");
            Check(problems[2].Contains("MaxMultiplier") && problems[2].Contains("clamped down"),
                  "the report names the value and says which way it moved");

            // NaN and infinity are not numbers: each falls back to the floor with its own wording.
            problems.Clear();
            MarketRules nan = new MarketRules
            {
                Elasticity = double.NaN, MinMultiplier = double.NaN, MaxMultiplier = double.PositiveInfinity,
                Spread = double.NegativeInfinity, HalfLifeGameDays = double.NaN,
            };
            nan.Sanitize(problems);
            Equal(0.05, nan.Elasticity, "a NaN Elasticity falls back to the floor");
            Equal(0.05, nan.MinMultiplier, "a NaN MinMultiplier falls back to the floor");
            Equal(1, nan.MaxMultiplier, "an infinite MaxMultiplier falls back to the floor");
            Equal(0.1, nan.Spread, "a negative-infinity Spread falls back to the floor");
            Equal(0.1, nan.HalfLifeGameDays, "a NaN HalfLifeGameDays falls back to the floor");
            Equal(5, problems.Count, "five non-numbers, five problems reported");
            Check(problems[0].Contains("was not a number"), "a NaN is reported as not a number, not as a clamp");

            // The integer knobs.
            problems.Clear();
            MarketRules purse = new MarketRules { PurseCoins = -5, PurseCarryPercent = 150, PurseCapMultiple = 0 };
            purse.Sanitize(problems);
            Equal(0, purse.PurseCoins, "a negative PurseCoins is clamped to 0");
            Equal(100, purse.PurseCarryPercent, "PurseCarryPercent above 100 is clamped to 100");
            Equal(1, purse.PurseCapMultiple, "PurseCapMultiple below 1 is clamped to 1");
            Equal(3, problems.Count, "each integer clamp is reported");

            problems.Clear();
            MarketRules negCarry = new MarketRules { PurseCarryPercent = -10 };
            negCarry.Sanitize(problems);
            Equal(0, negCarry.PurseCarryPercent, "a negative PurseCarryPercent is clamped to 0");
            Equal(1, problems.Count, "and reported");

            // Sanitize is idempotent: rules already sanitized report nothing the second time.
            problems.Clear();
            low.Sanitize(problems);
            high.Sanitize(problems);
            nan.Sanitize(problems);
            purse.Sanitize(problems);
            Equal(0, problems.Count, "sanitizing already-sanitized rules reports nothing");

            // A null problems list is accepted: the game side does not always collect them.
            MarketRules quiet = new MarketRules { Elasticity = 99 };
            quiet.Sanitize(null);
            Equal(1.5, quiet.Elasticity, "Sanitize still clamps when nobody is collecting problems");
        }

        private static void PriceCurveTests()
        {
            Section("Market.PriceFor / PaysFor / Trend");

            MarketRules r = MarketRules.Default;   // elasticity 0.35, clamps 0.4 / 3.0, spread 0.7

            // At target the multiplier is 1^0.35 = 1, so the price is the base price.
            Equal(25, Market.PriceFor(25, 20, 20, r), "at target stock the price is the base price");
            Equal(300, Market.PriceFor(300, 2, 2, r), "at target, whatever the numbers");
            Equal(1, Market.PriceFor(1, 200, 200, r), "a base of 1 at target stays 1");

            // Stock 0 is priced through the max(1, stock) guard, never a division by zero.
            Equal(Market.PriceFor(25, 20, 1, r), Market.PriceFor(25, 20, 0, r),
                  "stock 0 is priced as stock 1: the max(1, stock) guard");
            Equal(71, Market.PriceFor(25, 20, 0, r),
                  "Iron sold out: 25 * (20/1)^0.35 = 25 * 2.8535 = 71.34 -> 71, still under the 3x ceiling");

            // The ceiling only bites when target/stock exceeds 3^(1/0.35) = 44.9.
            Equal(300, Market.PriceFor(100, 100, 1, r), "ceiling: (100/1)^0.35 = 5.01, clamped to 3.0 -> 300");
            Equal(300, Market.PriceFor(100, 100, 0, r), "sold out quotes min(base * MaxMultiplier, the curve): 300");

            // Flooded: the floor bites below 0.4^(1/0.35) = 0.073 of target.
            Equal(40, Market.PriceFor(100, 10, 1000, r), "floor: (10/1000)^0.35 = 0.1996, clamped to 0.4 -> 40");
            Equal(1, Market.PriceFor(1, 10, 1000, r), "a price is never below 1 coin, even at the floor");

            // Monotone in stock: more stock is never dearer.
            bool monotone = true;
            int previous = int.MaxValue;
            for (int stock = 1; stock <= 200; stock++)
            {
                int p = Market.PriceFor(25, 20, stock, r);
                if (p > previous) monotone = false;
                previous = p;
            }
            Check(monotone, "the price never rises as stock rises");

            // docs/CATALOGUE.md section 5, each worked by hand against the formula.
            Equal(22, Market.PriceFor(22, 30, 30, r), "IronScrap at target is its base 22");
            Equal(15, Market.PriceFor(22, 30, 90, r),
                  "IronScrap flooded to its max: 22 * (30/90)^0.35 = 22 * 0.68078 = 14.98 -> 15");
            Equal(382, Market.PriceFor(300, 2, 0, r),
                  "BlackCore sold out: the guard makes the ratio 2/1, so 300 * 2^0.35 = 300 * 1.27458 = 382.4 -> 382. " +
                  "CATALOGUE section 5 records 382: with a target of 2 the ratio never reaches the 3x ceiling.");

            // Amber, the anchored ware: he pays Haldor's 5 at target.
            Equal(7, Market.PriceFor(7, 30, 30, r), "Amber at target charges its base 7");
            Equal(5, Market.PaysFor(7, 30, 30, r),
                  "and he pays round(7 * 0.7) = round(4.9) = 5, Haldor's rate");
            Equal(5, Market.PriceFor(7, 30, 90, r),
                  "Amber flooded to 90: 7 * (30/90)^0.35 = 4.765 -> 5 charged");
            Equal(3, Market.PaysFor(7, 30, 90, r),
                  "so he pays round(7 * 0.68078 * 0.7) = round(3.34) = 3: the spread is applied to the UNROUNDED curve, " +
                  "once (CATALOGUE section 5). round(5 * 0.7) = 4 would squash the spread on every cheap row.");
            Equal(10, Market.PriceFor(7, 30, 10, r), "Amber down to 10: 7 * 3^0.35 = 10.28 -> 10 charged");
            Equal(7, Market.PaysFor(7, 30, 10, r),
                  "and he pays round(10 * 0.7) = 7 (CATALOGUE's 7.3 before rounding)");

            // PaysFor is the spread, and never below one coin.
            Equal(18, Market.PaysFor(25, 20, 20, r), "he pays round(25 * 0.7) = round(17.5) = 18, away from zero");
            Equal(1, Market.PaysFor(1, 200, 200, r), "he never pays less than 1 coin");
            Equal(1, Market.PaysFor(0, 20, 20, r), "not even for a base of 0");
            Equal(1, Market.PaysFor(-5, 20, 20, r), "nor for a nonsense negative base");

            // A zero target is guarded the same way as stock.
            Equal(Market.PriceFor(25, 1, 5, r), Market.PriceFor(25, 0, 5, r), "a target of 0 is priced as a target of 1");

            // Charge / Pays / Trend on a live market.
            Market m = NewMarket(0);
            MarketItem iron = m.Find("Iron");                 // base 25, target 20, max 60
            Equal(25, m.Charge(iron), "Charge reads the item's own base, target and stock");
            Equal(18, m.Pays(iron), "Pays is that charge through the spread");
            Equal(0, m.Trend(iron), "at target the trend is flat");
            iron.Stock = 15;
            Equal(28, m.Charge(iron), "scarce: 25 * (20/15)^0.35 = 27.65 -> 28");
            Equal(1, m.Trend(iron), "above base, the trend reads up");
            iron.Stock = 25;
            Equal(23, m.Charge(iron), "flooded: 25 * (20/25)^0.35 = 23.12 -> 23");
            Equal(-1, m.Trend(iron), "below base, the trend reads down");
            iron.Stock = 20;
            Equal(0, m.Trend(iron), "and back to flat at target");

            // A base of 1 can never round off its base, so its trend is honestly flat.
            MarketItem wood = m.Find("Wood");                 // base 1, target 200, max 600
            wood.Stock = 600;
            Equal(0, m.Trend(wood), "a base price of 1 cannot move, so Wood's trend stays flat");

            // ---- the Fair Market Act, 2026-09-07 (docs/DECISIONS-WUBARRK.md §2) ----------------------------
            // Unclamped, MaxMultiplier (3.0) x SpreadBuy (0.7) = 2.1 > 1: an empty Ware shelf pays MORE than a
            // full one charges, so buying a shelf out and selling it straight back pumps the purse for free
            // (docs/ECONOMY-SIM.md §9, "the round trip"). The fix clamps the buy-back multiplier at 1.0 for a
            // Ware only: PriceFor (what he CHARGES) and every Want are untouched.
            Section("Market: the Fair Market Act (2026-09-07)");

            MarketRules fma = MarketRules.Default;                       // FairMarketAct true by default
            Check(fma.FairMarketAct, "the rule ships ON: nobody gets the old exploit by accident");
            MarketRules noFma = new MarketRules { FairMarketAct = false };

            // (1) The round trip itself, played through Settle -- the same door the exploit used. Buy the
            // whole Iron shelf (base 25, target 20) in one deal at the full-shelf price, then sell the same
            // 20 back in one deal at the now-empty-shelf price. Pre-fix (docs/ECONOMY-SIM.md §9): charge 25,
            // pay back 50 -- a PROFIT of 500. The clamp leaves the charge at 25 (the scarcity signal survives
            // on the way out) but caps the pay-back at round(25 * 1.0 * 0.7) = 18, not 50.
            Market rt = NewMarket(0);
            rt.StartVisit(1, 0, 0);
            MarketItem rtIron = rt.Find("Iron");
            Equal(20, rtIron.Stock, "Iron starts at its target stock, 20");
            DealResult bought = rt.Settle(new Deal
            {
                VisitId = 1,
                Nonce = 1,
                Wanted = new DealLine { Prefab = "Iron", Count = 20, UnitPriceSeen = rt.Charge(rtIron) },
            }, 10000, 0);
            Check(bought.Ok, "buying the whole shelf in one deal succeeds");
            int spent = -bought.CoinsDelta;
            Equal(500, spent, "20 Iron at the full-shelf charge of 25 is 500 coins spent");
            Equal(0, rtIron.Stock, "the shelf is now empty");
            DealResult soldBack = rt.Settle(new Deal
            {
                VisitId = 1,
                Nonce = 2,
                Offered = new List<DealLine> { new DealLine { Prefab = "Iron", Count = 20, UnitPriceSeen = rt.Pays(rtIron) } },
            }, 0, 0);
            Check(soldBack.Ok, "selling the same 20 back in one deal succeeds");
            int receivedBack = soldBack.CoinsDelta;
            Equal(360, receivedBack, "he pays round(25 * 1.0 * 0.7) = 18 a unit clamped, 18 * 20 = 360");
            Check(receivedBack < spent, "a real LOSS on the round trip, not merely break-even: the pump is dead");

            // (2) A Ware flooded to its MAX (not merely back to target) pays the ordinary, unchanged
            // number: the clamp only bites above a 1.0 multiplier and never touches the flooded side.
            Equal(12, Market.PaysFor(25, 20, 60, EntryKind.Ware, fma),
                  "Iron flooded to its 60 max: mult 0.68078, round(25 * 0.68078 * 0.7) = 12, clamp or not");
            Equal(Market.PaysFor(25, 20, 60, noFma), Market.PaysFor(25, 20, 60, EntryKind.Ware, fma),
                  "below a 1.0 multiplier the clamped and legacy numbers agree exactly");

            // (3) PriceFor -- what he CHARGES -- still reaches the full 3.0x ceiling on an empty Ware
            // shelf: the Act touches only what he pays, never what he asks.
            Equal(300, Market.PriceFor(100, 100, 1, fma), "an empty shelf still charges the 3x ceiling, Act on");
            Equal(300, Market.PriceFor(100, 100, 1, noFma), "and the same with the Act off: PriceFor never changed");

            // (4) A real Want -- he never sells it back, so there is no round trip to protect it from --
            // pays the unclamped amount at scarce stock, Act on or off alike.
            Equal(23, Market.PaysFor(22, 30, 10, EntryKind.Want, fma),
                  "IronScrap (a Want) scarce at stock 10 of 30: mult 1.4689, round(22 * 1.4689 * 0.7) = 23, unclamped");
            Equal(Market.PaysFor(22, 30, 10, noFma), Market.PaysFor(22, 30, 10, EntryKind.Want, fma),
                  "a Want pays the same whether the Act is on or off");

            // (5) With the rule off, the old exploitable number returns exactly -- an owner who wants the
            // pre-2026-09-07 behaviour on a private server can still have it.
            Equal(50, Market.PaysFor(25, 20, 0, EntryKind.Ware, noFma),
                  "Act off: an empty Iron shelf pays the old, unclamped 50 -- the exact exploit number");
            Equal(50, Market.PaysFor(25, 20, 0, noFma), "and the legacy 4-argument overload still agrees: no clamp, ever");

            // (6) At a multiplier of exactly 1.0 the clamp has nothing to do: min(1.0, 1.0) is 1.0.
            Equal(1.0, Market.MultiplierFor(20, 20, fma), "target stock is exactly a 1.0 multiplier");
            Equal(Market.PaysFor(25, 20, 20, noFma), Market.PaysFor(25, 20, 20, EntryKind.Ware, fma),
                  "so at target stock a Ware pays the identical number clamped or not");

            // (7) MarketRules never crosses EncodeState/ApplyState or the sidecar: Core/Sidecar.cs has no
            // Rules row at all, and the director rebuilds Rules from Server.* config fresh every boot
            // through ModConfig.FillMarketRules -- ServerSync, not the world save, is what carries a changed
            // FairMarketAct to a rejoining client. Checked by inspection; nothing to round-trip. Prove it
            // stays that way: the encoded state is identical whichever way the rule is set.
            Market ruleOnMarket = new Market(Catalogue.Parse(Catalogue.DefaultLine, null), MarketRules.Default, 0);
            Market ruleOffMarket = new Market(Catalogue.Parse(Catalogue.DefaultLine, null), new MarketRules { FairMarketAct = false }, 0);
            Equal(ruleOnMarket.EncodeState(), ruleOffMarket.EncodeState(),
                  "EncodeState never mentions Rules, so FairMarketAct cannot leak into the sidecar");

            Section("Market: the purse carry is measured on the GROSS (2026-09-07)");

            // `Takings` is the NET, so a visit where players sold him as much as they bought carried
            // NOTHING forward -- and that is exactly the supplying server the catalogue was written for.
            // Over twenty simulated visits the carry cap engaged 19 times for a shopping server and 0
            // times for a selling one (docs/ECONOMY-SIM.md, "what looks off" 4).
            var carryProblems = new List<string>();
            Market carry = new Market(Catalogue.Parse(Catalogue.DefaultLine, carryProblems), MarketRules.Default, 0);
            carry.StartVisit(1, 0, 0);
            Equal(0, carry.Coined, "a fresh visit has taken nothing in");

            int purse0 = carry.Purse;
            // He SELLS the player 2 Iron: coins come in.
            MarketSnapshot cs = carry.Snapshot();
            int ironPrice = cs.Find("Iron").Buy;
            DealResult cbuy = carry.Settle(new Deal { VisitId = 1, Nonce = 1,
                Wanted = new DealLine { Prefab = "Iron", Count = 2, UnitPriceSeen = ironPrice } }, 100000, 0);
            Check(cbuy.Ok, "the player buys 2 Iron");
            Equal(2 * ironPrice, carry.Coined, "the gross counts what he was paid");
            Equal(2 * ironPrice, carry.Takings, "and with nothing paid out yet the net agrees");

            // Now he BUYS goods back for about the same money: the net collapses, the gross does not.
            int spentIn = carry.Coined;
            MarketSnapshot cs2 = carry.Snapshot();
            int woodPays = cs2.Find("Wood").Sell;
            int woodCount = Math.Max(1, (2 * ironPrice) / Math.Max(1, woodPays));
            DealResult csell = carry.Settle(new Deal { VisitId = 1, Nonce = 2,
                Offered = new List<DealLine> { new DealLine { Prefab = "Wood", Count = woodCount, UnitPriceSeen = woodPays } } }, 100000, 0);
            Check(csell.Ok, "and sells him " + woodCount + " Wood back");
            Equal(spentIn, carry.Coined, "the GROSS is unchanged by money going OUT -- that is the whole point");
            Check(carry.Takings < spentIn, "while the net has fallen, which is what used to be carried");
            Check(carry.Purse < purse0 + spentIn, "and the purse really did pay out");

            // The row has to survive a restart or the carry silently resets to nothing.
            var reload = new List<string>();
            Market carried = new Market(Catalogue.Parse(Catalogue.DefaultLine, reload), MarketRules.Default, 0);
            carried.ApplyState(carry.EncodeState(), reload);
            Equal(0, reload.Count, "the market state with a coined row applies with no problems");
            Equal(carry.Coined, carried.Coined, "and the gross survives the round trip through the sidecar");

            carry.StartVisit(2, 0, carry.Coined);
            Equal(0, carry.Coined, "a new visit starts the gross again at zero");

            Section("Catalogue: the four rows that used to pay firewood rates (2026-09-07)");

            // PaysFor's max(1, ...) swallows the whole curve below base 3, so RoundLog, FineWood,
            // Feathers and LeatherScraps paid EXACTLY what firewood pays -- though fine wood is 31
            // recipes and leather scraps 32 (docs/CATALOGUE.md 3).
            var catProblems = new List<string>();
            var entries = Catalogue.Parse(Catalogue.DefaultLine, catProblems);
            Equal(0, catProblems.Count, "the catalogue still parses clean");
            Market cm = new Market(entries, MarketRules.Default, 0);
            cm.StartVisit(1, 0, 0);
            foreach (string raised in new[] { "RoundLog", "FineWood", "Feathers", "LeatherScraps" })
            {
                MarketItem it = cm.Find(raised);
                Check(it != null && it.Entry.BasePrice == 3, raised + " is base 3, not 2");
                Check(cm.Pays(it) == 2, raised + " pays 2 at target stock, where it used to pay 1");
            }
            MarketItem firewood = cm.Find("Wood");
            Equal(1, cm.Pays(firewood), "and firewood still pays 1, which is the joke");

        }

        private static void MarketConstructionTests()
        {
            Section("Market construction");

            Market m = NewMarket(1234.5);
            Equal(72, m.Count, "the default catalogue builds 72 items");
            Equal(800, m.Purse, "the purse starts at MarketRules.PurseCoins");
            Equal(0, m.VisitId, "no visit has started yet");

            int offTarget = 0, badStamp = 0;
            foreach (MarketItem it in m.Items)
            {
                if (it.Stock != it.Entry.TargetStock) offTarget++;
                if (it.UpdatedWorldTime != 1234.5) badStamp++;
            }
            Equal(0, offTarget, "every item starts at its target stock");
            Equal(0, badStamp, "and stamped with the world time it was built at");

            Check(m.Find("Iron") != null, "Find returns the item for a known prefab");
            Check(m.Find("NoSuchPrefab") == null, "Find returns null for an unknown prefab");
            Check(m.Find("") == null, "Find returns null for an empty prefab name");
            Check(m.Find(null) == null, "Find returns null for a null prefab name");
            Check(m.Find("iron") == null, "Find is ordinal: 'iron' is not 'Iron'");

            MarketItem iron = m.Find("Iron");
            Check(iron.Prefab == "Iron" && iron.Kind == EntryKind.Ware, "the item carries its catalogue entry");
            Check(!iron.SoldOut && !iron.Full, "at target it is neither sold out nor full");
            iron.Stock = 0;
            Check(iron.SoldOut, "stock 0 is SOLD OUT");
            iron.Stock = iron.Entry.MaxStock;
            Check(iron.Full, "stock at max is full");
            iron.Stock = iron.Entry.TargetStock;

            Market empty = new Market(null, MarketRules.Default, 0);
            Equal(0, empty.Count, "a null catalogue gives an empty market");
            Equal(800, empty.Purse, "with the purse still filled");
            Check(empty.Find("Iron") == null, "and nothing to find in it");
            Equal(0, empty.Snapshot().Count, "its snapshot is empty");

            Market defaulted = new Market(Catalogue.Parse(Catalogue.DefaultLine, null), null, 0);
            Check(defaulted.Rules != null, "a null rules object falls back to the defaults");
            Equal(800, defaulted.Purse, "so the purse is the default 800");
            Equal(25, defaulted.Charge(defaulted.Find("Iron")), "and the default curve prices Iron at 25");

            // The snapshot is a value copy of the state, not a window onto it.
            MarketSnapshot snap = m.Snapshot();
            Equal(72, snap.Count, "the snapshot carries every row");
            Equal(m.Purse, snap.Purse, "and the purse");
            Equal(m.VisitId, snap.VisitId, "and the visit id");
            int stockWas = snap.Find("Iron").Stock;
            m.Find("Iron").Stock = 3;
            Equal(stockWas, snap.Find("Iron").Stock, "a row already handed out never changes underneath its holder");
            Equal(3, m.Snapshot().Find("Iron").Stock, "only a fresh snapshot shows the new stock");
        }

        private static void StartVisitTests()
        {
            Section("Market.StartVisit");

            Market m = NewMarket(0);

            m.StartVisit(3, 0, 0);
            Equal(3, m.VisitId, "StartVisit numbers the visit");
            Equal(800, m.Purse, "with no takings last visit the purse is PurseCoins");
            Equal(0, m.Takings, "and this visit's takings start at zero");
            Equal(3, m.Snapshot().VisitId, "the snapshot carries the new visit id");

            m.StartVisit(4, 0, 400);
            Equal(1000, m.Purse, "800 + PurseCarryPercent (50%) of 400 takings");

            m.StartVisit(5, 0, 10000);
            Equal(2400, m.Purse, "capped at PurseCapMultiple (3) x PurseCoins, whatever last visit took");

            m.StartVisit(6, 0, -50);
            Equal(800, m.Purse, "negative takings carry nothing");

            m.StartVisit(7, 0, 1);
            // Every rounding in Market.cs is away from zero, the carry included.
            Equal(801, m.Purse, "50% of 1 takings is 0.5, rounded away from zero: 1 coin carried");
            m.StartVisit(8, 0, 3);
            Equal(802, m.Purse, "50% of 3 is 1.5, so 2");
            m.StartVisit(9, 0, 5);
            Equal(803, m.Purse, "50% of 5 is 2.5, so 3, not the 2 of to-even");

            // The nonce ring is forgotten between visits.
            m.StartVisit(1, 0, 0);
            Deal a = new Deal { VisitId = 1, Nonce = 4242, Wanted = new DealLine { Prefab = "Iron", Count = 1, UnitPriceSeen = 25 } };
            Check(m.Settle(a, 1000, 0).Ok, "nonce 4242 is accepted in visit 1");
            Deal again = new Deal { VisitId = 1, Nonce = 4242, Wanted = new DealLine { Prefab = "Iron", Count = 1, UnitPriceSeen = 25 } };
            Equal(DealReason.Duplicate, m.Settle(again, 1000, 0).Reason, "and refused a second time inside the same visit");
            m.StartVisit(2, 0, 0);
            Deal next = new Deal { VisitId = 2, Nonce = 4242, Wanted = new DealLine { Prefab = "Iron", Count = 1, UnitPriceSeen = 25 } };
            Check(m.Settle(next, 1000, 0).Ok, "the same nonce is accepted again in visit 2: the ring was cleared");

            // Takings: what the purse gained, never negative.
            m.StartVisit(3, 0, 0);
            Equal(800, m.Purse, "a fresh visit refills the purse");
            Equal(0, m.Takings, "takings reset with the visit");
            Deal buy = new Deal { VisitId = 3, Nonce = 1, Wanted = new DealLine { Prefab = "Iron", Count = 1, UnitPriceSeen = 26 } };
            Check(m.Settle(buy, 1000, 0).Ok, "a third Iron sold, at 26 now the shelf is down to 18");
            Equal(826, m.Purse, "the purse takes the price");
            Equal(26, m.Takings, "takings are the coins the purse gained this visit");

            m.StartVisit(4, 0, 0);
            Deal sell = new Deal { VisitId = 4, Nonce = 2, Offered = new List<DealLine> { new DealLine { Prefab = "Wood", Count = 10, UnitPriceSeen = 1 } } };
            Check(m.Settle(sell, 1000, 0).Ok, "he buys 10 Wood at 1 apiece");
            Equal(790, m.Purse, "which comes out of the purse");
            Equal(0, m.Takings, "takings never go negative");

            // The delivery sequence restarts with the visit.
            m.StartVisit(9, 0, 0);
            Deal d9 = new Deal { VisitId = 9, Nonce = 3, Wanted = new DealLine { Prefab = "Honey", Count = 1, UnitPriceSeen = 2 } };
            Equal("v-9-1", m.Settle(d9, 1000, 0).DeliveryId, "the delivery id names the visit and restarts at 1");
        }

        private static void RelaxTests()
        {
            Section("Market.Relax");

            Market m = NewMarket(0);
            MarketItem iron = m.Find("Iron");        // target 20

            // Half a game day at a one-game-day half-life closes 1 - 0.5^0.5 = 29.29% of the gap.
            iron.Stock = 10; iron.UpdatedWorldTime = 0;
            m.Relax(900);
            Equal(13, iron.Stock, "900 s is half a game day: round(10 * 0.2929) = 3, so 10 -> 13");
            Equal(900.0, iron.UpdatedWorldTime, "and the item's own clock advances to the relaxed time");

            // A whole game day closes half the gap.
            iron.Stock = 10; iron.UpdatedWorldTime = 0;
            m.Relax(1800);
            Equal(15, iron.Stock, "1800 s is one half-life: half of a gap of 10, so 10 -> 15");

            iron.Stock = 30; iron.UpdatedWorldTime = 0;
            m.Relax(1800);
            Equal(25, iron.Stock, "a surplus relaxes downward by the same half: 30 -> 25");

            // A gap of one unit still closes: rounding is away from zero, so 0.5 becomes 1.
            iron.Stock = 19; iron.UpdatedWorldTime = 0;
            m.Relax(1800);
            Equal(20, iron.Stock, "a gap of 1 closes within one half-life: round(0.5) away from zero is 1");
            iron.Stock = 21; iron.UpdatedWorldTime = 0;
            m.Relax(1800);
            Equal(20, iron.Stock, "and the same from above");

            // Never overshoots, and stays put once it arrives.
            iron.Stock = 0; iron.UpdatedWorldTime = 0;
            m.Relax(1800 * 20);
            Equal(20, iron.Stock, "twenty game days away relaxes to target and no further");
            m.Relax(1800 * 40);
            Equal(20, iron.Stock, "and at target it stays at target");
            iron.Stock = 60; iron.UpdatedWorldTime = 0;
            m.Relax(1800 * 20);
            Equal(20, iron.Stock, "a full shelf relaxes down to target and stops there");

            // The overshoot clamp itself. With a sane half-life the fraction stays in [0, 1], so the move
            // can never exceed the gap and the clamp is unreachable. Market takes its rules AS GIVEN --
            // it never calls Sanitize -- so a config that skipped sanitising can hand it a NEGATIVE
            // half-life, which makes 1 - 0.5^(days / halfLife) fall below -1 and the raw move overshoot,
            // backwards and without limit. The clamp is what keeps the promise "never overshoots".
            Market hostile = new Market(Catalogue.Parse(Catalogue.DefaultLine, null),
                                        new MarketRules { HalfLifeGameDays = -1f }, 0);
            MarketItem h = hostile.Find("Iron");                      // target 20
            h.Stock = 10; h.UpdatedWorldTime = 0;
            hostile.Relax(3600);
            Equal(20, h.Stock, "two days at a -1 half-life computes a move of -30 on a gap of +10: clamped to the gap, 10 -> 20, not -20");
            h.Stock = 30; h.UpdatedWorldTime = 0;
            hostile.Relax(3600);
            Equal(20, h.Stock, "and the same from a surplus: a move of +30 on a gap of -10 is clamped, 30 -> 20, not 60");

            // Each item drifts from its OWN timestamp.
            Market m2 = NewMarket(0);
            MarketItem a = m2.Find("Iron"), b = m2.Find("Bronze");   // both target 20
            a.Stock = 10; a.UpdatedWorldTime = 0;
            b.Stock = 10; b.UpdatedWorldTime = 900;
            m2.Relax(1800);
            Equal(15, a.Stock, "Iron, last touched at 0, has had a whole half-life: 10 -> 15");
            Equal(13, b.Stock, "Bronze, last touched at 900, has had half of one: 10 -> 13");

            // A deal stamps only the lines it touched; everything else keeps its older clock.
            Market m3 = NewMarket(0);
            m3.StartVisit(1, 0, 0);
            MarketItem ironD = m3.Find("Iron"), bronzeD = m3.Find("Bronze");
            bronzeD.Stock = 10;                                       // scarce, and its clock still reads 0
            Deal buy = new Deal { VisitId = 1, Nonce = 1, Wanted = new DealLine { Prefab = "Iron", Count = 10, UnitPriceSeen = 25 } };
            Check(m3.Settle(buy, 10000, 900).Ok, "ten Iron bought at world time 900");
            Equal(900.0, ironD.UpdatedWorldTime, "the traded line's clock is reset to the deal's time");
            Equal(0.0, bronzeD.UpdatedWorldTime, "a line nobody touched keeps its own, older, timestamp");
            m3.Relax(1800);
            Equal(13, ironD.Stock, "Iron drifts from 900: 29% of a gap of 10, so 10 -> 13");
            Equal(15, bronzeD.Stock, "Bronze drifts from 0: half of a gap of 10, so 10 -> 15");

            // dt <= 0 is a no-op.
            Market m4 = NewMarket(1000);
            MarketItem it4 = m4.Find("Iron");
            it4.Stock = 10;
            m4.Relax(1000);
            Equal(10, it4.Stock, "relaxing to the same world time moves nothing");
            m4.Relax(500);
            Equal(10, it4.Stock, "and a clock that ran backwards moves nothing");

            // A half-life of half a day moves half the gap in half a day.
            Market fast = new Market(Catalogue.Parse(Catalogue.DefaultLine, null),
                                     new MarketRules { HalfLifeGameDays = 0.5f }, 0);
            MarketItem f = fast.Find("Iron");
            f.Stock = 10; f.UpdatedWorldTime = 0;
            fast.Relax(900);
            Equal(15, f.Stock, "at a half-day half-life, half a day is one half-life: 10 -> 15");
        }

        private static void MarketSettleTests()
        {
            Section("Market.Settle (the real market, in the server's refusal order)");

            var problems = new List<string>();
            Market m = new Market(Catalogue.Parse(Catalogue.DefaultLine, problems), MarketRules.Default, 0);
            Equal(0, problems.Count, "the market is built from the default catalogue with no problems");
            m.StartVisit(7, 0, 0);
            Equal(7, m.VisitId, "visit 7 is open");
            Equal(800, m.Purse, "with the default purse");

            // ---- the refusals, in the order design 3.4 lists them ------------------------

            DealResult r = m.Settle(null, 1000, 0);
            Check(!r.Ok && r.Reason == DealReason.Malformed, "a null deal is malformed");

            r = m.Settle(new Deal { VisitId = 7, Nonce = 1 }, 1000, 0);
            Equal(DealReason.EmptyDeal, r.Reason, "an empty deal is refused first of all");
            r = m.Settle(new Deal { VisitId = 999, Nonce = 1 }, 1000, 0);
            Equal(DealReason.EmptyDeal, r.Reason, "even when its visit is also stale: empty is checked first");

            r = m.Settle(new Deal { VisitId = 6, Nonce = 2, Wanted = new DealLine { Prefab = "NoSuchPrefab", Count = 1, UnitPriceSeen = 1 } }, 1000, 0);
            Equal(DealReason.StaleVisit, r.Reason, "a stale visit is refused before the item is even looked up");

            r = m.Settle(new Deal { VisitId = 7, Nonce = 3, Wanted = new DealLine { Prefab = "NoSuchPrefab", Count = 1, UnitPriceSeen = 1 } }, 1000, 0);
            Equal(DealReason.UnknownItem, r.Reason, "a wanted prefab the catalogue does not carry is unknown_item");

            r = m.Settle(new Deal { VisitId = 7, Nonce = 4, Wanted = new DealLine { Prefab = "Wood", Count = 1, UnitPriceSeen = 1 } }, 1000, 0);
            Equal(DealReason.UnknownItem, r.Reason, "he does not SELL a Want: buying Wood is unknown_item");

            r = m.Settle(new Deal { VisitId = 7, Nonce = 5, Wanted = new DealLine { Prefab = "Iron", Count = 0, UnitPriceSeen = 25 } }, 1000, 0);
            Equal(DealReason.BadCount, r.Reason, "a wanted count below 1 is bad_count");
            r = m.Settle(new Deal { VisitId = 7, Nonce = 6, Wanted = new DealLine { Prefab = "Iron", Count = -3, UnitPriceSeen = 25 } }, 1000, 0);
            Equal(DealReason.BadCount, r.Reason, "and so is a negative one");

            r = m.Settle(new Deal { VisitId = 7, Nonce = 7, Wanted = new DealLine { Prefab = "Iron", Count = 21, UnitPriceSeen = 999 } }, 1000, 0);
            Equal(DealReason.SoldOut, r.Reason, "more than he has on the shelf is sold_out, checked before the price");

            r = m.Settle(new Deal { VisitId = 7, Nonce = 8, Wanted = new DealLine { Prefab = "Iron", Count = 1, UnitPriceSeen = 24 } }, 1000, 0);
            Equal(DealReason.PriceChanged, r.Reason, "a wanted unit price that no longer matches is price_changed");
            Check(r.NewMarketState.Length > 0, "and the refusal carries the market as it is now");
            problems.Clear();
            MarketSnapshot fresh = MarketSnapshot.Parse(r.NewMarketState, problems);
            Equal(0, problems.Count, "the carried market parses cleanly");
            Equal(25, fresh.Find("Iron").Buy, "and quotes the price he would actually charge");

            r = m.Settle(new Deal { VisitId = 7, Nonce = 9, Offered = new List<DealLine> { new DealLine { Prefab = "NoSuchPrefab", Count = 1, UnitPriceSeen = 1 } } }, 1000, 0);
            Equal(DealReason.UnknownItem, r.Reason, "an offered prefab he does not know is unknown_item");

            r = m.Settle(new Deal { VisitId = 7, Nonce = 10, Offered = new List<DealLine> { new DealLine { Prefab = "Wood", Count = 0, UnitPriceSeen = 1 } } }, 1000, 0);
            Equal(DealReason.BadCount, r.Reason, "an offered count below 1 is bad_count");

            r = m.Settle(new Deal { VisitId = 7, Nonce = 11, Offered = new List<DealLine> { new DealLine { Prefab = "Wood", Count = 401, UnitPriceSeen = 999 } } }, 1000, 0);
            Equal(DealReason.OverMax, r.Reason, "Wood is 200 of a max of 600: the 401st is over_max, checked before the price");
            r = m.Settle(new Deal { VisitId = 7, Nonce = 12, Offered = new List<DealLine> { new DealLine { Prefab = "Wood", Count = 400, UnitPriceSeen = 999 } } }, 1000, 0);
            Equal(DealReason.PriceChanged, r.Reason, "exactly up to the max is allowed through to the price check");

            r = m.Settle(new Deal { VisitId = 7, Nonce = 13, Offered = new List<DealLine> { new DealLine { Prefab = "Wood", Count = 1, UnitPriceSeen = 5 } } }, 1000, 0);
            Equal(DealReason.PriceChanged, r.Reason, "an offered unit price that no longer matches is price_changed");
            Check(r.NewMarketState.Length > 0, "and it too carries the market as it is now");

            r = m.Settle(new Deal { VisitId = 7, Nonce = 777, Wanted = new DealLine { Prefab = "Iron", Count = 5, UnitPriceSeen = 25 } }, 100, 0);
            Equal(DealReason.CoinsShort, r.Reason, "5 Iron at 25 is 125; a player holding 100 coins is coins_short");

            // Coins are checked LAST, on the net, after every line has been priced. A player who is both
            // short of coins and looking at a stale price must be told the price moved -- that is the
            // refusal he can act on (the terminal re-quotes and he tries again), where coins_short would
            // send him away to sell something he may not need to sell.
            r = m.Settle(new Deal { VisitId = 7, Nonce = 14, Wanted = new DealLine { Prefab = "Iron", Count = 5, UnitPriceSeen = 24 } }, 100, 0);
            Equal(DealReason.PriceChanged, r.Reason, "stale price AND too few coins reads price_changed: the wanted line is checked before the coins");
            Check(r.NewMarketState.Length > 0, "and it still carries the market to re-quote from");

            // IronScrap: 30 of a max of 90, and he pays round(22 * 0.7) = 15. Sixty of them is 900, over his 800.
            r = m.Settle(new Deal { VisitId = 7, Nonce = 15, Offered = new List<DealLine> { new DealLine { Prefab = "IronScrap", Count = 60, UnitPriceSeen = 15 } } }, 1000, 0);
            Equal(DealReason.PurseEmpty, r.Reason, "60 scrap iron at 15 is 900, and the purse holds 800: purse_empty");

            // P11 (docs/TRUST-BOUNDARY.md section 2): the coins a client claims to hold are ADVISORY. The purse is
            // the server's number, so the same sell is refused purse_empty whatever the client says it carries.
            r = m.Settle(new Deal { VisitId = 7, Nonce = 1516, Offered = new List<DealLine> { new DealLine { Prefab = "IronScrap", Count = 60, UnitPriceSeen = 15 } } }, int.MaxValue, 0);
            Equal(DealReason.PurseEmpty, r.Reason, "the same sell from a client claiming int.MaxValue coins is still purse_empty: the client's coins are not the bound");

            // Not one of those refusals moved anything.
            Equal(800, m.Purse, "no refusal touched the purse");
            Equal(20, m.Find("Iron").Stock, "no refusal touched a shelf");
            Equal(200, m.Find("Wood").Stock, "nor the other side of one");
            Equal(0, m.Nonces.Count, "and no refusal spent its nonce");

            // ---- what he accepts ---------------------------------------------------------

            var ids = new HashSet<string>(StringComparer.Ordinal);

            // The nonce a refusal did not spend is reusable.
            Deal buy5 = new Deal { VisitId = 7, Nonce = 777, Wanted = new DealLine { Prefab = "Iron", Count = 5, UnitPriceSeen = 25 } };
            r = m.Settle(buy5, 1000, 0);
            Check(r.Ok, "nonce 777, refused for coins_short above, is accepted now the coins are there");
            Equal(DealReason.Ok, r.Reason, "with reason ok");
            Equal(-125, r.CoinsDelta, "the whole quantity is priced at the moment of the deal: 5 x 25 = 125 from the player");
            Equal(15, m.Find("Iron").Stock, "and the shelf falls by the whole quantity at once");
            Equal(925, m.Purse, "the purse takes all 125");
            Equal(28, m.Charge(m.Find("Iron")), "the NEXT quote is dearer: 25 * (20/15)^0.35 = 27.65 -> 28");
            Equal(28, m.Snapshot().Find("Iron").Buy, "and the snapshot says so too");
            Equal(1, r.ItemsToAdd.Count, "the answer tells the client what to add");
            Check(r.ItemsToAdd[0].Prefab == "Iron" && r.ItemsToAdd[0].Count == 5 && r.ItemsToAdd[0].UnitPriceSeen == 25,
                  "naming the prefab, the count and the price actually charged");
            Equal(0, r.ItemsToRemove.Count, "a buy removes nothing");
            Check(ids.Add(r.DeliveryId), "the delivery id is new");
            Check(Wire.IsToken(r.DeliveryId), "and carries none of ';' '|' ':'");

            r = m.Settle(new Deal { VisitId = 7, Nonce = 777, Wanted = new DealLine { Prefab = "Iron", Count = 1, UnitPriceSeen = 28 } }, 1000, 0);
            Equal(DealReason.Duplicate, r.Reason, "a nonce an ACCEPTED deal spent is refused as duplicate");
            Equal(15, m.Find("Iron").Stock, "and the duplicate changed nothing");

            // A pure sell: coins to the player, out of the purse.
            Deal sell = new Deal { VisitId = 7, Nonce = 20, Offered = new List<DealLine> { new DealLine { Prefab = "Wood", Count = 10, UnitPriceSeen = 1 } } };
            r = m.Settle(sell, 1000, 0);
            Check(r.Ok, "he buys 10 Wood");
            Equal(10, r.CoinsDelta, "CoinsDelta is positive for the player on a sell");
            Equal(210, m.Find("Wood").Stock, "the shelf rises by the whole quantity");
            Equal(915, m.Purse, "and the purse pays");
            Equal(0, r.ItemsToAdd.Count, "a sell adds nothing");
            Equal(1, r.ItemsToRemove.Count, "and removes the offered line");
            Check(ids.Add(r.DeliveryId), "a second, different delivery id");

            // Barter with change out of the purse: he owes more than he charges.
            Deal barterOut = new Deal
            {
                VisitId = 7, Nonce = 21,
                Wanted = new DealLine { Prefab = "Iron", Count = 2, UnitPriceSeen = 28 },
                Offered = new List<DealLine> { new DealLine { Prefab = "Wood", Count = 100, UnitPriceSeen = 1 } },
            };
            r = m.Settle(barterOut, 1000, 0);
            Check(r.Ok, "2 Iron at 28 against 100 Wood at 1");
            Equal(44, r.CoinsDelta, "net is 56 - 100 = -44, so 44 coins of change go to the player");
            Equal(871, m.Purse, "and that change comes out of the purse: 915 - 44");
            Equal(13, m.Find("Iron").Stock, "the wanted line falls");
            Equal(310, m.Find("Wood").Stock, "the offered line rises");
            Equal(1, r.ItemsToAdd.Count, "a barter both adds");
            Equal(1, r.ItemsToRemove.Count, "and removes");
            Check(ids.Add(r.DeliveryId), "a third, different delivery id");

            // Barter the other way: the player pays the difference.
            Deal barterIn = new Deal
            {
                VisitId = 7, Nonce = 22,
                Wanted = new DealLine { Prefab = "Iron", Count = 2, UnitPriceSeen = 29 },
                Offered = new List<DealLine> { new DealLine { Prefab = "Wood", Count = 5, UnitPriceSeen = 1 } },
            };
            r = m.Settle(barterIn, 1000, 0);
            Check(r.Ok, "2 Iron at 29 (the shelf is down to 13) against 5 Wood at 1");
            Equal(-53, r.CoinsDelta, "net is 58 - 5 = 53, paid by the player");
            Equal(924, m.Purse, "which the purse takes: 871 + 53");
            Equal(11, m.Find("Iron").Stock, "the wanted line falls again");
            Check(ids.Add(r.DeliveryId), "a fourth, different delivery id");

            Equal(4, ids.Count, "four accepted deals, four distinct delivery ids");
            foreach (string id in ids)
                Check(id.StartsWith("v-7-") && Wire.IsToken(id), "delivery id '" + id + "' names the visit and is wire-safe");

            // The whole answer survives the wire.
            problems.Clear();
            DealResult reparsed = DealResult.Parse(r.Encode(), problems);
            Equal(0, problems.Count, "an accepted answer re-parses with no problems");
            Equal(r.Encode(), reparsed.Encode(), "and round-trips byte for byte");

            // The coins test is on the NET, not on the price: goods can pay for goods. A penniless player
            // whose offer covers the whole charge is served, so the coins check must come after both
            // sides are priced, not before the wanted line.
            Market poor = new Market(Catalogue.Parse(Catalogue.DefaultLine, null), MarketRules.Default, 0);
            poor.StartVisit(9, 0, 0);
            int ironBuy = poor.Charge(poor.Find("Iron"));             // 25
            int woodPay = poor.Pays(poor.Find("Wood"));               // 1
            Equal(1, woodPay, "he pays 1 a log for Wood sitting at its target stock");
            DealResult pr = poor.Settle(new Deal
            {
                VisitId = 9, Nonce = 1,
                Wanted = new DealLine { Prefab = "Iron", Count = 1, UnitPriceSeen = ironBuy },
                Offered = new List<DealLine> { new DealLine { Prefab = "Wood", Count = ironBuy, UnitPriceSeen = woodPay } },
            }, 0, 0);
            Check(pr.Ok, "a player holding 0 coins swaps Wood for 1 Iron: the offer covers the charge exactly");
            Equal(0, pr.CoinsDelta, "and not a coin changes hands");
        }

        private static void MarketStateTests()
        {
            Section("Market.EncodeState / ApplyState");

            Market a = NewMarket(0);
            a.StartVisit(1, 0, 0);
            Check(a.Settle(new Deal { VisitId = 1, Nonce = 1, Wanted = new DealLine { Prefab = "Iron", Count = 1, UnitPriceSeen = 25 } }, 1000, 0).Ok,
                  "one accepted buy, so the purse is not simply the default");
            a.Find("Iron").Stock = 7;   a.Find("Iron").UpdatedWorldTime = 1234.5;
            a.Find("Wood").Stock = 555; a.Find("Wood").UpdatedWorldTime = 99.25;

            string rows = a.EncodeState();
            Check(rows.Contains("stock\tIron\t7\t1234.5"), "a stock row is prefab, stock and the item's own timestamp");
            Check(rows.Contains("purse\t825"), "and the purse is one row of its own");

            Market b = NewMarket(0);
            var problems = new List<string>();
            b.ApplyState(rows, problems);
            Equal(0, problems.Count, "state written by EncodeState applies with no problems");
            Equal(825, b.Purse, "the purse round-trips");
            Equal(7, b.Find("Iron").Stock, "stock round-trips");
            Equal(1234.5, b.Find("Iron").UpdatedWorldTime, "the timestamp round-trips, so drift resumes where it stopped");
            Equal(555, b.Find("Wood").Stock, "every row round-trips, not just the first");
            Equal(99.25, b.Find("Wood").UpdatedWorldTime, "with its own timestamp");
            Equal(a.EncodeState(), b.EncodeState(), "and the whole state round-trips byte for byte");

            // A catalogue edit between saves: the row is reported and dropped, never a crash.
            problems.Clear();
            b.ApplyState("stock\tNotInTheCatalogue\t5\t0", problems);
            Equal(1, problems.Count, "a row for a prefab the catalogue no longer carries is reported");
            Check(problems[0].Contains("unknown prefab") && problems[0].Contains("NotInTheCatalogue"),
                  "and the report names it");

            // A max lowered between saves: the stock is clamped, not refused.
            problems.Clear();
            b.ApplyState("stock\tIron\t9999\t0", problems);
            Equal(0, problems.Count, "a stock above max is not a parse problem");
            Equal(60, b.Find("Iron").Stock, "it is clamped to the item's max");

            // Junk.
            problems.Clear();
            b.ApplyState("this is not a row at all", problems);
            Equal(1, problems.Count, "a junk row is reported");
            Check(problems[0].Contains("unknown row ignored"), "and named as ignored");

            problems.Clear();
            int bronzeWas = b.Find("Bronze").Stock;
            b.ApplyState("stock\tBronze\t-1\t0", problems);
            Equal(1, problems.Count, "a negative stock is reported");
            Check(problems[0].Contains("did not parse"), "as a row that did not parse");
            Equal(bronzeWas, b.Find("Bronze").Stock, "and the item keeps the stock it had");

            problems.Clear();
            int purseWas = b.Purse;
            b.ApplyState("purse\tlots", problems);
            Equal(1, problems.Count, "a purse row that is not a number is reported");
            Check(problems[0].Contains("purse row did not parse"), "by name");
            Equal(purseWas, b.Purse, "and the purse is left alone");

            problems.Clear();
            b.ApplyState("purse\t-1", problems);
            Equal(1, problems.Count, "a negative purse is reported");
            Equal(purseWas, b.Purse, "and refused");

            // Empty is a no-op.
            problems.Clear();
            string before = b.EncodeState();
            b.ApplyState("", problems);
            b.ApplyState(null, problems);
            Equal(0, problems.Count, "an empty or null state reports nothing");
            Equal(before, b.EncodeState(), "and changes nothing");

            // A missing row keeps target stock.
            Market c = NewMarket(0);
            problems.Clear();
            c.ApplyState("stock\tIron\t3\t50", problems);
            Equal(0, problems.Count, "a partial state applies cleanly");
            Equal(3, c.Find("Iron").Stock, "the saved line takes its saved stock");
            Equal(20, c.Find("Bronze").Stock, "a line with no saved row keeps its target stock");
            Equal(800, c.Purse, "and with no purse row the purse is left as it was");

            // Line endings from a Windows sidecar.
            problems.Clear();
            c.ApplyState("stock\tIron\t9\t60\r\npurse\t111\r\n", problems);
            Equal(0, problems.Count, "carriage returns and a trailing newline are tolerated");
            Equal(9, c.Find("Iron").Stock, "the stock row applied");
            Equal(111, c.Purse, "and the purse row with it");
        }

        private static void NonceRingTests()
        {
            Section("NonceRing");

            var ring = new NonceRing(3);
            Check(ring.Add(1), "a nonce not seen before is accepted");
            Check(!ring.Add(1), "the same nonce a second time is refused");
            Check(ring.Contains(1), "and it is remembered");
            Check(!ring.Contains(99), "one never seen is not");
            Equal(1, ring.Count, "one nonce in the ring");

            Check(ring.Add(2) && ring.Add(3), "two more fill it to capacity");
            Equal(3, ring.Count, "three of three");
            Check(ring.Add(4), "a fourth is still accepted");
            Equal(3, ring.Count, "and the ring stays at its capacity");
            Check(!ring.Contains(1), "the oldest was evicted");
            Check(ring.Contains(2) && ring.Contains(3) && ring.Contains(4), "the newest three are kept");

            ring.Forget(3);
            Check(!ring.Contains(3), "a refused deal's nonce is forgotten");
            Equal(2, ring.Count, "and the ring shrinks");
            Check(ring.Add(3), "so the same nonce may be offered again");

            // Eviction order survives a Forget: the queue is rebuilt, not left with a hole.
            var ring2 = new NonceRing(3);
            ring2.Add(1); ring2.Add(2); ring2.Add(3);
            ring2.Forget(2);
            Check(ring2.Add(4) && ring2.Add(5), "two more after forgetting the middle one");
            Equal(3, ring2.Count, "still three");
            Check(!ring2.Contains(1), "and it is the OLDEST that went, not an arbitrary one");
            Check(ring2.Contains(3) && ring2.Contains(4) && ring2.Contains(5), "leaving the three newest");

            ring.Clear();
            Equal(0, ring.Count, "Clear empties the ring");
            Check(!ring.Contains(4), "nothing is remembered after it");
            Check(ring.Add(4), "and a nonce it had is accepted again");

            var tiny = new NonceRing(0);
            Check(tiny.Add(1), "a capacity of 0 still holds one");
            Check(tiny.Add(2) && !tiny.Contains(1), "and evicts on the next");

            Check(ring.Add(0), "nonce 0 is a value like any other to the ring");
            Check(!ring.Add(0), "and repeats like any other");
        }

        private static void SchedulerTests()
        {
            Section("Scheduler");

            var one = new List<Candidate> { Player(1, "Sigrun", 0f, 0f) };

            // ---- the interval ------------------------------------------------------------

            Scheduler s = new Scheduler(SchedulerRules.Default);
            s.Arm(0);
            Equal(1500.0, s.NextRollAt, "Arm puts the first roll one interval (25 minutes) out");
            Check(s.Tick(100, one, false, true, Rolls(0.0)) == null, "a tick before the interval does not roll at all");
            Check(s.Tick(1499.9, one, false, true, Rolls(0.0)) == null, "not even a tenth of a second early");
            Decision d = s.Tick(1500, one, false, true, Rolls(0.0, 0.0));
            Check(d != null, "a tick at the interval rolls");
            Equal(3000.0, s.NextRollAt, "and the next roll is one interval further on");

            Scheduler unarmed = new Scheduler(SchedulerRules.Default);
            Check(unarmed.Tick(500, one, false, true, Rolls(0.0)) == null,
                  "the first tick on an unarmed scheduler arms it rather than rolling");
            Equal(2000.0, unarmed.NextRollAt, "one interval after that first tick");
            Equal("no roll yet", unarmed.LastDecision, "and no decision has been made yet");

            // ---- the three holds ---------------------------------------------------------

            Scheduler off = new Scheduler(new SchedulerRules { Enabled = false });
            off.Arm(0);
            Decision dOff = off.Tick(1500, one, false, true, Rolls(0.0));
            Check(!dOff.Visit && dOff.Reason.StartsWith("held: Server.Enabled"), "Enabled false holds the roll and says so");

            Scheduler ev = new Scheduler(SchedulerRules.Default);
            ev.Arm(0);
            Decision dEv = ev.Tick(1500, one, true, true, Rolls(0.0));
            Check(!dEv.Visit && dEv.Reason.StartsWith("held: a random event"), "a random event already running holds the roll");

            Scheduler night = new Scheduler(SchedulerRules.Default);
            night.Arm(0);
            Decision dNight = night.Tick(1500, one, false, false, Rolls(0.0));
            Check(!dNight.Visit && dNight.Reason.StartsWith("held: night"), "night holds the roll while DaytimeOnly is on");
            Equal(dNight.Reason, night.LastDecision, "and the last decision is kept in words for cargo status");

            Scheduler anytime = new Scheduler(new SchedulerRules { DaytimeOnly = false });
            anytime.Arm(0);
            Check(anytime.Tick(1500, one, false, false, Rolls(0.0, 0.0)).Visit, "with DaytimeOnly off, night does not hold it");

            // Two holds at once: the order design 3.1 lists them in decides which one is reported, and
            // `cargo status` shows only that one. An event during the night must read as the event.
            Scheduler raidAtNight = new Scheduler(SchedulerRules.Default);
            raidAtNight.Arm(0);
            Decision dBoth = raidAtNight.Tick(1500, one, true, false, Rolls(0.0));
            Check(!dBoth.Visit, "a raid running at night holds the roll");
            Check(dBoth.Reason.StartsWith("held: a random event"),
                  "and names the event, not the night: the event hold is checked before the night hold");

            Scheduler offAtNight = new Scheduler(new SchedulerRules { Enabled = false });
            offAtNight.Arm(0);
            Check(offAtNight.Tick(1500, one, true, false, Rolls(0.0)).Reason.StartsWith("held: Server.Enabled"),
                  "and Server.Enabled is checked before either of them");

            // ---- who is not eligible, and how it is reported -----------------------------

            var flawed = new List<Candidate>();
            Candidate c;
            c = Player(11, "NotRested", 0f, 0f);   c.Rested = false;   flawed.Add(c);
            c = Player(12, "Uncomfy", 0f, 0f);     c.Comfort = 3;      flawed.Add(c);
            c = Player(13, "NoBase", 0f, 0f);      c.BaseValue = 0;    flawed.Add(c);
            c = Player(14, "InACrypt", 0f, 0f);    c.Y = 3500f;        flawed.Add(c);
            c = Player(15, "Dead", 0f, 0f);        c.Alive = false;    flawed.Add(c);
            c = Player(16, "Loading", 0f, 0f);     c.Ready = false;    flawed.Add(c);

            Scheduler nobody = new Scheduler(SchedulerRules.Default);
            nobody.Arm(0);
            Decision dn = nobody.Tick(1500, flawed, false, true, Rolls(0.0));
            Equal(0, dn.Eligible, "six players, none eligible");
            Check(!dn.Visit, "so no visit");
            Check(dn.Reason.StartsWith("no eligible player: "), "the reason opens with the cause");
            Check(dn.Reason.Contains("1 not rested"), "one is not rested");
            Check(dn.Reason.Contains("1 comfort < 4"), "one is below MinComfort 4");
            Check(dn.Reason.Contains("1 baseValue < 1"), "one is below MinBaseValue 1");
            Check(dn.Reason.Contains("1 in a dungeon"), "one is above the 3000 m dungeon line");
            Check(dn.Reason.Contains("1 dead"), "one is dead");
            Check(dn.Reason.Contains("1 not ready"), "one is still loading");

            var twoTired = new List<Candidate>();
            c = Player(17, "A", 0f, 0f); c.Rested = false; twoTired.Add(c);
            c = Player(18, "B", 0f, 0f); c.Rested = false; twoTired.Add(c);
            Scheduler counting = new Scheduler(SchedulerRules.Default);
            counting.Arm(0);
            Check(counting.Tick(1500, twoTired, false, true, Rolls(0.0)).Reason.Contains("2 not rested"),
                  "the counter counts, it does not just flag");

            Scheduler alone = new Scheduler(SchedulerRules.Default);
            alone.Arm(0);
            Check(alone.Tick(1500, new List<Candidate>(), false, true, Rolls(0.0)).Reason.EndsWith("nobody online"),
                  "an empty player list reads 'nobody online'");
            Check(alone.Tick(3000, null, false, true, Rolls(0.0)).Reason.EndsWith("nobody online"),
                  "and so does a null one");

            Scheduler cool = new Scheduler(SchedulerRules.Default);
            cool.Arm(0);
            cool.StampCooldown(21, 0f, 0f, 1500);
            Check(cool.OnPlayerCooldown(21, 1500), "a stamped player is on cooldown at once");
            Check(!cool.OnPlayerCooldown(21, 5100), "and off it one PlayerCooldownSeconds later");
            Check(cool.Tick(1500, new List<Candidate> { Player(21, "Sigrun", 0f, 0f) }, false, true, Rolls(0.0))
                      .Reason.Contains("1 on cooldown"),
                  "that player is counted as on cooldown");
            Check(cool.Tick(3000, new List<Candidate> { Player(22, "Bjorn", 10f, 0f) }, false, true, Rolls(0.0))
                      .Reason.Contains("1 near a base on cooldown"),
                  "and a different player 10 m from that base is counted as near one");

            Check(cool.NearBaseCooldown(59f, 0f, 3000), "59 m is inside CooldownRadius 60");
            Check(!cool.NearBaseCooldown(61f, 0f, 3000), "61 m is outside it");
            cool.Prune(5100);
            Check(!cool.OnPlayerCooldown(21, 5100), "Prune drops the expired player cooldown");
            Check(!cool.NearBaseCooldown(0f, 0f, 5100), "and the expired base cooldown with it");

            // ---- tickets -----------------------------------------------------------------

            var near = new List<Candidate> { Player(31, "A", 0f, 0f), Player(32, "B", 30f, 0f) };
            Equal(1, Scheduler.Tickets(near, 40f).Count, "two players 30 m apart are one town, one ticket");
            Equal(31L, Scheduler.Tickets(near, 40f)[0].Uid, "and the ticket is the first of them, in list order");
            var far = new List<Candidate> { Player(33, "A", 0f, 0f), Player(34, "B", 50f, 0f) };
            Equal(2, Scheduler.Tickets(far, 40f).Count, "two players 50 m apart are two tickets");
            Equal(0, Scheduler.Tickets(new List<Candidate>(), 40f).Count, "nobody is no tickets");

            // ---- the chance --------------------------------------------------------------

            Scheduler ch = new Scheduler(SchedulerRules.Default);
            ch.Arm(0);
            Check(ch.Tick(1500, one, false, true, Rolls(0.24, 0.0)).Visit,
                  "a roll of 0.24 is under EventChancePercent 25 and visits");

            Scheduler ch2 = new Scheduler(SchedulerRules.Default);
            ch2.Arm(0);
            Decision d25 = ch2.Tick(1500, one, false, true, Rolls(0.25));
            Check(!d25.Visit, "a roll of exactly 0.25 is NOT under 25 and does not visit");
            Check(d25.Reason.Contains("no visit"), "and says so");
            Equal(1, d25.Eligible, "the eligible count is still reported when only the chance failed");
            Equal(1, d25.Tickets, "and so is the ticket count");

            // ---- who gets it -------------------------------------------------------------

            var twoTowns = new List<Candidate> { Player(41, "First", 0f, 0f), Player(42, "Last", 500f, 0f) };

            Scheduler pickLast = new Scheduler(SchedulerRules.Default);
            pickLast.Arm(0);
            Decision dLast = pickLast.Tick(1500, twoTowns, false, true, Rolls(0.0, 0.99));
            Equal(2, dLast.Tickets, "two towns, two tickets");
            Check(dLast.Visit && dLast.Pilot.Uid == 42, "the SECOND random number picks: 0.99 of two tickets is the last");

            Scheduler pickFirst = new Scheduler(SchedulerRules.Default);
            pickFirst.Arm(0);
            Decision dFirst = pickFirst.Tick(1500, twoTowns, false, true, Rolls(0.0, 0.0));
            Check(dFirst.Visit && dFirst.Pilot.Uid == 41, "and 0.0 picks the first");

            Scheduler pickEdge = new Scheduler(SchedulerRules.Default);
            pickEdge.Arm(0);
            Decision dEdge = pickEdge.Tick(1500, twoTowns, false, true, Rolls(0.0, 1.0));
            Check(dEdge.Visit && dEdge.Pilot.Uid == 42, "a pick of 1.0 is clamped inside the list, never out of range");

            // ---- a visit stamps the cooldowns --------------------------------------------

            Scheduler stamped = new Scheduler(SchedulerRules.Default);
            stamped.Arm(0);
            Decision ds = stamped.Tick(1500, new List<Candidate> { Player(51, "Vik", 100f, 200f) }, false, true, Rolls(0.0, 0.0));
            Check(ds.Visit, "a visit is decided");
            Check(ds.Reason.StartsWith("visit: "), "and named in words");
            Check(stamped.OnPlayerCooldown(51, 1500), "the pilot is stamped at dispatch, not at success");
            Check(stamped.NearBaseCooldown(140f, 200f, 1500), "40 m from where he stood is within CooldownRadius 60");
            Check(!stamped.NearBaseCooldown(200f, 200f, 1500), "100 m from it is not");
            Check(!stamped.OnPlayerCooldown(52, 1500), "and nobody else is stamped");

            // ---- cargo visit -------------------------------------------------------------

            Scheduler f = new Scheduler(SchedulerRules.Default);
            f.Arm(0);
            Decision fOff = f.Force(0, one, false, true, 999);
            Check(!fOff.Visit && fOff.Reason.Contains("is not online"), "forcing a uid nobody is playing says so");

            Candidate tired = Player(61, "Sleepy", 0f, 0f);
            tired.Rested = false;
            Decision fBad = f.Force(0, new List<Candidate> { tired }, false, true, 61);
            Check(!fBad.Visit, "an ineligible player is not forced into a visit");
            Check(fBad.Reason.StartsWith("forced: ") && fBad.Reason.Contains("not eligible"),
                  "the refusal says it was a forced attempt");
            Check(fBad.Reason.Contains("1 not rested"), "and why he did not qualify");

            Scheduler fHold = new Scheduler(SchedulerRules.Default);
            Check(fHold.Force(0, one, true, true, 1).Reason.StartsWith("held: a random event"),
                  "a force still respects the holds: one random event at a time");

            Scheduler f0 = new Scheduler(new SchedulerRules { ChancePercent = 0f });
            f0.Arm(0);
            Decision fz = f0.Force(0, one, false, true, 1);
            Check(fz.Visit && fz.Pilot.Uid == 1, "an eligible player is forced to a visit at chance 0, long before the interval");
            Check(fz.Reason.StartsWith("forced visit: "), "and the reason says it was forced");
            Check(f0.OnPlayerCooldown(1, 0), "a forced visit stamps the cooldown like any other");
            Equal(1500.0, f0.NextRollAt, "and does not move the scheduled roll");
            Decision fAgain = f0.Force(100, one, false, true, 1);
            Check(fAgain.Visit, "a forced visit ignores the cooldown it just stamped: the admin asked, and milestone testing needs it");
            Scheduler fCount = new Scheduler(SchedulerRules.Default);
            var crowd = new List<Candidate> { Player(1, "Don", 0f, 0f), Player(2, "Far", 1000f, 0f), Player(3, "Wide", 2000f, 0f) };
            Decision fc = fCount.Force(0, crowd, false, true, 2);
            Check(fc.Visit && fc.Pilot.Uid == 2, "the named player is the one forced");
            Equal(3, fc.Eligible, "and the counters still count everyone online, so cargo status tells the truth");
            Equal(3, fc.Tickets, "three towns, three tickets");
            Candidate tiredForced = Player(4, "Yawn", 3000f, 0f); tiredForced.Rested = false;
            crowd.Add(tiredForced);
            Decision fWhy = fCount.Force(0, crowd, false, true, 4);
            Check(!fWhy.Visit && fWhy.Reason.StartsWith("forced: Yawn not eligible: not rested"), "a forced refusal names the forced player's own shortfall first");
            Check(fWhy.Reason.Contains("online: 1 not rested"), "and the crowd's summary after it");

            Candidate edge = Player(91, "Edge", 0f, 0f); edge.Y = 3000f;
            Scheduler sEdge = new Scheduler(SchedulerRules.Default); sEdge.Arm(0);
            Check(sEdge.Tick(1500, new List<Candidate> { edge }, false, true, Rolls(0.0)).Reason.Contains("1 in a dungeon"),
                  "y = 3000 exactly is not on the surface (design 3.1: eligible needs y < 3000)");
            Scheduler sHundred = new Scheduler(new SchedulerRules { ChancePercent = 100f }); sHundred.Arm(0);
            Check(sHundred.Tick(1500, one, false, true, Rolls(1.0, 0.0)).Visit, "an inclusive random source returning 1.0 cannot starve chance 100");
            Check(sHundred.LastDecision.EndsWith("1 eligible, 1 ticket(s)"), "the counters are invariant-culture ints");

            var srProblems = new List<string>();
            SchedulerRules wild = new SchedulerRules { MinComfort = -1, MinBaseValue = -1, IntervalSeconds = 0f, ChancePercent = 250f, PlayerCooldownSeconds = -1f, CooldownRadius = float.NaN, TownRadius = -5f };
            wild.Sanitize(srProblems);
            Equal(7, srProblems.Count, "every scheduler knob out of range is reported");
            Check(wild.MinComfort == 0 && wild.MinBaseValue == 0 && wild.IntervalSeconds == 10f && wild.ChancePercent == 100f &&
                  wild.PlayerCooldownSeconds == 0f && wild.CooldownRadius == 0f && wild.TownRadius == 0f, "and clamped into range");
            srProblems.Clear();
            wild.Sanitize(srProblems);
            Equal(0, srProblems.Count, "idempotent");
            SchedulerRules everyTick = new SchedulerRules { IntervalSeconds = 0f };
            new Scheduler(everyTick);
            Equal(10f, everyTick.IntervalSeconds, "the constructor sanitizes in place: an interval of 0 cannot roll every tick");

            // ---- the sidecar rows --------------------------------------------------------

            Scheduler src = new Scheduler(SchedulerRules.Default);
            src.StampCooldown(71, 10f, -20f, 100);
            string rows = src.EncodeCooldowns(100);
            Check(rows.Contains("cool\t71\t3600"), "a player row is uid and the seconds remaining at save time");
            Check(rows.Contains("coolbase\t10\t-20\t3600"), "a base row is x, z and the seconds remaining");

            Scheduler dst = new Scheduler(SchedulerRules.Default);
            var problems = new List<string>();
            dst.ApplyCooldowns(rows, 100, problems);
            Equal(0, problems.Count, "rows written by EncodeCooldowns apply with no problems");
            Check(dst.OnPlayerCooldown(71, 100), "the player cooldown came back");
            Check(!dst.OnPlayerCooldown(71, 3700), "with its expiry intact");
            Check(dst.NearBaseCooldown(10f, -20f, 100), "the base cooldown came back");
            Check(!dst.NearBaseCooldown(10f, -20f, 3700), "with its expiry intact");
            Equal(rows, dst.EncodeCooldowns(100), "and the rows round-trip byte for byte");

            // Rebased: a restart resets the caller's clock to zero, and the saved remainder counts from the new now.
            Scheduler rebased = new Scheduler(SchedulerRules.Default);
            rebased.ApplyCooldowns(rows, 0, problems);
            Check(rebased.OnPlayerCooldown(71, 3599), "3600 s remained at save; after a restart to clock 0 the player is on cooldown at 3599");
            Check(!rebased.OnPlayerCooldown(71, 3600), "and off it at 3600, not at the old absolute 3700");
            Check(rebased.NearBaseCooldown(10f, -20f, 3599) && !rebased.NearBaseCooldown(10f, -20f, 3600), "the base row is rebased the same way");
            rebased.ApplyCooldowns(rows, 0, problems);
            Equal(rows, rebased.EncodeCooldowns(0), "applying the same rows twice replaces them, never duplicates a base row");
            Equal("", src.EncodeCooldowns(5000), "an expired cooldown is not written at all");
            Scheduler stale = new Scheduler(SchedulerRules.Default);
            stale.ApplyCooldowns("cool\t71\t-5\ncoolbase\t1\t1\t0", 0, problems);
            Check(!stale.OnPlayerCooldown(71, 0) && !stale.NearBaseCooldown(1f, 1f, 0), "a row with no time left is dropped on load");

            problems.Clear();
            dst.ApplyCooldowns("", 100, problems);
            dst.ApplyCooldowns(null, 100, problems);
            Equal(0, problems.Count, "an empty set of rows is a no-op");

            problems.Clear();
            dst.ApplyCooldowns("garbage\trow", 100, problems);
            Equal(1, problems.Count, "a junk cooldown row is reported");
            Check(problems[0].Contains("cooldown row ignored"), "and named as ignored");

            problems.Clear();
            dst.ApplyCooldowns("cool\tnotanumber\t50", 100, problems);
            Equal(1, problems.Count, "a cooldown row whose uid does not parse is reported too");
        }

        private static void VisitClockTests()
        {
            Section("VisitClock");

            VisitClock c = VisitClock.Start(1000, 300f);
            Equal(1000.0, c.StartWorldTime, "Start records the world time it began at");
            Equal(1300.0, c.EndWorldTime, "and the end one lifespan later");
            Equal(300.0, c.Remaining(1000), "the whole lifespan remains at the start");
            Equal(150.0, c.Remaining(1150), "half of it halfway through");
            Equal(0.0, c.Remaining(1300), "none of it at the end");
            Equal(0.0, c.Remaining(1400), "and Remaining never goes negative");
            Check(!c.Expired(1299), "not expired a second before the end");
            Check(c.Expired(1300), "expired at the end time itself");
            Check(c.Expired(9999), "and after it");

            Check(!c.Warned, "a fresh clock has not warned");
            Check(!c.OneMinuteWarningDue(1239), "the warning is not due with 61 s left");
            Check(c.OneMinuteWarningDue(1240), "it is due the moment 60 s remain");
            Check(!c.OneMinuteWarningDue(1240), "and never a second time");
            Check(!c.OneMinuteWarningDue(1250), "not later in the same minute either");
            Check(c.Warned, "the clock remembers that it warned");

            VisitClock missed = VisitClock.Start(0, 300f);
            Check(!missed.OneMinuteWarningDue(400), "a clock nobody asked until after the end does not warn late");
            Check(!missed.Warned, "and is not marked as having warned");

            VisitClock resumed = VisitClock.Resume(0, 300, 270);
            Equal(0.0, resumed.StartWorldTime, "Resume keeps the saved start");
            Equal(300.0, resumed.EndWorldTime, "and the saved end");
            Equal(30.0, resumed.Remaining(270), "so the remainder is what the save said");
            Check(!resumed.Warned, "resuming with 30 s left has NOT warned yet: the player who comes back still hears one 'hurry'");
            Check(resumed.OneMinuteWarningDue(271), "and it fires once, at once");
            Check(!resumed.OneMinuteWarningDue(272), "and never again");

            VisitClock resumedEarly = VisitClock.Resume(0, 300, 100);
            Check(!resumedEarly.Warned, "resuming with 200 s left has not warned");
            Check(resumedEarly.OneMinuteWarningDue(241), "and warns when the minute comes");

            VisitClock moved = VisitClock.Start(0, 300f);
            Check(moved.OneMinuteWarningDue(240), "warned at 60 s left");
            moved.Retarget(400);
            Equal(400.0, moved.EndWorldTime, "Retarget moves the deadline (the event paused while nobody was near)");
            Equal(160.0, moved.Remaining(240), "so more time remains");
            Check(moved.Warned && !moved.OneMinuteWarningDue(340), "and the warning is not re-armed");
            moved.Retarget(double.NaN);
            Equal(400.0, moved.EndWorldTime, "a NaN deadline is ignored");
            moved.Retarget(-50);
            Equal(0.0, moved.EndWorldTime, "a deadline before the start is clamped to the start");
            Equal(0.5, VisitClock.Start(0, 300f).Fraction(150), "Fraction is elapsed over lifespan");
            Equal(1.0, VisitClock.Start(0, 300f).Fraction(999), "clamped at 1");
            Equal(0.0, VisitClock.Start(100, 300f).Fraction(50), "and at 0 before the start");
            Equal("03:42", VisitClock.Start(0, 300f).FormatRemaining(78), "FormatRemaining is Format(Remaining(now))");
            Equal("00:00", VisitClock.Format(double.NaN), "a NaN reads 00:00, never a garbage number");
            Equal("100:00", VisitClock.Format(6000), "and a long remainder widens rather than wraps");

            Equal("03:42", VisitClock.Format(222), "222 s reads 03:42");
            Equal("00:00", VisitClock.Format(0), "0 s reads 00:00");
            Equal("00:00", VisitClock.Format(-5), "a negative remainder reads 00:00, never a minus sign");
            Equal("05:00", VisitClock.Format(300), "the full visit reads 05:00");
            Equal("00:01", VisitClock.Format(0.6), "a fraction rounds to the nearest second");
            Equal("59:59", VisitClock.Format(3599), "and it counts on without widening");

            VisitClock tiny = VisitClock.Start(0, 0f);
            Equal(1.0, tiny.EndWorldTime, "a lifespan below a second is clamped to one second");
            VisitClock negative = VisitClock.Start(0, -30f);
            Equal(1.0, negative.EndWorldTime, "and so is a negative one");
        }

        private static void DemoMarketTests()
        {
            Section("DemoMarket.Settle");

            DemoMarket market = DemoMarket.Default();
            int playerCoins = 840;

            // Snapshots are VALUES (WORKSPLIT section 2): a MarketRow held from an earlier snapshot
            // never changes, and every accepted deal moves the price. So these tests keep only the
            // prefab NAMES and re-read the row from a fresh snapshot immediately before each deal.
            string warePrefab = null, wantPrefab = null;
            foreach (var row in market.Market.Rows)
            {
                if (row.Kind == EntryKind.Ware && warePrefab == null) warePrefab = row.Prefab;
                if (row.Kind == EntryKind.Want && wantPrefab == null) wantPrefab = row.Prefab;
                if (warePrefab != null && wantPrefab != null) break;
            }
            Check(warePrefab != null && wantPrefab != null, "the demo market has at least one Ware and one Want");

            // Empty deal
            Deal empty = new Deal { VisitId = 1, Nonce = 1 };
            DealResult r = market.Settle(empty, playerCoins);
            Check(r.Reason == DealReason.EmptyDeal, "Empty deal returns empty_deal");

            // Wrong VisitId
            MarketRow ware = market.Market.Find(warePrefab);
            Deal wrongVisit = new Deal { VisitId = 999, Nonce = 2, Wanted = new DealLine { Prefab = warePrefab, Count = 1, UnitPriceSeen = ware.Buy } };
            r = market.Settle(wrongVisit, playerCoins);
            Check(r.Reason == DealReason.StaleVisit, "Wrong VisitId returns stale_visit");

            // Duplicate nonce
            ware = market.Market.Find(warePrefab);
            Deal first = new Deal { VisitId = 1, Nonce = 100, Wanted = new DealLine { Prefab = warePrefab, Count = 1, UnitPriceSeen = ware.Buy } };
            r = market.Settle(first, playerCoins);
            Check(r.Ok, "First deal with nonce 100 succeeds");
            ware = market.Market.Find(warePrefab);
            Deal dup = new Deal { VisitId = 1, Nonce = 100, Wanted = new DealLine { Prefab = warePrefab, Count = 1, UnitPriceSeen = ware.Buy } };
            r = market.Settle(dup, playerCoins);
            Check(r.Reason == DealReason.Duplicate, "Same nonce twice returns duplicate");

            // Refused deal's nonce may be reused
            Deal refused = new Deal { VisitId = 1, Nonce = 101, Wanted = new DealLine { Prefab = "Unknown", Count = 1, UnitPriceSeen = 0 } };
            r = market.Settle(refused, playerCoins);
            Check(!r.Ok && r.Reason == DealReason.UnknownItem, "Unknown item is refused");
            ware = market.Market.Find(warePrefab);
            Deal reused = new Deal { VisitId = 1, Nonce = 101, Wanted = new DealLine { Prefab = warePrefab, Count = 1, UnitPriceSeen = ware.Buy } };
            r = market.Settle(reused, playerCoins);
            Check(r.Ok, "Refused nonce 101 may be reused and accepted");

            // Buying a Want-kind prefab (only Ware can be bought)
            Deal buyWant = new Deal { VisitId = 1, Nonce = 102, Wanted = new DealLine { Prefab = wantPrefab, Count = 1, UnitPriceSeen = 0 } };
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
            ware = market.Market.Find(warePrefab);
            Deal wrongPrice = new Deal { VisitId = 1, Nonce = nonce, Wanted = new DealLine { Prefab = warePrefab, Count = 1, UnitPriceSeen = ware.Buy + 50 } };
            r = market.Settle(wrongPrice, playerCoins);
            Check(r.Reason == DealReason.PriceChanged && r.NewMarketState.Length > 0, "Wrong UnitPriceSeen returns price_changed with NewMarketState");

            // Offering more than max
            nonce++;
            MarketRow want = market.Market.Find(wantPrefab);
            Deal overMax = new Deal { VisitId = 1, Nonce = nonce, Offered = new List<DealLine> { new DealLine { Prefab = wantPrefab, Count = want.Max + 10, UnitPriceSeen = want.Sell } } };
            r = market.Settle(overMax, playerCoins);
            Check(r.Reason == DealReason.OverMax, "Offering more than max returns over_max");

            // coins_short: buy enough of a ware to exceed the 10 coins we're giving
            nonce++;
            ware = market.Market.Find(warePrefab);
            int coinShortQuantity = Math.Max(1, (10 / Math.Max(1, ware.Buy)) + 5); // Enough to exceed 10 coins
            Deal coinShort = new Deal { VisitId = 1, Nonce = nonce, Wanted = new DealLine { Prefab = warePrefab, Count = coinShortQuantity, UnitPriceSeen = ware.Buy } };
            r = market.Settle(coinShort, 10);
            Check(r.Reason == DealReason.CoinsShort, "Buying too much with too few coins returns coins_short");

            // purse_empty: sell enough of a want to exceed the merchant's purse. The headroom is
            // Max MINUS the stock he already holds; more than that is over_max, a different refusal.
            nonce++;
            bool purseEmptyTested = false;
            foreach (var testWant in market.Market.Rows)
            {
                if (testWant.Kind == EntryKind.Want && testWant.Sell > 0 && testWant.Max > testWant.Stock)
                {
                    int testPurse = market.Market.Purse;
                    // To trigger purse_empty: offeredValue > purse
                    // offeredValue = quantity * sell_price
                    // So: quantity * sell_price > purse
                    //     quantity > purse / sell_price
                    long minQuantity = (long)testPurse / testWant.Sell + 1;
                    int headroom = testWant.Max - testWant.Stock;
                    int tryQuantity = (int)Math.Min(headroom, minQuantity);

                    // If we can fit the quantity under his max and it would still overflow the purse, try it
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
            ware = market.Market.Find(warePrefab);
            int stockBefore = ware.Stock;
            int purseBefore = market.Market.Purse;
            int priceSeen = ware.Buy;
            Deal acceptBuy = new Deal { VisitId = 1, Nonce = nonce, Wanted = new DealLine { Prefab = warePrefab, Count = 1, UnitPriceSeen = priceSeen } };
            r = market.Settle(acceptBuy, 10000);
            Check(r.Ok, "Accept buy succeeds");
            Check(market.Market.Find(warePrefab).Stock == stockBefore - 1, "Stock decremented");
            Check(market.Market.Purse == purseBefore + priceSeen, "Purse raised by price");
            Check(r.CoinsDelta < 0, "CoinsDelta is negative for player");

            // Accepted sell raises stock and lowers purse
            nonce++;
            want = market.Market.Find(wantPrefab);
            stockBefore = want.Stock;
            purseBefore = market.Market.Purse;
            int valueSeen = want.Sell;
            Deal acceptSell = new Deal { VisitId = 1, Nonce = nonce, Offered = new List<DealLine> { new DealLine { Prefab = wantPrefab, Count = 1, UnitPriceSeen = valueSeen } } };
            r = market.Settle(acceptSell, 10000);
            Check(r.Ok, "Accept sell succeeds");
            Check(market.Market.Find(wantPrefab).Stock == stockBefore + 1, "Stock incremented");
            Check(market.Market.Purse == purseBefore - valueSeen, "Purse lowered by value");
            Check(r.CoinsDelta > 0, "CoinsDelta is positive for player");

            // Barter: net is price minus offered value
            nonce++;
            ware = market.Market.Find(warePrefab);
            want = market.Market.Find(wantPrefab);
            Deal barter = new Deal
            {
                VisitId = 1, Nonce = nonce,
                Wanted = new DealLine { Prefab = warePrefab, Count = 1, UnitPriceSeen = ware.Buy },
                Offered = new List<DealLine> { new DealLine { Prefab = wantPrefab, Count = 1, UnitPriceSeen = want.Sell } }
            };
            r = market.Settle(barter, 10000);
            Check(r.Ok && r.ItemsToAdd.Count > 0 && r.ItemsToRemove.Count > 0, "Barter accepted with both add and remove");

            // Tick walks the stock until the SHOWN price (Buy for a ware) moves: Iron, base 25 and target 20, moves at stock 18.
            MarketRow iron = market.Market.Find("Iron");
            int buyBefore = iron.Buy;
            int sellBefore = iron.Sell;
            Check(market.Tick("Iron", true), "Tick finds Iron");
            iron = market.Market.Find("Iron");
            Check(iron.Buy > buyBefore && iron.Sell >= sellBefore && iron.Trend == 1, "Tick scarcer raises the price and sets Trend to 1");
            // Down puts the shelf back to target (trend flat); a second step floods it past target,
            // which is what a trend of -1 means.
            market.Tick("Iron", false);
            Equal(0, market.Market.Find("Iron").Trend, "one Tick down is back at target, so the trend is flat");
            market.Tick("Iron", false);
            Check(market.Market.Find("Iron").Trend == -1, "Tick down sets Trend to -1");
        }

        private static void DemoMarketKnobTests()
        {
            Section("DemoMarket knobs");

            DemoMarket demo = DemoMarket.Default();
            Equal(1, demo.Market.VisitId, "the demo opens on visit 1");
            Equal(800, demo.Market.Purse, "with the default purse of 800");
            Equal(72, demo.Market.Count, "and the whole default catalogue");
            Check(demo.Core != null && demo.Core.Find("Iron") != null, "Core exposes the real Market underneath");
            Equal("demo", demo.Core.Salt, "the demo's delivery ids are salted 'demo', so a real server's never collide with them in the inbox");
            Check(!ReferenceEquals(demo.Market, demo.Market), "Market hands out a FRESH snapshot on every call");
            Equal(20, demo.Market.Find("Iron").Stock, "every line starts at target stock");
            Equal(25, demo.Market.Find("Iron").Buy, "so Iron opens at its base price");

            // Tick walks the stock one unit at a time until the SHOWN number moves (Buy for a Ware).
            int before, after;
            Check(demo.Tick("Iron", true, out before, out after), "Tick answers true for a line it found");
            Equal(25, before, "the price it showed before");
            Equal(26, after, "and after: the first stock at which 25 * (20/s)^0.35 rounds past 25");
            Equal(18, demo.Market.Find("Iron").Stock, "which is stock 18 (19 still rounds to 25)");
            Equal(26, demo.Market.Find("Iron").Buy, "25 * (20/18)^0.35 = 25.94 -> 26");
            Equal(1, demo.Market.Find("Iron").Trend, "with the trend reading up");

            Check(demo.Tick("Iron", false), "Tick the other way");
            Equal(19, demo.Market.Find("Iron").Stock, "one unit back is enough: 25.45 -> 25");
            Equal(25, demo.Market.Find("Iron").Buy, "back at the base price");
            Equal(0, demo.Market.Find("Iron").Trend, "and the trend is flat again");

            Check(demo.Tick("Iron", false), "and again");
            Equal(22, demo.Market.Find("Iron").Stock, "20 and 21 still show 25; 22 is the first flooded price");
            Equal(24, demo.Market.Find("Iron").Buy, "25 * (20/22)^0.35 = 24.18 -> 24");
            Equal(-1, demo.Market.Find("Iron").Trend, "and the trend reads down");

            // A base-1 Want, the row a quarter-target step could never move: Wood shows Sell.
            Check(demo.Tick("Wood", true, out before, out after), "Tick moves Wood, base 1, target 200, max 600");
            Equal(1, before, "he paid 1");
            Equal(2, after, "and now pays 2");
            Equal(22, demo.Market.Find("Wood").Stock, "which took the shelf down to 22: (200/22)^0.35 * 0.7 = 1.516 -> 2, where 23 gives 1.49 -> 1");
            Equal(2, demo.Market.Find("Wood").Sell, "as the snapshot shows");

            // At the bound the walk fails and leaves the stock alone.
            Check(demo.Tick("BlackCore", true), "BlackCore 2 -> 1: 300 -> 382");
            Equal(1, demo.Market.Find("BlackCore").Stock, "one left");
            Check(!demo.Tick("BlackCore", true), "1 -> 0 shows the same 382 and there is nothing below 0, so the tick fails");
            Equal(1, demo.Market.Find("BlackCore").Stock, "and the stock is left where it was");
            Check(demo.Tick("BlackCore", false), "the other way still works");
            Equal(2, demo.Market.Find("BlackCore").Stock, "back to 2");
            Check(!demo.Tick("NoSuchPrefab", true), "Tick answers false for a prefab the catalogue does not carry");

            // Every default row can be moved both ways from target: the demo can show price_changed on any line.
            DemoMarket every = DemoMarket.Default();
            int stuck = 0;
            foreach (MarketRow row in every.Market.Rows)
            {
                if (!every.Tick(row.Prefab, true)) stuck++;
                if (!every.Tick(row.Prefab, false)) stuck++;
            }
            Equal(0, stuck, "all 72 rows move scarcer and less scarce from target (the quarter-target step left 42 of them stuck)");

            // Advance relaxes the ticked line back toward target and leaves the rest alone.
            DemoMarket drift = DemoMarket.Default();
            drift.Tick("Iron", true);
            Equal(18, drift.Market.Find("Iron").Stock, "Iron is ticked down to 18, a gap of 2");
            drift.Advance(1800);
            Equal(19, drift.Market.Find("Iron").Stock, "one game day is one half-life: half of a gap of 2 is 1, so 18 -> 19");
            Equal(20, drift.Market.Find("Bronze").Stock, "a line already at target does not move");
            drift.Advance(1800 * 10);
            Equal(20, drift.Market.Find("Iron").Stock, "and it settles back at target, never past it");
        }

        private static void MarketReviewFixTests()
        {
            Section("Market: the review's fixes (2026-09-06)");

            // A null offered line is malformed, refused before the nonce is spent.
            Market m = NewMarket(0);
            m.StartVisit(1, 0, 0);
            Deal bad = new Deal { VisitId = 1, Nonce = 77, Wanted = new DealLine { Prefab = "Iron", Count = 1, UnitPriceSeen = 25 } };
            bad.Offered.Add(null);
            DealResult r = m.Settle(bad, 1000, 0);
            Check(!r.Ok && r.Reason == DealReason.Malformed, "a null offered line is malformed, not a NullReferenceException");
            Check(!m.Nonces.Contains(77), "and the nonce was never spent");
            Deal nullList = new Deal { VisitId = 1, Nonce = 78, Wanted = new DealLine { Prefab = "Iron", Count = 1, UnitPriceSeen = 25 }, Offered = null };
            Equal(DealReason.Malformed, m.Settle(nullList, 1000, 0).Reason, "a null offered list is malformed too");
            Equal(DealReason.Malformed, m.Settle(null, 1000, 0).Reason, "and so is a null deal");

            // The over-max guard cannot be wrapped past with a huge count.
            Deal huge = new Deal { VisitId = 1, Nonce = 79, Offered = new List<DealLine> { new DealLine { Prefab = "Wood", Count = int.MaxValue, UnitPriceSeen = 1 } } };
            Equal(DealReason.OverMax, m.Settle(huge, 1000, 0).Reason, "int.MaxValue units of Wood is over max, not a wrapped negative that slips through");

            // Delivery ids carry the salt; the default salt keeps the 'v' form.
            Market salted = new Market(Catalogue.Parse(Catalogue.DefaultLine, null), MarketRules.Default, 0, "w1a2b3");
            salted.StartVisit(1, 0, 0);
            Equal("w1a2b3-1-1", salted.Settle(new Deal { VisitId = 1, Nonce = 1, Wanted = new DealLine { Prefab = "Iron", Count = 1, UnitPriceSeen = 25 } }, 1000, 0).DeliveryId,
                  "a salted market's ids name the world, so two worlds' visit 1 never collide in one inbox");
            Equal("v", NewMarket(0).Salt, "no salt is the plain 'v' prefix");
            Equal("v", new Market(null, null, 0, "a;b").Salt, "a salt that is not a wire token falls back to 'v'");
            Equal("v", new Market(null, null, 0, "a-b").Salt, "and so does one with a dash, which the id format uses");
            Equal(2, salted.NextVisitId, "NextVisitId is one past the current visit");

            // The restart rows: purseStart, visit, seq.
            Market a = NewMarket(0);
            a.StartVisit(5, 0, 300);
            Check(a.Settle(new Deal { VisitId = 5, Nonce = 1, Wanted = new DealLine { Prefab = "Iron", Count = 1, UnitPriceSeen = 25 } }, 1000, 0).Ok, "one buy in visit 5");
            Check(a.Settle(new Deal { VisitId = 5, Nonce = 2, Wanted = new DealLine { Prefab = "Bronze", Count = 1, UnitPriceSeen = 15 } }, 1000, 0).Ok, "and a second");
            string rows = a.EncodeState();
            Check(rows.Contains("purseStart\t950"), "the visit's purse baseline is a row (800 + half of 300)");
            Check(rows.Contains("visit\t5"), "the visit id is a row");
            Check(rows.Contains("seq\t2"), "and the delivery sequence is a row");
            Market b = NewMarket(0);
            var problems = new List<string>();
            b.ApplyState(rows, problems);
            Equal(0, problems.Count, "the new rows apply cleanly");
            Equal(5, b.VisitId, "a resumed market is still visit 5, so a deal sent before the restart is not stale");
            Equal(6, b.NextVisitId, "and the next visit will be 6, never 1 again");
            Equal(40, b.Takings, "Takings survive the restart: 25 + 15, because the baseline came back");
            Equal("v-5-3", b.Settle(new Deal { VisitId = 5, Nonce = 3, Wanted = new DealLine { Prefab = "Honey", Count = 1, UnitPriceSeen = 2 } }, 1000, 0).DeliveryId,
                  "the sequence continues past the restart: v-5-3, never a second v-5-1 for the inbox to swallow");
            problems.Clear();
            b.ApplyState("visit\t-1\nseq\tx\npurseStart\t-2", problems);
            Equal(3, problems.Count, "bad visit, seq and purseStart rows are each reported");
            Equal(5, b.VisitId, "and refused");

            // Relax keeps the time it could not yet spend.
            Market often = NewMarket(0);
            MarketItem iron = often.Find("Iron");
            iron.Stock = 19; iron.UpdatedWorldTime = 0;
            for (int i = 1; i <= 50; i++) often.Relax(i * 720);          // every 0.4 game days for 20 days
            Equal(20, iron.Stock, "a gap of one closes even when Relax is called every 0.4 days: the stamp waits until a unit moves");
            Market once = NewMarket(0);
            MarketItem iron2 = once.Find("Iron");
            iron2.Stock = 19; iron2.UpdatedWorldTime = 0;
            once.Relax(720);
            Equal(19, iron2.Stock, "0.4 days is 24% of a gap of 1: nothing moves yet");
            Equal(0.0, iron2.UpdatedWorldTime, "so the stamp is kept, not thrown away");
            once.Relax(2160);
            Equal(20, iron2.Stock, "1.2 days later it closes");
            Equal(2160.0, iron2.UpdatedWorldTime, "and is stamped then");
            Market back = NewMarket(1000);
            MarketItem iron3 = back.Find("Iron");
            iron3.Stock = 10;
            back.Relax(500);
            Equal(1000.0, iron3.UpdatedWorldTime, "a clock that ran backwards leaves the stamp alone, not rewound to the earlier time");

            // The purse cap cannot overflow, and the rules are sanitized on the way in.
            MarketRules big = new MarketRules { PurseCoins = int.MaxValue, PurseCapMultiple = 100 };
            Market rich = new Market(Catalogue.Parse(Catalogue.DefaultLine, null), big, 0);
            Equal(MarketRules.MaxPurseCoins, big.PurseCoins, "the constructor sanitizes the rules in place");
            rich.StartVisit(1, 0, int.MaxValue);
            Equal(MarketRules.MaxPurseCoins * 100, rich.Purse, "capped at PurseCapMultiple x PurseCoins, computed in long: never negative");

            // The day length is a rule, read from EnvMan on the game side; the drift follows it.
            MarketRules shortDay = new MarketRules { SecondsPerGameDay = 1200 };
            Market sd = new Market(Catalogue.Parse(Catalogue.DefaultLine, null), shortDay, 0);
            MarketItem si = sd.Find("Iron");
            si.Stock = 10; si.UpdatedWorldTime = 0;
            sd.Relax(1200);
            Equal(15, si.Stock, "with a 1200 s day (the compiled EnvMan default), 1200 s is one half-life: 10 -> 15");
            Equal(1800.0, MarketRules.DefaultSecondsPerGameDay, "and the default is the 30-minute day the scene is expected to carry");
            problems.Clear();
            new MarketRules { SecondsPerGameDay = 1 }.Sanitize(problems);
            Equal(1, problems.Count, "a one-second day is clamped and reported");

            // The curve's pieces.
            Equal(1.0, Market.MultiplierFor(20, 20, MarketRules.Default), "at target the multiplier is exactly 1");
            Equal(3.0, Market.MultiplierFor(600, 0, MarketRules.Default), "an empty high-target shelf hits the 3x ceiling");
            Equal(0.4, Market.MultiplierFor(2, 600, MarketRules.Default), "a flooded low-target shelf hits the 0.4 floor");
        }

        private static void VisitSessionTests()
        {
            Section("VisitSession (the server's visit record and the clock mirror)");

            VisitSession s = new VisitSession();
            Check(!s.Active, "a new session is not active");
            Equal("", s.Encode(), "and encodes as the empty channel, which parses as no visit");
            Check(s.Sync(100, 300) == null, "Sync on an inactive session does nothing");
            Check(s.SetPhase(VisitPhase.Trading) == null, "and so does SetPhase");
            Equal("", s.End("nothing"), "End on an inactive session stays empty");
            Equal("", s.LastEndReason, "and records no reason, because nothing ended");

            // Begin publishes a Flying visit with the pilot, the drop point, the clock and the seed.
            string state = s.Begin(7, 4242L, "Don", 10f, 30f, -20f, 1000.0, 300f, 800, 99);
            Check(s.Active && s.Phase == VisitPhase.Flying, "Begin makes the session active, in the Flying phase");
            var problems = new List<string>();
            VisitSnapshot v = VisitSnapshot.Parse(state, problems);
            Equal(0, problems.Count, "what Begin returns parses cleanly as a VisitSnapshot");
            Equal(7, v.VisitId, "with the visit id");
            Equal(VisitPhase.Flying, v.Phase, "the phase");
            Equal(4242L, v.PilotUid, "the pilot");
            Check(v.DropX == 10f && v.DropY == 30f && v.DropZ == -20f, "where the pilot stood, as the drop point until the flight refines it");
            Equal(1300.0, v.EndWorldTime, "the deadline one lifespan after the start");
            Equal(800, v.Purse, "the purse he arrives with");
            Equal(99, v.Seed, "and the seed every client derives his lines from");
            Equal("Don", s.PilotName, "the pilot's name is kept for the log, not sent");
            Equal(1300.0, s.PublishedEnd, "the published deadline is the clock's");
            Equal(7, s.LastVisitId, "LastVisitId follows Begin");

            // Sync: the event runs in real seconds; while world time keeps pace nothing is republished.
            Check(s.Sync(1001.0, 299.0) == null, "one second in, 299 s left: the deadline matches, nothing to send");
            Check(s.Sync(1010.0, 290.4) == null, "a drift of 0.4 s is within the threshold");
            Check(s.Sync(1010.0, 291.0) == null, "so is exactly 1 s");
            Equal(0, s.Republishes, "no republish yet");

            // The event paused with nobody near: world time runs, the event's remainder does not.
            string re = s.Sync(1020.0, 290.0);
            Check(re != null, "a drift of 10 s republishes");
            Equal(1, s.Republishes, "counted");
            Equal(1310.0, VisitSnapshot.Parse(re, null).EndWorldTime, "the new deadline is now + remaining");
            Equal(1310.0, s.Clock.EndWorldTime, "and the clock was retargeted to it");
            Equal(1310.0, s.PublishedEnd, "and remembered as published");
            Check(s.Sync(1021.0, 289.0) == null, "back in step: nothing more to send");

            // A sleep skip: world time jumps hours ahead; the event's remainder is unchanged.
            string skip = s.Sync(9000.0, 289.0);
            Check(skip != null, "a world-clock jump republishes");
            Equal(9289.0, s.Clock.EndWorldTime, "with the deadline rebased onto the new world time");
            Equal(289.0, s.Clock.Remaining(9000.0), "so the countdown still reads the event's remaining seconds");

            // The warning is not re-armed by retargeting.
            Check(s.Clock.OneMinuteWarningDue(9229.0), "warned at 60 s left");
            Check(s.Sync(9229.0, 70.0) != null, "an extension republishes");
            Check(!s.Clock.OneMinuteWarningDue(9240.0), "but the warning stays given");

            // Bad remainders are ignored, never NaN into the channel.
            Check(s.Sync(9230.0, double.NaN) == null, "a NaN remainder is ignored");
            Check(s.Sync(9230.0, double.PositiveInfinity) == null, "and so is an infinite one");
            string neg = s.Sync(9230.0, -50.0);
            Check(neg != null && VisitSnapshot.Parse(neg, null).EndWorldTime == 9230.0, "a negative remainder means the deadline is now");

            // Phases and the drop point.
            Check(s.SetPhase(VisitPhase.Flying) == null, "the same phase again is not a change");
            Check(s.SetPhase(VisitPhase.None) == null, "None is not a phase to set; End does that");
            string dropped = s.SetPhase(VisitPhase.Dropped);
            Equal(VisitPhase.Dropped, VisitSnapshot.Parse(dropped, null).Phase, "a new phase is published");
            string drop = s.SetDrop(1f, 2f, 3f);
            VisitSnapshot vd = VisitSnapshot.Parse(drop, null);
            Check(vd.DropX == 1f && vd.DropY == 2f && vd.DropZ == 3f, "the refined drop point is published");
            Equal(VisitPhase.Dropped, vd.Phase, "with the phase unchanged");

            // End.
            Equal("", s.End("timer"), "End publishes the empty channel");
            Check(!s.Active, "and the session is inactive");
            Equal("timer", s.LastEndReason, "with the reason kept for cargo status");
            Equal(7, s.LastVisitId, "and the id of the visit that ended");
            Check(s.Sync(9300.0, 10.0) == null && s.SetDrop(0f, 0f, 0f) == null, "nothing publishes after the end");
            s.Begin(8, 1L, "", 0f, 0f, 0f, 0.0, 300f, -5, 0);
            Equal(0, s.Purse, "a nonsense negative purse is floored at 0");
            Equal("", s.PilotName, "a null or empty name stays empty");
            Equal(0, s.Republishes, "the republish count restarts with the visit");
            Equal("", s.LastEndReason, "and the old end reason is cleared");
            s.End(null);
            Equal("ended", s.LastEndReason, "a missing reason reads 'ended'");

            // Lines: every seed picks an arrival line, the same on every client.
            Check(Lines.ArrivalFor(0) == Lines.Arrival[0] && Lines.ArrivalFor(5) == Lines.Arrival[1], "the seed indexes the arrival lines");
            Check(Lines.ArrivalFor(-3) == Lines.Arrival[1], "a negative seed still lands inside the table");
            Check(Lines.ArrivalFor(int.MinValue).Length > 0, "even int.MinValue");
        }

        private static void SidecarTests()
        {
            Section("Sidecar (the world file's bundles)");

            Market m = NewMarket(0);
            m.StartVisit(3, 0, 100);
            Check(m.Settle(new Deal { VisitId = 3, Nonce = 1, Wanted = new DealLine { Prefab = "Iron", Count = 2, UnitPriceSeen = 25 } }, 1000, 0).Ok, "a deal so the market is not the default");
            Scheduler sch = new Scheduler(SchedulerRules.Default);
            sch.StampCooldown(42, 5f, 6f, 100);
            VisitSession vs = new VisitSession();
            vs.Begin(3, 42L, "Don", 5f, 30f, 6f, 1000.0, 300f, m.Purse, 77);
            OwedLedger led = new OwedLedger();
            led.Add("steam1", new DealResult { Ok = true, DeliveryId = "v-3-1", Nonce = 1, CoinsDelta = -50, ItemsToAdd = new List<DealLine> { new DealLine { Prefab = "Iron", Count = 2, UnitPriceSeen = 25 } } });

            string text = Sidecar.Compose(m.EncodeState(), sch.EncodeCooldowns(100), vs.EncodeSessionRow(), led.EncodeRows());
            Check(text.StartsWith("format\t1\n"), "the file opens with the format line");
            Check(text.EndsWith("\n"), "and ends with a newline");
            Check(text.Contains("\nstock\tIron\t18\t") && text.Contains("\npurse\t") && text.Contains("\ncool\t42\t") && text.Contains("\ncoolbase\t5\t6\t") &&
                  text.Contains("\nsession\t3\t42\tDon\t") && text.Contains("\nowed\tsteam1\tv-3-1\t"), "every owner's rows are in it");

            var problems = new List<string>();
            Sidecar sc = Sidecar.Split(text, problems);
            Equal(0, problems.Count, "what Compose wrote splits with no problems");
            Check(sc.FormatMatches, "the format matches");
            Equal(m.EncodeState(), sc.MarketRows, "the market rows come back byte for byte");
            Equal(sch.EncodeCooldowns(100), sc.CooldownRows, "and the cooldown rows");
            Equal(vs.EncodeSessionRow(), sc.SessionRow, "and the session row");
            Equal(1, sc.OwedRows.Count, "and the owed row");
            Equal(0, sc.UnknownRows, "nothing unknown");

            // Each owner rebuilds itself from its bundle.
            Market m2 = NewMarket(0);
            m2.ApplyState(sc.MarketRows, problems);
            Equal(m.EncodeState(), m2.EncodeState(), "a fresh market rebuilt from the bundle encodes the same");
            Equal(4, m2.NextVisitId, "including the visit counter");
            Scheduler sch2 = new Scheduler(SchedulerRules.Default);
            sch2.ApplyCooldowns(sc.CooldownRows, 100, problems);
            Check(sch2.OnPlayerCooldown(42, 100) && sch2.NearBaseCooldown(5f, 6f, 100), "the cooldowns rebuilt");
            OwedLedger led2 = new OwedLedger();
            Equal(1, led2.ApplyRows(sc.OwedRows, problems), "the ledger rebuilt");
            Equal(0, problems.Count, "all without a problem");

            // Robustness.
            problems.Clear();
            Sidecar empty = Sidecar.Split("", problems);
            Check(!empty.FormatMatches && problems.Count == 1, "an empty file is reported and does not match the format");
            problems.Clear();
            Sidecar noFormat = Sidecar.Split("stock\tIron\t5\t0\n", problems);
            Check(!noFormat.FormatMatches && problems.Count == 1 && problems[0].Contains("no format line"), "a file without a format line is reported");
            Equal("stock\tIron\t5\t0", noFormat.MarketRows, "but its rows are still routed");
            problems.Clear();
            Sidecar foreign = Sidecar.Split("format\t7\nstock\tIron\t5\t0\n", problems);
            Check(!foreign.FormatMatches && foreign.Format == 7 && problems.Count == 1, "a foreign format is reported with its number");
            problems.Clear();
            Sidecar junk = Sidecar.Split("format\t1\n# a comment\nwhatever\tx\n\r\nsession\ta\nsession\tb\nformat\tx\n", problems);
            Equal(1, junk.UnknownRows, "an unknown row is counted");
            Equal("session\ta", junk.SessionRow, "the first session row wins");
            Equal(3, problems.Count, "unknown row, second session row and a bad format line are each reported");
            Check(junk.FormatMatches, "and the good format line stands");
            Equal("", Sidecar.Split("format\t1\n", null).MarketRows, "a header-only file has empty bundles and tolerates a null problem list");
        }

        private static void OwedLedgerTests()
        {
            Section("OwedLedger (deliveries the server still owes)");

            OwedLedger l = new OwedLedger(perPlayer: 3, total: 5);
            DealResult ok(string id, int coins) => new DealResult { Ok = true, DeliveryId = id, Nonce = 9, CoinsDelta = coins, NewMarketState = "v1;1;800;", ItemsToAdd = new List<DealLine> { new DealLine { Prefab = "Iron", Count = 1, UnitPriceSeen = 25 } } };

            Check(l.Add("steam1", ok("w-1-1", -25)), "an accepted result is recorded");
            Check(l.Owes("w-1-1"), "and owed");
            Check(!l.Add("steam1", ok("w-1-1", -25)), "the same id twice is refused");
            Check(!l.Add("steam1", DealResult.Refuse(1, DealReason.SoldOut)), "a refusal is never owed");
            Check(!l.Add("steam1", new DealResult { Ok = true, DeliveryId = "" }), "nor an accepted result without an id");
            Check(!l.Add("bad;key", ok("w-1-2", -1)), "a key that is not a wire token is refused");
            Check(!l.Add("", ok("w-1-3", -1)), "and so is an empty one");
            Equal(1, l.Count, "one row so far");
            Equal("", l.For("steam1")[0].NewMarketState, "the stored copy drops NewMarketState");
            Equal(1, l.For("steam1")[0].ItemsToAdd.Count, "but keeps the goods");

            Check(!l.Ack("steam2", "w-1-1"), "another player cannot ack it");
            Check(l.Owes("w-1-1"), "so it is still owed");
            Check(!l.Ack("steam1", "nope"), "an unknown id acks nothing");
            Check(l.Ack("steam1", "w-1-1"), "the owner acks it");
            Check(!l.Owes("w-1-1") && l.Count == 0, "and it is gone");
            Check(!l.Ack("steam1", "w-1-1"), "a second ack finds nothing");

            // Per-player cap: the oldest of that player's rows goes.
            l.Add("steam1", ok("a1", -1)); l.Add("steam1", ok("a2", -1)); l.Add("steam1", ok("a3", -1)); l.Add("steam1", ok("a4", -1));
            Equal(3, l.CountFor("steam1"), "a player holds at most perPlayer rows");
            Check(!l.Owes("a1") && l.Owes("a4"), "the oldest was evicted");
            Equal(1, l.Evictions, "and counted");
            List<DealResult> mine = l.For("steam1");
            Check(mine[0].DeliveryId == "a2" && mine[2].DeliveryId == "a4", "For is oldest first");

            // Total cap across players.
            l.Add("steam2", ok("b1", -1)); l.Add("steam2", ok("b2", -1)); l.Add("steam3", ok("c1", -1));
            Equal(5, l.Count, "the ledger holds at most total rows");
            Check(!l.Owes("a2"), "the oldest overall went first");

            // Rows round-trip; bad rows are reported and skipped.
            List<string> rows = l.EncodeRows();
            Equal(5, rows.Count, "one row per owed delivery");
            Check(rows[0].StartsWith("owed\t") && rows[0].Split('\t').Length == 4, "owed, player, id, result");
            OwedLedger l2 = new OwedLedger(perPlayer: 3, total: 5);
            var problems = new List<string>();
            Equal(5, l2.ApplyRows(rows, problems), "every row applies");
            Equal(0, problems.Count, "with no problems");
            Check(l2.Owes("a4") && l2.Owes("b2") && l2.Owes("c1"), "the same deliveries are owed");
            CollectionsEqual(rows, l2.EncodeRows(), "and encode identically");
            problems.Clear();
            OwedLedger l3 = new OwedLedger();
            Equal(0, l3.ApplyRows(new List<string> { "owed\tsteam1", "owed\tbad;key\tx\tv1;1;x;1;ok;0;;;", "owed\tsteam1\tx\tv1;1;y;1;ok;0;;;", "owed\tsteam1\tz\tv1;1;z;0;sold_out;0;;;", "junk" }, problems),
                  "a short row, a bad key, an id that does not match its result, a refusal and junk all apply nothing");
            Equal(5, problems.Count, "and each is reported");
            l3.Clear();
            Equal(0, l3.ApplyRows(null, null), "null rows and a null problem list are tolerated");
        }

        private static void SessionRowTests()
        {
            Section("VisitSession: the sidecar row and Resume");

            VisitSession a = new VisitSession();
            Equal("", a.EncodeSessionRow(), "no visit, no row");
            a.Begin(5, 4242L, "Don\tTab", 1f, 2f, 3f, 1000.0, 300f, 900, 7);
            string row = a.EncodeSessionRow();
            Check(row.StartsWith("session\t5\t4242\tDon Tab\t1\t2\t3\t1000\t1300\t900\t7\tFlying"), "the row carries id, pilot, a tab-cleaned name, drop, clock, purse, seed, phase: " + row);
            a.SetPhase(VisitPhase.Trading);
            Check(a.EncodeSessionRow().EndsWith("\tTrading"), "and follows the phase");

            VisitSession b = new VisitSession();
            var problems = new List<string>();
            string state = b.Resume(a.EncodeSessionRow(), 1100.0, problems);
            Check(state != null && problems.Count == 0, "the row resumes cleanly");
            Check(b.Active && b.Resumed, "the session is active and marked resumed");
            Equal(5, b.VisitId, "same visit");
            Equal(4242L, b.PilotUid, "same pilot");
            Equal("Don Tab", b.PilotName, "same name");
            Equal(VisitPhase.Trading, b.Phase, "same phase");
            Equal(900, b.Purse, "same purse");
            Equal(7, b.Seed, "same seed, so the lines match");
            Equal(1300.0, b.Clock.EndWorldTime, "the saved deadline");
            Equal(200.0, b.Clock.Remaining(1100.0), "with 200 s left at the resume time");
            Check(!b.Clock.Warned, "the warning is not counted as given");
            VisitSnapshot v = VisitSnapshot.Parse(state, null);
            Check(v.VisitId == 5 && v.Phase == VisitPhase.Trading && v.EndWorldTime == 1300.0 && v.DropX == 1f, "what Resume returns is the VisitState to publish");
            Check(b.Sync(1100.0, 150.0) != null && b.Clock.EndWorldTime == 1250.0, "the event's own remainder then corrects the clock");
            Equal(5, b.LastVisitId, "LastVisitId follows the resumed visit");

            // Bad rows.
            foreach (string bad in new[] { "", "session\t5", "session\tx\t1\tn\t0\t0\t0\t0\t1\t0\t0\tFlying", "session\t0\t1\tn\t0\t0\t0\t0\t1\t0\t0\tFlying",
                                          "session\t5\t1\tn\ta\t0\t0\t0\t1\t0\t0\tFlying", "session\t5\t1\tn\t0\t0\t0\tNaN\t1\t0\t0\tFlying", "session\t5\t1\tn\t0\t0\t0\t0\t1\t-1\t0\tFlying", "visit\t5" })
            {
                problems.Clear();
                VisitSession c = new VisitSession();
                Check(c.Resume(bad, 0, problems) == null && problems.Count == 1 && !c.Active, "a bad row resumes nothing and reports once: " + bad.Replace("\t", " "));
            }
            VisitSession d = new VisitSession();
            Check(d.Resume("session\t5\t1\tn\t0\t0\t0\t0\t1\t0\t0\tNone", 0, null) != null && d.Phase == VisitPhase.Flying, "a row claiming phase None resumes as Flying");
            VisitSession e = new VisitSession();
            Check(e.Resume("session\t5\t1\tn\t0\t0\t0\t500\t300\t0\t0\tFlying", 0, null) != null && e.Clock.EndWorldTime == 500.0, "an end before the start is clamped to the start");
        }

        // ---- BodyMotion helpers ---------------------------------------------------------

        /// <summary>Tick a body model for `seconds` at a fixed raw speed, one 60 Hz frame at a time.</summary>
        private static void BodyRun(BodyMotion m, float seconds, float speed)
        {
            const float dt = 1f / 60f;
            int steps = (int)Math.Round(seconds / dt);
            for (int i = 0; i < steps; i++) m.Tick(dt, speed);
        }

        private static float BodySum(BodyMotion m)
        {
            float s = 0f;
            for (int i = 0; i < BodyMotion.ClipCount; i++) s += m.Weights[i];
            return s;
        }

        private static void BodyMotionTests()
        {
            Section("BodyMotion (Ingvar's six clips, as weights)");

            // ---- names: what the loader looks the clips up by -------------------------------------
            BodyMotion m = new BodyMotion();
            Equal("Walk", BodyMotion.ClipName(BodyClip.Walk), "the clip names are the bundle's");
            Equal("Idle", BodyMotion.ClipName(BodyClip.Idle), "Idle");
            Equal("Hello", BodyMotion.ClipName(BodyClip.Hello), "Hello");
            Equal("", BodyMotion.ClipName(BodyClip.None), "None has no name");
            Equal(BodyClip.Shrug, BodyMotion.ByName("shrug"), "ByName is case-insensitive (the console types it)");
            Equal(BodyClip.None, BodyMotion.ByName("Dance"), "and None for anything he cannot do");
            Equal(BodyClip.None, BodyMotion.ByName(""), "and for nothing at all");
            Check(BodyMotion.IsOneShot(BodyClip.Hello) && BodyMotion.IsOneShot(BodyClip.Nod), "Hello and Nod are one-shots");
            Check(!BodyMotion.IsOneShot(BodyClip.Idle) && !BodyMotion.IsOneShot(BodyClip.Walk), "Idle and Walk are not");

            // ---- a standing start ------------------------------------------------------------------
            Equal(1f, m.Weight(BodyClip.Idle), "a fresh body idles at full weight");
            Equal(0f, m.Weight(BodyClip.Walk), "and does not walk");
            Equal(BodyClip.None, m.Current, "with no one-shot");
            Check(Math.Abs(BodySum(m) - 1f) < 1e-5f, "the six weights sum to 1 before the first tick");
            Equal(0f, m.Weight(BodyClip.None), "Weight(None) is 0, not an index out of range");
            Equal(0f, m.ShotNormalized, "and no one-shot progress");

            // ---- clip lengths: the bundle's, with the constants as fallbacks ----------------------
            Equal(BodyMotion.HelloLength, m.Length(BodyClip.Hello), "Hello falls back to the measured 3.79 s");
            Equal(BodyMotion.NodLength, m.Length(BodyClip.Nod), "Nod to 1.25 s");
            m.SetLength(BodyClip.Hello, 2.5f);
            Equal(2.5f, m.Length(BodyClip.Hello), "a real length from the clip replaces it");
            m.SetLength(BodyClip.Hello, float.NaN);
            m.SetLength(BodyClip.Hello, 0f);
            m.SetLength(BodyClip.Hello, -3f);
            Equal(2.5f, m.Length(BodyClip.Hello), "a NaN, zero or negative length is ignored, not adopted");
            m.SetLength(BodyClip.Hello, BodyMotion.HelloLength);

            // ---- hysteresis, both ways (models/README.md section 7) -------------------------------
            BodyMotion h = new BodyMotion();
            BodyRun(h, 2f, 0.03f);
            Check(!h.Walking, "at 0.03 m/s he is idling");
            Check(Math.Abs(h.SmoothedSpeed - 0.03f) < 0.001f, "and the filter has settled on the speed");
            BodyRun(h, 2f, 0.055f);
            Check(!h.Walking, "0.055 m/s is inside the band: coming UP it is still idle");
            BodyRun(h, 2f, 0.07f);
            Check(h.Walking, "0.07 m/s is over the walk threshold");
            BodyRun(h, 2f, 0.055f);
            Check(h.Walking, "0.055 m/s coming DOWN is still walking: that is the hysteresis");
            BodyRun(h, 2f, 0.04f);
            Check(!h.Walking, "under 0.05 m/s he stops");
            Check(Math.Abs(h.Weight(BodyClip.Idle) - 1f) < 1e-4f, "and the blend followed him back to Idle");

            // ---- the crossfade takes CrossfadeSeconds ----------------------------------------------
            BodyMotion c = new BodyMotion();
            for (int i = 0; i < 4; i++) c.Tick(1f / 60f, 1f);
            Check(c.WalkBlend > 0.40f && c.WalkBlend < 0.50f, "four 60 Hz frames is about a third of the crossfade");
            for (int i = 0; i < 5; i++) c.Tick(1f / 60f, 1f);
            Check(c.WalkBlend >= 0.999f, "nine of them (0.15 s) complete it");
            Check(Math.Abs(c.Weight(BodyClip.Walk) - 1f) < 1e-3f, "so Walk owns the body");
            Check(c.Weight(BodyClip.Idle) < 1e-3f, "and Idle is out");
            BodyRun(c, 1f, 0f);
            Equal(0f, c.WalkBlend, "and it crossfades all the way back");

            // ---- the one-shot envelope ---------------------------------------------------------------
            BodyMotion o = new BodyMotion();
            Check(o.Fire(BodyClip.Hello), "Hello fires");
            Equal(BodyClip.Hello, o.Current, "and is the current one-shot");
            Equal(0f, o.ShotWeight, "at no weight yet: it blends in, never snaps");
            o.Tick(1f / 60f, 0f);
            Check(o.ShotWeight > 0f && o.ShotWeight < 1f, "one frame in, it is part way");
            BodyRun(o, 0.08f, 0f);
            Equal(1f, o.ShotWeight, "and full after OneShotBlendSeconds");
            Equal(1f, o.Weight(BodyClip.Hello), "so Hello owns the whole body");
            Equal(0f, o.Weight(BodyClip.Idle), "with Idle at nothing");
            Check(Math.Abs(BodySum(o) - 1f) < 1e-5f, "the weights still sum to 1 mid-gesture");
            BodyRun(o, 3.0f, 0f);
            Equal(BodyClip.Hello, o.Current, "at 3.0 s of a 3.79 s clip he is still waving");
            BodyRun(o, 0.40f, 0f);
            Equal(BodyClip.None, o.Current, "past 85% of its length plus the blend, it has handed back");
            Check(Math.Abs(o.Weight(BodyClip.Idle) - 1f) < 1e-4f, "to Idle, at full weight");

            // The hand-back follows the clip's OWN length, not the constant.
            BodyMotion s = new BodyMotion();
            s.SetLength(BodyClip.Nod, 1.0f);
            s.Fire(BodyClip.Nod);
            BodyRun(s, 0.80f, 0f);
            Equal(BodyClip.Nod, s.Current, "a 1.0 s nod is still going at 0.80 s");
            BodyRun(s, 0.30f, 0f);
            Equal(BodyClip.None, s.Current, "and is done by 1.10 s: 85% of 1.0 s plus the blend out");

            // ---- replacement, and no self-interrupt --------------------------------------------------
            BodyMotion r = new BodyMotion();
            r.Fire(BodyClip.Hello);
            BodyRun(r, 0.50f, 0f);
            float timeBefore = r.ShotTime;
            Check(!r.Fire(BodyClip.Hello), "a one-shot cannot interrupt itself");
            Equal(timeBefore, r.ShotTime, "so the running clip keeps its place");
            Check(r.Fire(BodyClip.Talk), "a different one-shot replaces it");
            Equal(BodyClip.Talk, r.Current, "and becomes the current one");
            Equal(0f, r.ShotTime, "from its start");
            Equal(1f, r.ShotWeight, "at the weight the last one held: the swap has no gap in it");
            Equal(1f, r.Weight(BodyClip.Talk), "so Talk owns the body at once");
            Equal(0f, r.Weight(BodyClip.Hello), "and Hello is gone");
            Check(!r.Fire(BodyClip.Idle), "Idle is not a one-shot and cannot be fired");
            Check(!r.Fire(BodyClip.Walk), "nor Walk");
            Check(!r.Fire(BodyClip.None), "nor None");
            Check(r.ShotNormalized >= 0f && r.ShotNormalized < 0.01f, "the fresh one-shot has barely started");
            r.CancelOneShot();
            Equal(BodyClip.None, r.Current, "CancelOneShot drops it at once");
            Check(Math.Abs(BodySum(r) - 1f) < 1e-5f, "and the weights still sum to 1");

            // ---- guards: a hitch, a bad delta, a bad sample -------------------------------------------
            // The crossfade is deliberately caught PART WAY, where a bad delta has somewhere to go wrong.
            // Asserted at a resting state these checks pass whatever the guard does, which is worthless.
            BodyMotion g = new BodyMotion();
            g.Tick(1f / 60f, 1f);
            g.Tick(1f / 60f, 1f);
            float walkBefore = g.WalkBlend, speedBefore;
            Check(walkBefore > 0.1f && walkBefore < 0.9f, "the crossfade is caught part way, mid-move");
            g.Tick(float.NaN, 1f);
            Equal(walkBefore, g.WalkBlend, "a NaN delta moves nothing");
            Check(!float.IsNaN(g.SmoothedSpeed), "and leaves no NaN in the filter");
            g.Tick(-5f, 1f);
            Equal(walkBefore, g.WalkBlend, "nor does a negative one");
            Check(g.SmoothedSpeed >= 0f, "and time never runs backwards through the filter");
            g.Tick(float.PositiveInfinity, 1f);
            Check(!float.IsNaN(g.WalkBlend) && !float.IsNaN(g.SmoothedSpeed), "an infinite one leaves no NaN behind");
            BodyMotion g2 = new BodyMotion();
            g2.Tick(1f / 60f, 1f);
            float held = g2.WalkBlend;
            g2.Tick(0f, 1f);
            Equal(held, g2.WalkBlend, "a zero delta is a no-op");
            g2.Tick(float.NaN, float.NaN);
            Check(!float.IsNaN(g2.SmoothedSpeed), "a NaN speed sample never reaches the filter");
            speedBefore = g2.SmoothedSpeed;
            g2.Tick(0.1f, float.NaN);
            Equal(speedBefore, g2.SmoothedSpeed, "the filter HOLDS on a NaN sample rather than snapping to zero");
            g2.Tick(0.1f, -4f);
            Check(g2.SmoothedSpeed < speedBefore && g2.SmoothedSpeed >= 0f, "a negative speed is read as standing still, never as motion");

            // A hitch must not swallow a gesture whole: without the clamp, one frame of 600 s takes a
            // one-shot past its hand-back point and Ingvar never waves at all.
            BodyMotion j = new BodyMotion();
            j.Fire(BodyClip.Hello);
            j.Tick(600f, 0f);
            Equal(BodyMotion.MaxStepSeconds, j.ShotTime, "a 10-minute hitch advances a one-shot by at most MaxStepSeconds");
            Equal(BodyClip.Hello, j.Current, "so the hitch does not swallow the gesture whole");
            Check(Math.Abs(BodySum(j) - 1f) < 1e-5f, "and leaves the weights whole");

            BodyMotion z = new BodyMotion();
            BodyRun(z, 2f, 1f);
            z.Fire(BodyClip.Shrug);
            z.Reset();
            Equal(BodyClip.None, z.Current, "Reset drops the one-shot");
            Equal(0f, z.SmoothedSpeed, "forgets the speed");
            Equal(1f, z.Weight(BodyClip.Idle), "and stands him back at idle");

            // ---- a one-shot fired MID-CROSSFADE (adversarial review) -----------------------------------
            // The checks above catch a gesture from a standing start and from a settled walk. Neither
            // touches the case where the crossfade is still moving underneath it, which is where the two
            // halves of Recompute - the locomotion pair and the one-shot - can disagree.
            BodyMotion x = new BodyMotion();
            for (int i = 0; i < 4; i++) x.Tick(1f / 60f, 1f);
            float blendAtFire = x.WalkBlend;
            Check(blendAtFire > 0.1f && blendAtFire < 0.9f, "the crossfade is caught part way before the gesture starts");
            Check(x.Fire(BodyClip.Shrug), "a one-shot fires mid-crossfade");
            BodyRun(x, 0.10f, 1f);
            Equal(1f, x.ShotWeight, "and takes the whole body");
            Equal(0f, x.Weight(BodyClip.Walk), "so locomotion shows at nothing while it plays");
            Check(Math.Abs(BodySum(x) - 1f) < 1e-5f, "the weights still sum to 1 with a gesture over a moving crossfade");
            Check(x.WalkBlend > blendAtFire, "and the crossfade kept running UNDERNEATH it rather than freezing");
            BodyRun(x, 3f, 1f);
            Equal(BodyClip.None, x.Current, "the shrug hands back");
            Check(Math.Abs(x.Weight(BodyClip.Walk) - 1f) < 1e-4f, "onto the walk it finished crossfading into, not onto the stale blend it started from");

            // ---- a DIFFERENT one-shot started during the hand-back --------------------------------------
            // The replacement check above swaps at full weight, where "picks up the weight it held" and
            // "snaps to 1" are the same number and prove nothing.
            BodyMotion q = new BodyMotion();
            q.Fire(BodyClip.Nod);                                   // 1.25 s: hands back at 1.0625 s
            BodyRun(q, 1.00f, 0f);
            Equal(1f, q.ShotWeight, "at 1.00 s of a 1.25 s nod it still owns the body");
            q.Tick(0.02f, 0f);
            Equal(1f, q.ShotWeight, "and at 1.02 s, still short of 85%");
            q.Tick(0.05f, 0f);                                      // 1.07 s: releasing, one partial step out
            Check(q.ShotWeight > 0.1f && q.ShotWeight < 0.3f, "one step past the hand-back point it is part way out");
            float mid = q.ShotWeight;
            Check(q.Fire(BodyClip.Talk), "a different one-shot can start DURING the hand-back");
            Equal(mid, q.ShotWeight, "and picks the weight up exactly where the nod dropped it");
            Equal(0f, q.Weight(BodyClip.Nod), "the nod is out at once");
            Equal(mid, q.Weight(BodyClip.Talk), "and Talk is in at that weight, so the swap still has no gap");
            Check(Math.Abs(BodySum(q) - 1f) < 1e-5f, "and the weights sum to 1 across the swap");
            q.Tick(1f / 60f, 0f);
            Check(q.ShotWeight > mid, "then it blends UP from there: the release was cleared, not carried over");

            // ---- a hitch DURING the hand-back must not leave a negative weight --------------------------
            BodyMotion neg = new BodyMotion();
            neg.Fire(BodyClip.Shrug);                               // 2.00 s: hands back at 1.70 s
            BodyRun(neg, 1.68f, 0f);
            Equal(BodyClip.Shrug, neg.Current, "the shrug is still running just short of its hand-back");
            neg.Tick(0.05f, 0f);
            Check(neg.ShotWeight > 0f && neg.ShotWeight < 1f, "and one step later it is part way out");
            neg.Tick(BodyMotion.MaxStepSeconds, 0f);
            Equal(BodyClip.None, neg.Current, "a hitch through the rest of the blend ends the gesture rather than stalling it");
            Equal(0f, neg.ShotWeight, "and never leaves a NEGATIVE weight behind");
            Equal(1f, neg.Weight(BodyClip.Idle), "with the body back on Idle at full weight");

            // ---- a clip shorter than the blend-in still ends ---------------------------------------------
            // 85% of 0.05 s is 0.0425 s, inside OneShotBlendSeconds: the gesture starts releasing before it
            // has finished blending in. It must still reach zero and give the body back, not stick part way.
            BodyMotion tiny = new BodyMotion();
            tiny.SetLength(BodyClip.Nod, 0.05f);
            tiny.Fire(BodyClip.Nod);
            BodyRun(tiny, 0.50f, 0f);
            Equal(BodyClip.None, tiny.Current, "a clip shorter than the blend-in still ends instead of sticking at partial weight");
            Equal(1f, tiny.Weight(BodyClip.Idle), "and hands the whole body back");

            // ---- a cast that is not a clip reaches every accessor without throwing -----------------------
            // The driver indexes these with (BodyClip)i and the console with ByName; a bad value must be
            // answered, not thrown, because both call sites are inside a cosmetic try/catch that would
            // otherwise eat the body whole.
            BodyMotion b = new BodyMotion();
            Equal(0f, b.Weight((BodyClip)99), "Weight of a value that is not a clip is 0, not an exception");
            Equal(0f, b.Length((BodyClip)(-7)), "and its length is 0 too");
            Equal(0f, b.Length(BodyClip.None), "as is None's");
            b.SetLength((BodyClip)99, 5f);
            b.SetLength(BodyClip.None, 5f);
            Equal(BodyMotion.IdleLength, b.Length(BodyClip.Idle), "and SetLength on one changes no real clip's length");
            Check(!b.Fire((BodyClip)99), "and it cannot be fired");
            Equal(BodyClip.None, b.Current, "so nothing is playing after any of that");

            // ---- the invariant, over a long deterministic random walk ---------------------------------
            // Weights that do not sum to 1 are a body that half-fades into its bind pose; nothing on a
            // screen says so in words, so it is asserted here instead.
            var rnd = new Random(20260906);
            var clips = new[] { BodyClip.Hello, BodyClip.Talk, BodyClip.Shrug, BodyClip.Nod };
            BodyMotion w = new BodyMotion();
            float worstSum = 0f, walkSpeed = 0f;
            bool anyNaN = false, anyOutOfRange = false, sawWalk = false, sawIdle = false, sawShot = false;
            for (int i = 0; i < 40000; i++)
            {
                float dt = (float)(rnd.NextDouble() * 0.1 + 0.001);
                walkSpeed += (float)(rnd.NextDouble() - 0.5) * 0.4f;
                if (walkSpeed < -0.5f) walkSpeed = -0.5f;
                if (walkSpeed > 3f) walkSpeed = 3f;
                float sample = rnd.Next(200) == 0 ? float.NaN : walkSpeed;
                if (rnd.Next(120) == 0) w.Fire(clips[rnd.Next(clips.Length)]);
                w.Tick(dt, sample);

                float sum = 0f;
                for (int k = 0; k < BodyMotion.ClipCount; k++)
                {
                    float weight = w.Weights[k];
                    if (float.IsNaN(weight) || float.IsInfinity(weight)) anyNaN = true;
                    if (weight < -1e-6f || weight > 1f + 1e-6f) anyOutOfRange = true;
                    sum += weight;
                }
                worstSum = Math.Max(worstSum, Math.Abs(sum - 1f));
                if (w.Weight(BodyClip.Walk) > 0.9f) sawWalk = true;
                if (w.Weight(BodyClip.Idle) > 0.9f) sawIdle = true;
                if (w.Current != BodyClip.None) sawShot = true;
            }
            Check(worstSum < 1e-4f, "over 40,000 random steps the weights never stop summing to 1 (worst " + worstSum.ToString("0.0000000") + ")");
            Check(!anyNaN, "and no weight is ever NaN or infinite");
            Check(!anyOutOfRange, "and every weight stays inside [0, 1]");
            Check(sawWalk && sawIdle && sawShot, "the walk covered idling, walking and gesturing, so the invariant was tested on all three");
        }

        private static void TrayModelTests()
        {
            Section("TrayModel (the terminal's staging tray)");

            DemoMarket demo = DemoMarket.Default();
            MarketSnapshot m = demo.Market;
            MarketRow iron = m.Find("Iron"), bronze = m.Find("Bronze"), wood = m.Find("Wood"), amber = m.Find("Amber");
            var carry = new Dictionary<string, int> { { "Wood", 40 }, { "Amber", 3 }, { "Iron", 2 } };
            Func<string, int> has = pf => carry.ContainsKey(pf) ? carry[pf] : 0;

            TrayModel t = new TrayModel();
            Check(t.IsEmpty && t.Net == 0 && !t.AnyAmber, "a fresh tray is empty, even, and not amber");
            Equal(DealReason.EmptyDeal, t.Validate(m, 1000, has), "an empty tray validates as empty_deal");
            Check(TrayModel.Words(DealReason.EmptyDeal).Contains("Nothing in the tray"), "with words for the footer");

            // Buying.
            Check(t.StageBuy(iron, 2), "a ware stages");
            Equal("Iron", t.Wanted.Prefab, "as the wanted line");
            Equal(2, t.Wanted.Count, "with its count");
            Equal(25, t.Wanted.UnitPriceSeen, "at the price on screen");
            Equal(50, (int)t.Price, "so the price is count x unit");
            Check(t.StageBuy(iron, 3), "staging the same ware again");
            Equal(5, t.Wanted.Count, "adds to it");
            Check(t.StageBuy(bronze, 1), "staging another ware");
            Equal("Bronze", t.Wanted.Prefab, "replaces the wanted line: one wanted per deal");
            Check(!t.StageBuy(wood, 1), "a Want cannot be bought");
            Check(!t.StageBuy(iron, 0), "nor a count of 0");
            t.Clear();
            Check(t.StageBuy(iron, 999), "a count beyond his stock");
            Equal(20, t.Wanted.Count, "is clamped to it");
            t.Unstage("Iron", 5);
            Equal(15, t.Wanted.Count, "right-click takes some back");
            t.Unstage("Iron", 100);
            Check(t.Wanted == null, "and all of it empties the line");

            // Offering.
            Check(t.StageOffer(wood, 10, has("Wood")), "goods the player carries stage as an offer");
            Equal(10, t.Offered[0].Count, "with the count");
            Equal(1, t.Offered[0].UnitPriceSeen, "at what he pays");
            Check(t.StageOffer(wood, 100, has("Wood")), "more than the player carries");
            Equal(40, t.Offered[0].Count, "is clamped to what they carry");
            Check(!t.StageOffer(iron, 1, 0), "nothing carried, nothing offered");
            MarketRow fullRow = new MarketRow { Prefab = "Full", Kind = EntryKind.Want, Stock = 10, Max = 10, Sell = 5 };
            Check(!t.StageOffer(fullRow, 1, 50), "a full shelf refuses");
            Check(t.StageOffer(amber, 2, has("Amber")), "a ware can be offered back");
            Equal(2, t.Offered.Count, "two offered lines");
            Equal(40 * 1 + 2 * 5, (int)t.OfferedValue, "their value at his prices");
            Equal(-50, (int)t.Net, "a pure sell is a negative net: he pays");
            Equal(0, t.CoinsOffered, "and no coins on the table");
            t.Unstage("Wood", 40);
            Equal(1, t.Offered.Count, "taking a whole offer back removes the line");
            t.Clear();

            // Validate, in the server's order.
            t.StageBuy(iron, 2);
            Check(t.Validate(m, 50, has) == null, "2 Iron at 25 with 50 coins can go");
            Equal(DealReason.CoinsShort, t.Validate(m, 49, has), "with 49 it is coins_short");
            carry["Amber"] = 1;
            t.StageOffer(amber, 1, 1);
            carry["Amber"] = 0;
            Equal("missing_items", t.Validate(m, 1000, has), "an offer the player no longer carries is missing_items");
            carry["Amber"] = 1;
            Check(t.Validate(m, 1000, has) == null, "and fine again once they do");
            Equal(45, (int)t.Net, "50 for the iron less 5 for the amber");
            Equal(45, t.CoinsOffered, "is what goes on the table");
            Deal d = t.Build(7);
            Equal(7, d.VisitId, "Build carries the visit id");
            Check(d.Nonce != 0, "and a nonce");
            Equal("Iron", d.Wanted.Prefab, "the wanted line");
            Equal(25, d.Wanted.UnitPriceSeen, "at the price seen now");
            Equal(1, d.Offered.Count, "the offered line");
            Equal(5, d.Offered[0].UnitPriceSeen, "at his price now");
            Equal(45, d.CoinsOffered, "and the coins");
            Check(d.IsBarter, "a wanted line plus an offer is a barter");
            t.Clear();

            // Amber: the price moved under the tray.
            t.StageBuy(iron, 2);
            demo.Tick("Iron", true);
            MarketSnapshot moved = demo.Market;
            Check(moved.Find("Iron").Buy == 26, "the demo moved Iron to 26");
            Check(!t.Wanted.Amber, "the tray does not know yet");
            t.Refresh(moved);
            Check(t.Wanted.Amber && t.AnyAmber, "after Refresh the line is amber");
            Equal(26, t.Wanted.UnitPriceNow, "showing the new price");
            Equal(25, t.Wanted.UnitPriceSeen, "and remembering the old");
            Equal(52, (int)t.Price, "the tray totals at the price shown NOW");
            Deal d2 = t.Build(7);
            Equal(26, d2.Wanted.UnitPriceSeen, "confirming sends the price the player is looking at");
            Check(!t.Wanted.Amber, "and clears the amber: the new price is accepted");

            // Answers.
            DealResult ok = new DealResult { Ok = true, Nonce = 4, DeliveryId = "demo-1-1", ItemsToAdd = new List<DealLine> { d2.Wanted } };
            Check(t.Answer(ok, moved), "an accepted answer returns true");
            Check(t.IsEmpty, "and empties the tray");
            Equal(Lines.Buy[4 % Lines.Buy.Length], t.Message, "and he speaks a buy line picked by the nonce");
            t.StageOffer(moved.Find("Wood"), 5, 40);
            DealResult okSell = new DealResult { Ok = true, Nonce = 1, DeliveryId = "demo-1-2", ItemsToRemove = new List<DealLine> { new DealLine { Prefab = "Wood", Count = 5, UnitPriceSeen = 1 } } };
            t.Answer(okSell, moved);
            Equal(Lines.Sell[1], t.Message, "a sell gets a sell line");

            t.StageBuy(moved.Find("Iron"), 1);
            demo.Tick("Iron", true);
            MarketSnapshot moved2 = demo.Market;
            DealResult pc = DealResult.Refuse(9, DealReason.PriceChanged, moved2.Encode());
            Check(!t.Answer(pc, moved2), "price_changed returns false");
            Check(!t.IsEmpty && t.Wanted.Amber, "keeps the tray and turns the line amber");
            Equal(Lines.PriceChanged, t.Message, "with his line about the wind");
            Check(!t.Answer(DealResult.Refuse(9, DealReason.PurseEmpty), moved2), "a refusal returns false");
            Equal(Lines.RefusePurse, t.Message, "with his words for it");
            Check(!t.IsEmpty, "and the tray is kept");
            Check(!t.Answer(null, moved2), "a null answer is refused");

            // A vanished row drops out on Refresh.
            MarketSnapshot small = MarketSnapshot.Parse("v1;1;800;Bronze:Ware:20:20:60:15:11:0", null);
            t.Refresh(small);
            Check(t.Wanted == null, "a wanted line whose row vanished is dropped");

            // AutoFill: highest of his prices first, until the wanted line is covered; change in coins.
            TrayModel b = new TrayModel { Mode = PayMode.Barter };
            MarketSnapshot m2 = DemoMarket.Default().Market;
            b.StageBuy(m2.Find("Iron"), 1);                      // 25c
            var goods = new Dictionary<string, int> { { "Wood", 100 }, { "Amber", 3 }, { "Honey", 10 } };   // pays 1, 5, 1
            Func<string, int> hasB = pf => goods.ContainsKey(pf) ? goods[pf] : 0;
            int touched = b.AutoFill(m2, hasB);
            Check(touched >= 2, "it took more than one kind");
            Equal("Amber", b.Offered[0].Prefab, "the dearest first");
            Equal(3, b.Offered[0].Count, "all three he carries");
            Check(b.OfferedValue >= b.Price, "and enough to cover the iron");
            Check(b.Net <= 0, "so the player pays no coins");
            Equal(0, b.CoinsOffered, "none on the table");
            Check(b.Offered.Count <= 3, "and it stopped once covered");
            TrayModel c = new TrayModel();
            Equal(0, c.AutoFill(m2, hasB), "nothing wanted, nothing filled");

            // The offered-lines cap.
            TrayModel capped = new TrayModel();
            int added = 0;
            foreach (MarketRow r in m2.Rows) if (capped.StageOffer(r, 1, 5)) added++;
            Equal(TrayModel.MaxOfferedLines, added, "no more than MaxOfferedLines offered lines");
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

        // ---- FlightPlan: the flight's geometry, inside the active block (design 3.2) ----

        private static void FlightPlanTests()
        {
            Section("FlightPlan: zones and the active block");

            // The zone maths must agree with the game's, so the stub is the oracle: it carries
            // ZoneSystem.GetZone from the decompile, and a disagreement here is a real disagreement.
            for (float v = -200f; v <= 200f; v += 7.5f)
            {
                var z = ZoneSystem.GetZone(new UnityEngine.Vector3(v, 0f, v));
                if (FlightPlan.ZoneOf(v) != z.x) { Check(false, $"ZoneOf({v}) matches ZoneSystem.GetZone"); return; }
            }
            Check(true, "ZoneOf agrees with ZoneSystem.GetZone across 54 sample points on both axes");

            Check(FlightPlan.ZoneOf(0f) == 0 && FlightPlan.ZoneOf(31.9f) == 0 && FlightPlan.ZoneOf(32.1f) == 1,
                  "the zone boundary is at +32, not 0 (the zone is centred on its coordinate)");
            Check(FlightPlan.ZoneOf(-32.1f) == -1, "the boundary is symmetric below zero");

            // activeArea 1 = the pilot's own zone only; 2 = the 3x3 block around it.
            Check(FlightPlan.InActiveArea(0, 0, 0, 0, 1), "activeArea 1: the pilot's own zone is active");
            Check(!FlightPlan.InActiveArea(1, 0, 0, 0, 1), "activeArea 1: the next zone is NOT active");
            Check(FlightPlan.InActiveArea(1, 1, 0, 0, 2), "activeArea 2: the diagonal neighbour is active");
            Check(!FlightPlan.InActiveArea(2, 0, 0, 0, 2), "activeArea 2: two zones out is NOT active");

            Section("FlightPlan: the seed decides the flight, identically everywhere");

            for (int seed = 1; seed < 50000; seed += 977)
            {
                double b = FlightPlan.Bearing(seed);
                if (b < 0 || b >= Math.PI * 2) { Check(false, $"Bearing({seed}) is inside [0, 2pi)"); return; }
                float d = FlightPlan.DropDistance(seed);
                if (d < 12f || d > 15f) { Check(false, $"DropDistance({seed}) is 12-15 m"); return; }
            }
            Check(true, "Bearing stays inside [0, 2pi) and DropDistance inside 12-15 m across 52 seeds");
            Check(FlightPlan.Bearing(12345) == FlightPlan.Bearing(12345) &&
                  FlightPlan.DropDistance(12345) == FlightPlan.DropDistance(12345),
                  "the same seed gives the same bearing and drop distance (every machine agrees without a message)");
            Check(FlightPlan.Bearing(-7) == FlightPlan.Bearing(7), "a negative seed does not throw or wrap oddly");

            Section("FlightPlan: every waypoint lands inside the block");

            // The pilot in the middle of a 3x3 block: the configured 90 m should survive whole.
            var mid = FlightPlan.Make(0f, 30f, 0f, 4242, 2, 90f, 120f, 50f);
            Check(mid.Ok, "a pilot in the middle of a 3x3 block gets a plan");
            Check(Math.Abs(mid.StartDistance - 90f) < 0.01f, "and keeps the full 90 m start distance");
            Check(Math.Abs(mid.StartY - 150f) < 0.01f, "the start is the pilot's ground plus the 120 m altitude");
            Check(FlightPlan.PointInBlock(mid.StartX, mid.StartZ, 0f, 0f, 2), "the start is inside the block");
            Check(FlightPlan.PointInBlock(mid.DescentX, mid.DescentZ, 0f, 0f, 2), "the descent waypoint is inside the block");
            Check(FlightPlan.PointInBlock(mid.DropX, mid.DropZ, 0f, 0f, 2), "the drop is inside the block");

            // The drop is 12-15 m from the pilot, on the same bearing as the start: the bird comes
            // in along one line and puts him down short of you.
            double dropDist = Math.Sqrt(mid.DropX * mid.DropX + mid.DropZ * mid.DropZ);
            Check(dropDist >= 12f && dropDist <= 15f, "the drop is 12-15 m from the pilot");
            double startDist = Math.Sqrt(mid.StartX * mid.StartX + mid.StartZ * mid.StartZ);
            Check(Math.Abs(startDist - 90f) < 0.01f, "the start is the planned distance from the pilot");
            Check(Math.Abs(mid.StartX / startDist - mid.DropX / dropDist) < 0.001f &&
                  Math.Abs(mid.StartZ / startDist - mid.DropZ / dropDist) < 0.001f,
                  "start and drop share one bearing: the approach is a straight line in, not a fly-past");

            Section("FlightPlan: the approach is straight and the altitude is on the waypoint (PR #8)");

            // Both halves of this section are regressions. The first version swung the descent waypoint
            // sideways by the same distance it sat forward, and left it at the START altitude; the bird
            // could not fly to the first (it was inside its own turning circle) and dropped the merchant
            // 105 m up when it could. Neither was visible to the harness, so both are asserted now.

            // 1. Straight: the descent waypoint is ON the start-to-drop line.
            double crossMid = (mid.DescentX - mid.StartX) * (mid.DropZ - mid.StartZ)
                            - (mid.DescentZ - mid.StartZ) * (mid.DropX - mid.StartX);
            Check(Math.Abs(crossMid) < 0.05f,
                  "the descent waypoint is collinear with the start and the drop: the approach is one straight line");
            double dDesc = Math.Sqrt(mid.DescentX * mid.DescentX + mid.DescentZ * mid.DescentZ);
            Check(dDesc > dropDist && dDesc < startDist,
                  "and it sits BETWEEN them, not past either end");
            Check(Math.Abs(mid.DescentDistance - 50f) < 0.01f,
                  "at the configured 50 m short of the drop, when the block leaves room for it");

            // 2. The altitude: a steady glide, so the bird arrives at drop height instead of putting
            //    110 m of descent on the last four seconds of the flight.
            float runMid = (float)(startDist - dropDist);
            float expected = 30f + FlightPlan.DropAltitude + (120f - FlightPlan.DropAltitude) * (50f / runMid);
            Check(Math.Abs(mid.DescentY - expected) < 0.05f,
                  $"its altitude is the glide's, interpolated along the run ({mid.DescentY:0.0} m, not the start's {mid.StartY:0.0})");
            Check(mid.DescentY < mid.StartY - 1f && mid.DescentY > mid.DropY + 1f,
                  "which is strictly below the start and strictly above the drop");

            // 3. Reachability. A pure pursuer cannot capture a point inside its own turning circle, and
            //    at the shipped 8 m/s and 45 deg/s that circle is 10 m across. THIS is the check that
            //    would have caught the shipped bug off-game.
            float radius = FlightPlan.TurningRadius(8f, 45f);
            Check(Math.Abs(radius - 10.19f) < 0.02f,
                  $"TurningRadius(8 m/s, 45 deg/s) = {radius:0.00} m (v / omega, omega in radians)");
            Check(FlightPlan.TurningRadius(20f, 20f) > 57f && FlightPlan.TurningRadius(20f, 20f) < 58f,
                  "and the prefab's own 20 m/s at 20 deg/s is the 57 m circle that broke the first version");
            Check(FlightPlan.TurningRadius(8f, 0f) > 1e6f, "a flyer that cannot turn has an unbounded circle");

            // The bird starts pointing at the drop (Spawner writes that rotation), so its first heading
            // is the start-to-drop bearing; from the waypoint it is already on the line.
            Check(FlightPlan.Reachable(mid.StartX, mid.StartZ, mid.DropX - mid.StartX, mid.DropZ - mid.StartZ,
                                       mid.DescentX, mid.DescentZ, radius),
                  "the bird can reach its descent waypoint from the start");
            Check(FlightPlan.Reachable(mid.DescentX, mid.DescentZ, mid.DropX - mid.DescentX, mid.DropZ - mid.DescentZ,
                                       mid.DropX, mid.DropZ, radius),
                  "and the drop from the descent waypoint");

            // The regression witness: rebuild the waypoint the way the first version did -- 50 m forward
            // of the drop and 50 m to the side -- and show the same predicate refuses it at the prefab's
            // numbers. If a swing is ever reintroduced, this is the check that has to be argued with.
            {
                float bx = (float)(mid.StartX / startDist), bz = (float)(mid.StartZ / startDist);   // pilot -> start
                float swungX = -bz * 50f + bx * (float)(dropDist + 50f);
                float swungZ = bx * 50f + bz * (float)(dropDist + 50f);
                Check(!FlightPlan.Reachable(mid.StartX, mid.StartZ, mid.DropX - mid.StartX, mid.DropZ - mid.StartZ,
                                            swungX, swungZ, FlightPlan.TurningRadius(20f, 20f)),
                      "the ORIGINAL swung waypoint is inside the prefab bird's turning circle: unreachable, which is why it orbited for 180 s");
            }

            Section("FlightPlan: the shrink and the turn (design 3.2's active-block constraint)");

            // Every position in a 3x3 block, every bearing: the plan must never put a waypoint outside.
            int planned = 0, turned = 0, shrunk = 0, failed = 0, straight = 0;
            for (float px = -96f; px <= 96f; px += 8f)
            {
                for (float pz = -96f; pz <= 96f; pz += 8f)
                {
                    for (int seed = 1; seed < 4000; seed += 397)
                    {
                        var p = FlightPlan.Make(px, 30f, pz, seed, 2, 90f, 120f, 50f);
                        if (!p.Ok) { failed++; continue; }
                        planned++;
                        if (p.Turned) turned++;
                        if (p.StartDistance < 90f) shrunk++;
                        if (!FlightPlan.PointInBlock(p.StartX, p.StartZ, px, pz, 2) ||
                            !FlightPlan.PointInBlock(p.DescentX, p.DescentZ, px, pz, 2) ||
                            !FlightPlan.PointInBlock(p.DropX, p.DropZ, px, pz, 2))
                        {
                            Check(false, $"a waypoint left the block at pilot ({px}, {pz}) seed {seed}");
                            return;
                        }
                        double cr = (p.DescentX - p.StartX) * (p.DropZ - p.StartZ)
                                  - (p.DescentZ - p.StartZ) * (p.DropX - p.StartX);
                        if (Math.Abs(cr) > 0.05f)
                        { Check(false, $"the approach bent at pilot ({px}, {pz}) seed {seed}"); return; }
                        if (!(p.DescentY <= p.StartY + 0.01f && p.DescentY >= p.DropY + FlightPlan.DropAltitude - 0.01f))
                        { Check(false, $"the descent altitude left the glide at pilot ({px}, {pz}) seed {seed}"); return; }
                        if (!FlightPlan.Reachable(p.StartX, p.StartZ, p.DropX - p.StartX, p.DropZ - p.StartZ,
                                                  p.DescentX, p.DescentZ, FlightPlan.TurningRadius(8f, 45f)))
                        { Check(false, $"the bird could not reach its waypoint at pilot ({px}, {pz}) seed {seed}"); return; }
                        double toStart = Math.Sqrt(Math.Pow(p.DescentX - p.StartX, 2) + Math.Pow(p.DescentZ - p.StartZ, 2));
                        double toDrop = Math.Sqrt(Math.Pow(p.DescentX - p.DropX, 2) + Math.Pow(p.DescentZ - p.DropZ, 2));
                        if (toStart < 1f || toDrop < 1f)
                        { Check(false, $"the descent waypoint collapsed onto an end at pilot ({px}, {pz}) seed {seed}"); return; }
                        straight++;
                    }
                }
            }
            Check(failed == 0, $"every one of {planned} plans across a 3x3 block found room (0 failures)");
            Check(straight == planned && planned > 0,
                  $"and all {straight} of them are straight, keep a real two-leg glide, and are flyable at 8 m/s / 45 deg/s");
            // A 3x3 block always has 30 m of room somewhere along the seeded bearing, so the shrink
            // carries it alone and the turn never fires here. The turn is a one-zone measure; it is
            // asserted below, where the block is small enough to actually run out of room.
            Check(shrunk > 0 && turned == 0,
                  $"the block bites: {shrunk} of {planned} plans shrank the start distance, and none had to turn");

            // The tightest case the design admits: activeArea 1, one 64 m zone, the pilot in a corner.
            var corner = FlightPlan.Make(30f, 30f, 30f, 99, 1, 90f, 120f, 50f);
            Check(!corner.Ok || FlightPlan.PointInBlock(corner.StartX, corner.StartZ, 30f, 30f, 1),
                  "activeArea 1 in a zone corner: the plan either declines or stays inside the one zone");

            int oneZoneOk = 0, oneZoneNo = 0, oneZoneTurned = 0;
            for (float px = -28f; px <= 28f; px += 4f)
                for (float pz = -28f; pz <= 28f; pz += 4f)
                {
                    var p = FlightPlan.Make(px, 30f, pz, 7, 1, 90f, 120f, 50f);
                    if (p.Ok)
                    {
                        oneZoneOk++;
                        if (p.Turned) oneZoneTurned++;
                        if (!FlightPlan.PointInBlock(p.StartX, p.StartZ, px, pz, 1))
                        { Check(false, $"activeArea 1: start left the zone at ({px}, {pz})"); return; }
                        if (p.StartDistance > 64f) { Check(false, "activeArea 1: a start further than one zone"); return; }
                    }
                    else oneZoneNo++;
                }
            Check(oneZoneOk + oneZoneNo == 225 && oneZoneOk > 0,
                  $"activeArea 1 (one 64 m zone): {oneZoneOk} of 225 positions still get a flight, {oneZoneNo} decline");
            // This is what the turn is for: one seed, one bearing, and the pilot standing where that
            // bearing points at the zone wall. Without the turn these positions would all decline.
            Check(oneZoneTurned > 0,
                  $"and {oneZoneTurned} of them only found room by turning the bearing a quarter at a time");

            Section("FlightPlan: a declined plan is safe to use");

            var none = FlightPlan.Make(31.9f, 30f, 31.9f, 1, 1, 90f, 120f, 50f);
            Check(none.Ok || (none.StartDistance == 0f && none.DropX == 31.9f && none.DropZ == 31.9f),
                  "a declined plan drops on the pilot rather than returning nonsense to fly");
            Check(!double.IsNaN(none.Bearing), "a declined plan still carries a readable bearing");
            Check(none.Ok || Math.Abs(none.DescentY - (30f + FlightPlan.DropAltitude)) < 0.01f,
                  "and parks its descent waypoint at drop height, so nothing reads a 120 m altitude off a flight that is not flown");

            Section("FlightPlan: the drop the pilot reports is bounded by the drop the server authored");

            // The bird's ZDO is owned by the PILOT, so `VCargo_target` is a value a client writes. Before
            // this bound, `Spawner.Tick` handed it to the visit untouched: a modified client could put
            // the drop point -- and, with P5, Ingvar himself -- anywhere in the world. P11's authority
            // audit found it; this is the check that stands between that key and the world.
            const float ax = 120f, ay = 40f, az = -80f;

            // The honest case, and the ONLY thing a legitimate client does: CargoFlight.Drop writes the
            // authored point back with its y replaced by the terrain height under it.
            Check(FlightPlan.DropAccepted(ax, ay - 18f, az, ax, ay, az),
                  "the authored point with the ground height in its y is accepted");
            Check(FlightPlan.DropAccepted(ax, ay, az, ax, ay, az),
                  "and so is the authored point unchanged");

            // The exploit itself.
            Check(!FlightPlan.DropAccepted(5000f, ay, 5000f, ax, ay, az),
                  "a drop on the other side of the world is refused");
            Check(!FlightPlan.DropAccepted(ax + 20f, ay, az, ax, ay, az),
                  "and so is one 20 m away, which is enough to put him through a wall");

            // Each half of the bound proves itself: move only in XZ, then only in y.
            Check(!FlightPlan.DropAccepted(ax, ay, az + FlightPlan.DropToleranceXZ + 0.5f, ax, ay, az),
                  "just outside the horizontal tolerance is refused (the XZ half is live)");
            Check(FlightPlan.DropAccepted(ax, ay, az + FlightPlan.DropToleranceXZ - 0.5f, ax, ay, az),
                  "just inside it is accepted");
            Check(!FlightPlan.DropAccepted(ax, ay + FlightPlan.DropToleranceY + 1f, az, ax, ay, az),
                  "a drop 65 m above the authored altitude is refused (the vertical half is live)");
            Check(FlightPlan.DropAccepted(ax, ay - FlightPlan.DropToleranceY + 1f, az, ax, ay, az),
                  "63 m below it -- a real mountainside -- is accepted");

            // The tolerance is a RADIUS, not a box: 6 m on each axis is 8.49 m away.
            Check(!FlightPlan.DropAccepted(ax + 6f, ay, az + 6f, ax, ay, az),
                  "6 m on each axis is 8.49 m out and refused: the horizontal bound is a circle, not a square");

            // A float is three keystrokes to forge, and every comparison against NaN is false, so the
            // natural spelling of this check (`d > tolerance`) ACCEPTS a NaN and writes it into the
            // session row, the wire and the sidecar.
            Check(!FlightPlan.DropAccepted(float.NaN, ay, az, ax, ay, az), "a NaN x is refused");
            Check(!FlightPlan.DropAccepted(ax, float.NaN, az, ax, ay, az), "a NaN y is refused");
            Check(!FlightPlan.DropAccepted(ax, ay, float.NaN, ax, ay, az), "a NaN z is refused");
            Check(!FlightPlan.DropAccepted(float.PositiveInfinity, ay, az, ax, ay, az), "an infinite x is refused");
            Check(!FlightPlan.DropAccepted(ax, float.NegativeInfinity, az, ax, ay, az), "an infinite y is refused");
        }

        private static void MerchantPlanTests()
        {
            Section("MerchantPlan: the carry, and what ends it");

            var s = MerchantPlan.Next(0, carried: true, grounded: false, distance: 40f, timeInState: 3f, farSeconds: 0f, approachDistance: 3.5f);
            Check(s.State == 0 && !s.Changed && !s.Follow, "carried and still hanging: he stays in state 0 and does not walk");

            // The bird cuts the link 10 m up. Landing on the state change rather than the link is
            // what stops the arrival effect firing while he is still in the air.
            s = MerchantPlan.Next(0, carried: false, grounded: false, distance: 14f, timeInState: 0.1f, farSeconds: 0f, approachDistance: 3.5f);
            Check(s.State == 0 && !s.Changed, "link cut but still falling: still state 0, no landing yet");

            s = MerchantPlan.Next(0, carried: false, grounded: true, distance: 14f, timeInState: 1f, farSeconds: 0f, approachDistance: 3.5f);
            Check(s.State == 1 && s.Changed && s.Follow && !s.CallOut,
                  "feet on the ground: state 1, he starts walking, and he does NOT call out yet");

            Section("MerchantPlan: the approach, and the timeout that saves it");

            s = MerchantPlan.Next(1, false, true, distance: 9f, timeInState: 4f, farSeconds: 0f, approachDistance: 3.5f);
            Check(s.State == 1 && !s.Changed && s.Follow, "still too far: keeps walking");

            s = MerchantPlan.Next(1, false, true, distance: 3.4f, timeInState: 4f, farSeconds: 0f, approachDistance: 3.5f);
            Check(s.State == 2 && s.Changed && s.CallOut, "inside ApproachDistance: state 2 and the callout fires");

            s = MerchantPlan.Next(1, false, true, distance: 60f, timeInState: 20f, farSeconds: 0f, approachDistance: 3.5f);
            Check(s.State == 2 && s.Changed && s.CallOut,
                  "unreachable player, 20 s gone: he stops and calls out anyway rather than walking into a wall forever");

            s = MerchantPlan.Next(1, false, true, distance: 60f, timeInState: 19.9f, farSeconds: 0f, approachDistance: 3.5f);
            Check(s.State == 1 && !s.CallOut, "and not one tick before 20 s");

            // `TimedOut` is what tells the two ways into trading apart. It is the whole reason the
            // 2026-09-07 visit's "gave up walking after 20 s" reads as a failure rather than an
            // arrival, and `CargoMerchant` writes its walk diagnosis off it - so a step that reached
            // the player must NEVER carry it, or every healthy visit logs a defect.
            s = MerchantPlan.Next(1, false, true, distance: 60f, timeInState: 20f, farSeconds: 0f, approachDistance: 3.5f);
            Check(s.TimedOut, "the 20 s fallback is flagged TimedOut, so the caller need not match on Why");

            s = MerchantPlan.Next(1, false, true, distance: 3.4f, timeInState: 4f, farSeconds: 0f, approachDistance: 3.5f);
            Check(!s.TimedOut, "but a real arrival is not: reaching the player carries no TimedOut");

            s = MerchantPlan.Next(1, false, true, distance: 3.4f, timeInState: 999f, farSeconds: 0f, approachDistance: 3.5f);
            Check(s.State == 2 && s.Changed && !s.TimedOut,
                  "and arriving on the very tick the timeout would have fired still counts as arriving");

            // The callout is once per visit: state 2 never re-enters itself.
            int callouts = 0;
            int st = 1; float t = 0f;
            for (int i = 0; i < 400; i++)
            {
                var step = MerchantPlan.Next(st, false, true, 2f, t, 0f, 3.5f);
                if (step.CallOut) callouts++;
                t = step.Changed ? 0f : t + 0.05f;
                st = step.State;
            }
            Check(callouts == 1, $"400 ticks beside the player produce exactly one callout (got {callouts})");

            Section("MerchantPlan: the trading leash");

            s = MerchantPlan.Next(2, false, true, distance: 13f, timeInState: 30f, farSeconds: 4.9f, approachDistance: 3.5f);
            Check(s.State == 2 && !s.Changed, "13 m for 4.9 s: he waits, he does not chase");

            s = MerchantPlan.Next(2, false, true, distance: 13f, timeInState: 30f, farSeconds: 5f, approachDistance: 3.5f);
            Check(s.State == 1 && s.Changed && s.Follow, "13 m for a full 5 s: he walks after them");

            s = MerchantPlan.Next(2, false, true, distance: 11.9f, timeInState: 30f, farSeconds: 60f, approachDistance: 3.5f);
            Check(s.State == 2, "inside 12 m, however long: he stays put (the distance gate is AND, not OR)");

            // The timer itself: it must reset on the way in, or a player who steps out and back
            // still sends him walking a minute later.
            float far = 0f;
            far = MerchantPlan.AccumulateFar(far, 20f, 1f);
            far = MerchantPlan.AccumulateFar(far, 20f, 1f);
            Check(Math.Abs(far - 2f) < 0.001f, "the far timer accumulates while he is outside the leash");
            far = MerchantPlan.AccumulateFar(far, 5f, 1f);
            Check(far == 0f, "and resets to zero the moment the player is back inside it");

            Section("MerchantPlan: leaving is terminal, and the restart rule");

            foreach (bool carried in new[] { true, false })
                foreach (bool grounded in new[] { true, false })
                {
                    var leaving = MerchantPlan.Next(3, carried, grounded, 1f, 100f, 100f, 3.5f);
                    if (leaving.State != 3 || leaving.Changed)
                    { Check(false, "leaving was pulled back out of state 3"); return; }
                }
            Check(true, "nothing measured on the ground pulls him back out of leaving");

            // The ZDOID trap: after a world reload every id in the save is renumbered, so a merchant
            // restored in state 0 with a stale carrier id must NOT be pinned to whatever now holds
            // that number. ShouldPin requires the carrier to have actually resolved.
            Check(MerchantPlan.ShouldPin(0, carrierResolved: true), "state 0 with a live carrier: pin him to the talon");
            Check(!MerchantPlan.ShouldPin(0, carrierResolved: false),
                  "state 0 with a carrier id that resolves to nothing (a restart renumbered it): do NOT pin");
            Check(!MerchantPlan.ShouldPin(1, carrierResolved: true), "and never pin once he is on his feet");

            Section("VisitSession.VisitIdOf: the boot sweep's peek (PR #15's review)");

            // The bug this exists to stop: at boot a restored row is NOT adopted yet, so the session
            // is inactive and its VisitId is 0. A sweep on that 0 destroys the merchant of the visit
            // that is about to resume.
            var vsPeek = new VisitSession();
            vsPeek.Begin(41, 700L, "Pilot", 5f, 6f, 7f, 1.0, 300f, 800, 12345);
            string savedRow = vsPeek.EncodeSessionRow();
            Check(VisitSession.VisitIdOf(savedRow) == 41,
                  "the visit id is readable from a saved session row without adopting it");

            var fresh = new VisitSession();
            Check(!fresh.Active && fresh.VisitId == 0,
                  "and a session that has not adopted that row yet still reads Active=false, VisitId=0 (which is the trap)");

            Check(VisitSession.VisitIdOf(null) == 0, "no row: 0");
            Check(VisitSession.VisitIdOf("") == 0, "empty row: 0");
            Check(VisitSession.VisitIdOf("notasession\t41") == 0, "a row that is not a session row: 0");
            Check(VisitSession.VisitIdOf("session\t41\ttoofewfields") == 0, "a truncated session row: 0");
            Check(VisitSession.VisitIdOf(savedRow.Replace("session\t41", "session\tzzz")) == 0,
                  "a session row whose id does not parse: 0");
            Check(VisitSession.VisitIdOf(savedRow.Replace("session\t41", "session\t0")) == 0,
                  "a session row claiming visit 0: 0, so it can never be mistaken for a live visit");
            Check(VisitSession.VisitIdOf(savedRow.Replace("session\t41", "session\t-5")) == 0,
                  "and a NEGATIVE id is 0 too (the id-0 case above passes with or without the guard, so it proves nothing alone)");
            Check(VisitSession.VisitIdOf(savedRow + "\r") == 41, "a row with a trailing CR still parses (Windows sidecar)");

        }

        /// <summary>
        /// Issue #16's centralisation: every ZDO key and RPC name lives once, in `Core/Keys.cs`. Read by
        /// reflection rather than a hand-typed list of the 21 names, so a future addition to `Keys` is
        /// covered automatically instead of silently skipped by a harness nobody remembered to update.
        /// </summary>
        private static void KeysTests()
        {
            Section("Keys (every VCargo_ name, in one place)");

            FieldInfo[] fields = typeof(Keys)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
                .ToArray();

            Check(fields.Length >= 21, "at least the 21 names issue #16 inventoried are present (found " + fields.Length + ")");

            var values = new List<string>();
            foreach (FieldInfo f in fields)
            {
                string v = (string)f.GetRawConstantValue();
                values.Add(v);
                Check(!string.IsNullOrEmpty(v), "Keys." + f.Name + " is not empty");
                Check(v.StartsWith("VCargo_", StringComparison.Ordinal),
                      "Keys." + f.Name + " ('" + v + "') carries the VCargo_ prefix, not the old two-letter one");
            }

            var distinct = new HashSet<string>(values, StringComparer.Ordinal);
            Equal(values.Count, distinct.Count,
                  "no two Keys constants collide (" + values.Count + " names declared, " + distinct.Count + " distinct)");
        }

        // ---- BarrkBOT export (BARRKBOT_CONTRACT.md) --------------------------------------------

        /// <summary>An accepted DealResult: buy (coins to Ingvar, items to the player) when coins is negative, sell the other way, 0 for a barter that nets out even.</summary>
        private static DealResult AcceptedDeal(int coins, (string prefab, int count)[] added, (string prefab, int count)[] removed)
        {
            var r = new DealResult { Ok = true, DeliveryId = "w-1-1", Nonce = 1, CoinsDelta = coins };
            foreach (var (prefab, count) in added ?? new (string, int)[0]) r.ItemsToAdd.Add(new DealLine { Prefab = prefab, Count = count, UnitPriceSeen = 1 });
            foreach (var (prefab, count) in removed ?? new (string, int)[0]) r.ItemsToRemove.Add(new DealLine { Prefab = prefab, Count = count, UnitPriceSeen = 1 });
            return r;
        }

        private static void TraderLedgerTests()
        {
            Section("TraderLedger (per-player totals for barrkbot_cargo_traders.json)");

            TraderLedger l = new TraderLedger();
            Equal(0, l.Count, "a fresh ledger has no rows");

            l.Record("", "Nobody", AcceptedDeal(-10, new[] { ("Iron", 2) }, null));
            l.Record(null, "Nobody", AcceptedDeal(-10, new[] { ("Iron", 2) }, null));
            l.Record("steam1", "Don", null);
            l.Record("steam1", "Don", DealResult.Refuse(1, DealReason.SoldOut));
            Equal(0, l.Count, "an empty key, a null result and a refusal all record nothing");

            // A buy: coins negative (spent), items added (bought).
            l.Record("steam1", "Don", AcceptedDeal(-50, new[] { ("Iron", 2), ("Wood", 3) }, null));
            Equal(1, l.Count, "the first accepted deal creates the row");
            TraderRow don = l.Rows["steam1"];
            Equal("Don", don.Name, "the display name is stored");
            Equal(1, don.DealsSettled, "one deal settled");
            Equal(50L, don.CoinsSpent, "CoinsDelta -50 is 50 coins spent");
            Equal(0L, don.CoinsEarned, "and nothing earned");
            Equal(5L, don.ItemsBought, "2 Iron + 3 Wood added = 5 items bought");
            Equal(0L, don.ItemsSold, "nothing sold yet");

            // A sell: coins positive (earned), items removed (sold).
            l.Record("steam1", "Don", AcceptedDeal(20, null, new[] { ("DeerHide", 4) }));
            Equal(2, don.DealsSettled, "a second deal settled");
            Equal(50L, don.CoinsSpent, "spent is unchanged by a sale");
            Equal(20L, don.CoinsEarned, "CoinsDelta +20 is 20 coins earned");
            Equal(4L, don.ItemsSold, "4 DeerHide removed = 4 items sold");

            // A barter that nets exactly zero: neither coins field moves, but the deal still counts.
            l.Record("steam1", "Don", AcceptedDeal(0, new[] { ("Iron", 1) }, new[] { ("Wood", 25) }));
            Equal(3, don.DealsSettled, "a zero-net barter still counts as a settled deal");
            Equal(50L, don.CoinsSpent, "CoinsDelta 0 moves neither coins field");
            Equal(20L, don.CoinsEarned, "same");
            Equal(6L, don.ItemsBought, "but items still move: +1 bought");
            Equal(29L, don.ItemsSold, "and +25 sold");

            // A negative Count on a line is defensive-clamped, never subtracted.
            var forged = new DealResult { Ok = true, DeliveryId = "w-1-2", Nonce = 2, CoinsDelta = -1 };
            forged.ItemsToAdd.Add(new DealLine { Prefab = "Iron", Count = -99, UnitPriceSeen = 1 });
            l.Record("steam1", "Don", forged);
            Equal(6L, don.ItemsBought, "a negative line count contributes 0, never a negative amount");

            // A second player gets a separate row; an empty new name does not overwrite the stored one.
            l.Record("steam2", "Kyr", AcceptedDeal(-5, new[] { ("Wood", 1) }, null));
            Equal(2, l.Count, "a second distinct player key is a second row");
            l.Record("steam1", "", AcceptedDeal(-1, new[] { ("Wood", 1) }, null));
            Equal("Don", l.Rows["steam1"].Name, "an empty display name never overwrites a real one");
            l.Record("steam1", "Donatello", AcceptedDeal(-1, new[] { ("Wood", 1) }, null));
            Equal("Donatello", l.Rows["steam1"].Name, "a real rename does");

            l.Clear();
            Equal(0, l.Count, "Clear forgets every row");
        }

        private static void VisitHistoryTests()
        {
            Section("VisitHistory (visits this session, for barrkbot_cargo_visits.json)");

            DateTime t0 = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
            VisitHistory h = new VisitHistory(capacity: 3);
            Equal(0, h.Count, "a fresh history has no rows");

            h.Record(0, "Nobody", t0, t0, 10, 5, "timer");
            h.Record(-1, "Nobody", t0, t0, 10, 5, "timer");
            Equal(0, h.Count, "visit id 0 or negative is never a real visit and records nothing");

            h.Record(1, "Don", t0, t0.AddSeconds(300), 300, 0, "timer");
            Equal(1, h.Count, "a real visit records");
            VisitRecord v1 = h.Rows[0];
            Equal(1, v1.VisitId, "id");
            Equal("Don", v1.PilotName, "pilot");
            Equal(300.0, v1.DurationSeconds, "duration");
            Equal(0, v1.Takings, "takings");
            Equal("timer", v1.EndedReason, "reason");

            h.Record(2, null, t0, t0, -50, -5, null);
            VisitRecord v2 = h.Rows[1];
            Equal(0.0, v2.DurationSeconds, "a negative duration is clamped to 0, never carried through as a negative number");
            Equal(0, v2.Takings, "and so is a negative takings");
            Equal("", v2.PilotName, "a null pilot name becomes empty, not null (so the JSON writer never has to null-check it)");
            Equal("", v2.EndedReason, "same for a null reason");

            // Oldest-first, and bounded: the 4th Record on a capacity-3 history evicts visit #1.
            h.Record(3, "P3", t0, t0, 10, 1, "timer");
            Equal(3, h.Count, "still at capacity");
            h.Record(4, "P4", t0, t0, 10, 1, "timer");
            Equal(3, h.Count, "capacity holds: the oldest was evicted, not appended past it");
            Check(h.Rows[0].VisitId == 2, "visit #1 (the oldest) is gone; #2 is now the oldest");
            Check(h.Rows[2].VisitId == 4, "and #4 (the newest) is last, oldest-first order preserved");

            h.Clear();
            Equal(0, h.Count, "Clear forgets every visit");
        }

        private static void BarrkRolloverTests()
        {
            Section("BarrkRollover (the v4 export's pagination and leaderboards)");

            // ---- Paginate ---------------------------------------------------------------------
            List<List<int>> emptyParts = BarrkRollover.Paginate(new List<int>(), 2600);
            Equal(1, emptyParts.Count, "an empty roster still writes one part");
            Equal(0, emptyParts[0].Count, "and that part is empty");
            List<List<int>> nullParts = BarrkRollover.Paginate(null, 2600);
            Equal(1, nullParts.Count, "null rowWidths is treated the same as empty, not a throw");
            Equal(0, nullParts[0].Count, "and that part is empty too");

            // The exact boundary: two 1000-char rows sum to exactly the 2000 budget and MUST share a
            // part (the contract's own "> cap", not ">="); the third pushes a new one.
            List<List<int>> exact = BarrkRollover.Paginate(new List<int> { 1000, 1000, 1000 }, 2000);
            Equal(2, exact.Count, "1000+1000 fits the 2000 budget exactly; the third row needs a new part");
            CollectionsEqual(new List<string> { "0", "1" }, IndicesAsStrings(exact[0]), "part 1 holds rows 0 and 1");
            CollectionsEqual(new List<string> { "2" }, IndicesAsStrings(exact[1]), "part 2 holds row 2 alone");

            List<List<int>> oneOver = BarrkRollover.Paginate(new List<int> { 1000, 1001 }, 2000);
            Equal(2, oneOver.Count, "one character over the budget still forces a new part");

            // A row wider than the whole budget is never dropped and never merged with a neighbour.
            List<List<int>> oversized = BarrkRollover.Paginate(new List<int> { 5000, 100 }, 2600);
            Equal(2, oversized.Count, "an oversized row still gets a part of its own");
            CollectionsEqual(new List<string> { "0" }, IndicesAsStrings(oversized[0]), "alone in the first part");
            CollectionsEqual(new List<string> { "1" }, IndicesAsStrings(oversized[1]), "the next row starts the next part, not appended to the oversized one");

            // Every index appears exactly once, across however many parts, in order -- the property
            // that makes the split safe: nothing is lost, nothing is duplicated.
            List<int> widths = new List<int> { 900, 900, 900, 900, 900, 900, 900 };
            List<List<int>> many = BarrkRollover.Paginate(widths, 2600);
            List<int> seen = new List<int>();
            foreach (List<int> part in many) seen.AddRange(part);
            CollectionsEqual(new List<string> { "0", "1", "2", "3", "4", "5", "6" }, IndicesAsStrings(seen), "every row appears exactly once, in order, across all parts");

            // ---- TopN ---------------------------------------------------------------------------
            List<LeaderEntry> candidates = new List<LeaderEntry>
            {
                new LeaderEntry { Credit = "A", Value = 10 },
                new LeaderEntry { Credit = "B", Value = 30 },
                new LeaderEntry { Credit = "C", Value = 20 },
                new LeaderEntry { Credit = "D", Value = 0 },
                new LeaderEntry { Credit = "E", Value = -5 },
                new LeaderEntry { Credit = "F", Value = double.NaN },
                new LeaderEntry { Credit = "G", Value = double.PositiveInfinity },
                null,
            };
            List<LeaderEntry> top = BarrkRollover.TopN(candidates, 3);
            Equal(3, top.Count, "top 3 of 8, after exclusions");
            Check(top[0].Credit == "B" && top[1].Credit == "C" && top[2].Credit == "A", "highest first: B (30), C (20), A (10)");
            Check(top.TrueForAll(e => e.Credit != "D" && e.Credit != "E" && e.Credit != "F" && e.Credit != "G"),
                  "zero, negative, NaN and infinite values are excluded -- 'unmeasured', never a false last place");

            // Isolated from the top-3 cutoff above (there D's rank-4 finish would hide a broken filter
            // just as well as a correct one): a roster of ONLY unrankable values must come back empty.
            List<LeaderEntry> onlyExcluded = new List<LeaderEntry>
            {
                new LeaderEntry { Credit = "D", Value = 0 },
                new LeaderEntry { Credit = "E", Value = -5 },
                new LeaderEntry { Credit = "F", Value = double.NaN },
                new LeaderEntry { Credit = "G", Value = double.PositiveInfinity },
            };
            Equal(0, BarrkRollover.TopN(onlyExcluded, 3).Count, "a roster with nothing rankable comes back empty, not padded with zeroes or NaNs");

            List<LeaderEntry> ties = new List<LeaderEntry>
            {
                new LeaderEntry { Credit = "First", Value = 10 },
                new LeaderEntry { Credit = "Second", Value = 10 },
                new LeaderEntry { Credit = "Third", Value = 10 },
            };
            List<LeaderEntry> tieTop = BarrkRollover.TopN(ties, 2);
            Check(tieTop[0].Credit == "First" && tieTop[1].Credit == "Second", "a genuine tie keeps the input's own order (a stable sort), not an arbitrary one");

            Equal(0, BarrkRollover.TopN(candidates, 0).Count, "n=0 asks for nothing and gets nothing");
            Equal(0, BarrkRollover.TopN(null, 3).Count, "a null candidate list is empty, not a throw");
            Equal(2, BarrkRollover.TopN(new List<LeaderEntry> { new LeaderEntry { Credit = "Only1", Value = 1 }, new LeaderEntry { Credit = "Only2", Value = 2 } }, 5).Count,
                  "fewer candidates than n returns all of them, not padded");
        }

        private static List<string> IndicesAsStrings(IEnumerable<int> indices)
        {
            List<string> s = new List<string>();
            foreach (int i in indices) s.Add(i.ToString());
            return s;
        }

        private static void BarrkExportTests()
        {
            Section("BarrkExport (the payload shaping BarrkBotExport.cs renders to JSON)");

            Equal(0, BarrkExport.MarketRows(null).Count, "a null market shapes to an empty row list, not a throw");
            Equal(0, BarrkExport.TraderRows(null).Count, "same for a null trader ledger");
            Equal(0, BarrkExport.VisitRows(null).Count, "and a null visit history");

            Market m = NewMarket(0);
            List<MarketExportRow> marketRows = BarrkExport.MarketRows(m);
            Equal(m.Count, marketRows.Count, "one export row per catalogue entry");
            MarketExportRow bronze = marketRows.Find(r => r.Prefab == "Bronze");
            Check(bronze != null, "the shipped catalogue's Bronze row is present");
            Equal("Ware", bronze.Kind, "Bronze is a Ware");
            Check(bronze.Purchasable, "and so purchasable is true");
            MarketItem bronzeItem = m.Find("Bronze");
            Equal(m.Charge(bronzeItem), bronze.BuyPrice, "buy_price is exactly Market.Charge, not a re-derived copy that could disagree");
            Equal(m.Pays(bronzeItem), bronze.SellPrice, "sell_price is exactly Market.Pays");
            Equal(m.Trend(bronzeItem), bronze.Trend, "trend is exactly Market.Trend");
            Equal(bronzeItem.Entry.TargetStock, bronze.TargetStock, "target_stock");
            Equal(bronzeItem.Entry.MaxStock, bronze.MaxStock, "max_stock");

            MarketExportRow wood = marketRows.Find(r => r.Prefab == "Wood");
            Check(wood != null, "the shipped catalogue's Wood row is present");
            Equal("Want", wood.Kind, "Wood is a Want");
            Check(!wood.Purchasable, "so purchasable is false, even though it still carries a buy_price for the trend arrow");

            TraderLedger tl = new TraderLedger();
            tl.Record("steam1", "Don", AcceptedDeal(-30, new[] { ("Iron", 1) }, null));
            tl.Record("steam2", "Kyr", AcceptedDeal(15, null, new[] { ("Wood", 5) }));
            List<TraderExportRow> traderRows = BarrkExport.TraderRows(tl);
            Equal(2, traderRows.Count, "one row per trading player");
            TraderExportRow donRow = traderRows.Find(r => r.PlayerKey == "steam1");
            Equal("Don", donRow.Name, "the key and the display name both carry through");
            Equal(30L, donRow.CoinsSpent, "and the totals");
            Equal(1L, donRow.DealsSettled, "");

            DateTime t0 = new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);
            VisitHistory vh = new VisitHistory();
            vh.Record(1, "Don", t0, t0.AddSeconds(300), 300, 10, "timer");
            vh.Record(2, "Kyr", t0.AddSeconds(1000), t0.AddSeconds(1100), 100, 999, "dismissed by Kyr");
            List<VisitExportRow> visitRows = BarrkExport.VisitRows(vh);
            Equal(2, visitRows.Count, "one row per ended visit");
            Equal(2, visitRows[0].VisitId, "NEWEST first: visit #2 (recorded second) leads, matching what a member asks about first");
            Equal(1, visitRows[1].VisitId, "visit #1 is last");

            // ---- leaders: ranked by a field selector, credited by the right subject -------------
            List<LeaderEntry> byBuyPrice = BarrkExport.MarketLeaders(marketRows, r => r.BuyPrice);
            Check(byBuyPrice.Count > 0, "at least one purchasable/priced row ranks");
            for (int i = 1; i < byBuyPrice.Count; i++)
                Check(byBuyPrice[i - 1].Value >= byBuyPrice[i].Value, "MarketLeaders is sorted highest first");

            List<LeaderEntry> byCoinsSpent = BarrkExport.TraderLeaders(traderRows, r => r.CoinsSpent);
            Check(byCoinsSpent.Count == 1 && byCoinsSpent[0].Credit == "Don" && byCoinsSpent[0].Value == 30.0,
                  "TraderLeaders credits by name (Kyr spent 0, so only Don -- who has coins_spent > 0 -- ranks)");

            List<LeaderEntry> byTakings = BarrkExport.VisitLeaders(visitRows, r => r.Takings);
            Check(byTakings.Count == 2 && byTakings[0].Credit == "Kyr" && byTakings[1].Credit == "Don",
                  "VisitLeaders credits by pilot, highest takings first (Kyr 999, Don 10)");
        }

        private static void SidecarThenMirrorTests()
        {
            Section("SidecarThenMirror (decision 4: the sidecar succeeds first, a mirror throw never reaches the caller)");

            List<string> order = new List<string>();
            bool ok = SidecarThenMirror.Run(
                () => { order.Add("primary"); return true; },
                () => { order.Add("mirror"); });
            Check(ok, "a successful primary is reported back");
            CollectionsEqual(new List<string> { "primary", "mirror" }, order, "primary runs to completion BEFORE the mirror is even started");

            order.Clear();
            bool okFalse = SidecarThenMirror.Run(
                () => { order.Add("primary"); return false; },
                () => { order.Add("mirror"); });
            Check(!okFalse, "a failed primary is reported back as failure");
            CollectionsEqual(new List<string> { "primary" }, order, "the mirror never runs when the primary did not succeed");

            order.Clear();
            bool okThrow = SidecarThenMirror.Run(
                () => { order.Add("primary"); throw new InvalidOperationException("disk full"); },
                () => { order.Add("mirror"); });
            Check(!okThrow, "a primary that throws is treated as a failure, not propagated (a second line of defence on top of Store.Save's own promise never to throw)");
            CollectionsEqual(new List<string> { "primary" }, order, "and the mirror still never runs");

            Exception caught = null;
            bool okMirrorThrows = SidecarThenMirror.Run(() => true, () => throw new InvalidOperationException("mirror bug"), ex => caught = ex);
            Check(okMirrorThrows, "a mirror that throws does not change the primary's own (successful) result");
            Check(caught != null && caught.Message == "mirror bug", "the exception reaches onMirrorFailed instead of the caller");

            Check(SidecarThenMirror.Run(() => true, () => throw new InvalidOperationException("x"), null), "a null onMirrorFailed still swallows the mirror's throw rather than propagating it");
            Check(!SidecarThenMirror.Run(null, () => { }), "a null primary is treated as failure, not a throw");
            Check(SidecarThenMirror.Run(() => true, null), "a null mirror is simply skipped");
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