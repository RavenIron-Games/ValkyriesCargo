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

        // ---- the plan ------------------------------------------------------------------------------

        /// <summary>
        /// Plan a flight for a pilot standing at (pilotX, pilotZ). Returns a plan that is always inside
        /// the block; `Turned` says the seeded bearing had no room and the plan rotated to find some,
        /// `StartDistance` says how much of the configured distance survived the shrink.
        /// </summary>
        public static Plan Make(float pilotX, float pilotY, float pilotZ, int seed, int activeArea,
                                float startDistance, float startAltitude, float descentDistance)
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

                    // The turn-in point sits between the start and the drop, pushed sideways so the
                    // approach is a banked arc rather than a straight line (vanilla's shape: the
                    // descent start carries a lateral offset the same size as its forward one).
                    float descent = Math.Min(descentDistance, d - drop - ShrinkStep);
                    if (descent < 0f) descent = 0f;
                    float lateral = descent;
                    float px = -dz, pz = dx;                                  // the left-hand perpendicular
                    float ddx = pilotX + dx * (drop + descent) + px * lateral;
                    float ddz = pilotZ + dz * (drop + descent) + pz * lateral;
                    // A lateral swing that leaves the block is dropped rather than shrunk: a straight
                    // approach is a worse-looking flight, never a broken one.
                    if (!PointInBlockWithMargin(ddx, ddz, pilotX, pilotZ, activeArea))
                    {
                        lateral = 0f;
                        ddx = pilotX + dx * (drop + descent);
                        ddz = pilotZ + dz * (drop + descent);
                    }

                    return new Plan
                    {
                        Ok = true,
                        Turned = turn > 0,
                        Bearing = bearing,
                        StartDistance = d,
                        StartX = sx, StartY = pilotY + startAltitude, StartZ = sz,
                        DescentX = ddx, DescentY = pilotY + startAltitude, DescentZ = ddz,
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
            return new Plan { Ok = false, Bearing = baseBearing, StartDistance = 0f,
                              StartX = pilotX, StartY = pilotY + startAltitude, StartZ = pilotZ,
                              DescentX = pilotX, DescentY = pilotY + startAltitude, DescentZ = pilotZ,
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
            public float StartX, StartY, StartZ;
            public float DescentX, DescentY, DescentZ;
            public float DropX, DropY, DropZ;
            public float AwayX, AwayY, AwayZ;

            public override string ToString() =>
                "start (" + Wire.Float(StartX) + ", " + Wire.Float(StartZ) + ") at " + Wire.Float(StartY) +
                ", turn (" + Wire.Float(DescentX) + ", " + Wire.Float(DescentZ) + ")" +
                ", drop (" + Wire.Float(DropX) + ", " + Wire.Float(DropZ) + ")" +
                ", " + Wire.Float(StartDistance) + " m out" + (Turned ? " (bearing turned for room)" : "") +
                (Ok ? "" : " [NO ROOM IN THE BLOCK]");
        }
    }
}
