using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HarmonyLib;
using RavenIron.ValkyriesCargo.Client;
using RavenIron.ValkyriesCargo.Client.Terminal;
using RavenIron.ValkyriesCargo.Config;
using RavenIron.ValkyriesCargo.Core;
using RavenIron.ValkyriesCargo.Net;
using RavenIron.ValkyriesCargo.Server;
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
    /// before a single coin moves. The admin verbs (`visit`, `dismiss`) run where the world runs:
    /// on a server or listen host directly, from a client through AdminRpc, where the SERVER's
    /// admin list decides (design 3.1). Console commands are not config: LockConfiguration does
    /// not touch them.
    /// </summary>
    [HarmonyPatch(typeof(Terminal), "InitTerminal")]
    public static class Patch_Terminal_Cargo
    {
        private static void Postfix()
        {
            try
            {
                new Terminal.ConsoleCommand("cargo",
                    "Valkyrie's Cargo: status | version | engine | prefab <name> | body [preview|walk|clip <name>|clear] | stock [prefab] | deal buy|sell <prefab> [count] | claim | terminal demo|open|close | visit [player] | dismiss | reset | save", Run);
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
                    case "engine":  Engine(args); return;
                    case "prefab":  Prefab(args); return;
                    case "body":    Body(args); return;
                    case "visit":   Admin(args, "visit", args.Args.Length > 2 ? args.Args[2] : ""); return;
                    case "dismiss": Admin(args, "dismiss", ""); return;
                    case "reset":   Admin(args, "reset", ""); return;
                    case "save":    Admin(args, "save", ""); return;
                    case "stock":   Stock(args, args.Args.Length > 2 ? args.Args[2] : ""); return;
                    case "deal":    DealCommand(args); return;
                    case "claim":   Claim(args); return;
                    case "terminal": TerminalCommand(args, args.Args.Length > 2 ? args.Args[2].ToLowerInvariant() : "demo"); return;
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
            Say(args, "cargo status          - role, config authority, catalogue, the director, the engine numbers the design depends on");
            Say(args, "cargo version         - this build and the ServerSync gate");
            Say(args, "cargo engine          - the Valheim build this DLL was written on against the one running, and every engine probe with its rank");
            Say(args, "cargo prefab <name>   - components, children and effect lists of a game prefab (Valkyrie, Dverger, odin, Haldor)");
            Say(args, "cargo body            - Ingvar's body: where the bundle came from, the six clips and their lengths, the rig, the derived ground offset");
            Say(args, "cargo body preview    - stand him 2.5 m in front of you, facing you: no ZDO, nothing networked, nobody else sees him");
            Say(args, "cargo body walk       - make the preview walk on the spot (a simulated speed; toggles off again)");
            Say(args, "cargo body clip <Hello|Talk|Shrug|Nod>  - fire that one-shot on the preview");
            Say(args, "cargo body clear      - take the preview away");
            Say(args, "cargo stock [prefab]  - his shelf as this machine last heard it: stock/target, what you pay, what he pays, trend");
            Say(args, "cargo deal buy <prefab> [count]   - buy from him at the price on the shelf (a plain deal, no terminal)");
            Say(args, "cargo deal sell <prefab> [count]  - sell to him at what he pays");
            Say(args, "cargo claim           - ask the server for deliveries it still owes you");
            Say(args, "cargo terminal demo   - open the Cargo Terminal on the in-process demo market (no server, no merchant)");
            Say(args, "cargo terminal open   - open it on the running visit without a merchant (before P5); close closes it");
            Say(args, "cargo visit [player]  - ADMIN: force a visit for yourself (or the named player), cooldowns ignored, the other gates kept");
            Say(args, "cargo dismiss         - ADMIN: end the running visit now");
            Say(args, "cargo reset           - ADMIN: forget every cooldown");
            Say(args, "cargo save            - ADMIN: write the world sidecar now");
        }

        private static void Version(Terminal.ConsoleEventArgs args)
        {
            Say(args, ValkyriesCargo.PluginName + " v" + ValkyriesCargo.PluginVersion +
                      " - every client must run exactly this version (ServerSync ModRequired, minimum = current).");
        }

        /// <summary>
        /// P10b: what this DLL was compiled against, what is actually running, and every engine probe
        /// with its risk rank, what it looked at and what turns itself off when it fails. Not an admin
        /// verb and not config: it reads and prints, and it is the first thing to paste into a bug
        /// report from a player whose Valheim moved.
        /// </summary>
        private static void Engine(Terminal.ConsoleEventArgs args)
        {
            Say(args, EngineBaseline.Describe() + ".");
            Say(args, "running: " + EngineCheck.Verdict + (EngineCheck.Comparison != null && EngineCheck.Comparison.WireAtRisk
                          ? "  <- the network version is the handshake and the packet layout: the deal wire is the risk" : ""));
            EngineProbes p = EngineProbes.Current;
            Say(args, EngineCheck.Ran ? p.Encode() : "probes: EngineCheck.Run() never ran (this is a bug, not a game change)");
            foreach (string line in p.Report()) Say(args, "  " + line);
            foreach (string problem in p.Problems) Say(args, "  registry: " + problem);
        }

        /// <summary>An admin verb: run it here if the world runs here, else ask the server and let its answer print when it comes.</summary>
        private static void Admin(Terminal.ConsoleEventArgs args, string verb, string arg)
        {
            ZNet znet = ZNet.instance;
            if (znet == null) { Say(args, "cargo: no world loaded."); return; }
            if (znet.IsServer())
            {
                Player local = Player.m_localPlayer;
                long uid = local != null ? ZNet.GetUID() : 0;
                string name = local != null ? local.GetPlayerName() : "";
                Say(args, CargoTick.Admin(verb, arg, uid, name));
                return;
            }
            Say(args, AdminRpc.Send(verb, arg));
        }

        /// <summary>The shelf as this machine last heard it through MarketState; the same rows the terminal renders.</summary>
        private static void Stock(Terminal.ConsoleEventArgs args, string prefab)
        {
            MarketSnapshot m = CargoRpc.Market;
            if (m.Count == 0) { Say(args, "cargo: no market state heard yet (no visit has started, or no world)"); return; }
            Say(args, "market for visit #" + m.VisitId + ", purse " + m.Purse + " coins; " + m.Count + " rows" + (prefab.Length > 0 ? "" : " (first 24; name one for its line)"));
            int shown = 0;
            foreach (MarketRow r in m.Rows)
            {
                if (prefab.Length > 0 && !string.Equals(r.Prefab, prefab, StringComparison.OrdinalIgnoreCase)) continue;
                if (prefab.Length == 0 && shown++ >= 24) break;
                Say(args, "  " + r.Prefab + " (" + r.Kind + "): " + r.Stock + "/" + r.Target + " max " + r.Max + (r.Stock == 0 ? " SOLD" : "") +
                          (r.Kind == EntryKind.Ware ? ", you pay " + r.Buy : "") + ", he pays " + r.Sell + ", trend " + (r.Trend > 0 ? "up" : r.Trend < 0 ? "down" : "flat"));
            }
            if (prefab.Length > 0 && m.Find(prefab) == null) Say(args, "  no row named '" + prefab + "'");
        }

        /// <summary>A plain deal from the console, so the wire can be proven before the terminal exists: builds the Deal the terminal would.</summary>
        private static void DealCommand(Terminal.ConsoleEventArgs args)
        {
            string kind = args.Args.Length > 2 ? args.Args[2].ToLowerInvariant() : "";
            string prefab = args.Args.Length > 3 ? args.Args[3] : "";
            int count = 1;
            if (args.Args.Length > 4 && (!int.TryParse(args.Args[4], out count) || count < 1)) { Say(args, "cargo deal: count must be 1 or more"); return; }
            if ((kind != "buy" && kind != "sell") || prefab.Length == 0) { Say(args, "cargo deal buy|sell <prefab> [count]"); return; }
            if (!CargoRpc.Ready) { Say(args, "cargo: no transport (join a world; the wire registers on connect)"); return; }
            VisitSnapshot v = CargoRpc.Visit;
            if (!v.Active) { Say(args, "cargo: no visit is running (cargo status)"); return; }
            MarketRow row = CargoRpc.Market.Find(prefab);
            if (row == null) { Say(args, "cargo: he has no row named '" + prefab + "' (cargo stock)"); return; }
            Player p = Player.m_localPlayer;
            if (p == null) { Say(args, "cargo: no local player"); return; }

            var deal = new Deal { VisitId = v.VisitId, Nonce = Deal.NewNonce() };
            if (kind == "buy")
            {
                if (row.Kind != EntryKind.Ware) { Say(args, "cargo: he only buys " + prefab + ", he does not sell it"); return; }
                deal.Wanted = new DealLine { Prefab = row.Prefab, Count = count, UnitPriceSeen = row.Buy };
                deal.CoinsOffered = count * row.Buy;
                int have = DealApplier.Count(p.GetInventory(), DealApplier.CoinsPrefab);
                if (have < deal.CoinsOffered) { Say(args, "cargo: that is " + deal.CoinsOffered + " coins and you carry " + have); return; }
            }
            else
            {
                deal.Offered.Add(new DealLine { Prefab = row.Prefab, Count = count, UnitPriceSeen = row.Sell });
                int have = DealApplier.Count(p.GetInventory(), row.Prefab);
                if (have < count) { Say(args, "cargo: you carry " + have + " " + row.Prefab); return; }
            }
            Say(args, "cargo: sending " + kind + " " + count + " " + row.Prefab + " at " + (kind == "buy" ? row.Buy : row.Sell) + " each (nonce " + Wire.Long(deal.Nonce) + ")");
            CargoRpc.Send(deal, r =>
            {
                if (r.Ok)
                {
                    bool applied = DealApplier.Apply(r);
                    Say(args, "cargo: DONE " + r.DeliveryId + ": " + DealApplier.Describe(r) + (applied ? "" : " (NOT applied to the inventory; see the log)"));
                }
                else if (r.Reason == DealReason.PriceChanged) Say(args, "cargo: the price moved while you looked; cargo stock " + row.Prefab + " and try again");
                else Say(args, "cargo: refused: " + r.Reason);
            });
        }

        private static void TerminalCommand(Terminal.ConsoleEventArgs args, string what)
        {
            CargoTerminal t = CargoTerminal.Instance;
            if (t == null) { Say(args, "cargo: no terminal on this machine (no renderer)"); return; }
            switch (what)
            {
                case "demo":
                    t.OpenDemo();
                    Say(args, "cargo: terminal opened on the demo market (visit #" + CargoRpc.Visit.VisitId + ", purse " + CargoRpc.Market.Purse + "); Escape closes it");
                    return;
                case "open":
                    if (!CargoRpc.Visit.Active) { Say(args, "cargo: no visit is running (cargo visit first)"); return; }
                    t.Open(null, CargoRpc.Visit.VisitId);
                    Say(args, "cargo: terminal opened on visit #" + CargoRpc.Visit.VisitId + " with no merchant to stand by");
                    return;
                case "close":
                    t.Close("console");
                    Say(args, "cargo: terminal closed");
                    return;
                default:
                    Say(args, "cargo terminal demo|open|close");
                    return;
            }
        }

        private static void Claim(Terminal.ConsoleEventArgs args)
        {
            var t = CargoTick.Transport as CargoTransport;
            if (t == null) { Say(args, "cargo: no server socket here" + (ZNet.instance != null && ZNet.instance.IsServer() ? " (a listen host is paid in-process at login)" : "")); return; }
            t.ClaimAgain();
            Say(args, "cargo: asked the server for anything it still owes you; deliveries print in the log and the HUD");
        }

        private static void Status(Terminal.ConsoleEventArgs args)
        {
            Say(args, ValkyriesCargo.PluginName + " v" + ValkyriesCargo.PluginVersion +
                      " - role=" + CargoTick.Role() + ", renderer=" + (ValkyriesCargo.HasRenderer ? "yes" : "no"));

            // P10b, and deliberately the SECOND line: if the game underneath moved, every number below
            // is suspect and this is the line that says so. `cargo engine` for the whole list.
            Say(args, "  " + EngineCheck.StatusLine() + (EngineProbes.Current.Failed.Count > 0 ? " (cargo engine)" : ""));
            if (CargoEvent.Disabled)
                Say(args, "  visit: DISABLED - the engine probe 'randevent' failed (" + CargoEvent.DisabledReason +
                          "); the event is not registered and no visit can start on this build");

            var sync = ModConfig.Sync;
            Say(args, "  config: " + (sync.IsSourceOfTruth ? "this side is the source of truth" : "following the server") +
                      ", locked=" + sync.IsLocked + ", admin here=" + sync.IsAdmin +
                      ", Enabled=" + ModConfig.Enabled.Value +
                      ", roll every " + F(ModConfig.EventCheckIntervalMinutes.Value, "0.#") + " min at " +
                      F(ModConfig.EventChancePercent.Value, "0.#") + "%, rested=" + ModConfig.RequireRested.Value +
                      ", comfort>=" + ModConfig.MinComfortLevel.Value + ", baseValue>=" + ModConfig.MinBaseValue.Value +
                      ", daytimeOnly=" + ModConfig.DaytimeOnly.Value + ", lifespan " + F(ModConfig.MerchantLifespanSeconds.Value, "0") + " s");

            Catalogue cat = ModConfig.CatalogueParsed;
            Say(args, "  catalogue: " + cat.Count + " entries (" + cat.CountOf(EntryKind.Ware) + " wares, " +
                      cat.CountOf(EntryKind.Want) + " wants), " + ModConfig.CatalogueProblems.Count + " problem(s)" +
                      (ModConfig.CatalogueProblems.Count > 0 ? " - first: " + ModConfig.CatalogueProblems[0] : ""));

            Say(args, "  channels: VisitState=" + Describe(ModConfig.VisitState.Value) +
                      ", MarketState=" + Describe(ModConfig.MarketState.Value) +
                      " -> parsed: visit " + CargoRpc.Visit.Phase + " #" + CargoRpc.Visit.VisitId +
                      ", market " + CargoRpc.Market.Count + " rows, purse " + CargoRpc.Market.Purse +
                      (CargoRpc.LastMarketProblems.Count + CargoRpc.LastVisitProblems.Count > 0
                          ? ", " + (CargoRpc.LastMarketProblems.Count + CargoRpc.LastVisitProblems.Count) + " parse problem(s)" : ""));
            var ct = CargoTick.Transport as CargoTransport;
            Say(args, "  transport: " + (CargoRpc.IsDemo ? "DEMO (in-process)" : CargoTick.Transport is LocalTransport ? "in-process (listen host)" : ct != null
                          ? "server socket" + (ct.Ready ? "" : " (not connected)") + ", sent " + ct.Sent + ", answered " + ct.Answered + ", pending " + ct.Pending + ", unsolicited " + ct.Unsolicited + (ct.Claimed ? ", claimed" : ", not claimed yet")
                          : "none (no world)") +
                      ", inbox " + CargoRpc.Inbox.Count + " applied deliver" + (CargoRpc.Inbox.Count == 1 ? "y" : "ies") +
                      ", terminal " + (CargoTerminal.Instance != null ? (CargoTerminal.Instance.IsOpen ? "OPEN" + (CargoTerminal.Instance.IsDemo ? " (demo)" : "") : "closed" + (CargoTerminal.Instance.LastCloseReason.Length > 0 ? " (last: " + CargoTerminal.Instance.LastCloseReason + ")" : "")) : "none (no renderer)") +
                      ", routed RPCs " + (AdminRpc.Registered ? "registered" : "not registered"));

            BodyLoader.Load();
            Say(args, "  " + BodyLoader.StatusLine() + " (cargo body for the whole of it)");

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
                Say(args, "  random event: " + (ev != null ? ev.m_name + " (" + F(ev.m_time, "0") + " of " + F(ev.m_duration, "0") + " s)" : "none") +
                          "; " + res.m_events.Count + " events registered, ours=" +
                          (CargoEvent.IsRegistered(res) ? "yes ('" + CargoEvent.Name + "')" : "NO"));
            }

            Say(args, "  time: world " + F((float)ZNet.instance.GetTimeSeconds(), "0") + " s, " +
                      (EnvMan.instance != null
                          ? (EnvMan.IsDay() ? "day" : "night") + ", day length " + EnvMan.instance.m_dayLengthSec +
                            " s (EnvMan.m_dayLengthSec, public; the drift half-life counts these; compiled default 1200, scene expected 1800)"
                          : "no EnvMan"));

            // Where a player is drawn: what this client reports about itself.
            ComfortReporter rep = CargoTick.Reporter;
            if (ValkyriesCargo.HasRenderer && rep != null)
            {
                Say(args, "  my report: " + (rep.Reported
                    ? "rested=" + (rep.LastRested ? "yes" : "no") + ", comfort=" + rep.LastComfort + ", written " + F(rep.SecondsSinceWrite, "0.#") +
                      " s ago (" + rep.Writes + " writes to my character ZDO as " + Keys.Rested + "/" + Keys.Comfort + ")"
                    : "nothing written yet (no local player, or not its owner)"));
            }

            // Where the world runs: the director.
            VisitDirector d = CargoTick.Director;
            if (d != null)
            {
                double now = Time.time;
                double world = ZNet.instance.GetTimeSeconds();
                Say(args, "  director: salt " + d.Market.Salt + ", day " + F((float)d.Market.Rules.SecondsPerGameDay, "0") + " s (" +
                          (d.DayLengthFromEngine ? "engine" : "ASSUMED") + "), next roll in " + F((float)Math.Max(0, d.Scheduler.NextRollAt - now), "0") +
                          " s; last roll: " + d.Scheduler.LastDecision + (d.Problems.Length > 0 ? "; problems: " + d.Problems : ""));
                VisitSession s = d.Session;
                Say(args, "  visit: " + (s.Active
                    ? "#" + s.VisitId + " " + s.Phase + ", pilot " + s.PilotName + " (uid " + Wire.Long(s.PilotUid) + "), " + s.Clock.FormatRemaining(world) +
                      " left" + (s.Clock.Warned ? ", one-minute warning given" : "") + ", " + s.Republishes + " clock republish(es)"
                    : "none" + (s.LastVisitId > 0 ? "; last #" + s.LastVisitId + " ended: " + s.LastEndReason + ", takings " + d.LastTakings + " coins" : "")) +
                    "; purse " + d.Market.Purse + ", next visit #" + d.Market.NextVisitId + (d.Session.Resumed ? " (resumed after a restart)" : ""));
                Say(args, "  " + Spawner.Describe() + (Spawner.Active
                    ? "; " + F(FlightPlan.MinimumStartDistance, "0") + " m is the shortest flight worth flying, " +
                      F(FlightPlan.EdgeMargin, "0") + " m the margin kept inside the block"
                    : ""));
                Say(args, "  wire: " + DealWire.Registered + " peer socket(s), " + DealWire.OpenTerminals + " terminal(s) open, " + DealWire.Deals + " deal(s), " +
                          DealWire.Redeliveries + " redeliver" + (DealWire.Redeliveries == 1 ? "y" : "ies") + "; owed ledger " + d.Ledger.Count + " row(s)");
                Say(args, "  sidecar: " + (d.Store != null && d.Store.Path != null
                    ? System.IO.Path.GetFileName(d.Store.Path) + ", " + d.Loaded + " row(s) loaded, " + d.Store.Saves + " save(s)" + (d.Store.Failures > 0 ? ", " + d.Store.Failures + " FAILED" : "") +
                      (d.Dirty ? ", changes pending (cadence " + F(VisitDirector.SaveCadenceSeconds, "0") + " s)" : ", clean")
                    : "NONE: " + (d.Store != null ? d.Store.Detail : "no store")));
                // P10b, "refuse rather than corrupt": a newer file is held, not quarantined, and nothing
                // this session saves. That is a loud state and it gets its own line.
                if (d.Store != null && d.Store.Held)
                    Say(args, "  sidecar: REFUSED AND HELD - " + d.Store.HoldReason +
                              ". The file is untouched; nothing is being saved. Run the newer build, or move the file aside yourself.");
                IReadOnlyList<Candidate> cs = d.Candidates;
                Say(args, "  candidates (" + cs.Count + "): " + (cs.Count == 0 ? "nobody online" : ""));
                for (int i = 0; i < cs.Count && i < 12; i++) Say(args, "    " + d.Describe(cs[i], now));
            }
            else if (ZNet.instance.IsServer())
            {
                Say(args, "  director: not created yet (waiting for the event system and the scene)");
            }
        }

        /// <summary>
        /// Ingvar's body (P8). `cargo body` answers on a dedicated server too, honestly; the rest need a
        /// renderer and a world. The preview is a plain local GameObject - no ZDO, no ZNetView, nothing
        /// networked - so the body can be looked at long before P5 exists to carry it.
        /// </summary>
        private static void Body(Terminal.ConsoleEventArgs args)
        {
            string what = args.Args.Length > 2 ? args.Args[2].ToLowerInvariant() : "";
            switch (what)
            {
                case "":        BodyReport(args); return;
                case "preview": BodyPreview(args); return;
                case "walk":    BodyWalk(args); return;
                case "clip":    BodyClipVerb(args, args.Args.Length > 3 ? args.Args[3] : ""); return;
                case "clear":
                    Say(args, BodyLoader.ClearPreview() ? "cargo: the preview is gone" : "cargo: there was no preview");
                    return;
                default:
                    Say(args, "cargo body [preview | walk | clip <Hello|Talk|Shrug|Nod> | clear]");
                    return;
            }
        }

        private static void BodyReport(Terminal.ConsoleEventArgs args)
        {
            BodyLoader.Load();
            Say(args, "body: source " + BodyLoader.Source.ToString().ToLowerInvariant() + " - " + BodyLoader.Detail);
            if (BodyLoader.Source == BodySource.Embedded)
                Say(args, "  resource: '" + BodyLoader.ResourceName + "' inside this DLL, which is what makes every player's Ingvar the same one");
            else if (BodyLoader.Source == BodySource.File)
                Say(args, "  file: " + BodyLoader.FilePath + " - a LOCAL file, NOT the copy other players have; embed it before it ships");

            Say(args, "  bundle " + (BodyLoader.BundleLoaded ? "open" : "not open") +
                      ", prefab '" + BodyLoader.PrefabName + "' " + (BodyLoader.PrefabFound ? "found" : "not found") +
                      ", CustomBody=" + ModConfig.CustomBody.Value + " (false keeps the " + ModConfig.BodyPrefab.Value + " stand-in)" +
                      ", renderer=" + (ValkyriesCargo.HasRenderer ? "yes" : "no"));

            Say(args, "  clips (" + BodyLoader.Clips.Count + " of " + BodyMotion.ClipCount + " wanted): " + BodyLoader.ClipList());
            for (int i = 0; i < BodyMotion.ClipCount; i++)
            {
                string want = BodyMotion.ClipName((BodyClip)i);
                if (BodyLoader.PrefabFound && BodyLoader.Clip(want) == null)
                    Say(args, "    MISSING '" + want + "': it plays at weight 0 and the rest carry on");
            }

            if (BodyLoader.PrefabFound)
                Say(args, "  rig: SkinnedMeshRenderer=" + (BodyLoader.HasSkinnedMesh ? "yes" : "NO") +
                          ", bones=" + BodyLoader.BoneCount + " (24 expected), tris=" + BodyLoader.Triangles + " (31112 expected); " +
                          BodyLoader.BoundsWords() + "; ground offset " + F(BodyLoader.GroundOffset, "0.###") +
                          " m (the BIND-POSE box, an observation only -- the posed-mesh lift is what places him; see `cargo body preview`)");

            IngvarBody p = BodyLoader.Preview;
            Say(args, "  preview: " + (p == null ? "none (cargo body preview)"
                : "up, graph " + (p.GraphLive ? "live" : "DEAD") + ", " + p.ClipsBound + " clip(s) bound, speed " +
                  F(p.Speed, "0.00") + " m/s" + (float.IsNaN(p.SimulatedSpeed) ? "" : " (SIMULATED " + F(p.SimulatedSpeed, "0.0") + ")") +
                  ", blend " + F(p.WalkBlend, "0.00") + " toward " + (p.Walking ? "Walk" : "Idle") +
                  ", one-shot " + (p.CurrentClip == BodyClip.None ? "none" : BodyMotion.ClipName(p.CurrentClip))));
        }

        private static void BodyPreview(Terminal.ConsoleEventArgs args)
        {
            if (!ValkyriesCargo.HasRenderer) { Say(args, "cargo: nothing to draw here (no renderer)"); return; }
            BodyLoader.Load();
            if (!BodyLoader.PrefabFound) { Say(args, "cargo: no body to show - " + BodyLoader.Detail); return; }
            Player me = Player.m_localPlayer;
            if (me == null) { Say(args, "cargo: no local player to stand in front of"); return; }

            Vector3 facing = me.transform.forward;
            facing.y = 0f;
            if (facing.sqrMagnitude < 0.0001f) facing = Vector3.forward;
            facing.Normalize();

            Vector3 spot = me.transform.position + facing * 2.5f;
            ZoneSystem zs = ZoneSystem.instance;
            if (zs != null && zs.GetGroundHeight(spot, out float ground)) spot.y = ground;

            // Face the player, not away from him.
            IngvarBody body = BodyLoader.StartPreview(spot, Quaternion.LookRotation(-facing, Vector3.up));
            if (body == null) { Say(args, "cargo: the preview could not be built; see the log"); return; }
            Say(args, "cargo: Ingvar is standing 2.5 m in front of you at y " + F(spot.y, "0.##") +
                      " (lifted " + F(BodyLoader.PreviewLift, "0.###") + " m off the ground point, measured on the posed mesh" +
                      (BodyLoader.PreviewStrays > 0 ? "; " + BodyLoader.PreviewStrays + " stray renderer(s) in the bundle switched off" : "") +
                      "), " + body.ClipsBound + " clip(s) bound, graph " +
                      (body.GraphLive ? "live" : "DEAD") + ". Nothing about him is networked. `cargo body walk`, `cargo body clip Hello`, `cargo body clear`.");
        }

        private static void BodyWalk(Terminal.ConsoleEventArgs args)
        {
            IngvarBody p = BodyLoader.Preview;
            if (p == null) { Say(args, "cargo: no preview (cargo body preview)"); return; }
            bool on = float.IsNaN(p.SimulatedSpeed);
            p.SimulatedSpeed = on ? 1f : float.NaN;
            Say(args, on ? "cargo: walking on the spot at a simulated 1.0 m/s; the blend crosses over " +
                           F(BodyMotion.CrossfadeSeconds, "0.00") + " s"
                         : "cargo: back to his real speed, which for a body that does not move is zero");
        }

        private static void BodyClipVerb(Terminal.ConsoleEventArgs args, string name)
        {
            IngvarBody p = BodyLoader.Preview;
            if (p == null) { Say(args, "cargo: no preview (cargo body preview)"); return; }
            BodyClip clip = BodyMotion.ByName(name);
            if (!BodyMotion.IsOneShot(clip)) { Say(args, "cargo body clip <Hello|Talk|Shrug|Nod>"); return; }
            if (!p.Fire(clip)) { Say(args, "cargo: '" + BodyMotion.ClipName(clip) + "' is already playing, or the bundle does not carry it"); return; }
            Say(args, "cargo: " + BodyMotion.ClipName(clip) + " - blends in over " + F(BodyMotion.OneShotBlendSeconds, "0.00") +
                      " s, hands back at " + F(BodyMotion.HandBackFraction * 100f, "0") + "% of its length");
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
