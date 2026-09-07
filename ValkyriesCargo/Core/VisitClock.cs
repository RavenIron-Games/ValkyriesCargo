using System;

namespace RavenIron.ValkyriesCargo.Core
{
    /// <summary>
    /// The visit's countdown in WORLD seconds (ZNet.GetTimeSeconds), the one clock every machine shares.
    /// The authority is the event's own timer on the server (RandomEvent.m_time: REAL seconds, and it
    /// pauses while nobody is within range); the server republishes the end time whenever
    /// now + (duration − m_time) drifts more than a second from what it last published (a pause, a
    /// resume, a sleep skip), and every mirror calls Retarget. The terminal's countdown, the one-minute
    /// warning and the restart sweep all read this. Whether the visit ENDED is VisitSnapshot.Phase, not
    /// Expired: a dismissal ends it early. PURE.
    /// </summary>
    public sealed class VisitClock
    {
        public const double WarningSeconds = 60.0;

        public double StartWorldTime { get; private set; }
        public double EndWorldTime { get; private set; }
        public bool Warned { get; private set; }

        public static VisitClock Start(double worldTime, float lifespanSeconds)
        {
            double life = float.IsNaN(lifespanSeconds) ? 1.0 : Math.Max(1.0, lifespanSeconds);
            return new VisitClock { StartWorldTime = worldTime, EndWorldTime = worldTime + life };
        }

        /// <summary>
        /// Resume after a restart or a late join from a saved start and end. The warning is NOT counted as
        /// given: a player who comes back with 40 s left still hears "hurry", once, as soon as it is due.
        /// </summary>
        public static VisitClock Resume(double startWorldTime, double endWorldTime, double nowWorldTime)
        {
            return new VisitClock { StartWorldTime = startWorldTime, EndWorldTime = Math.Max(startWorldTime, endWorldTime) };
        }

        /// <summary>The server moved the deadline (the event paused, resumed, or the world clock jumped). The warning is not re-armed.</summary>
        public void Retarget(double endWorldTime)
        {
            if (double.IsNaN(endWorldTime) || double.IsInfinity(endWorldTime)) return;
            EndWorldTime = Math.Max(StartWorldTime, endWorldTime);
        }

        public double Remaining(double worldTime) => Math.Max(0.0, EndWorldTime - worldTime);
        public bool Expired(double worldTime) => worldTime >= EndWorldTime;

        /// <summary>0 at the start, 1 at the end; for a progress bar.</summary>
        public double Fraction(double worldTime)
        {
            double life = EndWorldTime - StartWorldTime;
            if (life <= 0) return 1.0;
            return Math.Min(1.0, Math.Max(0.0, (worldTime - StartWorldTime) / life));
        }

        /// <summary>True exactly once, the first time a minute or less remains and the visit is not over.</summary>
        public bool OneMinuteWarningDue(double worldTime)
        {
            if (Warned || Expired(worldTime)) return false;
            if (Remaining(worldTime) > WarningSeconds) return false;
            Warned = true;
            return true;
        }

        /// <summary>"03:42" for the terminal header.</summary>
        public string FormatRemaining(double worldTime) => Format(Remaining(worldTime));

        /// <summary>"03:42". A NaN reads 00:00: (int)NaN is 0 on .NET Core and undefined on Mono, so it is guarded here.</summary>
        public static string Format(double seconds)
        {
            if (double.IsNaN(seconds) || seconds <= 0) return "00:00";
            if (double.IsPositiveInfinity(seconds)) seconds = int.MaxValue;
            int s = (int)Math.Min(int.MaxValue, Math.Round(seconds));
            return (s / 60).ToString("00", System.Globalization.CultureInfo.InvariantCulture) + ":" +
                   (s % 60).ToString("00", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
