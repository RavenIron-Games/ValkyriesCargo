using System.Collections.Generic;
using UnityEngine;
using RavenIron.ValkyriesCargo.Core;

namespace RavenIron.ValkyriesCargo
{
    /// <summary>
    /// The one place the game's own locations are read, the twin of `ActiveAreaLive` and `CarryPinLive`.
    /// The rule it serves is `Core/HomeGround.cs`; the reason it exists is issue #79.
    ///
    /// Two reads, because the two sides of the wire know different things (Storm10, 2026-09-15, the
    /// probe under a forced visit: on the DEDICATED SERVER `Location.GetLocation` answered none and all
    /// nine zones' `GetZoneLocation` answered none at a point the zone registry called "inside
    /// Hildir_camp" - a dedicated server never instantiates a location root; on a client the registry
    /// is empty and the instances are what exist):
    ///
    /// - **The server reads the registry.** `ZoneSystem.m_locationInstances` is keyed by zone, so the
    ///   3x3 block around the point is nine lookups, each with the location's own `m_exteriorRadius`.
    ///   What is INSIDE a location - a `Trader`, an interior - is read off the location's prefab ASSET
    ///   through the same `m_prefab.Load()` / `.Asset` / `.Release()` the engine's own `ZoneSystem`
    ///   uses, once per location type, and remembered. The third fact, whether the game pins the location
    ///   on the map, is on the registry ENTRY itself - `ZoneLocation.m_iconAlways`, or `m_iconPlaced` with
    ///   the instance's `m_placed`, exactly the engine's own `ZoneSystem.GetLocationIcons` test - so it is
    ///   the server's alone.
    /// - **A client reads the instances.** `Location.GetLocation(point)` - public static, the walk the
    ///   hammer uses - at the player and at `HomeGround.RingOffsets` for the clearance; the instance
    ///   root carries the Trader and the interior; the map-icon flag is not on it, so a client cannot see
    ///   a landmark at all and says so.
    ///
    /// A location has ONE kind, decided in this order, and each kind answers to exactly one switch: a
    /// merchant's camp when it holds a `Trader` (Haldor, Hildir, the Bog Witch, any modded one; no name
    /// list) - and never ALSO a landmark, though every vanilla camp carries the map icon, so that
    /// `Server.AvoidMerchantCamps=false` really does open the camps (the rule-2 review of PR #84 caught the
    /// first cut refusing them as landmarks instead); else a dungeon's door when it has an interior; else a
    /// landmark when the game pins it on the map (the Sacrificial Stones, every boss altar). A ruin, a
    /// runestone, a stone circle is the player's to build on and is never reported.
    ///
    /// Everything named here is public on the real assembly: `ZoneSystem.instance`, `GetZone`,
    /// `m_locationInstances`, `LocationInstance.m_position` / `.m_location` / `.m_placed`, `ZoneLocation.m_prefab` /
    /// `.m_exteriorRadius` / `.m_name` / `.m_prefabName` / `.m_iconAlways` / `.m_iconPlaced`, `SoftReference.Load` /
    /// `.Asset` / `.Release`,
    /// `Location.GetLocation` / `.m_exteriorRadius` / `.m_hasInterior`, `Trader`. House rule 5 holds.
    /// </summary>
    public static class LocationsLive
    {
        /// <summary>What the point sits at, if anything that counts.</summary>
        public struct Verdict
        {
            /// <summary>True when the point, or the clearance around it, is inside a location that counts.</summary>
            public bool Inside;
            /// <summary>The location's name ("Hildir_camp"), for the log line. Empty when clear.</summary>
            public string Name;
            /// <summary>`HomeGround.MerchantCamp`, `HomeGround.DungeonEntrance` or `HomeGround.Landmark`. Empty when clear.</summary>
            public string Kind;
            /// <summary>Metres from the point to that location's centre.</summary>
            public float Distance;
            /// <summary>The location's own exterior radius, as the game has it.</summary>
            public float Radius;
        }

        /// <summary>
        /// The facts the rule turns on, for one location type. Trader and Interior come off the prefab
        /// asset; the two icon flags are the registry entry's own (`m_iconAlways` / `m_iconPlaced`), which a
        /// location INSTANCE does not carry - so on a client, where only instances exist, they read false.
        /// Whether the flags PIN the location is decided per instance (`Pinned`), because `m_iconPlaced` only
        /// counts once the instance is placed - the engine's own test. The gate runs on the server.
        /// </summary>
        private struct Facts { public bool Trader; public bool Interior; public bool IconAlways; public bool IconPlaced; }

