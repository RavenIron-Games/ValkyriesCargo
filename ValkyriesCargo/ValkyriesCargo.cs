using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using RavenIron.ValkyriesCargo.Config;
using RavenIron.ValkyriesCargo.Core;

namespace RavenIron.ValkyriesCargo
{
    /// <summary>
    /// The entry point. Binds the config (which creates the ServerSync instance and its
    /// version gate), installs the patches, starts the one tick, prints the boot line.
    ///
    /// One role-aware DLL, as in the studio's other mods: the server rolls visits and owns
    /// the market, a client renders and trades, a listen host does both. The roles are told
    /// apart at RUNTIME, never at build time.
    /// </summary>
    [BepInPlugin(PluginId, PluginName, PluginVersion)]
    [BepInProcess("valheim.exe")]
    [BepInProcess("valheim_server.exe")]
    public class ValkyriesCargo : BaseUnityPlugin
    {
        public const string PluginId   = "com.raveniron.valkyriescargo";
        public const string PluginName = "Valkyrie's Cargo";

        /// <summary>
        /// Generated at build time from the csproj Version property (GenerateVersionConst).
        /// Never edit or hardcode: two copies of a version drift.
        /// </summary>
        public const string PluginVersion = BuildVersion.Value;

        public static ValkyriesCargo Instance { get; private set; }
        public static ManualLogSource Log { get; private set; }

        private Harmony _harmony;

        /// <summary>
        /// Can this process draw anything at all? Decided at Awake, before ZNet exists.
        /// GraphicsDeviceType.Null is the headless tell that survives compiling against the
        /// client's reference assembly, in which ZNet.IsDedicated is a hardcoded false.
        /// </summary>
        public static bool HasRenderer =>
            UnityEngine.SystemInfo.graphicsDeviceType !=
            UnityEngine.Rendering.GraphicsDeviceType.Null;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            // Creates the ConfigSync (version gate included) and parses the catalogue.
            ModConfig.Bind(base.Config, PluginId, PluginName, PluginVersion);

            // P10b, and it belongs exactly here: AFTER the config binds (a probe's answer is printed by
            // `cargo status`, which reads config) and BEFORE PatchAll (a patch must never be waiting on
            // a probe that has not run). It logs the four version numbers it found against the ones this
            // DLL was compiled with, and resolves every engine fact a patch relies on, once. A mismatch
            // is information: nothing here refuses to load.
            EngineCheck.Run();

            _harmony = new Harmony(PluginId);
            _harmony.PatchAll();

            // A plain MonoBehaviour driven from Update - deliberately NOT a coroutine.
            gameObject.AddComponent<CargoTick>();

            // The Cargo Terminal exists only where a player can be drawn; the merchant (P5) opens it through ICargoTerminal.
            if (HasRenderer) Client.Terminal.CargoTerminal.Install();

            // Proof of life. A silent success and a silent no-op are indistinguishable from
            // outside the game, so this line exists before there is anything to report.
            Log.LogInfo(
                $"{PluginName} v{PluginVersion} loaded - renderer={HasRenderer}, " +
                $"patches={_harmony.GetPatchedMethods().Count()}, " +
                $"catalogue={ModConfig.CatalogueParsed.Count} entries" +
                (ModConfig.CatalogueProblems.Count > 0 ? $" ({ModConfig.CatalogueProblems.Count} problem(s), see `cargo status`)" : "") +
                $", {EngineCheck.StatusLine()}" +
                ", ServerSync version gate armed; role is decided when a world loads.");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            _harmony = null;
            Instance = null;
        }
    }
}
