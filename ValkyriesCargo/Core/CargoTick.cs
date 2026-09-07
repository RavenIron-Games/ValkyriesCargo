using RavenIron.ValkyriesCargo.Client;
using RavenIron.ValkyriesCargo.Client.Terminal;
using RavenIron.ValkyriesCargo.Config;
using RavenIron.ValkyriesCargo.Net;
using RavenIron.ValkyriesCargo.Server;
using UnityEngine;

namespace RavenIron.ValkyriesCargo.Core
{
    /// <summary>
    /// The ONLY Update in the mod (house rule 2). Nothing else owns a timer; every system that
    /// needs time is called from here with the frame's delta.
    ///
    /// It decides the role once per world session and logs it. Readiness is COMPUTED from live
    /// objects, never tracked with a flag set by an event (a flag has to be right on every path
    /// that could change it; a computed property cannot desync). Per session it registers the
    /// routed RPCs, runs the director and the deal wire where the world runs (server, listen host),
    /// the comfort report and the client transport where a player is drawn (client, listen host),
    /// and tears everything down, flushing the sidecar, when ZNet goes away.
    /// </summary>
    public sealed class CargoTick : MonoBehaviour
    {
        public static CargoTick Instance { get; private set; }

        /// <summary>Which side of the wire this process is, in words, for logs and `cargo status`.</summary>
        public static string Role()
        {
            ZNet znet = ZNet.instance;
            if (znet == null) return "no world";
            if (znet.IsServer()) return ValkyriesCargo.HasRenderer ? "listen host (server + client)" : "dedicated server";
            return "client";
        }

        /// <summary>A world is loaded and the network object exists.</summary>
        public static bool WorldLive => ZNet.instance != null;

        /// <summary>The local player has spawned (clients and listen hosts only).</summary>
        public static bool PlayerLive => Player.m_localPlayer != null;

        public static VisitDirector Director => Instance != null ? Instance._director : null;
        public static ComfortReporter Reporter => Instance != null ? Instance._reporter : null;
        public static ICargoTransport Transport => Instance != null ? Instance._transport : null;

        private readonly ComfortReporter _reporter = new ComfortReporter();
        private VisitDirector _director;
        private ICargoTransport _transport;
        private string _loggedRole;
        private bool _live;
        private bool _inboxLoaded;
        private bool _hostClaimed;
        private int _pilotLineShownFor;
        private int _throws;

        private void Awake() { Instance = this; }

        private void OnDestroy()
        {
            if (_director != null) _director.Flush("shutdown", force: true);
            // Nothing ticks after this, so a terminal left open would hold UIFocus's cursor and
            // input tokens with no way to put them down: the cursor stays free and Chat.HasFocus
            // keeps answering true, which is the player unable to move with nothing on screen.
            if (CargoTerminal.Instance != null) CargoTerminal.Instance.Reset();
            if (Instance == this) Instance = null;
        }

        /// <summary>The ONE OnGUI in the mod: the terminal draws here and nowhere else (design 3.4).</summary>
        private void OnGUI()
        {
            if (!ValkyriesCargo.HasRenderer || CargoTerminal.Instance == null) return;
            try
            {
                CargoTerminal.Instance.Draw();
            }
            catch (System.Exception ex)
            {
                if (_throws++ < 3) ValkyriesCargo.Log.LogError("OnGUI threw: " + ex);
            }
        }

        private void Update()
        {
            try
            {
                Tick(Time.deltaTime);
            }
            catch (System.Exception ex)
            {
                // The one place a throw would silence every system after it. Log, keep ticking.
                if (_throws++ < 3) ValkyriesCargo.Log.LogError("tick threw: " + ex);
            }
        }

        private void Tick(float dt)
        {
            string role = Role();
            if (role != _loggedRole)
            {
                _loggedRole = role;
                if (role != "no world") ValkyriesCargo.Log.LogInfo("role: " + role);
            }

            ZNet znet = ZNet.instance;
            if (znet == null)
            {
                if (_live) EndSession();
                // `cargo terminal demo` must work from the MAIN MENU (CLAUDE.md item 17), where there is
                // no ZNet. Everything the window needs per frame lives in its own tick: the panel rules
                // (Escape), the cursor and input tokens, the demo purse `_coins` reads and the tray's
                // price refresh. Ticked below the ZNet gate it never ran there, so the window opened and
                // could not be closed with Escape and every Confirm was refused coins_short against a
                // purse of zero.
                if (ValkyriesCargo.HasRenderer && CargoTerminal.Instance != null) CargoTerminal.Instance.Tick(dt);
                return;
            }
            _live = true;

            AdminRpc.EnsureRegistered();

            if (znet.IsServer())
            {
                // The director needs the scene the world runs in: the event system and the zone system.
                if (_director == null && RandEventSystem.instance != null && ZNetScene.instance != null)
                    _director = VisitDirector.Create(znet, Time.time);
                if (_director != null)
                {
                    _director.Tick(dt, Time.time, znet.GetTimeSeconds());
                    DealWire.Tick(znet);
                }
            }

            if (ValkyriesCargo.HasRenderer)
            {
                _reporter.Tick(dt);
                PilotLine();
                ClientWire(znet);
                if (CargoTerminal.Instance != null) CargoTerminal.Instance.Tick(dt);
            }
        }