        /// <summary>`ZoneSystem.GetLocationIcons`'s test, verbatim: always, or placed-and-flagged.</summary>
        private static bool Pinned(Facts f, bool placed) => f.IconAlways || (f.IconPlaced && placed);

        /// <summary>
        /// Answers "clear" when nothing counts, and deliberately also when there is no world yet: a
        /// check that has not run is never allowed to be the thing that stops a visit.
        /// </summary>
        public static Verdict Read(Vector3 p, float clearance, bool merchants, bool dungeons, bool landmarks)
        {
            Verdict v = new Verdict { Inside = false, Name = "", Kind = "", Distance = 0f, Radius = 0f };
            if (!merchants && !dungeons && !landmarks) return v;
            ZoneSystem zs = ZoneSystem.instance;
            if (zs == null) return v;
            if (zs.m_locationInstances != null && zs.m_locationInstances.Count > 0)
                return ReadRegistry(zs, p, clearance, merchants, dungeons, landmarks, v);
            return ReadInstances(p, clearance, merchants, dungeons, v);
        }

        // ---- the server: the zone registry, and the prefab asset for what is inside ----------------

        private static Verdict ReadRegistry(ZoneSystem zs, Vector3 p, float clearance, bool merchants, bool dungeons, bool landmarks, Verdict v)
        {
            Vector2s centre = ZoneSystem.GetZone(p);
            float bestOverlap = float.NegativeInfinity;
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    ZoneSystem.LocationInstance li;
                    if (!zs.m_locationInstances.TryGetValue(new Vector2s(centre.x + dx, centre.y + dz), out li)) continue;
                    if (li.m_location == null) continue;
                    // Flat distance: a location's radius is a footprint on the map, and a player on a
                    // roof thirty metres up is still standing in the camp.
                    float ddx = p.x - li.m_position.x, ddz = p.z - li.m_position.z;
                    float distance = Mathf.Sqrt(ddx * ddx + ddz * ddz);
                    float radius = li.m_location.m_exteriorRadius;
                    if (!HomeGround.InsideLocation(distance, radius, clearance)) continue;
                    string kind = KindOf(FactsOf(li.m_location), li.m_placed, merchants, dungeons, landmarks);
                    if (kind.Length == 0) continue;
                    // Overlapping locations are possible; the one the point is deepest inside is the one
                    // a player would say they were standing in.
                    float overlap = (radius < 0f ? 0f : radius) + clearance - distance;
                    if (overlap <= bestOverlap) continue;
                    bestOverlap = overlap;
                    v.Inside = true; v.Name = Name(li.m_location); v.Kind = kind; v.Distance = distance; v.Radius = radius;
                }
            return v;
        }

        // One load per location TYPE, ever. The reference is counted, so Load/Release is neutral to the
        // engine, and the answer for a prefab never changes.
        private static readonly Dictionary<string, Facts> _factsByPrefab = new Dictionary<string, Facts>();

        private static Facts FactsOf(ZoneSystem.ZoneLocation loc)
        {
            string key = loc.m_prefabName ?? "";
            Facts f;
            if (_factsByPrefab.TryGetValue(key, out f)) return f;
            // The icon flags are on the registry entry itself; no asset needed for them. Caching them by
            // prefab name is exact: the registry holds ONE ZoneLocation per prefab name and every
            // LocationInstance points at that one object, so they cannot differ between two instances.
            f = new Facts { Trader = false, Interior = false, IconAlways = loc.m_iconAlways, IconPlaced = loc.m_iconPlaced };
            try
            {
                loc.m_prefab.Load();
                try
                {
                    GameObject asset = loc.m_prefab.Asset;
                    if (asset != null)
                    {
                        f.Trader = asset.GetComponentInChildren<Trader>(true) != null;
                        Location root = asset.GetComponent<Location>();
                        f.Interior = root != null && root.m_hasInterior;
                    }
                }
                finally { loc.m_prefab.Release(); }
            }
            catch (System.Exception ex)
            {
                // Unreadable is treated as "nothing counts": a location we cannot read never refuses a visit.
                if (_factThrows++ < 3) ValkyriesCargo.Log.LogWarning("location prefab '" + key + "' could not be read: " + ex.Message);
            }
            _factsByPrefab[key] = f;
            return f;
        }
        private static int _factThrows;

        // ---- a client: the instances, at the point and around it ---------------------------------

