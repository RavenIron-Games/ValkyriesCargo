using System;
using System.Collections.Generic;

namespace RavenIron.ValkyriesCargo.Core
{
    /// <summary>
    /// The server's record of one visit from the decision to the end, and the rule that keeps every
    /// client's countdown honest. The vanilla event's timer (RandomEvent.m_time, real seconds, paused
    /// while nobody is in range) is the authority; this holds the world-time deadline the channel
    /// carries and republishes it only when the two drift apart by more than a second (design 3.7).
    /// Vanilla saves the running event with the world and restores it on load, so a restart mid-visit
    /// resumes the event; the `session` row in the sidecar lets the director adopt it (Resume).
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
        /// <summary>True when this session came back from the sidecar rather than a fresh Begin.</summary>
        public bool Resumed { get; private set; }
        /// <summary>
        /// How many terminals are open on him right now (issue #59, 2026-09-08): the director copies the
        /// deal wire's count in every tick and the state republishes when it moves. Never in the sidecar
        /// row: a restart starts at 0 and the wire re-counts as the terminals reopen.
        /// </summary>
        public int TerminalsOpen { get; private set; }

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
            PilotName = CleanName(pilotName);
            DropX = x; DropY = y; DropZ = z;
            Purse = Math.Max(0, purse);
            Seed = seed;
            Clock = VisitClock.Start(worldTime, lifespanSeconds);
            PublishedEnd = Clock.EndWorldTime;
            LastEndReason = "";
            Republishes = 0;
            Resumed = false;
            TerminalsOpen = 0;
            return Encode();
        }

        /// <summary>
        /// The deal wire's count of open terminals, copied in every tick. Returns the state to publish
        /// when the count moved (and a visit is running), else null. A negative count reads as 0.
        /// </summary>
        public string SetTerminalsOpen(int count)
        {
            int n = Math.Max(0, count);
            if (!Active || n == TerminalsOpen) return null;
            TerminalsOpen = n;
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
            TerminalsOpen = 0;
            return "";
        }

        public VisitSnapshot Snapshot()
        {
            return new VisitSnapshot
            {
                VisitId = VisitId, Phase = Phase, PilotUid = PilotUid, BirdZdo = "", MerchantZdo = "",
                DropX = DropX, DropY = DropY, DropZ = DropZ,
                EndWorldTime = Clock != null ? Clock.EndWorldTime : 0.0, Purse = Purse, Seed = Seed,
                TerminalsOpen = TerminalsOpen,
            };
        }

        public string Encode() => Active ? Snapshot().Encode() : "";

        // ---- the sidecar row ----------------------------------------------------------------------

        /// <summary>
        /// "session\tvisitId\tpilotUid\tpilotName\tdropX\tdropY\tdropZ\tstartWorldTime\tendWorldTime\tpurse\tseed\tphase",
        /// or "" when no visit is running (the row is simply absent from the file).
        /// </summary>
        public string EncodeSessionRow()
        {
            if (!Active) return "";
            return "session\t" + Wire.Int(VisitId) + "\t" + Wire.Long(PilotUid) + "\t" + PilotName + "\t" +
                   Wire.Float(DropX) + "\t" + Wire.Float(DropY) + "\t" + Wire.Float(DropZ) + "\t" +
                   Wire.Double(Clock.StartWorldTime) + "\t" + Wire.Double(Clock.EndWorldTime) + "\t" +
                   Wire.Int(Purse) + "\t" + Wire.Int(Seed) + "\t" + Phase;
        }

        /// <summary>
        /// Adopt a saved visit after a restart: the engine restored the event, the sidecar says whose visit
        /// it was. The clock resumes from the saved deadline (Sync corrects it against the event on the
        /// next tick) and the warning is not counted as given. Returns the VisitState to publish, or null
        /// (with a problem) when the row does not parse.
        /// </summary>
        /// <summary>
        /// The visit id inside a saved `session` row, WITHOUT adopting it: 0 when there is no row or
        /// it does not parse. The boot-time merchant sweep needs this and cannot use `VisitId`,
        /// because a restored row is only adopted later, on a tick, once the engine has brought its
        /// random event back - so at boot `Active` is false and `VisitId` is 0 even when a visit is
        /// about to resume. Sweeping on that 0 destroys the merchant of the visit about to resume.
        /// </summary>
        public static int VisitIdOf(string sessionRow)
        {
            if (string.IsNullOrEmpty(sessionRow)) return 0;
            // No TrimEnd, unlike `Resume`: only f[0] and f[1] are read here, and a row written on
            // Windows carries its stray CR in the LAST field, where it cannot reach either of them.
            string[] f = sessionRow.Split('\t');
            if (f.Length != 12 || f[0] != "session") return 0;
            int visitId;
            // `>= 1` is not decoration: a row claiming 0 or a negative id must never come back as a
            // visit to spare, or the sweep keeps a merchant that belongs to nothing.
            return Wire.TryInt(f[1], out visitId) && visitId >= 1 ? visitId : 0;
        }

        public string Resume(string sessionRow, double nowWorldTime, List<string> problems)
        {
            if (string.IsNullOrEmpty(sessionRow)) { Wire.Report(problems, "session: empty row"); return null; }
            string[] f = sessionRow.TrimEnd('\r').Split('\t');
            if (f.Length != 12 || f[0] != "session") { Wire.Report(problems, "session: expected 12 fields, found " + f.Length); return null; }
            int visitId, purse, seed; long pilot; float x, y, z; double start, end;
            if (!Wire.TryInt(f[1], out visitId) || visitId < 1) { Wire.Report(problems, "session: visitId did not parse"); return null; }
            if (!Wire.TryLong(f[2], out pilot)) { Wire.Report(problems, "session: pilotUid did not parse"); return null; }
            if (!Wire.TryFloat(f[4], out x) || !Wire.TryFloat(f[5], out y) || !Wire.TryFloat(f[6], out z)) { Wire.Report(problems, "session: drop point did not parse"); return null; }
            if (!Wire.TryDouble(f[7], out start) || !Wire.TryDouble(f[8], out end) || double.IsNaN(start) || double.IsNaN(end)) { Wire.Report(problems, "session: clock did not parse"); return null; }
            if (!Wire.TryInt(f[9], out purse) || purse < 0) { Wire.Report(problems, "session: purse did not parse"); return null; }
            if (!Wire.TryInt(f[10], out seed)) { Wire.Report(problems, "session: seed did not parse"); return null; }
            VisitPhase phase;
            if (!Enum.TryParse(f[11], true, out phase) || phase == VisitPhase.None) phase = VisitPhase.Flying;

            VisitId = visitId;
            LastVisitId = visitId;
            Phase = phase;
            PilotUid = pilot;
            PilotName = CleanName(f[3]);
            DropX = x; DropY = y; DropZ = z;
            Purse = purse;
            Seed = seed;
            Clock = VisitClock.Resume(start, end, nowWorldTime);
            PublishedEnd = Clock.EndWorldTime;
            LastEndReason = "";
            Republishes = 0;
            Resumed = true;
            TerminalsOpen = 0;
            return Encode();
        }

        private static string CleanName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "";
            return name.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
        }
    }
}
