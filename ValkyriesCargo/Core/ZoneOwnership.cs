namespace RavenIron.ValkyriesCargo.Core
{
    /// <summary>
    /// PURE. Vanilla's ownership sweep, as arithmetic, so the reason D5 happened can be proven at the
    /// desk instead of argued from a log.
    ///
    /// The server -- and only the server: `ZDOMan.Update` calls it inside
    /// `if (ZNet.instance.IsServer())` (`asm:65095`) -- runs `ReleaseZDOS` every 2 s (`asm:65155`) and
    /// for each connected peer calls `ReleaseNearbyZDOS(peer.m_refPos, peer.m_uid)` (`asm:65164`). For a
    /// PERSISTENT ZDO that peer already owns, the whole of the strip branch is (`asm:65193`):
    ///
    ///     if (!ZNetScene.InActiveArea(sector, zone, m_activeArea - 1)) zdo.SetOwner(0L);
    ///
    /// So a claim survives only while the object stays inside a square block of zones centred on the
    /// owner's reference position. `m_activeArea` reads 2 live, giving `activatedArea = 1`: a 3x3 block
    /// of 64 m zones. The merchant is persistent and `Spawner` authors him owned by the pilot at the
    /// flight start, ~90 m out -- and 90 m is not reliably inside a 3x3 block, which is the entire
    /// defect. `Distance` alone cannot answer it, because the block is built from ZONE indices, not
    /// from a radius: two points 90 m apart may share a zone edge or straddle three of them depending
    /// on where the origin falls. That is why this is arithmetic and not a constant.
    ///
    /// Nothing here reads a game type; the zone maths is `ZoneSystem.GetZone` (`asm:99674`), which
    /// hardcodes the 64 m grid and the +32 offset rather than reading `m_zoneSize`, so this mirror is
    /// exact rather than approximate.
    ///
    /// CHECKED AGAINST THE 1.0 PLAYTEST, 2026-09-07 (build 23105022 / 0.221.13, the dedicated-server
    /// assembly in `libs-Tools/DECOMPILED-PLAYTEST-build23105022/`): `ReleaseNearbyZDOS` is the same
    /// method statement for statement - the `!Persistent` skip, `activatedArea = m_activeArea - 1`, the
    /// `SetOwner(0L)` strip and the grant branch all unchanged - and `GetZone` still computes
    /// `FloorToInt((v + 32.0) / 64.0)` on both axes. What moved is only the TYPE: `GetZone` returns
    /// `Vector2s` and the three `ZNetScene.InActiveArea` overloads take it, where 0.221.12 had
    /// `Vector2i`. Because this file is arithmetic over plain ints rather than a call into either, D5's
    /// mechanism and this mirror both survive the version step untouched. The typed surface does not:
    /// `EngineCheck.cs`'s three `InActiveArea` probe rows name `Vector2i` explicitly and will report
    /// FAILED on 1.0, which is P10b behaving exactly as designed - a probe is a veto, and this is the
    /// stop-ship signal firing. See `docs/TODO.md` §2, the 1.0 sweep.
    /// </summary>
    public static class ZoneOwnership
    {
        /// <summary>The zone grid vanilla hardcodes in `ZoneSystem.GetZone`, in metres.</summary>
        public const float ZoneSize = 64f;

        /// <summary>`ZoneSystem.m_activeArea` reads 2 on a live scene (CLAUDE.md engine facts, 2026-09-07).</summary>
        public const int LiveActiveArea = 2;

        /// <summary>
        /// `ZoneSystem.GetZone` for one axis (`asm:99674`): `FloorToInt((v + 32) / 64)`. Written with a
        /// double and an explicit floor so a negative coordinate rounds the way vanilla's does -- C#
        /// integer division truncates toward zero, which would put everything in the zone west of where
        /// vanilla puts it for half the world.
        /// </summary>
        public static int ZoneIndex(float v)
        {
            double scaled = ((double)v + ZoneSize / 2.0) / ZoneSize;
            return (int)System.Math.Floor(scaled);
        }

        /// <summary>
        /// `ZNetScene.InActiveArea(zone, refCenterZone, activatedArea)` (`asm:69735`): the Chebyshev
        /// distance between the two zone indices is within <paramref name="activatedArea"/>.
        /// </summary>
        public static bool InActiveArea(int zoneX, int zoneZ, int centreX, int centreZ, int activatedArea)
        {
            return zoneX >= centreX - activatedArea && zoneX <= centreX + activatedArea &&
                   zoneZ >= centreZ - activatedArea && zoneZ <= centreZ + activatedArea;
        }

        /// <summary>
        /// True when the server's next sweep would take the owner's claim away from a persistent object
        /// at (<paramref name="x"/>, <paramref name="z"/>) whose owner's reference position is at
        /// (<paramref name="refX"/>, <paramref name="refZ"/>). The y axis is deliberately absent: zones
        /// are a 2D grid, so the Valkyrie's 155 m of altitude is not what saves or damns the claim.
        ///
        /// <paramref name="activeArea"/> is `ZoneSystem.m_activeArea` as read at runtime, NOT the
        /// compiled default -- the scene overrides it (it reads 2, the compiled default is 1), and
        /// vanilla subtracts one from it here.
        /// </summary>
        public static bool WouldStripClaim(float x, float z, float refX, float refZ, int activeArea)
        {
            return !InActiveArea(ZoneIndex(x), ZoneIndex(z), ZoneIndex(refX), ZoneIndex(refZ), activeArea - 1);
        }
    }
}
