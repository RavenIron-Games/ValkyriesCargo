using System;
using System.Collections.Generic;

namespace RavenIron.ValkyriesCargo.Core
{
    /// <summary>What the server knows about one online player, read from their character ZDO (design 3.1).</summary>
    public sealed class Candidate
    {
        public long Uid;
        public string Name = "";
        public float X, Y, Z;
        public int BaseValue;
        public bool Rested;
        public int Comfort;
        public bool Alive = true;
        public bool Ready = true;

        public override string ToString() => (Name.Length > 0 ? Name : "uid " + Wire.Long(Uid));
    }

    /// <summary>The scheduling knobs, bound from Server.* on the game side. Times in REAL seconds.</summary>
    public sealed class SchedulerRules
    {
        public bool Enabled = true;
        public bool RequireRested = true;
        public bool DaytimeOnly = true;
        public int MinComfort = 4;
        public int MinBaseValue = 1;
        public float IntervalSeconds = 25f * 60f;
        public float ChancePercent = 25f;
        public float PlayerCooldownSeconds = 60f * 60f;
        public float CooldownRadius = 60f;
        public float TownRadius = 40f;
        /// <summary>Dungeons and the like sit at y ≈ 5000; a player at or above this is not on the surface.</summary>
        public const float DungeonY = 3000f;

        public static SchedulerRules Default => new SchedulerRules();

        /// <summary>Clamp every number into its range; report what was clamped. Idempotent.</summary>
        public void Sanitize(List<string> problems)
        {
            if (MinComfort < 0) { MinComfort = 0; Wire.Report(problems, "MinComfort clamped to 0"); }
            if (MinBaseValue < 0) { MinBaseValue = 0; Wire.Report(problems, "MinBaseValue clamped to 0"); }
            IntervalSeconds = Clamp(IntervalSeconds, 10f, 86400f, "IntervalSeconds", problems);
            ChancePercent = Clamp(ChancePercent, 0f, 100f, "ChancePercent", problems);
            PlayerCooldownSeconds = Clamp(PlayerCooldownSeconds, 0f, 604800f, "PlayerCooldownSeconds", problems);
            CooldownRadius = Clamp(CooldownRadius, 0f, 2000f, "CooldownRadius", problems);
            TownRadius = Clamp(TownRadius, 0f, 2000f, "TownRadius", problems);
        }

