using System;

namespace RavenIron.ValkyriesCargo.Core
{
    /// <summary>
    /// Where the Valkyrie starts, where it turns, where it drops and where it leaves (design 3.2).
    /// PURE: floats in, floats out, no Unity type and no engine call, so the whole geometry is
    /// proven off-game and the game side is left with nothing but "read the terrain, write the ZDO".
    ///
    /// The one hard constraint is the ACTIVE BLOCK. A client instantiates a ZDO only while
    /// `ZNetScene.InActiveArea(zone(zdo), zone(client))` holds, and destroys the instance the moment
    /// it stops holding; a non-persistent owned ZDO dies with it. Vanilla's intro survives its 500 m
    /// approach only because the passenger rides the bird and drags the reference position along.
    /// Ours has no passenger, so every waypoint has to be inside the pilot's own block from the first
    /// frame. That is why the numbers are 90/120/50 and not the prefab's 500/500/200.
    ///
    /// The approach is STRAIGHT, and that is a finding, not a preference. The first version swung the
    /// turn-in point sideways the way vanilla's does. Vanilla gets away with it because its legs are
    /// 300 m long; ours are 76. A flyer at speed v turning at w degrees a second cannot get inside
    /// v/w metres of its own path, and at the prefab's 20 m/s and 20 deg/s that circle is 57 m across
    /// while the swung turn-in sat 56.6 m off the nose - inside it. The bird orbited the waypoint for
    /// the full 180 s and never dropped, on every flight. `TurningRadius` and `Reachable` below are
    /// that lesson kept as code, and the harness asserts it against the shipped numbers.
    ///
    /// The altitude lives on the descent waypoint, interpolated along the run, so the bird glides down
    /// at a constant angle and is AT drop height when it arrives. The first version left the whole
    /// approach level at 120 m and put the entire descent on the last leg, which is 4 s of flight for
    /// 110 m of altitude: the merchant was cut loose 105 m up. Both were found by simulating `Fly`
    /// against the shipped prefab numbers (PR #8's review).
    /// </summary>
    public static class FlightPlan
    {
        /// <summary>`ZoneSystem.m_zoneSize`. Asserted against the live value by `cargo status`.</summary>
        public const float ZoneSize = 64f;

        /// <summary>Design 3.2: shrink the start distance by this much until the block holds.</summary>
        public const float ShrinkStep = 12f;

        /// <summary>
        /// Below this the flight is not worth flying: the bird would appear on top of the player.
        /// Reaching it means this bearing has no room, and the plan turns rather than shrinking further.
        /// </summary>
        public const float MinimumStartDistance = 30f;

        /// <summary>The drop lands 12-15 m from the pilot (design 3.2), by seed so every machine agrees.</summary>
        public const float DropDistanceMin = 12f;
        public const float DropDistanceSpan = 3f;

        /// <summary>Kept clear of the block edge, so a pilot drifting one step does not strand the bird.</summary>
        public const float EdgeMargin = 8f;

        /// <summary>
        /// How high above the ground the bird is when it lets go, and so the altitude the glide is
        /// planned to arrive at. `CargoFlight` clamps the bird to this over the real terrain; this is
        /// the same number as a plain float, because the plan is pure and the terrain is not.
        /// </summary>
        public const float DropAltitude = 10f;

        /// <summary>
        /// The descent waypoint never eats more than this much of the run. Without it a shrunk start
        /// (54 m out, a 42 m run) puts the configured 50 m descent past the start point, the waypoint
        /// lands ON the start, and the flight silently degenerates to one leg carrying the whole
        /// altitude drop -- the same shape as the bug this waypoint exists to fix.
        /// </summary>
        public const float MaxDescentFraction = 0.75f;

        // ---- zones ---------------------------------------------------------------------------------

        /// <summary>`ZoneSystem.GetZone`: floor((v + zoneSize/2) / zoneSize), on one axis.</summary>
        public static int ZoneOf(float v) => (int)Math.Floor((v + ZoneSize / 2f) / ZoneSize);

        /// <summary>
        /// `ZNetScene.InActiveArea`: |zone - centre| &lt;= activeArea - 1 on both axes. With the decompiled
        /// default of 1 that is the pilot's single 64 m zone; the runtime value is what counts and
        /// `cargo status` prints it.
        /// </summary>
        public static bool InActiveArea(int zx, int zz, int cx, int cz, int activeArea)
        {
            int reach = activeArea - 1;
            return Math.Abs(zx - cx) <= reach && Math.Abs(zz - cz) <= reach;
        }

        /// <summary>True when the world point (x, z) is inside the block centred on the pilot's zone.</summary>
        public static bool PointInBlock(float x, float z, float pilotX, float pilotZ, int activeArea)
            => InActiveArea(ZoneOf(x), ZoneOf(z), ZoneOf(pilotX), ZoneOf(pilotZ), activeArea);

