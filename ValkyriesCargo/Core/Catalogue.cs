using System;
using System.Collections.Generic;
using System.Globalization;

namespace RavenIron.ValkyriesCargo.Core
{
    /// <summary>Ware: Ingvar sells it and buys it back. Want: he only buys it.</summary>
    public enum EntryKind
    {
        Ware,
        Want,
    }

    /// <summary>One line of the catalogue. Prices in coins, stock in units.</summary>
    public sealed class CatalogueEntry
    {
        public string Prefab;
        public int BasePrice;
        public int TargetStock;
        public int MaxStock;
        public EntryKind Kind;

        public override string ToString() =>
            Prefab + ":" + BasePrice.ToString(CultureInfo.InvariantCulture) + ":" +
            TargetStock.ToString(CultureInfo.InvariantCulture) + ":" +
            MaxStock.ToString(CultureInfo.InvariantCulture) + ":" + Kind;
    }

    /// <summary>
    /// The catalogue: what Ingvar sells and buys, parsed from one config line so a server
    /// owner can edit it without a build. PURE: no game or Unity dependency, so the off-game
    /// harness compiles this exact file and checks the defaults against the item table
    /// (docs/data). Every number's reason is in docs/CATALOGUE.md.
    ///
    /// Failure policy: a bad entry is reported in words and skipped; the rest of the line
    /// still loads. A misspelled prefab that survives parsing is dropped at boot by the game
    /// side with one log line. Neither ever throws.
    /// </summary>
    public sealed class Catalogue
    {
        /// <summary>The 72 defaults of docs/CATALOGUE.md section 4: 18 wares, 54 wants.</summary>
        public const string DefaultLine =
            "Bronze:15:20:60:Ware, Iron:25:20:60:Ware, Silver:40:12:36:Ware, BlackMetal:60:10:30:Ware, " +
            "FlametalNew:110:6:18:Ware, Eitr:45:10:30:Ware, BlackCore:300:2:6:Ware, Amber:7:30:90:Ware, AmberPearl:14:20:60:Ware, " +
            "Ruby:29:15:45:Ware, SilverNecklace:43:8:24:Ware, ArrowIron:2:100:300:Ware, ArrowFrost:3:100:300:Ware, " +
            "BoltIron:3:100:300:Ware, MeadHealthMinor:12:10:30:Ware, MeadStaminaMinor:12:10:30:Ware, MeadTasty:10:10:30:Ware, " +
            "Honey:2:50:150:Ware, " +
            "Wood:1:200:600:Want, RoundLog:2:100:300:Want, FineWood:2:100:300:Want, ElderBark:3:60:180:Want, " +
            "Blackwood:4:60:180:Want, YggdrasilWood:5:60:180:Want, Resin:1:100:300:Want, Coal:1:100:300:Want, " +
            "Stone:1:200:600:Want, Flint:1:60:180:Want, Feathers:2:60:180:Want, " +
            "LeatherScraps:2:60:180:Want, DeerHide:3:60:180:Want, TrollHide:6:20:60:Want, WolfPelt:6:40:120:Want, " +
            "LoxPelt:8:40:120:Want, ScaleHide:6:40:120:Want, AskHide:10:40:120:Want, BjornHide:10:40:120:Want, " +
            "Flax:3:100:300:Want, LinenThread:10:50:150:Want, Barley:3:100:300:Want, JuteRed:6:40:120:Want, " +
            "JuteBlue:8:40:120:Want, WolfHairBundle:4:40:120:Want, " +
            "CopperOre:5:40:120:Want, TinOre:5:40:120:Want, IronScrap:22:30:90:Want, SilverOre:36:20:60:Want, " +
            "BlackMetalScrap:50:20:60:Want, FlametalOreNew:90:10:30:Want, " +
            "Guck:4:40:120:Want, Bloodbag:3:40:120:Want, Entrails:3:40:120:Want, Ooze:3:40:120:Want, Chain:12:20:60:Want, " +
            "Chitin:5:40:120:Want, Obsidian:4:40:120:Want, Crystal:8:20:60:Want, FreezeGland:4:40:120:Want, " +
            "Needle:6:40:120:Want, SurtlingCore:15:10:30:Want, Sap:6:40:120:Want, Softtissue:8:30:90:Want, " +
            "Carapace:10:40:120:Want, WitheredBone:8:30:90:Want, " +
            "TrophyDeer:8:10:30:Want, TrophyBoar:8:10:30:Want, TrophyNeck:6:10:30:Want, TrophyGreydwarf:8:10:30:Want, " +
            "TrophySkeleton:8:10:30:Want, TrophyDraugr:12:10:30:Want, TrophyWolf:15:10:30:Want, TrophyGoblin:15:10:30:Want";

