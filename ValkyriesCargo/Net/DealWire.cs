using System;
using System.Collections.Generic;
using RavenIron.ValkyriesCargo.Core;
using RavenIron.ValkyriesCargo.Server;

namespace RavenIron.ValkyriesCargo.Net
{
    /// <summary>
    /// The server end of the deal wire (design 3.4, 3.8): `VCargo_open`, `VCargo_close`, `VCargo_deal`, `VCargo_ack`,
    /// `VCargo_claim`, `VCargo_dismiss` registered on EACH peer's own ZRpc as it connects, answered on the same
    /// socket with `VCargo_dealt`. A direct peer socket cannot be forged by another client (the peer IS the
    /// socket), which is why deals ride here and not on the routed RPC. Handlers resolve the peer from the
    /// ZRpc that delivered the call; the player key for the owed ledger is the peer's platform id
    /// (`m_socket.GetHostName()`), stable across sessions where the uid is not. Registered per session,
    /// re-registered for a new socket (`ZRpc.Register` replaces by name, so repeating is safe).
    /// </summary>
    public static class DealWire
    {
        public const string Open = Keys.Open, Close = Keys.Close, DealName = Keys.Deal, Ack = Keys.Ack, Claim = Keys.Claim, Dismiss = Keys.Dismiss, Dealt = Keys.Dealt;

        private static readonly HashSet<ZRpc> _registered = new HashSet<ZRpc>();
        private static readonly HashSet<long> _open = new HashSet<long>();
        private static int _throws;
        private static int _farDismissals;
        private static int _badDeals;

        public static int Registered => _registered.Count;
        public static int OpenTerminals => _open.Count;
        public static int Deals { get; private set; }
        public static int Redeliveries { get; private set; }

        /// <summary>Every tick where the world runs: register on new peers, forget dead sockets.</summary>
        public static void Tick(ZNet znet)
        {
            if (znet == null || !znet.IsServer()) return;
            List<ZNetPeer> peers;
            try { peers = znet.GetPeers(); }
            catch (Exception ex) { if (_throws++ < 3) ValkyriesCargo.Log.LogError("deal wire: GetPeers threw: " + ex); return; }
            var live = new HashSet<ZRpc>();
            foreach (ZNetPeer peer in peers)
            {
                if (peer == null || peer.m_rpc == null) continue;
                live.Add(peer.m_rpc);
                if (_registered.Contains(peer.m_rpc)) continue;
                try
                {
                    ZRpc rpc = peer.m_rpc;
                    rpc.Register<int>(Open, OnOpen);
                    rpc.Register<int>(Close, OnClose);
                    rpc.Register<string>(DealName, OnDeal);
                    rpc.Register<string>(Ack, OnAck);
                    rpc.Register(Claim, OnClaim);
                    rpc.Register<int>(Dismiss, OnDismiss);
                    _registered.Add(rpc);
                    ValkyriesCargo.Log.LogInfo("deal wire registered for " + Who(peer) + " (" + Wire.Long(peer.m_uid) + ")");
                }
                catch (Exception ex)
                {
                    if (_throws++ < 3) ValkyriesCargo.Log.LogError("deal wire: registration threw: " + ex);
                }
            }
            if (_registered.Count > live.Count) _registered.RemoveWhere(r => !live.Contains(r));
        }

        public static void Reset() { _registered.Clear(); _open.Clear(); _lastClaim.Clear(); }

        // ---- handlers -------------------------------------------------------------------------------

        private static void OnOpen(ZRpc rpc, int visitId)
        {
            ZNetPeer peer = PeerFor(rpc);
            if (peer != null) _open.Add(peer.m_uid);
        }

        private static void OnClose(ZRpc rpc, int visitId)
        {
            ZNetPeer peer = PeerFor(rpc);
            if (peer != null) _open.Remove(peer.m_uid);
        }

        private static void OnDeal(ZRpc rpc, string encoded)
        {
            try
            {
                ZNetPeer peer = PeerFor(rpc);
                VisitDirector d = CargoTick.Director;
                var problems = new List<string>();
                Deal deal = Deal.Parse(encoded, problems);
                if (deal == null)
                {
                    // Client-driven: a malformed deal costs the sender one packet, so the log line is capped.
                    if (_badDeals++ < 3)
                        ValkyriesCargo.Log.LogWarning(DealName + " from " + Who(peer) + " did not parse: " + string.Join("; ", problems.ToArray()) +
                                                      (_badDeals == 3 ? "; further parse failures are silent" : ""));
                    Answer(rpc, DealResult.Refuse(0, DealReason.Malformed));
                    return;
                }
                if (d == null || peer == null) { Answer(rpc, DealResult.Refuse(deal.Nonce, DealReason.VisitOver)); return; }
                DealResult r = d.Settle(deal, KeyFor(peer), Who(peer));
                Deals++;
                Answer(rpc, r);
            }
            catch (Exception ex)
            {
                if (_throws++ < 3) ValkyriesCargo.Log.LogError(DealName + " handler threw: " + ex);
            }
        }

        private static void OnAck(ZRpc rpc, string deliveryId)
        {
            try
            {
                ZNetPeer peer = PeerFor(rpc);
                VisitDirector d = CargoTick.Director;
                if (d == null || peer == null) return;
                d.Ack(KeyFor(peer), deliveryId);
            }
            catch (Exception ex)
            {
                if (_throws++ < 3) ValkyriesCargo.Log.LogError(Ack + " handler threw: " + ex);
            }
        }

