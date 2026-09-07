using System;
using HarmonyLib;
using RavenIron.ValkyriesCargo.Client;
using RavenIron.ValkyriesCargo.Core;

namespace RavenIron.ValkyriesCargo.Patches
{
    /// <summary>
    /// Ghost mode (F11; the owner's decision 2026-09-07): hostiles neither target nor fear Ingvar.
    ///
    /// The hook is the static `BaseAI.IsEnemy(Character a, Character b)` (assembly_valheim 0.221.12
    /// decompiled line 4994). It is the ONE gate: `BaseAI.FindEnemy` / `FindClosestEnemy` (targeting),
    /// `Attack`'s melee and area hit filters, `Aoe`, the instance `IsEnemy(other)`, `HaveFriendsInRange`,
    /// and the enemy HUD (`EnemyHud`) all call it. A tamed creature falls through to `true` there for
    /// every hostile faction, which is how tamed wolves get attacked and why a tamed Ingvar parks a raid.
    ///
    /// A PREFIX at `Priority.Low` honouring `__runOriginal`, the house rule. Any pair that has our
    /// merchant in it answers false and vanilla does not run; every other pair is untouched, so a raid
    /// still fights the player. `LiveCount` keeps the cost outside a visit at one int compare, and the
    /// decision itself is pure (`Core/Ghost`), with the checks that F1 taught us to write.
    /// </summary>
    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.IsEnemy), new[] { typeof(Character), typeof(Character) })]
    public static class Patch_BaseAI_IsEnemy
    {
        private static int _throws;

        [HarmonyPriority(Priority.Low)]
        private static bool Prefix(Character a, Character b, ref bool __result, ref bool __runOriginal)
        {
            if (!__runOriginal) return false;                 // another prefix already cancelled
            if (CargoMerchant.LiveCount == 0) return true;    // no visit: not ours, vanilla runs
            try
            {
                bool am = a != null && a.GetComponent<CargoMerchant>() != null;
                bool bm = b != null && b.GetComponent<CargoMerchant>() != null;
                switch (Ghost.Decide(__runOriginal, CargoMerchant.LiveCount, am, bm))
                {
                    case Ghost.Verdict.NotEnemies:
                        __result = false;
                        __runOriginal = false;
                        return false;
                    case Ghost.Verdict.Cancelled:
                        return false;
                    default:
                        return true;
                }
            }
            catch (Exception ex)
            {
                if (_throws++ < 3) ValkyriesCargo.Log.LogError("Patch_BaseAI_IsEnemy threw: " + ex);
                return true;
            }
        }
    }
}
