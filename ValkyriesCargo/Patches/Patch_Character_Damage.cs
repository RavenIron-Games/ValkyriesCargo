using System;
using HarmonyLib;
using RavenIron.ValkyriesCargo.Client;
using RavenIron.ValkyriesCargo.Core;

namespace RavenIron.ValkyriesCargo.Patches
{
    /// <summary>
    /// Design 3.3: Ingvar is immortal. **The patch is on `RPC_Damage`, not on `Damage`, and that is a
    /// correction to the design's own filename** (see CLAUDE.md's knowledge-base section).
    ///
    /// `Character.Damage(HitData)` is a thin sender: it runs on the ATTACKER's machine, computes a
    /// weak-spot index and calls `InvokeRPC("RPC_Damage", hit)`. No damage maths happens in it at all.
    /// Cancelling there would work only for hits whose attacker is running this patch, and would miss
    /// anything that reaches the victim's own `RPC_Damage` by another road. The private
    /// `Character.RPC_Damage` is the victim-side choke point every hit passes through, so that is
    /// where a merchant stops being damageable.
    ///
    /// A PREFIX at `Priority.Low` honouring `__runOriginal`, the house rule; the try/catch returns
    /// true so vanilla runs if anything here throws. `LiveCount` keeps the cost of a hit on any other
    /// character in the world at one int compare.
    /// </summary>
    [HarmonyPatch(typeof(Character), "RPC_Damage")]
    public static class Patch_Character_RPC_Damage
    {
        private static int _throws;

        [HarmonyPriority(Priority.Low)]
        private static bool Prefix(Character __instance, ref bool __runOriginal)
        {
            // These two are NOT the same answer and must never share a branch again. A prefix's
            // return value is "run the original": `false` SKIPS it. So the old
            // `if (!__runOriginal || LiveCount == 0) return false;` cancelled `Character.RPC_Damage`
            // for EVERY character in the world whenever no merchant was instanced -- which is almost
            // always -- and nothing could take damage at all. Shipped on main and in v0.1.0-rc1;
            // found by Track A's P4/P5 adversarial audit as F1 (`docs/AUDIT-P4P5-2026-09-07.md`).
            // The decision now lives in `Core/Immortality.RunOriginal`, where it is proven off-game.
            if (!__runOriginal) return false;                        // another prefix already cancelled
            if (CargoMerchant.LiveCount == 0) return true;           // no visit: not ours, vanilla runs
            try
            {
                CargoMerchant m = __instance.GetComponent<CargoMerchant>();
                if (Immortality.RunOriginal(__runOriginal, CargoMerchant.LiveCount, m != null)) return true;
                __runOriginal = false;
                return false;
            }
            catch (Exception ex)
            {
                if (_throws++ < 3) ValkyriesCargo.Log.LogError("Patch_Character_RPC_Damage threw: " + ex);
                return true;
            }
        }
    }
}