        private static float Clamp(float v, float lo, float hi, string name, List<string> problems)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) { Wire.Report(problems, name + " was not a number; using " + Wire.Float(lo)); return lo; }
            if (v < lo) { Wire.Report(problems, name + " clamped up to " + Wire.Float(lo)); return lo; }
            if (v > hi) { Wire.Report(problems, name + " clamped down to " + Wire.Float(hi)); return hi; }
            return v;
        }
    }

    /// <summary>The outcome of one roll, with the reason in words for `cargo status`.</summary>
    public sealed class Decision
    {
        public Candidate Pilot;          // null = no visit this roll
        public string Reason = "";
        public int Eligible;
        public int Tickets;
        public bool Visit => Pilot != null;
    }

    /// <summary>
    /// Decides when a visit happens and who gets it (design 3.1). PURE: time, players, the world flags
    /// and the random number are all passed in, so the harness can drive every branch. The last
    /// decision is kept in words because "no visit" has eight causes with one symptom.
    /// The random source must be half-open [0, 1): Random.Range(0f, 1f) or System.Random, never
    /// UnityEngine.Random.value (inclusive of 1); a 1.0 is clamped anyway so chance 100 always visits.
    /// </summary>
    public sealed class Scheduler
    {
        private readonly SchedulerRules _rules;
        private readonly Dictionary<long, double> _playerCooldownUntil = new Dictionary<long, double>();
        private readonly List<BaseCooldown> _baseCooldowns = new List<BaseCooldown>();
        private double _nextRollAt = double.NegativeInfinity;

        private sealed class BaseCooldown { public float X, Z; public double Until; }

        // The buckets a candidate can fall into, in the order `cargo status` lists them.
        private const int NotRested = 0, LowComfort = 1, LowBase = 2, Dungeon = 3, OnCooldown = 4, NearCooldown = 5, Dead = 6, NotReady = 7, Buckets = 8;

        /// <summary>The rules are sanitized IN PLACE (the game side calls Sanitize(problems) first if it wants the report).</summary>
        public Scheduler(SchedulerRules rules)
        {
            _rules = rules ?? SchedulerRules.Default;
            _rules.Sanitize(null);
        }

        public SchedulerRules Rules => _rules;
        public string LastDecision { get; private set; } = "no roll yet";
        public double NextRollAt => _nextRollAt;

        /// <summary>The first roll happens one interval after this call (the world just loaded; nobody is at a base yet).</summary>
        public void Arm(double now) { _nextRollAt = now + _rules.IntervalSeconds; }

        /// <summary>
        /// Called every tick with the real clock. Returns a Decision only when a roll happened (the
        /// interval elapsed); null means "not yet", so the caller does nothing.
        /// </summary>
        public Decision Tick(double now, IList<Candidate> players, bool eventActive, bool isDay, Func<double> rng)
        {
            if (double.IsNegativeInfinity(_nextRollAt)) Arm(now);
            if (now < _nextRollAt) return null;
            _nextRollAt = now + _rules.IntervalSeconds;
            return Roll(now, players, eventActive, isDay, rng, forced: null);
        }

        /// <summary>
        /// An admin's `cargo visit`: skip the interval, the chance and the cooldowns for one named player
        /// (the admin asked); rested, comfort, baseValue, alive, ready and the dungeon bound still apply,
        /// and so do the holds. The counters still count everyone online.
        /// </summary>
        public Decision Force(double now, IList<Candidate> players, bool eventActive, bool isDay, long uid)
        {
            Candidate forced = null;
            if (players != null) foreach (Candidate c in players) if (c != null && c.Uid == uid) { forced = c; break; }
            if (forced == null)
            {
                var d = new Decision { Reason = "forced: uid " + Wire.Long(uid) + " is not online" };
                LastDecision = d.Reason;
                return d;
            }
            return Roll(now, players, eventActive, isDay, () => 0.0, forced);
        }

        private Decision Roll(double now, IList<Candidate> players, bool eventActive, bool isDay, Func<double> rng, Candidate forced)
        {
            var d = new Decision();
            Prune(now);

            if (!_rules.Enabled) return Finish(d, "held: Server.Enabled is false");
            if (eventActive) return Finish(d, "held: a random event is active (a raid, a storm, or a visit)");
            if (_rules.DaytimeOnly && !isDay) return Finish(d, "held: night, and DaytimeOnly is on");

            var eligible = new List<Candidate>();
            var counts = new int[Buckets];
            int forcedBucket = -1;
            bool anyone = false;
            if (players != null)
            {
                foreach (Candidate c in players)
                {
                    if (c == null) continue;
                    anyone = true;
                    int bucket = Bucket(c, now, skipCooldowns: c == forced);
                    if (bucket < 0) { eligible.Add(c); continue; }
                    counts[bucket]++;
                    if (c == forced) forcedBucket = bucket;
                }
            }
            d.Eligible = eligible.Count;

            if (forced != null && forcedBucket >= 0)
                return Finish(d, "forced: " + forced + " not eligible: " + BucketName(forcedBucket) + "; online: " + Summary(counts, anyone));
            if (eligible.Count == 0)
                return Finish(d, "no eligible player: " + Summary(counts, anyone));

            List<Candidate> tickets = Tickets(eligible, _rules.TownRadius);
            d.Tickets = tickets.Count;

            if (forced == null)
            {
                double roll = rng != null ? rng() : 1.0;
                if (double.IsNaN(roll) || roll < 0.0) roll = 0.0;
                if (roll >= 1.0) roll = 0.999999;                      // an inclusive source cannot starve chance 100
                if (roll * 100.0 >= _rules.ChancePercent)
                    return Finish(d, "rolled " + roll.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) +
                                     " >= " + Wire.Float(_rules.ChancePercent / 100f) + ": no visit (" + Wire.Int(d.Eligible) + " eligible, " + Wire.Int(d.Tickets) + " ticket(s))");
                int pick = (int)Math.Floor(Math.Min(0.999999, Math.Max(0.0, rng != null ? rng() : 0.0)) * tickets.Count);
                d.Pilot = tickets[pick];
            }
            else d.Pilot = forced;

            StampCooldown(d.Pilot.Uid, d.Pilot.X, d.Pilot.Z, now);
            return Finish(d, (forced != null ? "forced visit: " : "visit: ") + d.Pilot + " at (" +
                             Wire.Float(d.Pilot.X) + ", " + Wire.Float(d.Pilot.Z) + "); " + Wire.Int(d.Eligible) + " eligible, " + Wire.Int(d.Tickets) + " ticket(s)");
        }

        /// <summary>The first gate a candidate fails, or -1 when eligible. Each candidate lands in exactly one bucket.</summary>
        private int Bucket(Candidate c, double now, bool skipCooldowns)
        {
            if (!c.Ready) return NotReady;
            if (!c.Alive) return Dead;
            if (_rules.RequireRested && !c.Rested) return NotRested;
            if (c.Comfort < _rules.MinComfort) return LowComfort;
            if (c.BaseValue < _rules.MinBaseValue) return LowBase;
            if (c.Y >= SchedulerRules.DungeonY) return Dungeon;
            if (!skipCooldowns && OnPlayerCooldown(c.Uid, now)) return OnCooldown;
            if (!skipCooldowns && NearBaseCooldown(c.X, c.Z, now)) return NearCooldown;
            return -1;
        }

        private string BucketName(int bucket)
        {
            switch (bucket)
            {
                case NotRested: return "not rested";
                case LowComfort: return "comfort < " + Wire.Int(_rules.MinComfort);
                case LowBase: return "baseValue < " + Wire.Int(_rules.MinBaseValue);
                case Dungeon: return "in a dungeon";
                case OnCooldown: return "on cooldown";
                case NearCooldown: return "near a base on cooldown";
                case Dead: return "dead";
                default: return "not ready";
            }
        }

        private string Summary(int[] counts, bool anyone)
        {
            var parts = new List<string>();
            for (int b = 0; b < Buckets; b++) if (counts[b] > 0) parts.Add(Wire.Int(counts[b]) + " " + BucketName(b));
            return parts.Count > 0 ? string.Join(", ", parts.ToArray()) : (anyone ? "everyone eligible" : "nobody online");
        }

        private Decision Finish(Decision d, string reason) { d.Reason = reason; LastDecision = reason; return d; }

        /// <summary>Players within TownRadius of each other are one ticket, so a town is one target, not five. Greedy, order-stable.</summary>
        public static List<Candidate> Tickets(IList<Candidate> eligible, float townRadius)
        {
            var tickets = new List<Candidate>();
            float r2 = townRadius * townRadius;
            foreach (Candidate c in eligible)
            {
                bool covered = false;
                foreach (Candidate t in tickets)
                {
                    float dx = c.X - t.X, dz = c.Z - t.Z;
                    if (dx * dx + dz * dz <= r2) { covered = true; break; }
                }
                if (!covered) tickets.Add(c);
            }
            return tickets;
        }

        // ---- cooldowns ----------------------------------------------------------------------

        /// <summary>Stamped at dispatch, not at success, so a failed flight cannot be farmed.</summary>
        public void StampCooldown(long uid, float x, float z, double now)
        {
            _playerCooldownUntil[uid] = now + _rules.PlayerCooldownSeconds;
            _baseCooldowns.Add(new BaseCooldown { X = x, Z = z, Until = now + _rules.PlayerCooldownSeconds });
        }

        public bool OnPlayerCooldown(long uid, double now)
        {
            double until;
            return _playerCooldownUntil.TryGetValue(uid, out until) && now < until;
        }

        public bool NearBaseCooldown(float x, float z, double now)
        {
            float r2 = _rules.CooldownRadius * _rules.CooldownRadius;
            foreach (BaseCooldown b in _baseCooldowns)
            {
                if (now >= b.Until) continue;
                float dx = x - b.X, dz = z - b.Z;
                if (dx * dx + dz * dz <= r2) return true;
            }
            return false;
        }

        public void Prune(double now)
        {
            var expired = new List<long>();
            foreach (KeyValuePair<long, double> kv in _playerCooldownUntil) if (now >= kv.Value) expired.Add(kv.Key);
            foreach (long uid in expired) _playerCooldownUntil.Remove(uid);
            _baseCooldowns.RemoveAll(b => now >= b.Until);
        }

        /// <summary>
        /// Rows for the sidecar: "cool\tuid\tremaining" and "coolbase\tx\tz\tremaining", remaining in SECONDS
        /// from `now`, never an absolute stamp: the caller's clock is process uptime, which restarts at zero,
        /// so an absolute stamp would put the whole world on cooldown after every restart. Expired rows are not written.
        /// </summary>
        public string EncodeCooldowns(double now)
        {
            var lines = new List<string>();
            foreach (KeyValuePair<long, double> kv in _playerCooldownUntil)
                if (kv.Value > now) lines.Add("cool\t" + Wire.Long(kv.Key) + "\t" + Wire.Double(kv.Value - now));
            foreach (BaseCooldown b in _baseCooldowns)
                if (b.Until > now) lines.Add("coolbase\t" + Wire.Float(b.X) + "\t" + Wire.Float(b.Z) + "\t" + Wire.Double(b.Until - now));
            return string.Join("\n", lines.ToArray());
        }

        /// <summary>Replace the cooldowns with the saved rows, rebased to `now`. Bad rows are reported and skipped. Never throws.</summary>
        public void ApplyCooldowns(string rows, double now, List<string> problems)
        {
            if (string.IsNullOrEmpty(rows)) return;
            _playerCooldownUntil.Clear();
            _baseCooldowns.Clear();
            foreach (string raw in rows.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0) continue;
                string[] f = line.Split('\t');
                long uid; double remaining; float x, z;
                if (f[0] == "cool" && f.Length == 3 && Wire.TryLong(f[1], out uid) && Wire.TryDouble(f[2], out remaining) && Finite(remaining))
                { if (remaining > 0) _playerCooldownUntil[uid] = now + remaining; continue; }
                if (f[0] == "coolbase" && f.Length == 4 && Wire.TryFloat(f[1], out x) && Wire.TryFloat(f[2], out z) && Wire.TryDouble(f[3], out remaining) && Finite(remaining))
                { if (remaining > 0) _baseCooldowns.Add(new BaseCooldown { X = x, Z = z, Until = now + remaining }); continue; }
                Wire.Report(problems, "cooldown row ignored: " + line);
            }
        }

        private static bool Finite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);
    }
}
