using System;
using System.Collections.Generic;

namespace RavenIron.ValkyriesCargo.Core
{
    /// <summary>One ended visit, for barrkbot_cargo_visits.json.</summary>
    public sealed class VisitRecord
    {
        public int VisitId;
        public string PilotName = "";
        public DateTime StartedAtUtc;
        public DateTime EndedAtUtc;
        public double DurationSeconds;
        public int Takings;
        public string EndedReason = "";
    }

    /// <summary>
    /// Visits this session, oldest first, for BarrkBOT. Reset every session -- unlike the market, a
    /// visit's own record is not in the world sidecar and does not survive a restart (BARRKBOT_CONTRACT.md).
    /// Bounded so an exceptionally long uptime cannot grow this without limit; the export's own rollover
    /// (BarrkRollover) is what actually keeps any one file small, this cap is just a memory backstop far
    /// above anything a real server reaches. PURE: the caller (the director, at the moment a visit ends)
    /// supplies every field, including the wall-clock timestamps -- nothing here reads a clock.
    /// </summary>
    public sealed class VisitHistory
    {
        public const int DefaultCapacity = 1000;

        private readonly int _capacity;
        private readonly List<VisitRecord> _rows = new List<VisitRecord>();

        public VisitHistory(int capacity = DefaultCapacity) { _capacity = Math.Max(1, capacity); }

        public int Count => _rows.Count;
        /// <summary>Oldest first (ended order). The export reverses this for a newest-first file.</summary>
        public IReadOnlyList<VisitRecord> Rows => _rows;

        public void Record(int visitId, string pilotName, DateTime startedAtUtc, DateTime endedAtUtc, double durationSeconds, int takings, string endedReason)
        {
            if (visitId <= 0) return;
            _rows.Add(new VisitRecord
            {
                VisitId = visitId,
                PilotName = pilotName ?? "",
                StartedAtUtc = startedAtUtc,
                EndedAtUtc = endedAtUtc,
                DurationSeconds = Math.Max(0.0, durationSeconds),
                Takings = Math.Max(0, takings),
                EndedReason = endedReason ?? "",
            });
            while (_rows.Count > _capacity) _rows.RemoveAt(0);
        }

        public void Clear() => _rows.Clear();
    }
}