        private static Verdict ReadInstances(Vector3 p, float clearance, bool merchants, bool dungeons, Verdict v)
        {
            string kind;
            Location hit = Counts(Location.GetLocation(p), merchants, dungeons, out kind);
            if (hit == null)
            {
                float[] ring = HomeGround.RingOffsets(clearance);
                for (int i = 0; i + 1 < ring.Length && hit == null; i += 2)
                    hit = Counts(Location.GetLocation(new Vector3(p.x + ring[i], p.y, p.z + ring[i + 1])), merchants, dungeons, out kind);
            }
            if (hit == null) return v;
            Vector3 c = hit.transform.position;
            float dx = p.x - c.x, dz = p.z - c.z;
            v.Inside = true; v.Name = Name(hit); v.Kind = kind;
            v.Distance = Mathf.Sqrt(dx * dx + dz * dz); v.Radius = hit.m_exteriorRadius;
            return v;
        }

        private static Location Counts(Location loc, bool merchants, bool dungeons, out string kind)
        {
            kind = "";
            if (loc == null) return null;
            kind = KindOf(new Facts { Trader = HoldsTrader(loc), Interior = loc.m_hasInterior, IconAlways = false, IconPlaced = false }, true, merchants, dungeons, false);
            return kind.Length == 0 ? null : loc;
        }

        private static readonly Dictionary<int, bool> _traderByInstance = new Dictionary<int, bool>();

        private static bool HoldsTrader(Location loc)
        {
            int id = loc.GetInstanceID();
            bool holds;
            if (_traderByInstance.TryGetValue(id, out holds)) return holds;
            holds = loc.GetComponentInChildren<Trader>(true) != null;
            if (_traderByInstance.Count > 256) _traderByInstance.Clear();
            _traderByInstance[id] = holds;
            return holds;
        }

        // ---- shared ---------------------------------------------------------------------------------

        /// <summary>
        /// The ONE kind a location is, in this order, and whether its own switch is on: a Trader makes it a
        /// merchant's camp and nothing else (every vanilla camp is also pinned on the map, and the merchant
        /// switch must be the switch that opens them); else an interior makes it a dungeon's door; else the
        /// map pin makes it a landmark. "" when it is none of these, or its kind's switch is off.
        /// </summary>
        private static string KindOf(Facts f, bool placed, bool merchants, bool dungeons, bool landmarks)
        {
            if (f.Trader) return merchants ? HomeGround.MerchantCamp : "";
            if (f.Interior) return dungeons ? HomeGround.DungeonEntrance : "";
            if (Pinned(f, placed)) return landmarks ? HomeGround.Landmark : "";
            return "";
        }

        private static string Name(ZoneSystem.ZoneLocation loc)
        {
            string n = loc.m_name;
            if (!string.IsNullOrEmpty(n)) return n;
            n = loc.m_prefabName;
            return string.IsNullOrEmpty(n) ? "" : n;
        }

        /// <summary>"Hildir_camp(Clone)" -> "Hildir_camp".</summary>
        private static string Name(Location loc)
        {
            string n = loc.gameObject != null ? loc.gameObject.name : "";
            if (string.IsNullOrEmpty(n)) return "";
            int clone = n.IndexOf("(Clone)", System.StringComparison.Ordinal);
            return clone > 0 ? n.Substring(0, clone).Trim() : n.Trim();
        }

        /// <summary>
        /// The RAW engine answers, for a diagnosis and nothing else: the registry's nine zones around the
        /// point (with the facts read off each prefab) and the instance walk, side by side. Printed by
        /// `cargo status` and logged on the server on a FORCED visit (an admin's own action, so no spam).
        /// It is what found the server-has-no-instances fact on 2026-09-15.
        /// </summary>
        public static string Probe(Vector3 p)
        {
            ZoneSystem zs = ZoneSystem.instance;
            if (zs == null) return "engine: no world";
            var sb = new System.Text.StringBuilder("engine: registry ");
            sb.Append(zs.m_locationInstances != null ? zs.m_locationInstances.Count : 0).Append(" location(s);");
            Vector2s centre = ZoneSystem.GetZone(p);
            int n = 0;
            if (zs.m_locationInstances != null)
                for (int dx = -1; dx <= 1; dx++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        ZoneSystem.LocationInstance li;
                        if (!zs.m_locationInstances.TryGetValue(new Vector2s(centre.x + dx, centre.y + dz), out li) || li.m_location == null) continue;
                        n++;
                        Facts f = FactsOf(li.m_location);
                        float ddx = p.x - li.m_position.x, ddz = p.z - li.m_position.z;
                        sb.Append(' ').Append(Name(li.m_location)).Append("[r=").Append(Wire.Float(li.m_location.m_exteriorRadius))
                          .Append(" d=").Append(Wire.Float(Mathf.Sqrt(ddx * ddx + ddz * ddz)))
                          .Append(" trader=").Append(f.Trader ? "y" : "n").Append(" interior=").Append(f.Interior ? "y" : "n")
                          .Append(" icon=").Append(Pinned(f, li.m_placed) ? "y" : "n").Append(']');
                    }
            if (n == 0) sb.Append(" none in the 3x3");
            sb.Append("; instances: GetLocation=").Append(One(Location.GetLocation(p), p));
            return sb.ToString();
        }

