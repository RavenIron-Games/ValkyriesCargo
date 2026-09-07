using System;
using HarmonyLib;
using RavenIron.ValkyriesCargo.Client;
using RavenIron.ValkyriesCargo.Server;

namespace RavenIron.ValkyriesCargo.Patches
{
    /// <summary>
    /// Design 3.3: a body whose ZDO carries `VCargo_ingvar` becomes Ingvar. A POSTFIX at default
    /// priority, not a prefix - vanilla `Humanoid.Awake` must run in full, because it is what caches
    /// the Rigidbody and the collider, and `Humanoid.Awake` takes `m_visEquipment` and `m_seed`.
    /// It does NOT hand out `m_defaultItems`: `GiveDefaultItems()` is called from `Humanoid.Start`, for
    /// non-players, so a postfix here runs BEFORE the crossbow arrives and not after (decompile-checked
    /// 2026-09-07; an earlier comment claimed otherwise). `CargoMerchant` needs all
    /// of that to exist before it strips the crossbow back off him.
    ///
    /// Every machine. The server never instantiates anything of ours (see `Spawner`'s header), so in
    /// practice this fires on players' machines only - but it is written as if it fires anywhere.
    /// </summary>
    [HarmonyPatch(typeof(Humanoid), "Awake")]
    public static class Patch_Humanoid_Awake
    {
        private static int _throws;

        private static void Postfix(Humanoid __instance)
        {
            try
            {
                // F8 (the 2026-09-07 audit): component Awake order on a GameObject is the prefab's
                // serialized order, not a guarantee, so ZNetView is not promised to run before Humanoid
                // just because this is a postfix -- a postfix only orders us after Humanoid.Awake (and
                // its base.Awake()) on THIS component, not after a SIBLING component's Awake. If ZNetView
                // sits later in the prefab, `nview.GetZDO()` reads null (ZNetView.Awake has not assigned
                // its private m_zdo yet), and without the fallback this postfix would silently no-op:
                // a bare vanilla Dverger standing in a visit with no CargoMerchant on it.
                //
                // The fallback is the same one `Patch_Valkyrie_Awake` uses and carries the same proof
                // (decompiled 2026-09-07, see that file's comment for the full reasoning):
                // ZNetScene.CreateObject sets the PUBLIC STATIC `ZNetView.m_initZDO` immediately before
                // the synchronous Instantiate call and ZNetView.Awake consumes-and-nulls it as its first
                // act; CreateObject nulls it itself if nothing claimed it. Unity runs every component's
                // Awake on a freshly instantiated object before Instantiate returns, synchronously and on
                // one thread, so a non-null m_initZDO seen from here is always THIS object's ZDO.
                ZNetView nview = __instance.GetComponent<ZNetView>();
                ZDO zdo = nview != null ? nview.GetZDO() : null;
                if (zdo == null) zdo = ZNetView.m_initZDO;
                if (zdo == null || zdo.GetInt(Spawner.IngvarHash, 0) == 0) return;
                if (__instance.GetComponent<CargoMerchant>() == null)
                    __instance.gameObject.AddComponent<CargoMerchant>();
            }
            catch (Exception ex)
            {
                if (_throws++ < 3) ValkyriesCargo.Log.LogError("Patch_Humanoid_Awake threw: " + ex);
            }
        }
    }
}