        private readonly List<CatalogueEntry> _entries = new List<CatalogueEntry>();
        private readonly Dictionary<string, CatalogueEntry> _byPrefab =
            new Dictionary<string, CatalogueEntry>(StringComparer.Ordinal);

        public IReadOnlyList<CatalogueEntry> Entries => _entries;
        public int Count => _entries.Count;

        public CatalogueEntry Find(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return null;
            CatalogueEntry e;
            return _byPrefab.TryGetValue(prefab, out e) ? e : null;
        }

        public int CountOf(EntryKind kind)
        {
            int n = 0;
            for (int i = 0; i < _entries.Count; i++) if (_entries[i].Kind == kind) n++;
            return n;
        }

        /// <summary>
        /// Parse "Prefab:Base:Target:Max:Kind, ..." Never throws. Problems, if a list is
        /// given, receive one plain sentence per refused entry.
        /// </summary>
        public static Catalogue Parse(string line, List<string> problems)
        {
            var cat = new Catalogue();
            if (string.IsNullOrEmpty(line)) return cat;

            string[] raw = line.Split(',');
            for (int i = 0; i < raw.Length; i++)
            {
                string s = raw[i].Trim();
                if (s.Length == 0) continue;

                string[] f = s.Split(':');
                if (f.Length != 5)
                {
                    Report(problems, "'" + s + "': expected Prefab:Base:Target:Max:Kind (" + f.Length + " field(s) found)");
                    continue;
                }

                string prefab = f[0].Trim();
                if (!IsPrefabName(prefab))
                {
                    Report(problems, "'" + s + "': '" + prefab + "' is not a prefab name (letters, digits and underscores only)");
                    continue;
                }

                int basePrice, target, max;
                if (!TryInt(f[1], out basePrice) || basePrice < 1)
                {
                    Report(problems, "'" + s + "': base price must be a whole number of at least 1");
                    continue;
                }
                if (!TryInt(f[2], out target) || target < 1)
                {
                    Report(problems, "'" + s + "': target stock must be a whole number of at least 1");
                    continue;
                }
                if (!TryInt(f[3], out max) || max < target)
                {
                    Report(problems, "'" + s + "': max stock must be a whole number no smaller than the target");
                    continue;
                }

                EntryKind kind;
                string k = f[4].Trim();
                if (string.Equals(k, "Ware", StringComparison.OrdinalIgnoreCase)) kind = EntryKind.Ware;
                else if (string.Equals(k, "Want", StringComparison.OrdinalIgnoreCase)) kind = EntryKind.Want;
                else
                {
                    Report(problems, "'" + s + "': kind must be Ware or Want, not '" + k + "'");
                    continue;
                }

                if (cat._byPrefab.ContainsKey(prefab))
                {
                    Report(problems, "'" + s + "': " + prefab + " appears twice; the first entry is kept");
                    continue;
                }

                var e = new CatalogueEntry
                {
                    Prefab = prefab, BasePrice = basePrice, TargetStock = target, MaxStock = max, Kind = kind,
                };
                cat._entries.Add(e);
                cat._byPrefab.Add(prefab, e);
            }
            return cat;
        }

        /// <summary>Letters, digits and underscores, at least one character. What ZNetScene names look like.</summary>
        public static bool IsPrefabName(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_';
                if (!ok) return false;
            }
            return true;
        }

        private static bool TryInt(string s, out int value) =>
            int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

        private static void Report(List<string> problems, string text)
        {
            if (problems != null) problems.Add(text);
        }
    }
}
