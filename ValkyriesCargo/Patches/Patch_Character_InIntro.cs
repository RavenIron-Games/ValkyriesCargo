using System;
using HarmonyLib;
using RavenIron.ValkyriesCargo.Client;

namespace RavenIron.ValkyriesCargo.Patches
{
    /// <summary>
    /// Design 3.3, the carry. `Character.InIntro()` is `public virtual bool` and returns false for
    /// everything except the intro Valkyrie's passenger. Its one caller in `CustomFixedUpdate` does:
    ///
    ///     if (InIntro()) { m_maxAirAltitude = transform.position.y;
    ///                      m_body.linearVelocity = Vector3.zero; m_body.angularVelocity = Vector3.zero; }
    ///
    /// which is exactly what a carried merchant needs: velocity zeroed every step instead of a 120 m
    /// fall accumulating under the pin, and `m_maxAirAltitude` kept level with him so the drop is not
    /// scored as a plummet. It is NOT immunity and was never claimed to be - immortality is a
    /// separate patch, and non-players take no fall damage anyway (`UpdateGroundContact` gates on
    /// `IsPlayer()`).
    ///
    /// `LiveCount` is the cheap gate: this method is called every physics step for every character in
    /// the world, and outside a visit the answer is a single int compare.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.InIntro))]
    public static class Patch_Character_InIntro
    {
        private static int _throws;

        private static void Postfix(Character __instance, ref bool __result)
        {
            if (__result || CargoMerchant.LiveCount == 0) return;
            try
            {
                CargoMerchant m = __instance.GetComponent<CargoMerchant>();
                if (m != null && m.Pinned) __result = true;
            }
            catch (Exception ex)
            {
                if (_throws++ < 3) ValkyriesCargo.Log.LogError("Patch_Character_InIntro threw: " + ex);
            }
        }
    }
}
