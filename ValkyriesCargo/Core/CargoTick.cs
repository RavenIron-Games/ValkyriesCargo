using UnityEngine;

namespace RavenIron.ValkyriesCargo.Core
{
    /// <summary>
    /// The ONLY Update in the mod (house rule 2). Nothing else owns a timer; every system
    /// that needs time is called from here with the frame's delta.
    ///
    /// Phase 0: it decides the role once per world session and logs it. Readiness is
    /// COMPUTED from live objects, never tracked with a flag set by an event (a flag has to be
    /// right on every path that could change it; a computed property cannot desync).
    /// </summary>
    public sealed class CargoTick : MonoBehaviour
    {
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

        private string _loggedRole;

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

        private int _throws;

        private void Tick(float dt)
        {
            string role = Role();
            if (role != _loggedRole)
            {
                _loggedRole = role;
                if (role != "no world")
                    ValkyriesCargo.Log.LogInfo("role: " + role);
            }
            // Phase 1+: scheduler on the server, comfort report on a client, terminal on a screen.
        }
    }
}
