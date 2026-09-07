using System;
using HarmonyLib;
using RavenIron.ValkyriesCargo.Client;

namespace RavenIron.ValkyriesCargo.Patches
{
    /// <summary>
    /// F2 (`docs/AUDIT-P4P5-2026-09-07.md`): the hover prompt never appeared. `CargoMerchant` (design
    /// 3.3, `Client/CargoMerchant.cs`) implements <see cref="Hoverable"/> and carries the real
    /// `GetHoverText`/`GetHoverName` bodies -- the countdown, the "[E] Trade" / "Shift+E" prompt -- but
    /// `Character` (the Dverger's own base) is ALSO a `Hoverable` and wins the lookup before we ever get
    /// asked.
    ///
    /// Confirmed against the real decompile
    /// (`~/WubarrkCODING/libs-Tools/DECOMPILED ASSEMBLY VALHEIM/assembly_valheim.decompiled.cs`, the
    /// 0.221.12 build this DLL is compiled against):
    ///
    /// - `:6817` -- `public class Character : MonoBehaviour, IDestructible, Hoverable, IWaterInteractable, IMonoUpdater`.
    /// - `:10158` -- `public virtual string GetHoverText()`: returns `Tameable.GetHoverText()` if a
    ///   `Tameable` is present, else `""`. The shipped Dverger has no `Tameable` (CLAUDE.md engine facts).
    /// - `:10168` -- `public virtual string GetHoverName()`: same shape, else
    ///   `Localization.instance.Localize(m_name)`. Both PUBLIC VIRTUAL -- confirmed, not assumed.
    /// - `:12799` -- `public class Humanoid : Character` (the Dverger's own script; grepping the whole
    ///   assembly for "class X : Humanoid" finds only `Player`, so the shipped Dverger prefab runs
    ///   `Humanoid` itself, no further subclass) spans to `:14756` and overrides NEITHER method -- so
    ///   `Character`'s own virtuals above are exactly what a Dverger resolves to, with no override in
    ///   between to worry about. (The `GetHoverName`/`GetHoverText` pair that sits textually near
    ///   `Humanoid` in the decompiled file, around `:15242`, belongs to the unrelated `Pet` class --
    ///   `:15076` to `:15312` -- reading `m_tameable`/`m_itemStand`; not `Humanoid`'s.)
    /// - `:39723` -- `Hud.UpdateCrosshair(Player, float)`: `hoverObject.GetComponentInParent<Hoverable>()`
    ///   then `hoverable.GetHoverText()`. `GetComponentInParent<T>`, starting on the object itself, returns
    ///   the FIRST match in Unity's internal component order; `Character`/`Humanoid` are part of the
    ///   Dverger prefab and `CargoMerchant` is `AddComponent`ed at runtime by `Patch_Humanoid_Awake`, so
    ///   it is always last and never wins.
    ///
    /// Result: looking at Ingvar drew nothing at all -- no name, no trade prompt, no countdown --
    /// although `Interact` still reached `CargoMerchant` correctly (`Character` does not implement
    /// `Interactable`), so pressing E worked blind.
    ///
    /// One class, two targets -- the shape `Libs/SharedUI/UIFocus.cs`'s `UIFocusPatch` already uses in
    /// this same assembly: a bare class-level `[HarmonyPatch]` (so `Patching.cs`'s discovery, which keys
    /// on any `HarmonyAttribute`, still finds it) and each method carrying its own
    /// `[HarmonyPatch(typeof, nameof)]` + explicit `[HarmonyPostfix]`. POSTFIXES at DEFAULT priority
    /// (house rule 1: decorating a result is the whole point here, not replacing behaviour -- the same
    /// shape as `Patch_Character_InIntro`), each gated on `CargoMerchant.LiveCount` first so every OTHER
    /// character's hover lookup in the world, visit or not, costs one int compare. `CargoMerchant :
    /// Hoverable` stays the one place the text is written (`Client/CargoMerchant.cs`
    /// `GetHoverText`/`GetHoverName`); these two methods only forward to it, and only when our component
    /// is actually present, so an unrelated character's own hover (or another mod's postfix on it) is
    /// left exactly as vanilla or that mod produced it.
    /// </summary>
    [HarmonyPatch]
    public static class Patch_Character_Hover
    {
        private static int _throwsText, _throwsName;

        [HarmonyPatch(typeof(Character), nameof(Character.GetHoverText))]
        [HarmonyPostfix]
        private static void Character_GetHoverText_Postfix(Character __instance, ref string __result)
        {
            if (CargoMerchant.LiveCount == 0) return;
            try
            {
                CargoMerchant m = __instance.GetComponent<CargoMerchant>();
                if (m != null) __result = m.GetHoverText();
            }
            catch (Exception ex)
            {
                if (_throwsText++ < 3)
                    ValkyriesCargo.Log.LogError("Patch_Character_Hover.Character_GetHoverText_Postfix threw: " + ex);
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.GetHoverName))]
        [HarmonyPostfix]
        private static void Character_GetHoverName_Postfix(Character __instance, ref string __result)
        {
            if (CargoMerchant.LiveCount == 0) return;
            try
            {
                CargoMerchant m = __instance.GetComponent<CargoMerchant>();
                if (m != null) __result = m.GetHoverName();
            }
            catch (Exception ex)
            {
                if (_throwsName++ < 3)
                    ValkyriesCargo.Log.LogError("Patch_Character_Hover.Character_GetHoverName_Postfix threw: " + ex);
            }
        }
    }
}
