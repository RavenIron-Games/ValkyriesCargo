using System;
using HarmonyLib;
using RavenIron.ValkyriesCargo.Client;
using RavenIron.ValkyriesCargo.Server;

namespace RavenIron.ValkyriesCargo.Patches
{
    /// <summary>
    /// The one named exception to "never replace a vanilla method", on our own object only (design 4;
    /// RavenEye's `UpdateNoMap` shape). A prefix at `Priority.Low` that returns false for a bird carrying
    /// `vc_cargo` and true for everything else, so a real intro Valkyrie is untouched.
    ///
    /// Why it must be a skip and not a postfix, from the decompiled body (2026-09-06):
    /// - `Valkyrie.Awake` line 1 is `m_instance = this`. `Game.SkipIntro` calls
    ///   `Valkyrie.m_instance.DropPlayer(destroy: true)`, and `DropPlayer` rotates and un-intros
    ///   `Player.m_localPlayer` unguarded. If our bird took `m_instance`, a player who skipped their
    ///   own intro while our bird was overhead would be dropped by OUR Valkyrie - or NRE, if they had
    ///   no local player yet.
    /// - `Awake` then reads `Player.m_localPlayer.transform.position` with no null check and TELEPORTS
    ///   that player to the bird. On a dedicated server that is an NRE; on a client it is a kidnapping.
    /// - it also re-rolls a random 500 m approach, overwriting the start point the server authored.
    ///
    /// A non-owner never reaches any of that (`if (!m_nview.IsOwner()) { enabled = false; return; }`),
    /// so this patch is strictly speaking only load-bearing on the pilot - but `m_instance` is assigned
    /// BEFORE that guard, on every machine, which is reason enough to skip it everywhere.
    /// </summary>
    [HarmonyPatch(typeof(Valkyrie), "Awake")]
    public static class Patch_Valkyrie_Awake
    {
        private static int _throws;

        [HarmonyPriority(Priority.Low)]
        private static bool Prefix(Valkyrie __instance, ref bool __runOriginal)
        {
            if (!__runOriginal) return false;    // somebody ahead of us already decided; honour it
            try
            {
                // Public API only: ZNetView.GetZDO. On the create path the ZDO is already attached and
                // already carries the server's keys, because ZNetScene.CreateObject parks it in
                // ZNetView.m_initZDO and ZNetView.Awake consumes it before any other Awake runs.
                ZNetView nview = __instance.GetComponent<ZNetView>();
                ZDO zdo = nview != null ? nview.GetZDO() : null;
                if (zdo == null || zdo.GetInt(Spawner.CargoHash, 0) == 0) return true;   // a real intro; leave it alone

                __instance.enabled = false;
                if (__instance.GetComponent<CargoFlight>() == null) __instance.gameObject.AddComponent<CargoFlight>();
                __runOriginal = false;
                return false;
            }
            catch (Exception ex)
            {
                // Cosmetics never break the game (house rule 3). If we cannot tell whose bird this is,
                // vanilla runs - which for a real intro is right, and for ours is a visible wrong bird
                // rather than a silent missing one.
                if (_throws++ < 3) ValkyriesCargo.Log.LogError("Valkyrie.Awake prefix threw, letting vanilla run: " + ex);
                return true;
            }
        }
    }
}