        /// <summary>Where a player is drawn: the inbox from disk, then the transport the terminal talks through.</summary>
        private void ClientWire(ZNet znet)
        {
            if (!_inboxLoaded) { CargoRpc.LoadInbox(InboxStore.Load()); _inboxLoaded = true; }
            if (znet.IsServer())
            {
                var local = _transport as LocalTransport;
                if (local == null) { local = new LocalTransport(HostKey()); _transport = local; CargoRpc.UseTransport(local); }
                if (!_hostClaimed && _director != null && Player.m_localPlayer != null) { _hostClaimed = true; local.ClaimOnce(); }
                return;
            }
            var remote = _transport as CargoTransport;
            if (remote == null) { remote = new CargoTransport(); _transport = remote; CargoRpc.UseTransport(remote); }
            else if (!CargoRpc.IsDemo && !CargoRpc.Ready && remote.Ready) CargoRpc.UseTransport(remote);   // the demo let go of the surface
            remote.EnsureRegistered(znet);
            remote.ClaimOnce();
        }

        /// <summary>The listen host's own ledger key: its profile id, stable across sessions like a platform id.</summary>
        private static string HostKey()
        {
            try
            {
                PlayerProfile profile = Game.instance != null ? Game.instance.GetPlayerProfile() : null;
                return profile != null ? "host" + Wire.Long(profile.GetPlayerID()) : "host";
            }
            catch { return "host"; }
        }

        /// <summary>The pilot's private line at dispatch (design 7), once per visit, on the chosen player's screen only.</summary>
        private void PilotLine()
        {
            VisitSnapshot v = CargoRpc.Visit;
            if (!v.Active || v.VisitId == _pilotLineShownFor) return;
            if (v.PilotUid != ZNet.GetUID()) return;
            _pilotLineShownFor = v.VisitId;
            if (ModConfig.ShowArrivalMessage.Value && MessageHud.instance != null)
                MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, Lines.PilotDispatch);
        }

        private void EndSession()
        {
            _live = false;
            if (_director != null) _director.Flush("session end", force: true);
            _director = null;
            _transport = null;
            _inboxLoaded = false;
            _hostClaimed = false;
            _pilotLineShownFor = 0;
            _reporter.Reset();
            if (CargoTerminal.Instance != null) CargoTerminal.Instance.Reset();
            DealWire.Reset();
            AdminRpc.Reset();
            CargoRpc.EndSession();
            ValkyriesCargo.Log.LogInfo("session ended: sidecar flushed; director, wire, reporter, routed RPCs and the terminal surface dropped");
        }

        /// <summary>
        /// The admin verbs, run where the world runs. Called by the routed handler (a remote admin) and by
        /// the console on a server or listen host. `senderUid` is the peer whose character is meant by a bare
        /// `cargo visit`; a name argument picks someone else (the only form a dedicated console has).
        /// </summary>
        public static string Admin(string verb, string arg, long senderUid, string senderName)
        {
            VisitDirector d = Director;
            if (d == null) return "cargo: no director here (is this the server? has the world loaded?)";
            ZNet znet = ZNet.instance;
            if (znet == null) return "cargo: no world";
            switch ((verb ?? "").ToLowerInvariant())
            {
                case "visit":
                {
                    long uid = senderUid;
                    string who = senderName ?? "";
                    if (!string.IsNullOrEmpty(arg))
                    {
                        Candidate c = d.FindByName(arg);
                        if (c == null) return "cargo: no online player named '" + arg + "'";
                        uid = c.Uid; who = c.Name;
                    }
                    if (uid == 0) return "cargo: this console has no player; use `cargo visit <name>`";
                    return "cargo visit " + who + ": " + d.Force(uid, Time.time, znet.GetTimeSeconds());
                }
                case "dismiss":
                    return "cargo: " + d.Dismiss("admin" + (string.IsNullOrEmpty(senderName) ? "" : " " + senderName));
                case "reset":
                    return "cargo: " + d.ResetCooldowns();
                case "save":
                    return "cargo: sidecar " + (d.Flush("admin", force: true) ? "written" : "NOT written (see the log)");
                default:
                    return "cargo: unknown admin verb '" + verb + "'";
            }
        }
    }
}
