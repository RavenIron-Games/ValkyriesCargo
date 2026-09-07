using System;
using System.Collections.Generic;
using RavenIron.ValkyriesCargo.Client;
using RavenIron.ValkyriesCargo.Core;
using RavenIron.ValkyriesCargo.Server;

namespace RavenIron.ValkyriesCargo.Net
{
    /// <summary>
    /// The client end of the deal wire: the real ICargoTransport behind CargoRpc when a world is joined
    /// (design 3.4). Talks on the server's own ZRpc (`ZNet.GetServerRPC()`, one per connection), answers
    /// arrive as `vc_dealt`. A solicited answer goes back to CargoRpc's callback (which keeps the inbox);
    /// an unsolicited one (a redelivery after `vc_claim`) is applied here through DealApplier and acked.
    /// Every accepted answer is acked once the client has it, so the server's owed ledger clears.
    /// </summary>
    public sealed class CargoTransport : ICargoTransport
    {
        private ZRpc _rpc;
        private readonly Dictionary<long, Action<DealResult>> _pending = new Dictionary<long, Action<DealResult>>();
        private bool _claimed;
        private int _throws;

        public int Sent { get; private set; }
        public int Answered { get; private set; }
        public int Unsolicited { get; private set; }
        public int Pending => _pending.Count;
        public bool Claimed => _claimed;

        public bool Ready => _rpc != null && _rpc.IsConnected();

        /// <summary>Every tick on a client: follow the server socket, register the answer handler, claim once.</summary>
        public void EnsureRegistered(ZNet znet)
        {
            if (znet == null || znet.IsServer()) return;
            ZRpc rpc;
            try { rpc = znet.GetServerRPC(); } catch { rpc = null; }
            if (rpc == null || ReferenceEquals(rpc, _rpc)) return;
            try
            {
                rpc.Register<string>(DealWire.Dealt, OnDealt);
                _rpc = rpc;
                _pending.Clear();
                _claimed = false;
                ValkyriesCargo.Log.LogInfo("deal wire: registered vc_dealt on the server socket");
            }
            catch (Exception ex)
            {
                if (_throws++ < 3) ValkyriesCargo.Log.LogError("deal wire: client registration threw: " + ex);
            }
        }

        /// <summary>Ask the server for anything it still owes this player; once per connection, after the player exists.</summary>
        public void ClaimOnce()
        {
            if (_claimed || !Ready || Player.m_localPlayer == null) return;
            _claimed = true;
            try { _rpc.Invoke(DealWire.Claim); }
            catch (Exception ex) { if (_throws++ < 3) ValkyriesCargo.Log.LogError("vc_claim send threw: " + ex); }
        }

        /// <summary>`cargo claim`: ask again, whatever happened before.</summary>
        public void ClaimAgain() { _claimed = false; ClaimOnce(); }

        public void Open(int visitId) { Invoke(DealWire.Open, visitId); }
        public void Close(int visitId) { Invoke(DealWire.Close, visitId); }
        public void Dismiss(int visitId) { Invoke(DealWire.Dismiss, visitId); }

        public void Send(Deal deal, Action<DealResult> onAnswer)
        {
            if (deal == null || onAnswer == null) return;
            if (!Ready) { onAnswer(DealResult.Refuse(deal.Nonce, DealReason.NotConnected)); return; }
            _pending[deal.Nonce] = onAnswer;
            Sent++;
            try { _rpc.Invoke(DealWire.DealName, deal.Encode()); }
            catch (Exception ex)
            {
                _pending.Remove(deal.Nonce);
                if (_throws++ < 3) ValkyriesCargo.Log.LogError("vc_deal send threw: " + ex);
                onAnswer(DealResult.Refuse(deal.Nonce, DealReason.NotConnected));
            }
        }

        private void Invoke(string name, int visitId)
        {
            if (!Ready) return;
            try { _rpc.Invoke(name, visitId); }
            catch (Exception ex) { if (_throws++ < 3) ValkyriesCargo.Log.LogError(name + " send threw: " + ex); }
        }

        private void OnDealt(ZRpc rpc, string encoded)
        {
            try
            {
                var problems = new List<string>();
                DealResult r = DealResult.Parse(encoded, problems);
                if (r == null) { ValkyriesCargo.Log.LogWarning("vc_dealt did not parse: " + string.Join("; ", problems.ToArray())); return; }
                Action<DealResult> cb;
                if (_pending.TryGetValue(r.Nonce, out cb))
                {
                    _pending.Remove(r.Nonce);
                    Answered++;
                    cb(r);
                    // The caller applies inside cb. Ack (and persist the inbox) ONLY for what the pack
                    // actually took: the ack clears the server's owed row, so acking a delivery the
                    // inventory refused loses it for good, and persisting the inbox id would make the
                    // redelivery a duplicate. This is the discipline Deliveries.Handle already keeps.
                    if (r.Ok && DealApplier.LastApplied == r.DeliveryId) { InboxStore.Save(CargoRpc.Inbox); AckNow(r.DeliveryId); }
                    else if (r.Ok) CargoRpc.Inbox.Forget(r.DeliveryId);   // Send marked it before the pack refused; the redelivery must apply, not ack
                    return;
                }
                Unsolicited++;
                Deliveries.Handle(r, this);
            }
            catch (Exception ex)
            {
                if (_throws++ < 3) ValkyriesCargo.Log.LogError("vc_dealt handler threw: " + ex);
            }
        }

