using System.Collections.Generic;
using UnityEngine;
using RavenIron.ValkyriesCargo.Core;

namespace RavenIron.ValkyriesCargo
{
    /// <summary>
    /// The one place the game's own locations are read, the twin of `ActiveAreaLive` and `CarryPinLive`.
    /// The rule it serves is `Core/HomeGround.cs`; the reason it exists is issue #79.
    ///
    /// It asks the engine's own `Location.GetLocation(point)` - public static, the same walk the hammer
    /// uses - which location a point is inside by that location's own exterior radius, at the player and
    /// at `HomeGround.RingOffsets` around them for the clearance. Then it asks the location instance two
    /// things that decide whether it COUNTS: does it hold a `Trader` (a merchant's camp), does it have an
    /// interior (a dungeon's door). Anything else - a ruin, a runestone, a stone circle - is the player's
    /// to build on and is not reported.
    ///
    /// Why the instance and not the zone registry: `ZoneSystem.m_locationInstances` knows every location
    /// in the world but nothing about what is inside one, and `ZoneSystem.GetLocation(name)` is private.
    /// The instance has the answer, and it exists on both sides: the engine spawns a location root with
    /// its networked children present but INACTIVE (`ZoneSystem.SpawnLocation`, Client mode, sets them
    /// inactive before `Instantiate` and re-enables the asset after), so `GetComponentInChildren<Trader>
    /// (includeInactive: true)` finds Haldor on a dedicated server and on a client alike. That is also why
    /// `cargo status` can answer this on the client, where the registry is empty.
    ///
    /// Everything named here is public on the real assembly: `Location.GetLocation`, `m_exteriorRadius`,
    /// `m_hasInterior`, `Trader`. House rule 5 holds: our files name no private member.
    /// </summary>
    public static class LocationsLive
    {
        /// <summary>What the point sits at, if anything that counts.</summary>
        public struct Verdict
        {
            /// <summary>True when the player, or the ring around them, is inside a location that counts.</summary>
            public bool Inside;
            /// <summary>The location's prefab name ("Hildir_camp"), for the log line. Empty when clear.</summary>
            public string Name;
            /// <summary>`HomeGround.MerchantCamp` or `HomeGround.DungeonEntrance`. Empty when clear.</summary>
            public string Kind;
            /// <summary>Metres from the player to that location's centre.</summary>
            public float Distance;
            /// <summary>The location's own exterior radius, as the game has it.</summary>
            public float Radius;
        }

        /// <summary>
        /// Answers "clear" when nothing counts, and deliberately also when there is no world yet: a
        /// check that has not run is never allowed to be the thing that stops a visit.
        /// </summary>
        public static Verdict Read(Vector3 p, float clearance, bool merchants, bool dungeons)
        {
            Verdict v = new Verdict { Inside = false, Name = "", Kind = "", Distance = 0f, Radius = 0f };
            if (!merchants && !dungeons) return v;
            if (ZNet.instance == null) return v;

            // The player's own point first, so when they are standing in a camp it is that camp that
            // gets named and not a neighbour the ring happened to touch.
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
            v.Inside = true;
            v.Name = Name(hit);
            v.Kind = kind;
            v.Distance = Mathf.Sqrt(dx * dx + dz * dz);
            v.Radius = hit.m_exteriorRadius;
            return v;
        }

        /// <summary>The location, if it is one of the two kinds that count; else null.</summary>
        private static Location Counts(Location loc, bool merchants, bool dungeons, out string kind)
        {
            kind = "";
            if (loc == null) return null;
            if (merchants && HoldsTrader(loc)) { kind = HomeGround.MerchantCamp; return loc; }
            if (dungeons && loc.m_hasInterior) { kind = HomeGround.DungeonEntrance; return loc; }
            return null;
        }

        // A camp root has a few hundred children; walking them is cheap but the answer never changes
        // for a given instance, so it is remembered. Bounded, because instances come and go with zones.
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

        /// <summary>"Hildir_camp(Clone)" -> "Hildir_camp". A location with no name is one we cannot talk about.</summary>
        private static string Name(Location loc)
        {
            string n = loc.gameObject != null ? loc.gameObject.name : "";
            if (string.IsNullOrEmpty(n)) return "";
            int clone = n.IndexOf("(Clone)", System.StringComparison.Ordinal);
            return clone > 0 ? n.Substring(0, clone).Trim() : n.Trim();
        }

        /// <summary>
        /// The RAW engine answer, for a diagnosis and nothing else: what `Location.GetLocation` returns at
        /// the point, and what `Location.GetZoneLocation` returns for each of the nine zones around it,
        /// each with its radius, its distance, and the two facts the rule turns on. Printed by
        /// `cargo status` and logged on the server on a FORCED visit (an admin's own action, so no spam).
        /// Added 2026-09-15 when the live test beside Hildir came back "clear" and nothing said why.
        /// </summary>
        public static string Probe(Vector3 p)
        {
            if (ZNet.instance == null) return "engine: no world";
            var sb = new System.Text.StringBuilder("engine: GetLocation=");
            sb.Append(One(Location.GetLocation(p), p));
            sb.Append("; IsInsideLocation(8m)=").Append(Location.IsInsideLocation(p, 8f) ? "yes" : "no");
            sb.Append("; zones:");
            Vector2s centre = ZoneSystem.GetZone(p);
            int n = 0;
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    Location z = Location.GetZoneLocation(new Vector2s(centre.x + dx, centre.y + dz));
                    if (z == null) continue;
                    n++;
                    sb.Append(' ').Append(One(z, p));
                }
            if (n == 0) sb.Append(" none");
            return sb.ToString();
        }

        private static string One(Location loc, Vector3 p)
        {
            if (loc == null) return "none";
            Vector3 c = loc.transform.position;
            float dx = p.x - c.x, dz = p.z - c.z;
            return Name(loc) + "[r=" + Wire.Float(loc.m_exteriorRadius) + " d=" + Wire.Float(Mathf.Sqrt(dx * dx + dz * dz)) +
                   " interior=" + (loc.m_hasInterior ? "y" : "n") + " trader=" + (HoldsTrader(loc) ? "y" : "n") +
                   " noBuild=" + (loc.m_noBuild ? "y" : "n") + "]";
        }

        /// <summary>One line for `cargo status`.</summary>
        public static string StatusLine(Vector3 p, float clearance, bool merchants, bool dungeons)
        {
            if (ZNet.instance == null) return "locations: no world yet (nothing is refused for this)";
            if (!merchants && !dungeons) return "locations: both gates are off (Server.AvoidMerchantCamps, Server.AvoidDungeonEntrances)";
            Verdict v = Read(p, clearance, merchants, dungeons);
            string gates = (merchants ? "merchant camps" : "") + (merchants && dungeons ? " + " : "") + (dungeons ? "dungeon entrances" : "");
            if (!v.Inside)
                return "locations: clear here (refusing " + gates + "; clearance " + Wire.Float(clearance) + " m)";
            return "locations: AT " + HomeGround.LocationLabel(v.Name, v.Kind) + " (" + Wire.Float(v.Distance) +
                   " m from its centre, exterior radius " + Wire.Float(v.Radius) + " m, clearance " + Wire.Float(clearance) + " m)";
        }
    }
}
