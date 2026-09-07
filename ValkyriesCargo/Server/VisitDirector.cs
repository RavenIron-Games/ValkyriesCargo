using System;
using System.Collections.Generic;
using System.Globalization;
using RavenIron.ValkyriesCargo.Client;
using RavenIron.ValkyriesCargo.Config;
using RavenIron.ValkyriesCargo.Core;
using UnityEngine;

namespace RavenIron.ValkyriesCargo.Server
{
    /// <summary>
    /// The server's side of design 3.1: once a second it reads every character ZDO, hands the pure
    /// Scheduler a roll, starts the vanilla event for the pilot it picks, publishes VisitState and
    /// MarketState through ServerSync, mirrors the event's clock (design 3.7), and ends the visit when
    /// the engine ends the event. Owns the one Market (P6 persists it; until then it lives for the session).
    /// Runs on a dedicated server and on a listen host; never on a client. Driven from CargoTick.
    /// </summary>
    public sealed class VisitDirector
    {
        public const float GatherIntervalSeconds = 1f;

        private readonly Scheduler _scheduler;
        private readonly Market _market;
        private readonly VisitSession _session = new VisitSession();
        private readonly System.Random _rng = new System.Random();
        private List<Candidate> _candidates = new List<Candidate>();
        private float _gather;
        private int _lastTakings;
        private string _lastLogged = "";
        private string _pendingEndReason;
        private bool _orphanLogged;
        private int _throws;

        public Scheduler Scheduler => _scheduler;
        public Market Market => _market;
        public VisitSession Session => _session;
        public IReadOnlyList<Candidate> Candidates => _candidates;
        public int LastTakings => _lastTakings;
        public bool DayLengthFromEngine { get; private set; }
        public string Problems { get; private set; } = "";

        public VisitDirector(Catalogue catalogue, MarketRules marketRules, SchedulerRules schedulerRules, double worldTime, string salt)
        {
            _market = new Market(catalogue, marketRules, worldTime, salt);
            _scheduler = new Scheduler(schedulerRules);
        }

        /// <summary>Build from the live config, the engine's day length and the world's uid. Logs its sources.</summary>
        public static VisitDirector Create(ZNet znet)
        {
            double day = MarketRules.DefaultSecondsPerGameDay;
            bool fromEngine = false;
            if (EnvMan.instance != null && EnvMan.instance.m_dayLengthSec > 0) { day = EnvMan.instance.m_dayLengthSec; fromEngine = true; }

            var problems = new List<string>();
            MarketRules mr = ModConfig.BuildMarketRules(day, problems);
            SchedulerRules sr = ModConfig.BuildSchedulerRules(problems);

            string salt = "w0";
            try { salt = "w" + znet.GetWorldUID().ToString("x", CultureInfo.InvariantCulture); }
            catch (Exception ex) { problems.Add("world uid unreadable (" + ex.GetType().Name + "); delivery ids salted 'w0'"); }

            var d = new VisitDirector(ModConfig.CatalogueParsed, mr, sr, znet.GetTimeSeconds(), salt)
            {
                DayLengthFromEngine = fromEngine,
                Problems = string.Join("; ", problems.ToArray()),
            };
            ValkyriesCargo.Log.LogInfo("director up: salt " + salt + ", day " + Wire.Double(day) + " s (" + (fromEngine ? "EnvMan.m_dayLengthSec" : "ASSUMED, no EnvMan") +
                                       "), catalogue " + d._market.Count + " entries, purse " + d._market.Purse + ", roll every " + Wire.Float(sr.IntervalSeconds) +
                                       " s at " + Wire.Float(sr.ChancePercent) + "%, first roll one interval from now; market state is NOT persisted yet (P6)" +
                                       (problems.Count > 0 ? "; problems: " + d.Problems : ""));
            return d;
        }

        /// <summary>Once a second: mirror a running visit, or roll for a new one.</summary>
        public void Tick(float dt, double now, double worldTime)
        {
            _gather += dt;
            if (_gather < GatherIntervalSeconds) return;
            _gather = 0f;
            try
            {
                ModConfig.FillMarketRules(_market.Rules, null);
                ModConfig.FillSchedulerRules(_scheduler.Rules, null);

                RandEventSystem res = RandEventSystem.instance;
                if (res == null) return;
                RandomEvent current = res.GetCurrentRandomEvent();
                bool ours = current != null && current.m_name == CargoEvent.Name;

                if (_session.Active)
                {
                    if (!ours)
                    {
                        End(_pendingEndReason ?? (current == null ? "timer" : "displaced by event '" + current.m_name + "'"));
                        return;
                    }
                    string republish = _session.Sync(worldTime, CargoEvent.Remaining(res));
                    if (republish != null) Publish(republish);
                    if (_session.Clock.OneMinuteWarningDue(worldTime))
                        ValkyriesCargo.Log.LogInfo("visit #" + _session.VisitId + ": one minute left");   // P5: vc_say the line
                    return;
                }

                if (ours)
                {
                    // Our event is running but we hold no session: the server restarted mid-visit (design 3.7,
                    // P6 resumes it from the sidecar) or it was left behind. Until then: end it, once, loudly.
                    if (!_orphanLogged) { _orphanLogged = true; ValkyriesCargo.Log.LogWarning("event '" + CargoEvent.Name + "' is running with no visit session (restart mid-visit?); ending it"); }
                    res.ResetRandomEvent();
                    return;
                }

                _candidates = Gather();
                Decision d = _scheduler.Tick(now, _candidates, current != null, EnvMan.IsDay(), () => _rng.NextDouble());
                if (d == null) return;
                LogDecision(d.Reason);
                if (d.Visit) Begin(d.Pilot, worldTime);
            }
            catch (Exception ex)
            {
                if (_throws++ < 3) ValkyriesCargo.Log.LogError("director tick threw: " + ex);
            }
        }

