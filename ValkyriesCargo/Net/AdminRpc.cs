using System;
using System.Collections.Generic;
using RavenIron.ValkyriesCargo.Core;
using RavenIron.ValkyriesCargo.Server;

namespace RavenIron.ValkyriesCargo.Net
{
    /// <summary>
    /// The admin verbs (`cargo visit`, `cargo dismiss`, `cargo reset`, `cargo save`) from a client's
    /// console to the server, and the answer back, on the DIRECT peer `ZRpc` -- the same wire the deal
    /// wire rides, and for the same reason.
    ///
    /// It was on the routed RPC until P11's authority audit. `ZRoutedRpc.RoutedRPCData.m_senderPeerID`
    /// is a field the SENDER writes and `RPC_RoutedRPC` deserializes without ever comparing it to the
    /// socket the packet arrived on (decompile 2026-09-07, ZRoutedRpc.cs 175-195). The handler's
    /// `sender` argument is therefore whatever the client typed, so `GetPeer(sender)` handed
    /// `AdminGate` an ADMIN's socket for a packet a non-admin sent. On a direct `ZRpc` there is no
    /// sender field: the connection is the identity, `DealWire.PeerFor` resolves the peer by reference,
    /// and `ZNet.IsAdmin` is asked about that peer's own `m_socket.GetHostName()`.
    ///
    /// Registered per session, on each peer's socket as it connects (server) and on the server socket
    /// (client); `ZRpc.Register` replaces by name, so repeating is safe. A listen host never uses this:
    /// its console runs `CargoTick.Admin` in place.
    /// </summary>
    public static class AdminRpc
    {
        public const string Request = Keys.Admin;
        public const string Reply = Keys.Reply;

        private static readonly HashSet<ZRpc> _serverSide = new HashSet<ZRpc>();
        private static ZRpc _clientSide;
        private static bool _swept;
        private static int _throws;
        private static int _refusals;

        /// <summary>The admin wire is up for this machine's role: every live peer (server), or the server socket (client).</summary>
        public static bool Registered
        {
            get
            {
                ZNet znet = ZNet.instance;
                if (znet == null) return false;
                if (znet.IsServer()) return _swept;
                return _clientSide != null && _clientSide.IsConnected();
            }
        }

        /// <summary>Called every tick while a world is live; registers once per socket.</summary>
        public static void EnsureRegistered()
        {
            ZNet znet = ZNet.instance;
            if (znet == null) return;
            try
            {
                if (znet.IsServer()) SweepPeers(znet);
                else FollowServerSocket(znet);
            }
            catch (Exception ex)
            {
                if (_throws++ < 3) ValkyriesCargo.Log.LogError("admin wire registration threw: " + ex);
            }
        }

        private static void SweepPeers(ZNet znet)
        {
            if (!_swept)
            {
                _swept = true;
                ValkyriesCargo.Log.LogInfo("admin wire up for this session: " + Request + " is registered on each peer's OWN socket as it connects and " +
                                           Reply + " answers on the same socket; the caller's identity is that socket, never a field in the packet");
            }
            List<ZNetPeer> peers = znet.GetPeers();
            int live = 0;
            foreach (ZNetPeer peer in peers)
            {
                if (peer == null || peer.m_rpc == null) continue;
                live++;
                if (_serverSide.Contains(peer.m_rpc)) continue;
                peer.m_rpc.Register<string, string>(Request, OnRequest);
                _serverSide.Add(peer.m_rpc);
                ValkyriesCargo.Log.LogInfo("admin wire registered for " + Who(peer) + " (" + Wire.Long(peer.m_uid) + ")");
            }
            // Only when someone left: a dead socket would otherwise hold its handler for the session.
            if (_serverSide.Count > live)
            {
                var alive = new HashSet<ZRpc>();
                foreach (ZNetPeer peer in peers) if (peer != null && peer.m_rpc != null) alive.Add(peer.m_rpc);
                _serverSide.RemoveWhere(r => !alive.Contains(r));
            }
        }

        private static void FollowServerSocket(ZNet znet)
        {
            ZRpc rpc;
            try { rpc = znet.GetServerRPC(); } catch { rpc = null; }
            if (rpc == null || ReferenceEquals(rpc, _clientSide)) return;
            rpc.Register<string>(Reply, OnReply);
            _clientSide = rpc;
            ValkyriesCargo.Log.LogInfo("admin wire: registered " + Reply + " on the server socket");
        }

        public static void Reset() { _serverSide.Clear(); _clientSide = null; _swept = false; }

        /// <summary>Client side: ask the server. Returns what to print now; the server's answer prints when it arrives.</summary>
        public static string Send(string verb, string arg)
        {
            ZNet znet = ZNet.instance;
            if (znet == null) return "cargo: no world loaded";
            if (_clientSide == null || !_clientSide.IsConnected()) return "cargo: not connected to a server yet; try again in a moment";
            try { _clientSide.Invoke(Request, verb, arg ?? ""); }
            catch (Exception ex)
            {
                if (_throws++ < 3) ValkyriesCargo.Log.LogError(Request + " send threw: " + ex);
                return "cargo: the request did not go out (see the log)";
            }
            return "cargo: asked the server to " + verb + (string.IsNullOrEmpty(arg) ? "" : " " + arg) + "; its answer prints here";
        }

        /// <summary>
        /// Server side. The caller is the socket this arrived on and nothing else: no uid from the
        /// packet, no name from the packet. A peer we cannot resolve is not an admin (fail closed).
        /// </summary>
        private static void OnRequest(ZRpc rpc, string verb, string arg)
        {
            try
            {
                ZNet znet = ZNet.instance;
                if (znet == null || !znet.IsServer()) return;
                ZNetPeer peer = DealWire.PeerFor(rpc);
                string who = Who(peer);
                string answer;
                if (peer == null || !AdminGate.IsAdmin(znet, peer))
                {
                    answer = "cargo: not an admin (the server's adminlist.txt decides)";
                    // Client-driven, so bounded: a refused caller can send this as fast as the socket allows.
                    if (_refusals++ < 3)
                        ValkyriesCargo.Log.LogWarning("refused " + Request + " " + verb + " from " + who +
                                                      (peer != null ? " (" + Wire.Long(peer.m_uid) + ")" : " (no peer on that socket)") +
                                                      ": not an admin" + (_refusals == 3 ? "; further refusals are silent" : ""));
                }
                else
                {
                    ValkyriesCargo.Log.LogInfo("admin " + who + " (" + Wire.Long(peer.m_uid) + "): cargo " + verb + " " + (arg ?? ""));
                    answer = CargoTick.Admin(verb, arg, peer.m_uid, who);
                }
                try { rpc.Invoke(Reply, answer ?? ""); }
                catch (Exception ex) { if (_throws++ < 3) ValkyriesCargo.Log.LogError(Reply + " send threw: " + ex); }
            }
            catch (Exception ex)
            {
                if (_throws++ < 3) ValkyriesCargo.Log.LogError(Request + " handler threw: " + ex);
            }
        }

        private static void OnReply(ZRpc rpc, string text)
        {
            try
            {
                ValkyriesCargo.Log.LogInfo("server answered: " + text);
                if (Console.instance != null) Console.instance.AddString(text ?? "");
            }
            catch (Exception ex)
            {
                if (_throws++ < 3) ValkyriesCargo.Log.LogError(Reply + " handler threw: " + ex);
            }
        }

        private static string Who(ZNetPeer peer) => peer != null && !string.IsNullOrEmpty(peer.m_playerName) ? peer.m_playerName : "?";
    }
}
