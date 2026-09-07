using HarmonyLib;
using RavenIron.ValkyriesCargo.Server;

namespace RavenIron.ValkyriesCargo.Patches
{
    /// <summary>
    /// Appends `valkyries_cargo` to the vanilla event list on every machine (design 4; Ragnarok's Wrath
    /// precedent). A PREFIX at Priority.Low with an unconditional `return true`: `RandEventSystem.Awake`
    /// is `m_instance = this;` and nothing else (decompiled 2026-09-06), and Unity has already deserialized
    /// `m_events` from the prefab by the time it runs, so there is nothing to wait for and we cede the
    /// final say on whether Awake runs to everyone else.
    /// </summary>
    [HarmonyPatch(typeof(RandEventSystem), "Awake")]
    public static class Patch_RandEventSystem_Awake
    {
        [HarmonyPriority(Priority.Low)]
        private static bool Prefix(RandEventSystem __instance)
        {
            // Idempotent and self-contained: a world that loads without our event is a world missing one
            // event, never a world that fails to load.
            CargoEvent.Register(__instance);
            return true;
        }
    }
}
