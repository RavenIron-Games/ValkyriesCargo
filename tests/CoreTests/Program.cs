using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RavenIron.ValkyriesCargo.Core;

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
    }
}