        public void AckNow(string deliveryId)
        {
            if (!Ready || string.IsNullOrEmpty(deliveryId)) return;
            try { _rpc.Invoke(DealWire.Ack, deliveryId); }
            catch (Exception ex) { if (_throws++ < 3) ValkyriesCargo.Log.LogError("vc_ack send threw: " + ex); }
        }
    }

    /// <summary>
    /// A listen host is server and client at once and has no server socket; its deals go straight to the
    /// director in-process, with the same inbox and ack discipline so the ledger never differs by path.
    /// </summary>
    public sealed class LocalTransport : ICargoTransport
    {
        private readonly string _key;
        public LocalTransport(string playerKey) { _key = OwedLedger.IsPlayerKey(playerKey) ? playerKey : "host"; }

        public bool Ready => CargoTick.Director != null && Player.m_localPlayer != null;

        public void Open(int visitId) { }
        public void Close(int visitId) { }
        public void Dismiss(int visitId)
        {
            VisitDirector d = CargoTick.Director;
            if (d != null && d.Session.Active && d.Session.VisitId == visitId)
                ValkyriesCargo.Log.LogInfo(d.Dismiss("dismissed by " + (Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "host")));
        }

        public void Send(Deal deal, Action<DealResult> onAnswer)
        {
            if (deal == null || onAnswer == null) return;
            VisitDirector d = CargoTick.Director;
            if (d == null) { onAnswer(DealResult.Refuse(deal.Nonce, DealReason.NotConnected)); return; }
            DealResult r = d.Settle(deal, _key, Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "host");
            onAnswer(r);
            // Same rule as the remote path: the ledger keeps what the pack refused.
            if (r.Ok && DealApplier.LastApplied == r.DeliveryId) { InboxStore.Save(CargoRpc.Inbox); d.Ack(_key, r.DeliveryId); }
            else if (r.Ok) CargoRpc.Inbox.Forget(r.DeliveryId);
        }

        /// <summary>The host's owed rows are applied in-process at session start, the way a claim would.</summary>
        public void ClaimOnce()
        {
            VisitDirector d = CargoTick.Director;
            if (d == null) return;
            foreach (DealResult r in d.Owed(_key)) Deliveries.HandleLocal(r, _key, d);
        }
    }

    /// <summary>Unsolicited deliveries (a claim's redelivery): apply once through DealApplier, remember, ack.</summary>
    public static class Deliveries
    {
        public static int Applied { get; private set; }
        public static int Deferred { get; private set; }

        public static void Handle(DealResult r, CargoTransport t)
        {
            if (r == null || !r.Ok) return;
            if (CargoRpc.Inbox.AlreadyApplied(r.DeliveryId)) { t.AckNow(r.DeliveryId); return; }
            string why = DealApplier.CanApply(r);
            if (why != null) { Deferred++; ValkyriesCargo.Log.LogInfo("delivery " + r.DeliveryId + " deferred: " + why + " (the server keeps it until it fits)"); return; }
            if (!DealApplier.Apply(r)) return;
            CargoRpc.Inbox.MarkApplied(r.DeliveryId);
            InboxStore.Save(CargoRpc.Inbox);
            Applied++;
            t.AckNow(r.DeliveryId);
            Announce(r);
        }

        public static void HandleLocal(DealResult r, string key, VisitDirector d)
        {
            if (r == null || !r.Ok) return;
            if (CargoRpc.Inbox.AlreadyApplied(r.DeliveryId)) { d.Ack(key, r.DeliveryId); return; }
            if (DealApplier.CanApply(r) != null) { Deferred++; return; }
            if (!DealApplier.Apply(r)) return;
            CargoRpc.Inbox.MarkApplied(r.DeliveryId);
            InboxStore.Save(CargoRpc.Inbox);
            Applied++;
            d.Ack(key, r.DeliveryId);
            Announce(r);
        }

        private static void Announce(DealResult r)
        {
            ValkyriesCargo.Log.LogInfo("delivery " + r.DeliveryId + " applied: " + DealApplier.Describe(r));
            if (MessageHud.instance != null) MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, "Ingvar's delivery: " + DealApplier.Describe(r));
        }
    }
}
