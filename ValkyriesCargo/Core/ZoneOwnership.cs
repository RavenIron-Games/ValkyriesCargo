namespace RavenIron.ValkyriesCargo.Core
{
    /// <summary>
    /// PURE. Vanilla's ownership sweep, as arithmetic, so the reason D5 happened can be proven at the
    /// desk instead of argued from a log.
    ///
    /// The server -- and only the server: `ZDOMan.Update` calls it inside
    /// `if (ZNet.instance.IsServer())` -- runs `ReleaseZDOS` every 2 s and for each connected peer calls
    /// `ReleaseNearbyZDOS(peer.m_refPos, peer.m_uid)`. For a PERSISTENT ZDO that peer already owns, the
    /// whole of the strip branch is, on 1.0.7:
    ///
    ///     if (!ZNetScene.InActiveArea(position, zone)) zdo.SetOwner(0L);
    ///
    /// with `zone = ZoneSystem.GetZone(refPosition)` and `InActiveArea` the metre test that
    /// `ActiveArea.Contains` mirrors: a claim survives only while the object stays inside the owner's
    /// ACTIVE AREA, a square (and at one setting a circle) about the centre of the owner's zone. On a
    /// stock server that is the 3x3 block of 64 m zones 0.221.12's `m_activeArea = 2` gave. The
    /// merchant is persistent and `Spawner` authors him owned by the pilot at the flight start, ~90 m
    /// out -- and 90 m is not reliably inside that block, which is the entire defect. `Distance` alone
    /// cannot answer it, because the area is built about the ZONE centre, not the owner: two points
    /// 90 m apart may share a zone edge or straddle three of them depending on where the origin
    /// falls. That is why this is arithmetic and not a constant.
    ///
    /// 0.221.12 wrote the same test in zone indices (`InActiveArea(sector, zone, m_activeArea - 1)`, a
    /// Chebyshev test on the grid) and this file mirrored it that way. 1.0 (the sweep of 2026-09-09)
    /// replaced `m_activeArea` with the synced simulation distance and the index test with the metre
    /// one, so this file now rests on `ActiveArea`, and the live setting comes through
    /// `ActiveAreaLive.Read()`. Nothing here reads a game type.
    /// </summary>
    public static class ZoneOwnership
    {
        /// <summary>The zone grid vanilla hardcodes in `ZoneSystem.GetZone`, in metres.</summary>
        public const float ZoneSize = ActiveArea.ZoneSize;

        /// <summary>`ZoneSystem.GetZone` for one axis: `FloorToInt((v + 32) / 64)`, floored, never truncated.</summary>
        public static int ZoneIndex(float v) => ActiveArea.ZoneOf(v);

        /// <summary>
        /// True when the server's next sweep would take the owner's claim away from a persistent object
        /// at (<paramref name="x"/>, <paramref name="z"/>) whose owner's reference position is at
        /// (<paramref name="refX"/>, <paramref name="refZ"/>). The y axis is deliberately absent: the
        /// area is a 2D shape, so the Valkyrie's 155 m of altitude is not what saves or damns the claim.
        /// <paramref name="sim"/> is the LIVE simulation distance (`ActiveAreaLive.Read()`), never a
        /// compiled default.
        /// </summary>
        public static bool WouldStripClaim(float x, float z, float refX, float refZ, SimDistance sim)
            => !ActiveArea.ContainsPoint(x, z, refX, refZ, sim);
    }
}