        /// <summary>
        /// The same test with a margin: the point must be at least `EdgeMargin` inside the block on
        /// both axes. A point that only just qualifies falls out of the block as soon as the pilot
        /// walks a step the other way, and the bird vanishes mid-flight.
        /// </summary>
        public static bool PointInBlockWithMargin(float x, float z, float pilotX, float pilotZ, int activeArea)
        {
            if (!PointInBlock(x, z, pilotX, pilotZ, activeArea)) return false;
            int reach = activeArea - 1;
            // The block's outer edges, in world units, around the pilot's zone centre line.
            float cx = ZoneOf(pilotX) * ZoneSize, cz = ZoneOf(pilotZ) * ZoneSize;
            float half = (reach + 0.5f) * ZoneSize;
            return Math.Abs(x - cx) <= half - EdgeMargin && Math.Abs(z - cz) <= half - EdgeMargin;
        }

        // ---- the seed ------------------------------------------------------------------------------

        /// <summary>
        /// The compass bearing this visit's bird flies in on, in radians, from the visit seed. Every
        /// machine derives it from the same int, so nobody has to send a direction.
        /// </summary>
        public static double Bearing(int seed) => (Math.Abs((long)seed) % 3600) / 3600.0 * (Math.PI * 2.0);

        /// <summary>12-15 m, by seed.</summary>
        public static float DropDistance(int seed)
            => DropDistanceMin + (float)((Math.Abs((long)seed) / 3600L) % 1000L) / 1000f * DropDistanceSpan;

        // ---- what a flyer can actually fly ---------------------------------------------------------

        /// <summary>
        /// v / omega in metres: the tightest circle a flyer moving at `speed` and turning at
        /// `turnRateDegrees` a second can hold. Zero or negative turn rate means it cannot turn at all.
        /// </summary>
        public static float TurningRadius(float speed, float turnRateDegrees)
            => turnRateDegrees <= 0.0001f ? float.MaxValue
                                          : (float)(speed / (turnRateDegrees * Math.PI / 180.0));

        /// <summary>
        /// Can a flyer at (px, pz) pointing (hx, hz) ever reach (tx, tz) by steering straight at it?
        /// Only if the target is outside BOTH of its minimum turning circles - the two circles of
        /// `radius` tangent to the heading, one either side. `CargoFlight` is a pure pursuer: it aims
        /// at the waypoint every frame and turns as hard as it may. A pure pursuer whose target sits
        /// inside one of those circles never closes; it settles into an orbit at the radius and stays
        /// there. This is the exact shape of the bug PR #8's review found, and the harness now runs
        /// this over every waypoint of every plan at the shipped speed and turn rate.
        /// </summary>
        public static bool Reachable(float px, float pz, float hx, float hz, float tx, float tz, float radius)
        {
            float hm = (float)Math.Sqrt(hx * hx + hz * hz);
            if (hm < 0.0001f) return true;                       // no heading: nothing to be inside of
            hx /= hm; hz /= hm;
            // The two centres: radius out along the left-hand and right-hand normals of the heading.
            float lx = px - hz * radius, lz = pz + hx * radius;
            float rx = px + hz * radius, rz = pz - hx * radius;
            // STRICTLY inside is what cannot be reached; a point on the circle is on the path, and a
            // point straight ahead is exactly tangent to both circles, so the tolerance is the whole
            // difference between "already there" and "orbits forever".
            const float Tolerance = 0.01f;
            return Dist(tx, tz, lx, lz) >= radius - Tolerance && Dist(tx, tz, rx, rz) >= radius - Tolerance;
        }

