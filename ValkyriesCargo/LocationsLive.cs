using UnityEngine;
using RavenIron.ValkyriesCargo.Core;

namespace RavenIron.ValkyriesCargo
{
    /// <summary>
    /// The one place the game's own locations are read, the twin of `ActiveAreaLive` and `CarryPinLive`.
    /// The rule it serves is `Core/HomeGround.cs`; the reason it exists is issue #79.
    ///
    /// `ZoneSystem.m_locationInstances` is a dictionary keyed by the ZONE a location was registered
    /// against, so this looks at the 3x3 block of zones around the point rather than walking every
    /// location in the world. A world has thousands of them and the roll runs on a timer; an O(n) sweep
    /// of the lot once a minute would be a waste that grows with the map. Nine dictionary lookups do not.
    ///
    /// The 3x3 block is also why `HomeGround.MaxClearance` stops at one zone: a location filed under a
    /// zone further out than that could reach a point this never looks at, and a rule that is right only
    /// sometimes is worse than a rule with a stated bound.
    ///
    /// Everything named here is public on the real assembly - `ZoneSystem.instance`, the static
    /// `GetZone`, `m_locationInstances`, `LocationInstance.m_position` and `.m_location`, and
    /// `ZoneLocation.m_exteriorRadius`, `.m_name` and `.m_prefabName`. House rule 5 holds: our files name
    /// no private member.
    /// </summary>
    public static class LocationsLive
    {
        /// <summary>What the point sits inside, if anything.</summary>
        public struct Verdict
        {
            /// <summary>True when the point is inside a location's exterior radius plus the clearance.</summary>
            public bool Inside;
            /// <summary>The location's name, for the log line. Empty when nothing was found.</summary>
            public string Name;
            /// <summary>Metres from the point to that location's centre.</summary>
            public float Distance;
            /// <summary>The location's own exterior radius, as the game has it.</summary>
            public float Radius;
        }

        /// <summary>
        /// The nearest location to `p` whose exterior radius plus `clearance` contains it, or a Verdict
        /// with `Inside` false when the point is clear of every location in the 3x3 block.
        ///
        /// Answers "clear" when there is no ZoneSystem yet. That is deliberate and matches how every
        /// probe in this mod fails: a check that has not run is never allowed to be the thing that stops
        /// a visit. Refusing every candidate because a system had not woken up would be a far worse bug
        /// than the one this file fixes.
        /// </summary>
        public static Verdict Read(Vector3 p, float clearance)
        {
            Verdict v = new Verdict { Inside = false, Name = "", Distance = 0f, Radius = 0f };

            ZoneSystem zs = ZoneSystem.instance;
            if (zs == null || zs.m_locationInstances == null) return v;

            Vector2s centre = ZoneSystem.GetZone(p);
            float bestOverlap = float.NegativeInfinity;

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    Vector2s zone = new Vector2s(centre.x + dx, centre.y + dy);
                    ZoneSystem.LocationInstance li;
                    if (!zs.m_locationInstances.TryGetValue(zone, out li)) continue;
                    if (li.m_location == null) continue;

                    // Flat distance: a location's radius is a footprint on the map, and a player on a
                    // roof thirty metres up is still standing in the camp.
                    float ddx = p.x - li.m_position.x;
                    float ddz = p.z - li.m_position.z;
                    float distance = Mathf.Sqrt(ddx * ddx + ddz * ddz);
                    float radius = li.m_location.m_exteriorRadius;

                    if (!HomeGround.InsideLocation(distance, radius, clearance)) continue;

                    // Overlapping locations are possible; report the one the point is deepest inside,
                    // because that is the one a player would say they were standing in.
                    float overlap = (radius < 0f ? 0f : radius) + clearance - distance;
                    if (overlap <= bestOverlap) continue;

                    bestOverlap = overlap;
                    v.Inside = true;
                    v.Name = Name(li.m_location);
                    v.Distance = distance;
                    v.Radius = radius;
                }
            }

            return v;
        }

        /// <summary>
        /// `m_name` is the authored name and is what a person recognises; `m_prefabName` is the fallback
        /// because a location with neither is a location we cannot talk about.
        /// </summary>
        private static string Name(ZoneSystem.ZoneLocation loc)
        {
            string n = loc.m_name;
            if (!string.IsNullOrEmpty(n)) return n;
            n = loc.m_prefabName;
            return string.IsNullOrEmpty(n) ? "" : n;
        }

        /// <summary>One line for `cargo status`.</summary>
        public static string StatusLine(Vector3 p, float clearance)
        {
            ZoneSystem zs = ZoneSystem.instance;
            if (zs == null || zs.m_locationInstances == null) return "locations: no ZoneSystem yet (nothing is refused for this)";
            Verdict v = Read(p, clearance);
            if (!v.Inside)
                return "locations: clear here (" + zs.m_locationInstances.Count + " in the world, clearance " + Wire.Float(clearance) + " m)";
            return "locations: INSIDE " + v.Name + " (" + Wire.Float(v.Distance) + " m from its centre, exterior radius " +
                   Wire.Float(v.Radius) + " m, clearance " + Wire.Float(clearance) + " m)";
        }
    }
}
