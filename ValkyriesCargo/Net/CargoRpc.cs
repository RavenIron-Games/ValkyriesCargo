using System;
using System.Collections.Generic;
using RavenIron.ValkyriesCargo.Core;

namespace RavenIron.ValkyriesCargo.Net
{
    /// <summary>
    /// What a transport must do for the terminal. The real one (Phase 6) rides the direct peer ZRpc;
    /// the demo one settles against a DemoMarket in-process. PURE.
    /// </summary>
    public interface ICargoTransport
    {
        bool Ready { get; }
        void Open(int visitId);
        void Close(int visitId);
        void Dismiss(int visitId);
        void Send(Deal deal, Action<DealResult> onAnswer);
    }

    /// <summary>
    /// The client-side surface the terminal calls (design WORKSPLIT §2). It never knows how a message
    /// travels: a transport is plugged in by the game side (the real ZRpc one when a world is joined)
    /// or by `cargo terminal demo` (the in-process demo). Snapshots arrive through PublishMarket /
    /// PublishVisit, which the game side wires to ServerSync's ValueChanged and the demo calls directly.
    /// PURE: no game or Unity dependency, so the harness tests it end to end.
    /// </summary>
    public static class CargoRpc
    {
        private static ICargoTransport _transport;
        private static DealInbox _inbox = new DealInbox();

        public static MarketSnapshot Market { get; private set; } = new MarketSnapshot();
        public static VisitSnapshot Visit { get; private set; } = new VisitSnapshot();

        public static event Action<MarketSnapshot> MarketChanged;
        public static event Action<VisitSnapshot> VisitChanged;

        public static bool Ready => _transport != null && _transport.Ready;
        public static bool IsDemo => _transport is DemoTransport;
        public static DealInbox Inbox => _inbox;

        /// <summary>The game side installs the real transport per session; null on logout.</summary>
        public static void UseTransport(ICargoTransport transport) { _transport = transport; }

        /// <summary>No server: settle deals in-process against the demo market and publish its snapshots.</summary>
        public static void UseDemo(bool on)
        {
            if (!on) { if (IsDemo) _transport = null; return; }
            var demo = new DemoTransport(DemoMarket.Default());
            _transport = demo;
            PublishVisit(VisitSnapshot.Demo);
            PublishMarket(demo.Market.Market.Encode());
        }

        public static DemoTransport Demo => _transport as DemoTransport;

        public static void Open(int visitId)    { if (_transport != null) _transport.Open(visitId); }
        public static void Close(int visitId)   { if (_transport != null) _transport.Close(visitId); }
        /// <summary>
        /// Send him off. The server answers exactly once on VCargo_dismissed (DealReason.Ok, TooFar or
        /// StaleVisit) and it comes back through onAnswer; without a transport the answer is
        /// not_connected. Until 2026-09-08 this was fire-and-forget and the terminal closed on trust:
        /// visit 21's dismiss was refused three times on the server while the client said the farewell.
        /// </summary>
        public static void Dismiss(int visitId, Action<string> onAnswer = null)
        {
            _dismissAnswer = onAnswer;
            if (_transport == null) { AnswerDismiss(DealReason.NotConnected); return; }
            _transport.Dismiss(visitId);
        }

        /// <summary>The transport's answer to the last Dismiss: delivered once; with nobody waiting it is dropped.</summary>
        public static void AnswerDismiss(string reason)
        {
            Action<string> cb = _dismissAnswer;
            _dismissAnswer = null;
            if (cb != null) cb(string.IsNullOrEmpty(reason) ? DealReason.Malformed : reason);
        }

        private static Action<string> _dismissAnswer;

        /// <summary>
        /// Send a deal. The answer comes back exactly once through onAnswer; a redelivered accepted
        /// result whose DeliveryId is already in the inbox is answered as a duplicate so the caller
        /// never applies it twice. Without a transport the answer is not_connected.
        /// </summary>
        public static void Send(Deal deal, Action<DealResult> onAnswer)
        {
            if (onAnswer == null) return;
            if (deal == null || deal.IsEmpty) { onAnswer(DealResult.Refuse(deal != null ? deal.Nonce : 0, DealReason.EmptyDeal)); return; }
            if (!Ready) { onAnswer(DealResult.Refuse(deal.Nonce, DealReason.NotConnected)); return; }
            _transport.Send(deal, result =>
            {
                if (result == null) { onAnswer(DealResult.Refuse(deal.Nonce, DealReason.Malformed)); return; }
                if (result.Ok)
                {
                    if (!_inbox.MarkApplied(result.DeliveryId))
                    {
                        onAnswer(DealResult.Refuse(result.Nonce, DealReason.Duplicate));
                        return;
                    }
                }
                onAnswer(result);
            });
        }

        /// <summary>Game side: MarketState.ValueChanged → here. Demo: after every settle.</summary>
        public static void PublishMarket(string encoded)
        {
            var problems = new List<string>();
            Market = MarketSnapshot.Parse(encoded, problems);
            LastMarketProblems = problems;
            var h = MarketChanged;
            if (h != null) h(Market);
        }

        public static void PublishVisit(string encoded)
        {
            var problems = new List<string>();
            Visit = VisitSnapshot.Parse(encoded, problems);
            LastVisitProblems = problems;
            var h = VisitChanged;
            if (h != null) h(Visit);
        }

        public static List<string> LastMarketProblems { get; private set; } = new List<string>();
        public static List<string> LastVisitProblems { get; private set; } = new List<string>();

        /// <summary>Game side, at session end: drop the transport and every subscriber; keep the inbox (it is per player, not per session).</summary>
        public static void EndSession()
        {
            _transport = null;
            MarketChanged = null;
            VisitChanged = null;
            Market = new MarketSnapshot();
            Visit = new VisitSnapshot();
        }

        /// <summary>Game side, at login: restore the inbox the client persisted for this player.</summary>
        public static void LoadInbox(DealInbox inbox) { _inbox = inbox ?? new DealInbox(); }

        /// <summary>Test seam: EndSession plus a fresh inbox.</summary>
        public static void ResetForTests()
        {
            EndSession();
            _inbox = new DealInbox();
            _dismissAnswer = null;
        }
    }

    /// <summary>The in-process transport behind `cargo terminal demo`.</summary>
    public sealed class DemoTransport : ICargoTransport
    {
        public DemoMarket Market { get; }
        public int PlayerCoins = 840;
        public int Opens, Closes, Dismisses;

        public DemoTransport(DemoMarket market) { Market = market; }

        public bool Ready => true;
        public void Open(int visitId) { Opens++; }
        public void Close(int visitId) { Closes++; }
        public void Dismiss(int visitId) { Dismisses++; CargoRpc.AnswerDismiss(DealReason.Ok); }

        public void Send(Deal deal, Action<DealResult> onAnswer)
        {
            DealResult r = Market.Settle(deal, PlayerCoins);
            if (r.Ok) PlayerCoins += r.CoinsDelta;
            onAnswer(r);
            // The real server broadcasts MarketState after every accepted deal; so does the demo.
            if (r.Ok) CargoRpc.PublishMarket(Market.Market.Encode());
        }
    }
}