        /// <summary>`cargo visit`: force a roll for one player, cooldowns ignored (Scheduler.Force). Returns the decision in words.</summary>
        public string Force(long uid, double now, double worldTime)
        {
            RandEventSystem res = RandEventSystem.instance;
            if (res == null) return "no RandEventSystem yet";
            if (_session.Active) return "a visit is already running (#" + _session.VisitId + ", " + _session.Clock.FormatRemaining(worldTime) + " left)";
            _candidates = Gather();
            RandomEvent current = res.GetCurrentRandomEvent();
            Decision d = _scheduler.Force(now, _candidates, current != null, EnvMan.IsDay(), uid);
            LogDecision(d.Reason);
            if (d.Visit) Begin(d.Pilot, worldTime);
            return d.Reason;
        }

        /// <summary>`cargo dismiss` and, later, vc_dismiss: end the event now; the next tick ends the visit with this reason.</summary>
        public string Dismiss(string reason)
        {
            if (!_session.Active) return "no visit to dismiss";
            _pendingEndReason = reason;
            RandEventSystem res = RandEventSystem.instance;
            if (res != null) res.ResetRandomEvent();
            return "visit #" + _session.VisitId + " dismissed (" + reason + ")";
        }

        public Candidate FindByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (Candidate c in _candidates)
                if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) return c;
            foreach (Candidate c in Gather())
                if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) return c;
            return null;
        }

        private void Begin(Candidate pilot, double worldTime)
        {
            int visitId = _market.NextVisitId;
            _market.StartVisit(visitId, worldTime, _lastTakings);
            if (!CargoEvent.Start(RandEventSystem.instance, new Vector3(pilot.X, pilot.Y, pilot.Z)))
            {
                ValkyriesCargo.Log.LogError("visit #" + visitId + ": the event did not start; is '" + CargoEvent.Name + "' registered? (`cargo status` says)");
                return;
            }
            int seed = _rng.Next(1, int.MaxValue);
            float lifespan = CargoEvent.Lifespan();
            string state = _session.Begin(visitId, pilot.Uid, pilot.Name, pilot.X, pilot.Y, pilot.Z, worldTime, lifespan, _market.Purse, seed);
            _pendingEndReason = null;
            _orphanLogged = false;
            Publish(state);
            PublishMarket();
            ValkyriesCargo.Log.LogInfo("visit #" + visitId + " begins: pilot " + pilot + " (uid " + Wire.Long(pilot.Uid) + ") at (" + Wire.Float(pilot.X) + ", " +
                                       Wire.Float(pilot.Z) + "), " + Wire.Float(lifespan) + " s, purse " + _market.Purse + ", seed " + seed);
        }

        private void End(string reason)
        {
            _lastTakings = _market.Takings;
            int id = _session.VisitId;
            Publish(_session.End(reason));
            _pendingEndReason = null;
            ValkyriesCargo.Log.LogInfo("visit #" + id + " ended: " + reason + "; takings " + _lastTakings + " coins, purse " + _market.Purse +
                                       ", " + _session.Republishes + " clock republish(es)");
        }

        private static void Publish(string visitState) { ModConfig.VisitState.AssignLocalValue(visitState ?? ""); }

        /// <summary>The whole market, after a visit starts and after every accepted deal (P6).</summary>
        public void PublishMarket() { ModConfig.MarketState.AssignLocalValue(_market.Snapshot().Encode()); }

        private void LogDecision(string reason)
        {
            if (reason == _lastLogged) return;
            _lastLogged = reason;
            ValkyriesCargo.Log.LogInfo("roll: " + reason);
        }

        /// <summary>Every online character as the scheduler sees it, from the character ZDOs the engine keeps for ready peers.</summary>
        public static List<Candidate> Gather()
        {
            var list = new List<Candidate>();
            ZNet znet = ZNet.instance;
            if (znet == null) return list;
            foreach (ZDO zdo in znet.GetAllCharacterZDOS())
            {
                if (zdo == null) continue;
                Vector3 p = zdo.GetPosition();
                list.Add(new Candidate
                {
                    Uid = zdo.GetOwner(),
                    Name = zdo.GetString(ZDOVars.s_playerName, ""),
                    X = p.x, Y = p.y, Z = p.z,
                    BaseValue = zdo.GetInt(ZDOVars.s_baseValue, 0),
                    Rested = zdo.GetBool(ComfortReporter.RestedHash, false),
                    Comfort = zdo.GetInt(ComfortReporter.ComfortHash, 0),
                    Alive = !zdo.GetBool(ZDOVars.s_dead, false),
                    Ready = true,
                });
            }
            return list;
        }

        /// <summary>One candidate in words for `cargo status`.</summary>
        public string Describe(Candidate c, double now)
        {
            return c + " (uid " + Wire.Long(c.Uid) + "): rested=" + (c.Rested ? "yes" : "no") + " comfort=" + c.Comfort + " base=" + c.BaseValue +
                   " y=" + Wire.Float((float)Math.Round(c.Y)) + (c.Alive ? "" : " DEAD") +
                   (_scheduler.OnPlayerCooldown(c.Uid, now) ? " on cooldown" : "") + (_scheduler.NearBaseCooldown(c.X, c.Z, now) ? " near a base on cooldown" : "");
        }
    }
}
