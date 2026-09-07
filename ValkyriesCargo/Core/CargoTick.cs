using RavenIron.ValkyriesCargo.Client;
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
    /// routed RPCs, runs the director where the world runs (server, listen host) and the comfort
    /// report where a player is drawn (client, listen host), and tears everything down when ZNet
    /// goes away.
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

        private readonly ComfortReporter _reporter = new ComfortReporter();
        private VisitDirector _director;
        private string _loggedRole;
        private bool _live;
        private int _pilotLineShownFor;
        private int _throws;

        private void Awake() { Instance = this; }
        private void OnDestroy() { if (Instance == this) Instance = null; }

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
                return;
            }
            _live = true;

            AdminRpc.EnsureRegistered();

            if (znet.IsServer())
            {
                // The director needs the scene the world runs in: the event system and the zone system.
                if (_director == null && RandEventSystem.instance != null && ZNetScene.instance != null)
                    _director = VisitDirector.Create(znet);
                if (_director != null) _director.Tick(dt, Time.time, znet.GetTimeSeconds());
            }

            if (ValkyriesCargo.HasRenderer)
            {
                _reporter.Tick(dt);
                PilotLine();
            }
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
            _director = null;
            _pilotLineShownFor = 0;
            _reporter.Reset();
            AdminRpc.Reset();
            CargoRpc.EndSession();
            ValkyriesCargo.Log.LogInfo("session ended: director, reporter, routed RPCs and the terminal surface dropped");
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
                default:
                    return "cargo: unknown admin verb '" + verb + "'";
            }
        }
    }
}
