using System;
using HarmonyLib;
using RavenIron.ValkyriesCargo.Client;
using UnityEngine;

namespace RavenIron.ValkyriesCargo.Patches
{
    /// <summary>
    /// The use key on Ingvar is ours whatever else sits on the prefab. <c>Player.Interact(GameObject, bool,
    /// bool)</c> takes <c>go.GetComponentInParent&lt;Interactable&gt;()</c>, the FIRST Interactable in
    /// component order, and a component another mod put on the Dverger prefab always precedes
    /// <c>CargoMerchant</c>, which is added at runtime (Wonderland, 2026-09-15: DvergrAllies' Tameable took
    /// the key and answered with the tame-follow). <c>MerchantGuard</c> removes the components it knows;
    /// this prefix is the general case: a use on an object that carries CargoMerchant goes to
    /// CargoMerchant and the original does not run. Vanilla's own gates on the way in are kept as
    /// written (<c>InAttack</c>, <c>InDodge</c>, the 0.2 s hold throttle) and its stamp is written through
    /// the injected field, so a hold on him throttles the next hold exactly as vanilla would. The
    /// interact animation is not played: its helper is private, and no vanilla creature you talk to
    /// plays one either.
    /// </summary>
    [HarmonyPatch(typeof(Player), "Interact", typeof(GameObject), typeof(bool), typeof(bool))]
    public static class Patch_Player_Interact
    {
        private static int _throws;

        private static bool Prefix(Player __instance, GameObject go, bool hold, bool alt, ref float ___m_lastHoverInteractTime)
        {
            if (CargoMerchant.LiveCount == 0 || go == null) return true;
            try
            {
                CargoMerchant m = go.GetComponentInParent<CargoMerchant>();
                if (m == null) return true;
                if (__instance.InAttack() || __instance.InDodge()) return false;
                if (hold && Time.time - ___m_lastHoverInteractTime < 0.2f) return false;
                ___m_lastHoverInteractTime = Time.time;
                m.Interact(__instance, hold, alt);
                return false;
            }
            catch (Exception ex)
            {
                if (_throws++ < 3) ValkyriesCargo.Log.LogError("Patch_Player_Interact threw: " + ex);
                return true;
            }
        }
    }
}
