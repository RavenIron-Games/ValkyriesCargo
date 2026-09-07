using System;
using HarmonyLib;
using RavenIron.ValkyriesCargo.Client;
using RavenIron.ValkyriesCargo.Server;

namespace RavenIron.ValkyriesCargo.Patches
{
    /// <summary>
    /// Design 3.3: a body whose ZDO carries `VCargo_ingvar` becomes Ingvar. A POSTFIX at default
    /// priority, not a prefix - vanilla `Humanoid.Awake` must run in full, because it is what caches
    /// the Rigidbody and the collider and hands out `m_defaultItems`, and `CargoMerchant` needs all
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
                ZNetView nview = __instance.GetComponent<ZNetView>();
                ZDO zdo = nview != null ? nview.GetZDO() : null;
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
