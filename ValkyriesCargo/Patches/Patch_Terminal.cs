using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HarmonyLib;
using RavenIron.ValkyriesCargo.Config;
using RavenIron.ValkyriesCargo.Core;
using UnityEngine;

namespace RavenIron.ValkyriesCargo.Patches
{
    /// <summary>
    /// The `cargo` console. Registered from an InitTerminal postfix; the ConsoleCommand
    /// constructor assigns into Terminal's command map by lowered name, so re-registration
    /// per terminal is harmless (Cairn, decompile-verified; unchanged in 0.221.x).
    ///
    /// The instrument this build cannot do without. "Measure before you push": `status`
    /// states in words what the mod knows, `prefab` dumps what the game holds, and both exist
    /// before a single coin moves.
    /// </summary>
    [HarmonyPatch(typeof(Terminal), "InitTerminal")]
    public static class Patch_Terminal_Cargo
    {
        private static void Postfix()
        {
            try
            {
                new Terminal.ConsoleCommand("cargo",
                    "Valkyrie's Cargo: status | version | prefab <name>", Run);
            }
            catch (Exception ex)
            {
                ValkyriesCargo.Log.LogWarning("cargo console: registration failed: " + ex.Message);
            }
        }

        private static void Run(Terminal.ConsoleEventArgs args)
        {
            try
            {
                string sub = args.Args.Length > 1 ? args.Args[1].ToLowerInvariant() : "status";
                switch (sub)
                {
                    case "status":  Status(args); return;
                    case "version": Version(args); return;
                    case "prefab":  Prefab(args); return;
                    default:        Help(args); return;
                }
            }
            catch (Exception ex)
            {
                Say(args, "cargo: " + ex.Message);
                ValkyriesCargo.Log.LogWarning("cargo console threw: " + ex);
            }
        }

        private static void Help(Terminal.ConsoleEventArgs args)
        {
            Say(args, "cargo status         - role, config authority, catalogue, the engine numbers the design depends on");
            Say(args, "cargo version        - this build and the ServerSync gate");
            Say(args, "cargo prefab <name>  - components, children and effect lists of a game prefab (Valkyrie, Dverger, odin, Haldor)");
        }

        private static void Version(Terminal.ConsoleEventArgs args)
        {
            Say(args, ValkyriesCargo.PluginName + " v" + ValkyriesCargo.PluginVersion +
                      " - every client must run exactly this version (ServerSync ModRequired, minimum = current).");
        }

