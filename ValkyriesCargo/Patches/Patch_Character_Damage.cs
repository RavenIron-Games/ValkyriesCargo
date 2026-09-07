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

    /// <summary>
    /// F6 (`docs/AUDIT-P4P5-2026-09-07.md`): immortality missed the damage-over-time path. The prefix
    /// above only patches `Character.RPC_Damage`, which is the choke point for a HIT -- but three status
    /// effects call `Character.ApplyDamage` DIRECTLY and never go near `Damage`/`RPC_Damage` at all.
    ///
    /// Confirmed against the real decompile
    /// (`~/WubarrkCODING/libs-Tools/DECOMPILED ASSEMBLY VALHEIM/assembly_valheim.decompiled.cs`, 0.221.12):
    ///
    /// - `:8821` -- `public void ApplyDamage(HitData hit, bool showDamageText, bool triggerEffects,
    ///   HitData.DamageModifier mod = HitData.DamageModifier.Normal)`. This is the ONLY `ApplyDamage`
    ///   declared on `Character` (grep of the whole assembly finds exactly one other `ApplyDamage`,
    ///   `WearNTear.ApplyDamage(float, HitData)` at `:130337` -- a different class entirely, structures
    ///   not characters, irrelevant here) and it is NOT `virtual`, so there is exactly one implementation
    ///   in the whole assembly and no override-resolution question the way `GetHoverText`/`GetHoverName`
    ///   had one. The four-argument overload is named explicitly below anyway, as a guard against a
    ///   future Valheim update adding a second `ApplyDamage` overload and turning a name-only
    ///   `AccessTools.Method` lookup into an `AmbiguousMatchException` that silently drops every patch in
    ///   this class -- a real, precedented failure mode
    ///   (`docs/knowledge-base/IMPLEMENTATIONS/SoM-v2-Surveys/SURVEY-patches.md:841-844`). Worth pinning
    ///   with Valheim 1.0 landing 2026-09-09.
    /// - `:24785` -- `SE_Burning.UpdateStatusEffect(float dt)` -> `m_character.ApplyDamage(hitData,
    ///   showDamageText: true, triggerEffects: false)`.
    /// - `:25297` -- `SE_Poison.UpdateStatusEffect(float dt)` -> same call shape.
    /// - `:25541` -- `SE_Smoke.UpdateStatusEffect(float dt)` -> same call shape.
    /// - `:8859` -- inside `ApplyDamage`, `SetHealth(health)`; `Character.SetHealth` (`:9328`) writes
    ///   `ZDOVars.s_health` on the ZDO when owned. `:9237` `CheckDeath()` (called every
    ///   `CustomFixedUpdate`, `:7473`, on the owner only) calls `OnDeath()` when health reaches 0;
    ///   `OnDeath` (`:9249`) ends, at `:9319`, in `ZNetScene.instance.Destroy(base.gameObject)` -- the
    ///   merchant's ZDO is gone, and that is why this is fatal and not cosmetic.
    /// - The status effect can arrive with **no hit at all**: `EffectArea.CustomFixedUpdate` (`:106576`)
    ///   calls `item.GetSEMan().AddStatusEffect(m_statusEffectHash, resetTime: true)` directly on every
    ///   `Character` colliding with the trigger volume -- which is how a campfire or a hearth (a
    ///   `PlayerBase`/`Fire`-type `EffectArea`) gives a nearby character `Burning`/`Smoke` with zero
    ///   `HitData` involved. Ingvar lands 12-15 m from the pilot, beside their base.
    ///
    /// What else was checked and found NOT a second gap: `Character.UseHealth` (`:9345`) also reaches
    /// `SetHealth`, but its only two callers (`:1311`, `:1649`) reduce the ATTACKING character's OWN
    /// health as a weapon's cast cost -- Ingvar never wields a weapon (`UnequipAllItems`) and never
    /// attacks (no target: `m_alertRange = 0`), so this path is never reached against or by him.
    /// `Character.RPC_Heal` (`:8609`) only ever raises health (`if (num > health) SetHealth(num)`;
    /// `:8620-8623`) -- never a damage path. Every other `ZDOVars.s_health` writer in the assembly
    /// (`:104522`, `:126627`, `:126835`, `:129576`, `:129661`, `:130345`, `:130384`) belongs to
    /// `MineRock`/`Destructible`/`WearNTear`-family classes, not `Character` -- irrelevant to a
    /// Humanoid-based merchant. No second gap found; only `ApplyDamage`.
    ///
    /// Cosmetic note, not chased: cancelling the damage does not remove the `Burning`/`Poison`/`Smoke`
    /// status effect itself (that is `SEMan`'s business, driven by its own duration), so Ingvar may
    /// visibly burn or smoke until it expires on its own timer. The ZDO surviving is the point; a
    /// merchant who flinches at a campfire is not a bug this patch owns.
    ///
    /// Same shape as `Patch_Character_RPC_Damage` on purpose: a PREFIX at `Priority.Low` honouring
    /// `__runOriginal`, `LiveCount` as the cheap gate, and the decision delegated to the same pure
    /// `Immortality.RunOriginal` -- it already takes exactly "run the original, the live count, is this
    /// our merchant" and does not care which vanilla method is asking, so no change to that file was
    /// needed to cover this second choke point.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage),
        new[] { typeof(HitData), typeof(bool), typeof(bool), typeof(HitData.DamageModifier) })]
    public static class Patch_Character_ApplyDamage
    {
        private static int _throws;

        [HarmonyPriority(Priority.Low)]
        private static bool Prefix(Character __instance, ref bool __runOriginal)
        {
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
                if (_throws++ < 3) ValkyriesCargo.Log.LogError("Patch_Character_ApplyDamage threw: " + ex);
                return true;
            }
        }
    }
}
