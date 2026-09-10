using System;

namespace RavenIron.ValkyriesCargo.Core
{
    /// <summary>
    /// PURE. Valheim 1.0's simulation distance as the two numbers the ACTIVE AREA is a function of.
    /// `ZoneSystem.m_activeArea` is gone on 1.0 (the sweep of 2026-09-09,
    /// `docs/engine-sweeps/2026-09-09-server-0.221.12-vs-1.0.7.md`); in its place every peer carries a
    /// `SimulationDistance` (near, far, classic) that the server validates and syncs
    /// (`ZNet.GetSyncedSimulationDistance`). The far half decides what is DISTANT-loaded and touches
    /// nothing of ours, so it is not carried. `SimulationDistance.OriginalDistance` is (2, 2, classic),
    /// what every server runs unless told otherwise, and exactly the 3x3 block of 64 m zones that
    /// 0.221.12's `m_activeArea = 2` gave, so D5's keep-window and the flight's clamp keep their shape
    /// on a stock server. What changed is where the number comes from, and what a non-default setting
    /// does to the shape (`ActiveArea`).
    /// </summary>
    public readonly struct SimDistance : IEquatable<SimDistance>
    {
        /// <summary>`SimulationDistance.NearSimulationDistance`: 1 to 5 in vanilla's own table; at least 1 here.</summary>
        public readonly int Near;

        /// <summary>`SimulationDistance.IsClassic`: the pre-1.0 shape. Near 2 classic is the default.</summary>
        public readonly bool Classic;

        public SimDistance(int near, bool classic)
        {
            Near = near < 1 ? 1 : near;
            Classic = classic;
        }

        /// <summary>`SimulationDistance.OriginalDistance`: near 2, classic. The 0.221.12 window, byte for byte.</summary>
        public static readonly SimDistance Original = new SimDistance(2, true);

        public bool Equals(SimDistance other) => Near == other.Near && Classic == other.Classic;
        public override bool Equals(object obj) => obj is SimDistance o && Equals(o);
        public override int GetHashCode() => Near * 2 + (Classic ? 1 : 0);
        public override string ToString() => "near " + Wire.Int(Near) + (Classic ? " classic" : "");
    }

    /// <summary>
    /// PURE. `ZNetScene.PointInsideActiveArea(zone, point)` on 1.0.7, as arithmetic: whether a point is
    /// inside the active area of a reference position, measured in METRES from the centre of the
    /// reference position's zone.
    ///
    ///     reach  = 1.5 zones (96 m), or 1 zone (64 m) when near == 1
    ///     inside = Chebyshev(zoneCentre, point) &lt;= reach
    ///     at near == 2 off classic, ALSO strictly inside a circle of 1.75 zones (112 m)
    ///
    /// Two callers, one rule. `ZoneOwnership.WouldStripClaim`: the server's 2 s ownership sweep
    /// (`ZDOMan.ReleaseNearbyZDOS`) keeps a persistent claim only while the object is inside its
    /// owner's area, and takes an unowned one only if it is. `FlightPlan`: a client instantiates a ZDO
    /// only inside its own area, so every waypoint must be. Nothing here reads a game type;
    /// `ActiveAreaLive.Read()` is the one place the live numbers come from. 0.221.12 wrote the same
    /// test in zone indices (`InActiveArea(zone, centre, m_activeArea - 1)`) and the two callers
    /// mirrored it that way; the shape on a stock server is unchanged.
    /// </summary>
    public static class ActiveArea
    {
        /// <summary>The zone grid vanilla hardcodes in `ZoneSystem.GetZone` and `GetZonePos`, in metres.</summary>
        public const float ZoneSize = 64f;

        /// <summary>
        /// `ZoneSystem.GetZone` on one axis: floor((v + 32) / 64). A double and an explicit floor so a
        /// negative coordinate rounds the way vanilla's does; C# integer division truncates toward
        /// zero, which would put everything in the zone west of where vanilla puts it for half the
        /// world.
        /// </summary>
        public static int ZoneOf(float v) => (int)Math.Floor(((double)v + ZoneSize / 2.0) / ZoneSize);

        /// <summary>`ZoneSystem.GetZonePos` on one axis: a zone's centre, zone * 64.</summary>
        public static float ZoneCentre(int zone) => zone * ZoneSize;

        /// <summary>The square's half-width in metres from the zone centre: 1.5 zones, or 1 zone at near 1.</summary>
        public static float Reach(SimDistance sim) => (sim.Near == 1 ? 1f : 1.5f) * ZoneSize;

        /// <summary>The circle's radius at near 2 off classic, 1.75 zones; 0 when there is no circle.</summary>
        public static float Radius(SimDistance sim) => sim.Near == 2 && !sim.Classic ? 1.75f * ZoneSize : 0f;

        /// <summary>The smallest half-width any setting gives, less a margin: what a plan may count on.</summary>
        public static float MinimumReach(float margin) => ZoneSize - margin;

        /// <summary>`PointInsideActiveArea`, statement for statement, y ignored.</summary>
        public static bool Contains(float x, float z, int centreZoneX, int centreZoneZ, SimDistance sim)
            => ContainsWithMargin(x, z, centreZoneX, centreZoneZ, sim, 0f);

        /// <summary>
        /// The same test with a margin: at least <paramref name="margin"/> metres inside the square on
        /// both axes, and inside the circle by as much where there is one. A point that only just
        /// qualifies falls out the moment the reference position moves a step.
        /// </summary>
        public static bool ContainsWithMargin(float x, float z, int centreZoneX, int centreZoneZ, SimDistance sim, float margin)
        {
            float dx = Math.Abs(x - ZoneCentre(centreZoneX));
            float dz = Math.Abs(z - ZoneCentre(centreZoneZ));
            float reach = Reach(sim) - margin;
            if (dx > reach || dz > reach) return false;                 // Utils.ChebyshevDistance <= reach
            float r = Radius(sim);
            if (r <= 0f) return true;
            r -= margin;
            return dx * dx + dz * dz < r * r;                            // vanilla's strict '<'
        }

        /// <summary>The point against the area of a reference position: the owner's `m_refPos`, the pilot.</summary>
        public static bool ContainsPoint(float x, float z, float refX, float refZ, SimDistance sim)
            => Contains(x, z, ZoneOf(refX), ZoneOf(refZ), sim);

        /// <summary>One line for `cargo status`.</summary>
        public static string Describe(SimDistance sim)
        {
            string s = sim + ": a claim is kept within " + Wire.Int((int)Reach(sim)) +
                       " m of the reference zone's centre on both axes";
            float r = Radius(sim);
            if (r > 0f) s += " and inside a " + Wire.Int((int)r) + " m circle";
            return s;
        }
    }
}
