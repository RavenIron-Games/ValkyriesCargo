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
    /// the injected field, so a hold on him throttles the next hold exactly as vanilla would.
    ///
    /// House rule 1: <c>Priority.Low</c> and <c>__runOriginal</c> honoured. A mod that refuses interaction
    /// outright (a seated or spectator gate, a "no use while a window is open" rule) prefixes the same
    /// method and cancels it; running last and stepping aside when it has, we never open the terminal or
    /// fire a dismiss in a state such a mod forbade. Running last is also what "ours whatever else sits on
    /// the prefab" wants.
    ///
    /// What changes for every player, taming mod or not: the interact animation is NOT played for him.
    /// Vanilla's <c>DoInteractAnimation</c> is private (it snaps the player round to face the target and
    /// fires the <c>interact</c> trigger) and ran whenever <c>CargoMerchant.Interact</c> answered true; no
    /// vanilla trader plays one either - Haldor's <c>Trader.Interact</c> answers false on a successful
    /// open - so he now behaves like the game's own traders. Deliberate, and the reason it is written here.
    /// </summary>
    [HarmonyPatch(typeof(Player), "Interact", typeof(GameObject), typeof(bool), typeof(bool))]
    public static class Patch_Player_Interact
    {
        private static int _throws;

        [HarmonyPriority(Priority.Low)]
        private static bool Prefix(Player __instance, GameObject go, bool hold, bool alt, ref bool __runOriginal, ref float ___m_lastHoverInteractTime)
        {
            if (!__runOriginal) return false;    // somebody ahead of us already decided; honour it
            if (CargoMerchant.LiveCount == 0 || go == null) return true;
            // Once the merchant has been handed the use, the original must not run whatever happens after:
            // a second call on the same keypress would land inside the dismiss window and defeat the
            // two-press safety. The catch answers by this flag, not by a constant.
            bool acted = false;
            try
            {
                CargoMerchant m = go.GetComponentInParent<CargoMerchant>();
                if (m == null) return true;
                if (__instance.InAttack() || __instance.InDodge()) return false;
                if (hold && Time.time - ___m_lastHoverInteractTime < 0.2f) return false;
                ___m_lastHoverInteractTime = Time.time;
                acted = true;
                m.Interact(__instance, hold, alt);
                return false;
            }
            catch (Exception ex)
            {
                if (_throws++ < 3) ValkyriesCargo.Log.LogError("Patch_Player_Interact threw: " + ex);
                return !acted;
            }
        }
    }
}