        /// <summary>
        /// A claim redelivers every owed row on the socket, so one small packet can ask the server for up
        /// to `OwedLedger.DefaultPerPlayer` (50) packets back. That is an amplifier, and the client only
        /// ever needs it once per connection (`CargoTransport.ClaimOnce`) or when a player types
        /// `cargo claim`. One claim per peer per this many seconds; the rest are dropped in silence.
        /// </summary>
        public const float ClaimCooldownSeconds = 5f;

        private static readonly Dictionary<long, float> _lastClaim = new Dictionary<long, float>();

        private static void OnClaim(ZRpc rpc)
        {
            try
            {
                ZNetPeer peer = PeerFor(rpc);
                VisitDirector d = CargoTick.Director;
                if (d == null || peer == null) return;
                float now = UnityEngine.Time.time;
                float last;
                if (_lastClaim.TryGetValue(peer.m_uid, out last) && now - last < ClaimCooldownSeconds) return;
                _lastClaim[peer.m_uid] = now;
                List<DealResult> owed = d.Owed(KeyFor(peer));
                foreach (DealResult r in owed) { Answer(rpc, r); Redeliveries++; }
                if (owed.Count > 0) ValkyriesCargo.Log.LogInfo(Claim + " from " + Who(peer) + ": redelivered " + owed.Count + " owed deal(s)");
            }
            catch (Exception ex)
            {
                if (_throws++ < 3) ValkyriesCargo.Log.LogError(Claim + " handler threw: " + ex);
            }
        }

        private static void OnDismiss(ZRpc rpc, int visitId)
        {
            try
            {
                ZNetPeer peer = PeerFor(rpc);
                VisitDirector d = CargoTick.Director;
                if (d == null || peer == null) return;
                if (!d.Session.Active || d.Session.VisitId != visitId) return;
                if (!AtTheVisit(peer, d.Session))
                {
                    if (_farDismissals++ < 3)
                        ValkyriesCargo.Log.LogWarning("refused " + Dismiss + " from " + Who(peer) + ": " + Wire.Float(DistanceToVisit(peer, d.Session)) +
                                                      " m from visit #" + visitId + "'s drop point, and a visitor is within " + Wire.Float(VisitorRange) + " m" +
                                                      (_farDismissals == 3 ? "; further refusals are silent" : ""));
                    return;
                }
                ValkyriesCargo.Log.LogInfo(Dismiss + " from " + Who(peer) + ": " + d.Dismiss("dismissed by " + Who(peer)));
            }
            catch (Exception ex)
            {
                if (_throws++ < 3) ValkyriesCargo.Log.LogError(Dismiss + " handler threw: " + ex);
            }
        }

        /// <summary>
        /// Design 3.8 says vc_dismiss may come from "any visitor". A visitor is someone AT the visit, and
        /// the radius that already means that in this mod is the event's own `m_eventRange` (96 m,
        /// CargoEvent): inside it a player sees the banner and keeps the visit's clock running, outside it
        /// the clock pauses. Being online is not being a visitor.
        ///
        /// The position is the peer's own reported reference position (`ZNet.RPC_ServerSyncedPlayerData`
        /// resolves the peer from the SOCKET, so it belongs to this caller), but the number in it is the
        /// client's own claim -- the same trust class as `vc_rested`. What this closes is any client
        /// anywhere in the world ending anyone's visit; what it does not close is a modified client
        /// claiming to stand where it does not. There is nothing on the server that could tell the
        /// difference: vanilla keeps no server-side position for a player either.
        /// </summary>
        public const float VisitorRange = 96f;

        private static bool AtTheVisit(ZNetPeer peer, VisitSession session)
            => DistanceToVisit(peer, session) <= VisitorRange;

        private static float DistanceToVisit(ZNetPeer peer, VisitSession session)
        {
            UnityEngine.Vector3 at = peer.GetRefPos();
            float dx = at.x - session.DropX, dz = at.z - session.DropZ;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        // ---- helpers --------------------------------------------------------------------------------

        private static void Answer(ZRpc rpc, DealResult r)
        {
            if (rpc == null || r == null) return;
            try { rpc.Invoke(Dealt, r.Encode()); }
            catch (Exception ex) { if (_throws++ < 3) ValkyriesCargo.Log.LogError(Dealt + " send threw: " + ex); }
        }

        /// <summary>
        /// The peer on the other end of THIS socket, by reference. The one identity on the direct wire
        /// that a client cannot write: the ZRpc is the connection, not a field in the packet. `AdminRpc`
        /// uses it too, which is why it is public rather than a second copy of the loop.
        /// </summary>
        public static ZNetPeer PeerFor(ZRpc rpc)
        {
            ZNet znet = ZNet.instance;
            if (znet == null || rpc == null) return null;
            foreach (ZNetPeer p in znet.GetPeers()) if (p != null && ReferenceEquals(p.m_rpc, rpc)) return p;
            return null;
        }

        /// <summary>The player's platform id: the key the owed ledger is kept by. Falls back to the uid, which is per session.</summary>
        public static string KeyFor(ZNetPeer peer)
        {
            if (peer == null) return "";
            string host = null;
            try { host = peer.m_socket != null ? peer.m_socket.GetHostName() : null; } catch { }
            if (string.IsNullOrEmpty(host) || !OwedLedger.IsPlayerKey(host)) host = "uid" + Wire.Long(peer.m_uid);
            return host;
        }

        private static string Who(ZNetPeer peer) => peer != null && !string.IsNullOrEmpty(peer.m_playerName) ? peer.m_playerName : "?";
    }
}