        private static string One(Location loc, Vector3 p)
        {
            if (loc == null) return "none";
            Vector3 c = loc.transform.position;
            float dx = p.x - c.x, dz = p.z - c.z;
            return Name(loc) + "[r=" + Wire.Float(loc.m_exteriorRadius) + " d=" + Wire.Float(Mathf.Sqrt(dx * dx + dz * dz)) +
                   " trader=" + (HoldsTrader(loc) ? "y" : "n") + " interior=" + (loc.m_hasInterior ? "y" : "n") + "]";
        }

        /// <summary>
        /// One line for the server log, once per world: every location TYPE the game pins on the map, sorted
        /// by our rule - the ones `Server.AvoidLandmarks` refuses, then the camps and the doors, which are
        /// pinned too but answer to their own switches. This is the blast radius of the default, printed
        /// where an admin can read it instead of guessed from whatever sat near one test player.
        /// </summary>
        public static string LandmarkTypesLine()
        {
            ZoneSystem zs = ZoneSystem.instance;
            if (zs == null || zs.m_locationInstances == null || zs.m_locationInstances.Count == 0)
                return "landmarks: no registry on this side (a client has none)";
            var landmarks = new SortedDictionary<string, int>(System.StringComparer.Ordinal);
            var camps = new SortedDictionary<string, int>(System.StringComparer.Ordinal);
            var doors = new SortedDictionary<string, int>(System.StringComparer.Ordinal);
            foreach (ZoneSystem.LocationInstance li in zs.m_locationInstances.Values)
            {
                ZoneSystem.ZoneLocation loc = li.m_location;
                if (loc == null) continue;
                // The flags first, off the entry: only a pinned type pays for its asset read.
                if (!(loc.m_iconAlways || (loc.m_iconPlaced && li.m_placed))) continue;
                Facts f = FactsOf(loc);
                SortedDictionary<string, int> bucket = f.Trader ? camps : f.Interior ? doors : landmarks;
                string name = Name(loc);
                int n;
                bucket.TryGetValue(name, out n);
                bucket[name] = n + 1;
            }
            return "landmarks in this world (Server.AvoidLandmarks refuses these): " + Join(landmarks) +
                   "; pinned on the map but a merchant's camp (Server.AvoidMerchantCamps): " + Join(camps) +
                   "; pinned but a dungeon's door (Server.AvoidDungeonEntrances): " + Join(doors);
        }

        private static string Join(SortedDictionary<string, int> d)
        {
            if (d.Count == 0) return "none";
            var parts = new List<string>();
            foreach (KeyValuePair<string, int> kv in d) parts.Add(kv.Key + " x" + kv.Value);
            return string.Join(", ", parts.ToArray());
        }

        /// <summary>One line for `cargo status`.</summary>
        public static string StatusLine(Vector3 p, float clearance, bool merchants, bool dungeons, bool landmarks)
        {
            if (ZoneSystem.instance == null) return "locations: no world yet (nothing is refused for this)";
            if (!merchants && !dungeons && !landmarks) return "locations: all three gates are off (Server.AvoidMerchantCamps, Server.AvoidDungeonEntrances, Server.AvoidLandmarks)";
            Verdict v = Read(p, clearance, merchants, dungeons, landmarks);
            // A client has no registry, so it never evaluated the landmark gate: say what WAS checked here and
            // that the third answer is the server's, rather than claim a refusal this side cannot make.
            bool registry = ZoneSystem.instance.m_locationInstances != null && ZoneSystem.instance.m_locationInstances.Count > 0;
            var g = new List<string>();
            if (merchants) g.Add("merchant camps");
            if (dungeons) g.Add("dungeon entrances");
            if (landmarks && registry) g.Add("landmarks");
            string gates = g.Count == 0 ? "nothing checked on this side" : "refusing " + string.Join(" + ", g.ToArray());
            string note = landmarks && !registry ? "; landmarks are the server's read and not visible from a client" : "";
            if (!v.Inside)
                return "locations: clear here (" + gates + "; clearance " + Wire.Float(clearance) + " m" + note + ")";
            return "locations: AT " + HomeGround.LocationLabel(v.Name, v.Kind) + " (" + Wire.Float(v.Distance) +
                   " m from its centre, exterior radius " + Wire.Float(v.Radius) + " m, clearance " + Wire.Float(clearance) + " m)";
        }
    }
}
