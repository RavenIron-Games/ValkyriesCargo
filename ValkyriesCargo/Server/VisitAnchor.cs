using RavenIron.ValkyriesCargo.Core;
using UnityEngine;

namespace RavenIron.ValkyriesCargo.Server
{
    /// <summary>
    /// Where the visit IS. The drop point is where he landed; after the leash walk (MerchantPlan) he can
    /// be a hundred metres from it, following a player. Everything the server measures a player against
    /// -- the event's 96 m area (the banner, the clock's pause) and the deal wire's dismiss rule -- wants
    /// his live position, which the server reads off his ZDO (the owner syncs it like any other); the
    /// drop point is the fallback while none is bound (the flight, an adopted row before the spawn).
    /// Visit 21 (2026-09-08): a dismiss from beside him was refused as "134 m from the drop point".
    /// </summary>
    public static class VisitAnchor
    {
        /// <summary>His live position from the bound merchant ZDO; false when none is bound or it is gone.</summary>
        public static bool MerchantAt(out Vector3 at)
        {
            at = Vector3.zero;
            ZDOID id = Spawner.Merchant;
            if (id.IsNone()) return false;
            ZDOMan man = ZDOMan.instance;
            if (man == null) return false;
            ZDO zdo = man.GetZDO(id);
            if (zdo == null || !zdo.IsValid()) return false;
            at = zdo.GetPosition();
            return true;
        }

        /// <summary>The merchant's live position when bound (live = true), else the visit's drop point.</summary>
        public static Vector3 Of(VisitSession session, out bool live)
        {
            Vector3 at;
            live = MerchantAt(out at);
            return live ? at : new Vector3(session.DropX, session.DropY, session.DropZ);
        }
    }
}