        private static float Dist(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        // ---- the plan ------------------------------------------------------------------------------

        /// <summary>
        /// Plan a flight for a pilot standing at (pilotX, pilotZ). Returns a plan that is always inside
        /// the block; `Turned` says the seeded bearing had no room and the plan rotated to find some,
        /// `StartDistance` says how much of the configured distance survived the shrink.
        /// </summary>
        public static Plan Make(float pilotX, float pilotY, float pilotZ, int seed, int activeArea,
                                float startDistance, float startAltitude, float descentDistance,
                                float dropAltitude = DropAltitude)
        {
            // A block of one zone still has ~64 m of room, but only if the pilot is near its middle;
            // standing at a zone edge leaves almost nothing outward. So each bearing is tried at the
            // configured distance and shrunk, and only a bearing with no usable room at all is turned
            // away from. Quarter turns, in a fixed order, so the result stays deterministic.
            double baseBearing = Bearing(seed);
            float drop = DropDistance(seed);

            for (int turn = 0; turn < 4; turn++)
            {
                double bearing = baseBearing + turn * (Math.PI / 2.0);
                float dx = (float)Math.Sin(bearing), dz = (float)Math.Cos(bearing);
                for (float d = Math.Max(startDistance, MinimumStartDistance); d >= MinimumStartDistance; d -= ShrinkStep)
                {
                    float sx = pilotX + dx * d, sz = pilotZ + dz * d;
                    if (!PointInBlockWithMargin(sx, sz, pilotX, pilotZ, activeArea)) continue;

                    // The descent waypoint sits ON the approach line - no lateral swing; see the class
                    // comment for why one cannot be flown at these distances. Its only job is the
                    // altitude break: it carries the height a steady glide would be at when it is
                    // `descent` metres short of the drop, so the bird arrives AT drop height instead
                    // of putting the whole descent on the last leg.
                    float run = d - drop;                                     // horizontal, start -> drop
                    float descent = run > 0f ? Math.Min(descentDistance, run * MaxDescentFraction) : 0f;
                    if (descent < 0f) descent = 0f;
                    // The block-with-margin is an axis-aligned rectangle, so it is convex: a point on
                    // the line can only fall outside it if the DROP does, which happens when the pilot
                    // stands within a margin of their own block's edge. Slide the waypoint back toward
                    // the start, which the loop above has already proved is inside.
                    while (descent < run &&
                           !PointInBlockWithMargin(pilotX + dx * (drop + descent), pilotZ + dz * (drop + descent),
                                                   pilotX, pilotZ, activeArea))
                        descent = Math.Min(run, descent + ShrinkStep);

                    float frac = run > 0.001f ? descent / run : 0f;
                    float ddx = pilotX + dx * (drop + descent);
                    float ddz = pilotZ + dz * (drop + descent);
                    float ddy = pilotY + dropAltitude + (startAltitude - dropAltitude) * frac;

                    return new Plan
                    {
                        Ok = true,
                        Turned = turn > 0,
                        Bearing = bearing,
                        StartDistance = d,
                        DescentDistance = descent,
                        StartX = sx, StartY = pilotY + startAltitude, StartZ = sz,
                        DescentX = ddx, DescentY = ddy, DescentZ = ddz,
                        DropX = pilotX + dx * drop, DropY = pilotY, DropZ = pilotZ + dz * drop,
                        // Away along the entry line, still at altitude: a lateral exit, never a
                        // vertical one. It only has to survive long enough to leave the screen; the
                        // bird destroys itself, so this point may sit outside the block.
                        AwayX = pilotX - dx * (d * 2f), AwayY = pilotY + startAltitude, AwayZ = pilotZ - dz * (d * 2f),
                    };
                }
            }

            // Nowhere in the block has room on any bearing: the pilot is jammed into a corner of a
            // one-zone block. Fly the shortest honest flight there is, straight down the seeded
            // bearing from the drop, and let the caller decide whether that is worth doing.
            return new Plan { Ok = false, Bearing = baseBearing, StartDistance = 0f, DescentDistance = 0f,
                              StartX = pilotX, StartY = pilotY + startAltitude, StartZ = pilotZ,
                              DescentX = pilotX, DescentY = pilotY + dropAltitude, DescentZ = pilotZ,
                              DropX = pilotX, DropY = pilotY, DropZ = pilotZ,
                              AwayX = pilotX, AwayY = pilotY + startAltitude, AwayZ = pilotZ };
        }

        /// <summary>One planned flight: three waypoints and the drop, all in world XZ with an altitude.</summary>
        public sealed class Plan
        {
            public bool Ok;
            public bool Turned;
            public double Bearing;
            public float StartDistance;
            /// <summary>How far short of the drop the descent waypoint sits, after the block slide.</summary>
            public float DescentDistance;
            public float StartX, StartY, StartZ;
            public float DescentX, DescentY, DescentZ;
            public float DropX, DropY, DropZ;
            public float AwayX, AwayY, AwayZ;

            public override string ToString() =>
                "start (" + Wire.Float(StartX) + ", " + Wire.Float(StartZ) + ") at " + Wire.Float(StartY) +
                ", descent (" + Wire.Float(DescentX) + ", " + Wire.Float(DescentZ) + ") at " + Wire.Float(DescentY) +
                " (" + Wire.Float(DescentDistance) + " m short)" +
                ", drop (" + Wire.Float(DropX) + ", " + Wire.Float(DropZ) + ") at " + Wire.Float(DropY) +
                ", straight in " + Wire.Float(StartDistance) + " m out" + (Turned ? " (bearing turned for room)" : "") +
                (Ok ? "" : " [NO ROOM IN THE BLOCK]");
        }
    }
}