        private static void Status(Terminal.ConsoleEventArgs args)
        {
            Say(args, ValkyriesCargo.PluginName + " v" + ValkyriesCargo.PluginVersion +
                      " - role=" + CargoTick.Role() + ", renderer=" + (ValkyriesCargo.HasRenderer ? "yes" : "no"));

            var sync = ModConfig.Sync;
            Say(args, "  config: " + (sync.IsSourceOfTruth ? "this side is the source of truth" : "following the server") +
                      ", locked=" + sync.IsLocked + ", admin here=" + sync.IsAdmin +
                      ", Enabled=" + ModConfig.Enabled.Value +
                      ", roll every " + F(ModConfig.EventCheckIntervalMinutes.Value, "0.#") + " min at " +
                      F(ModConfig.EventChancePercent.Value, "0.#") + "%, rested=" + ModConfig.RequireRested.Value +
                      ", comfort>=" + ModConfig.MinComfortLevel.Value + ", baseValue>=" + ModConfig.MinBaseValue.Value);

            Catalogue cat = ModConfig.CatalogueParsed;
            Say(args, "  catalogue: " + cat.Count + " entries (" + cat.CountOf(EntryKind.Ware) + " wares, " +
                      cat.CountOf(EntryKind.Want) + " wants), " + ModConfig.CatalogueProblems.Count + " problem(s)" +
                      (ModConfig.CatalogueProblems.Count > 0 ? " - first: " + ModConfig.CatalogueProblems[0] : ""));

            Say(args, "  channels: VisitState=" + Describe(ModConfig.VisitState.Value) +
                      ", MarketState=" + Describe(ModConfig.MarketState.Value) +
                      " -> parsed: visit " + Net.CargoRpc.Visit.Phase + " #" + Net.CargoRpc.Visit.VisitId +
                      ", market " + Net.CargoRpc.Market.Count + " rows, purse " + Net.CargoRpc.Market.Purse +
                      (Net.CargoRpc.LastMarketProblems.Count + Net.CargoRpc.LastVisitProblems.Count > 0
                          ? ", " + (Net.CargoRpc.LastMarketProblems.Count + Net.CargoRpc.LastVisitProblems.Count) + " parse problem(s)" : ""));
            Say(args, "  transport: " + (Net.CargoRpc.IsDemo ? "DEMO (in-process)" : Net.CargoRpc.Ready ? "server socket" : "none until a world is joined (Phase 6)") +
                      ", inbox " + Net.CargoRpc.Inbox.Count + " applied deliver" + (Net.CargoRpc.Inbox.Count == 1 ? "y" : "ies") +
                      ", terminal " + (Client.Terminal.CargoTerminalHost.Instance != null ? "registered" : "not built yet (Track B)"));

            if (ZNet.instance == null) { Say(args, "  no world loaded."); return; }

            ZoneSystem zs = ZoneSystem.instance;
            if (zs != null)
            {
                int reach = Math.Max(0, zs.m_activeArea - 1);
                Say(args, "  ZoneSystem.m_activeArea=" + zs.m_activeArea + " -> objects exist within " + reach +
                          " zone(s) (" + (reach * 64) + " m) of a player's zone; distant area=" + zs.m_activeDistantArea +
                          "; water level=" + F(zs.m_waterLevel, "0.#"));
            }

            RandEventSystem res = RandEventSystem.instance;
            if (res != null)
            {
                RandomEvent ev = res.GetCurrentRandomEvent();
                Say(args, "  random event: " + (ev != null ? ev.m_name + " (" + F(ev.m_time, "0") + " s in)" : "none") +
                          "; " + res.m_events.Count + " events registered, ours=" +
                          (res.HaveEvent("valkyries_cargo") ? "yes" : "not yet (Phase 3)"));
            }

            Say(args, "  time: world " + F((float)ZNet.instance.GetTimeSeconds(), "0") + " s, " +
                      (EnvMan.instance != null
                          ? (EnvMan.IsDay() ? "day" : "night") + ", day length " + EnvMan.instance.m_dayLengthSec +
                            " s (EnvMan.m_dayLengthSec, public; the drift half-life counts these; compiled default 1200, scene expected 1800)"
                          : "no EnvMan"));
        }

