using System;

namespace RavenIron.ValkyriesCargo.Core
{
    /// <summary>
    /// The server's record of one visit from the decision to the end, and the rule that keeps every
    /// client's countdown honest. The vanilla event's timer (RandomEvent.m_time, real seconds, paused
    /// while nobody is in range) is the authority; this holds the world-time deadline the channel
    /// carries and republishes it only when the two drift apart by more than a second (design 3.7).
    /// PURE: the director feeds it world time and the event's remaining seconds; it hands back the
    /// VisitState string to publish, or null when nothing changed.
    /// </summary>
    public sealed class VisitSession
    {
        public const double RepublishThresholdSeconds = 1.0;

        public int VisitId { get; private set; }
        public VisitPhase Phase { get; private set; } = VisitPhase.None;
        public long PilotUid { get; private set; }
        public string PilotName { get; private set; } = "";
        public float DropX { get; private set; }
        public float DropY { get; private set; }
        public float DropZ { get; private set; }
        public int Purse { get; private set; }
        public int Seed { get; private set; }
        public VisitClock Clock { get; private set; }
        /// <summary>The end time the channel last carried; Sync compares against this.</summary>
        public double PublishedEnd { get; private set; }
        /// <summary>Why the last visit ended, in words, for `cargo status`: "timer", "dismissed by X", "admin", ...</summary>
        public string LastEndReason { get; private set; } = "";
        public int LastVisitId { get; private set; }
        public int Republishes { get; private set; }

        public bool Active => Phase != VisitPhase.None;

        /// <summary>
        /// A visit begins: the pilot, where they stood (the drop point until the flight refines it),
        /// the clock, the purse he arrives with and the seed every client derives his lines from.
        /// Returns the VisitState to publish.
        /// </summary>
        public string Begin(int visitId, long pilotUid, string pilotName, float x, float y, float z,
                            double worldTime, float lifespanSeconds, int purse, int seed)
        {
            VisitId = visitId;
            LastVisitId = visitId;
            Phase = VisitPhase.Flying;
            PilotUid = pilotUid;
            PilotName = pilotName ?? "";
            DropX = x; DropY = y; DropZ = z;
            Purse = Math.Max(0, purse);
            Seed = seed;
            Clock = VisitClock.Start(worldTime, lifespanSeconds);
            PublishedEnd = Clock.EndWorldTime;
            LastEndReason = "";
            Republishes = 0;
            return Encode();
        }

        /// <summary>
        /// Every server tick while active: the event says how many real seconds remain. If the
        /// world-time deadline that implies has drifted more than a second from what was published
        /// (the event paused with nobody near, resumed, or the world clock jumped through a sleep),
        /// retarget the clock and hand back the state to republish. Otherwise null.
        /// </summary>
        public string Sync(double worldTime, double eventRemainingSeconds)
        {
            if (!Active) return null;
            if (double.IsNaN(eventRemainingSeconds) || double.IsInfinity(eventRemainingSeconds)) return null;
            double end = worldTime + Math.Max(0.0, eventRemainingSeconds);
            if (Math.Abs(end - PublishedEnd) <= RepublishThresholdSeconds) return null;
            Clock.Retarget(end);
            PublishedEnd = Clock.EndWorldTime;
            Republishes++;
            return Encode();
        }

        /// <summary>A later phase (the flight, the drop, the approach, trading, leaving). Returns the state to publish, or null if unchanged or inactive.</summary>
        public string SetPhase(VisitPhase phase)
        {
            if (!Active || phase == VisitPhase.None || phase == Phase) return null;
            Phase = phase;
            return Encode();
        }

        /// <summary>The drop point, once the flight knows it. Returns the state to publish, or null if inactive.</summary>
        public string SetDrop(float x, float y, float z)
        {
            if (!Active) return null;
            DropX = x; DropY = y; DropZ = z;
            return Encode();
        }

        /// <summary>The visit is over. Returns the state to publish: the empty channel, which every reader parses as "no visit".</summary>
        public string End(string reason)
        {
            if (Active) LastEndReason = string.IsNullOrEmpty(reason) ? "ended" : reason;
            Phase = VisitPhase.None;
            return "";
        }

        public VisitSnapshot Snapshot()
        {
            return new VisitSnapshot
            {
                VisitId = VisitId, Phase = Phase, PilotUid = PilotUid, BirdZdo = "", MerchantZdo = "",
                DropX = DropX, DropY = DropY, DropZ = DropZ,
                EndWorldTime = Clock != null ? Clock.EndWorldTime : 0.0, Purse = Purse, Seed = Seed,
            };
        }

        public string Encode() => Active ? Snapshot().Encode() : "";
    }
}
