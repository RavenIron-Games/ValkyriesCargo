using System;
using BepInEx;
using BepInEx.Bootstrap;

namespace RavenIron.ValkyriesCargo.Server
{
    /// <summary>
    /// Is a backpack mod loaded on THIS machine? The backpack add-on (Wu'barrk's design, the owner's
    /// decision 2026-09-08: Smoothbrain's Backpacks, GUID `org.bepinex.plugins.backpacks`): players who can
    /// carry more get a bigger shelf, `Server.ShelfSize` times `Server.BackpackShelfMultiplier`
    /// (`Core/Shelf.Scaled`). Read on the SERVER, where the market lives, and read at director up rather than
    /// at plugin Awake: BepInEx loads plugins in its own order, and at our Awake the ones after us are not in
    /// `Chainloader.PluginInfos` yet. The GUID is a synced knob (`Server.BackpackModGuid`) so another backpack
    /// mod can be named without a build. A dedicated server runs the same chainloader, so the lookup is the
    /// same there. Never a permit: a lookup that throws reads as "no backpack mod", logged once.
    /// </summary>
    internal static class BackpackMod
    {
        public static bool Present { get; private set; }
        /// <summary>"guid version" of what was found, or "" when nothing was.</summary>
        public static string Found { get; private set; } = "";
        private static string _lastGuid;
        private static bool _lastPresent;
        private static bool _everChecked;
        private static int _throws;

        /// <summary>
        /// Look the GUID up; cheap (one dictionary read), so the director may call it every tick and a
        /// live change to the knob takes effect on the next. Logs once per change of answer.
        /// </summary>
        public static void Detect(string guid)
        {
            guid = (guid ?? "").Trim();
            bool present = false;
            string found = "";
            if (guid.Length > 0)
            {
                try
                {
                    PluginInfo info;
                    if (Chainloader.PluginInfos != null && Chainloader.PluginInfos.TryGetValue(guid, out info) &&
                        info != null && info.Instance != null && info.Metadata != null)
                    {
                        present = true;
                        found = info.Metadata.GUID + " " + info.Metadata.Version;
                    }
                }
                catch (Exception ex)
                {
                    if (_throws++ < 3) ValkyriesCargo.Log.LogError("backpack mod lookup threw (read as none): " + ex);
                    present = false;
                    found = "";
                }
            }
            Present = present;
            Found = found;
            if (!_everChecked || guid != _lastGuid || present != _lastPresent)
            {
                _everChecked = true;
                _lastGuid = guid;
                _lastPresent = present;
                ValkyriesCargo.Log.LogInfo("backpack mod: " + Describe(guid));
            }
        }

        /// <summary>One line for the log and `cargo status`: what was looked for, and what that does to the shelf.</summary>
        public static string Describe(string guid)
        {
            guid = (guid ?? "").Trim();
            if (guid.Length == 0) return "not looked for (Server.BackpackModGuid is empty); shelf x1";
            return Present
                ? Found + " loaded; shelf x" + Config.ModConfig.EffectiveBackpackMultiplier() + " (Server.BackpackShelfMultiplier)"
                : "none (" + guid + " is not loaded here); shelf x1";
        }
    }
}