        /// <summary>
        /// Dump a prefab the design depends on. Reads only public members; the effect lists
        /// tell which branch of the effect rule (design 3.6) each entry takes.
        /// </summary>
        private static void Prefab(Terminal.ConsoleEventArgs args)
        {
            string name = args.Args.Length > 2 ? args.Args[2] : "";
            if (name.Length == 0) { Say(args, "cargo prefab <name>"); return; }
            if (ZNetScene.instance == null) { Say(args, "cargo: no world loaded (ZNetScene is null)."); return; }

            GameObject p = ZNetScene.instance.GetPrefab(name);
            string source = "ZNetScene";
            if (p == null && ObjectDB.instance != null)
            {
                p = ObjectDB.instance.GetItemPrefab(name);
                source = "ObjectDB";
            }
            if (p == null) { Say(args, "cargo: no prefab named '" + name + "' in ZNetScene or ObjectDB."); return; }

            Say(args, "prefab '" + p.name + "' (" + source + "): " + p.transform.childCount + " child(ren)");
            Say(args, "  components: " + Join(p.GetComponents<Component>()));

            int shown = 0;
            foreach (Transform child in p.transform)
            {
                if (shown++ >= 16) { Say(args, "  ... more children"); break; }
                Say(args, "  child '" + child.name + "': " + Join(child.GetComponents<Component>()));
            }

            var nview = p.GetComponent<ZNetView>();
            Say(args, "  ZNetView: " + (nview != null ? "yes, persistent=" + nview.m_persistent + ", distant=" + nview.m_distant : "NO"));

            var odin = p.GetComponent<Odin>();
            if (odin != null) DumpEffects(args, "Odin.m_despawn", odin.m_despawn);

            var valk = p.GetComponent<Valkyrie>();
            if (valk != null)
                Say(args, "  Valkyrie: attachPoint=" + (valk.m_attachPoint != null ? valk.m_attachPoint.name : "NULL") +
                          ", speed=" + F(valk.m_speed, "0.#") + ", dropHeight=" + F(valk.m_dropHeight, "0.#") +
                          ", startDistance=" + F(valk.m_startDistance, "0") + ", startAltitude=" + F(valk.m_startAltitude, "0"));

            var chr = p.GetComponent<Character>();
            if (chr != null)
            {
                Say(args, "  Character: name=" + chr.m_name + ", faction=" + chr.m_faction +
                          ", collider=" + (p.GetComponent<CapsuleCollider>() != null ? "CapsuleCollider" : "none on root") +
                          ", skinned renderers=" + p.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length +
                          ", MonsterAI=" + (p.GetComponent<MonsterAI>() != null) +
                          ", NpcTalk=" + (p.GetComponent<NpcTalk>() != null) +
                          ", Tameable=" + (p.GetComponent<Tameable>() != null) +
                          ", ZSyncAnimation=" + (p.GetComponent<ZSyncAnimation>() != null));
                var anim = p.GetComponentInChildren<Animator>(true);
                if (anim != null && anim.runtimeAnimatorController != null)
                {
                    var names = new List<string>();
                    foreach (var prm in anim.parameters) names.Add(prm.name);
                    Say(args, "  animator '" + anim.runtimeAnimatorController.name + "' parameters: " + string.Join(" ", names.ToArray()));
                }
            }

            var trader = p.GetComponent<Trader>();
            if (trader != null)
                Say(args, "  Trader: name=" + trader.m_name + ", items=" + trader.m_items.Count + ", standRange=" + F(trader.m_standRange, "0.#"));
        }

        private static void DumpEffects(Terminal.ConsoleEventArgs args, string label, EffectList list)
        {
            if (list == null || list.m_effectPrefabs == null) { Say(args, "  " + label + ": empty"); return; }
            for (int i = 0; i < list.m_effectPrefabs.Length; i++)
            {
                EffectList.EffectData d = list.m_effectPrefabs[i];
                if (d == null || d.m_prefab == null) { Say(args, "  " + label + "[" + i + "]: null"); continue; }
                bool networked = d.m_prefab.GetComponent<ZNetView>() != null;
                Say(args, "  " + label + "[" + i + "]: " + d.m_prefab.name + " enabled=" + d.m_enabled +
                          " networked=" + (networked ? "yes -> owner creates it" : "no -> every client creates it"));
            }
        }

        private static string Join(Component[] comps)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < comps.Length; i++)
            {
                if (comps[i] == null) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(comps[i].GetType().Name);
            }
            return sb.ToString();
        }

        private static string Describe(string channel) =>
            string.IsNullOrEmpty(channel) ? "empty" : channel.Length + " chars";

        private static string F(float v, string fmt) => v.ToString(fmt, CultureInfo.InvariantCulture);

        private static void Say(Terminal.ConsoleEventArgs args, string text)
        {
            if (args != null && args.Context != null) args.Context.AddString(text);
            else ValkyriesCargo.Log.LogInfo(text);
        }
    }
}
