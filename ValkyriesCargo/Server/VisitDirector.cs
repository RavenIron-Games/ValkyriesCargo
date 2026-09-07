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
    /// The server's side of design 3.1, 3.4 and 3.5: once a second it reads every character ZDO, hands the
    /// pure Scheduler a roll, starts the vanilla event for the pilot it picks, publishes VisitState and
    /// MarketState through ServerSync, mirrors the event's clock (design 3.7), settles deals against the
    /// one Market, keeps the owed ledger, and ends the visit when the engine ends the event. Everything
    /// that must survive a restart (stock, purse, visit numbers, cooldowns, the running session, owed
    /// deliveries) lives in the world sidecar through MarketStore: loaded when the director is built, saved
    /// on a cadence while dirty and flushed when the session ends. Vanilla restores a running random event
    /// on load, so a saved session whose event is back is RESUMED, not ended.
    /// Runs on a dedicated server and on a listen host; never on a client. Driven from CargoTick.
    /// </summary>
    public sealed class VisitDirector
    {
        public const float GatherIntervalSeconds = 1f;
        public const float SaveCadenceSeconds = 30f;
        public const float AdoptWindowSeconds = 15f;
        /// <summary>How often the BarrkBOT export refreshes (Server/BarrkBotExport.cs): independent of
        /// _dirty, because generated_at must keep moving even on a quiet server, or BarrkBOT starts
        /// calling perfectly current numbers stale past its own 60-minute threshold.</summary>
        public const float ExportCadenceSeconds = 60f;

        private readonly Scheduler _scheduler;
        private readonly Market _market;
        private readonly VisitSession _session = new VisitSession();
        private readonly OwedLedger _ledger = new OwedLedger();
        private readonly TraderLedger _traders = new TraderLedger();
        private readonly VisitHistory _visitHistory = new VisitHistory();
        private readonly System.Random _rng = new System.Random();
        private List<Candidate> _candidates = new List<Candidate>();
        private float _gather;
        private float _sinceSave;
        private float _sinceExport;
        private int _lastTakings;
        /// <summary>
        /// The GROSS coins the last visit took in, which is what the purse carry is measured on -- not
        /// `_lastTakings`, which is the net and is what the log line and the visit history quote. A visit
        /// where players sold him as much as they bought has a net of zero and a gross worth carrying.
        /// </summary>
        private int _lastCoined;
        private string _lastLogged = "";
        private string _pendingEndReason;
        private string _pendingSessionRow;
        private float _adoptWaited;
        private bool _orphanLogged;
        private bool _dirty;
        private int _throws;
        private int _mirrorThrows;

        public Scheduler Scheduler => _scheduler;
        public Market Market => _market;
        public VisitSession Session => _session;
        public OwedLedger Ledger => _ledger;
        /// <summary>Per-player trade totals this session, for barrkbot_cargo_traders.json (BARRKBOT_CONTRACT.md). New: not persisted, not in the sidecar.</summary>
        public TraderLedger Traders => _traders;
        /// <summary>Visits that have ended this session, for barrkbot_cargo_visits.json. New: not persisted, not in the sidecar.</summary>
        public VisitHistory VisitHistory => _visitHistory;
        /// <summary>When this VisitDirector came up -- the "session" barrkbot_cargo_traders.json and barrkbot_cargo_visits.json reset against.</summary>
        public DateTime SessionStartedUtc { get; }
        public MarketStore Store { get; private set; }
        public IReadOnlyList<Candidate> Candidates => _candidates;
        public int LastTakings => _lastTakings;
        public bool DayLengthFromEngine { get; private set; }
        public string Problems { get; private set; } = "";
        public bool Dirty => _dirty;
        public int Loaded { get; private set; }

        public VisitDirector(Catalogue catalogue, MarketRules marketRules, SchedulerRules schedulerRules, double worldTime, string salt)
        {
            _market = new Market(catalogue, marketRules, worldTime, salt);
            _scheduler = new Scheduler(schedulerRules);
            SessionStartedUtc = DateTime.UtcNow;
        }

        /// <summary>Build from the live config, the engine's day length, the world's uid and the sidecar. Logs its sources.</summary>
        public static VisitDirector Create(ZNet znet, double now)
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

            var d = new VisitDirector(ModConfig.CatalogueParsed, mr, sr, znet.GetTimeSeconds(), salt) { DayLengthFromEngine = fromEngine };
            d.Store = MarketStore.Resolve(znet);
            d.LoadSidecar(now, problems);
            d.Problems = string.Join("; ", problems.ToArray());
            // A fresh world gets its file at once, so the path is proven on the desk and not on the first deal.
            if (d.Store.Path != null && d._pendingSessionRow == null) d.Flush(d.Loaded > 0 ? "boot" : "first write", force: true);

            // P5, design 3.7: the merchant is the persistent half of the pair, so a server that stopped
            // mid-visit brings him back with the world. Put away anyone who is not the visit we just
            // adopted, and clear any restored carry link -- a ZDOID does not survive a world read.
            // The id to KEEP. A restored row is adopted later, on a tick, once the engine brings its
            // event back - so at boot `_session` is not Active yet and its VisitId is 0. Sweeping on
            // that 0 would destroy the merchant of the visit about to resume.
            int keep = d._session != null && d._session.Active ? d._session.VisitId
                                                               : VisitSession.VisitIdOf(d._pendingSessionRow);
            string swept = Spawner.Sweep(keep);
            if (swept != null) ValkyriesCargo.Log.LogInfo(swept);

            ValkyriesCargo.Log.LogInfo("director up: salt " + salt + ", day " + Wire.Double(day) + " s (" + (fromEngine ? "EnvMan.m_dayLengthSec" : "ASSUMED, no EnvMan") +
                                       "), catalogue " + d._market.Count + " entries, purse " + d._market.Purse + ", next visit #" + d._market.NextVisitId +
                                       ", roll every " + Wire.Float(sr.IntervalSeconds) + " s at " + Wire.Float(sr.ChancePercent) + "%, first roll one interval from now; sidecar " +
                                       (d.Store.Path != null ? System.IO.Path.GetFileName(d.Store.Path) + " (" + (d.Loaded > 0 ? d.Loaded + " rows loaded" : "fresh world") +
                                       (d._pendingSessionRow != null ? ", a saved visit waits for its event" : "") + ")" : "NOT AVAILABLE: " + d.Store.Detail) +
                                       (problems.Count > 0 ? "; problems: " + d.Problems : ""));
            return d;
        }

        private void LoadSidecar(double now, List<string> problems)
        {
            string text = Store.Load();
            if (text == null) return;
            var before = problems.Count;
            Sidecar sc = Sidecar.Split(text, problems);
            if (!sc.FormatMatches)
            {
                ValkyriesCargo.Log.LogError("sidecar: format " + sc.Format + " is not " + Sidecar.FormatVersion + "; keeping the file as .corrupt and starting fresh");
                Store.Quarantine();
                return;
            }
            int rows = 0;
            if (sc.MarketRows.Length > 0) { _market.ApplyState(sc.MarketRows, problems); rows += sc.MarketRows.Split('\n').Length; }
            if (sc.CooldownRows.Length > 0) { _scheduler.ApplyCooldowns(sc.CooldownRows, now, problems); rows += sc.CooldownRows.Split('\n').Length; }
            rows += _ledger.ApplyRows(sc.OwedRows, problems);
            if (sc.SessionRow.Length > 0) { _pendingSessionRow = sc.SessionRow; rows++; }
            Loaded = rows;
            if (rows == 0 && problems.Count > before)
            {
                ValkyriesCargo.Log.LogError("sidecar: content but not one readable row; keeping it as .corrupt and starting fresh");
                Store.Quarantine();
            }
        }

        /// <summary>Once a second: adopt a saved visit, mirror a running one, or roll for a new one; save on cadence.</summary>
        public void Tick(float dt, double now, double worldTime)
        {
            _gather += dt;
            _sinceSave += dt;
            _sinceExport += dt;
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

                if (_pendingSessionRow != null) Adopt(ours, worldTime, dt);

                if (_session.Active)
                {
                    if (!ours)
                    {
                        End(_pendingEndReason ?? (current == null ? "timer" : "displaced by event '" + current.m_name + "'"), worldTime);
                    }
                    else
                    {
                        string republish = _session.Sync(worldTime, CargoEvent.Remaining(res));
                        if (republish != null) Publish(republish);

                        // The server never flies anything: it watches the pilot's `VCargo_dropped` flag and
                        // moves the visit's phase and drop point to follow (P4).
                        string flightState;
                        string note = Spawner.Tick(_session, GatherIntervalSeconds, out flightState);
                        if (flightState != null) { Publish(flightState); _dirty = true; }
                        if (note != null) ValkyriesCargo.Log.LogInfo(note);

                        if (_session.Clock.OneMinuteWarningDue(worldTime))
                            ValkyriesCargo.Log.LogInfo("visit #" + _session.VisitId + ": one minute left");   // P5: VCargo_say the line
                    }
                }
                else if (ours && _pendingSessionRow == null)
                {
                    // Our event runs with no session and no saved row to adopt it from: a leftover. End it, once, loudly.
                    if (!_orphanLogged) { _orphanLogged = true; ValkyriesCargo.Log.LogWarning("event '" + CargoEvent.Name + "' is running with no visit session and no saved session row; ending it"); }
                    res.ResetRandomEvent();
                }
                else if (_pendingSessionRow == null)
                {
                    _candidates = Gather();
                    Decision d = _scheduler.Tick(now, _candidates, current != null, EnvMan.IsDay(), () => _rng.NextDouble());
                    if (d != null)
                    {
                        LogDecision(d.Reason);
                        if (d.Visit) Begin(d.Pilot, worldTime);
                    }
                }

                if (_dirty && _sinceSave >= SaveCadenceSeconds) Flush("cadence");

                // BarrkBOT (BARRKBOT_CONTRACT.md). Decision 4: the sidecar is the source of truth and the
                // export a mirror of it, so Flush runs first, to completion, and only a successful save
                // lets the mirror run at all -- SidecarThenMirror is what proves that ordering off-game.
                // Independent of _dirty on purpose: with nothing changed Flush is a cheap no-op, but the
                // export still needs to move generated_at, or a perfectly current file starts reading as
                // stale to BarrkBOT past its own 60-minute threshold.
                if (_sinceExport >= ExportCadenceSeconds)
                {
                    _sinceExport = 0f;
                    SidecarThenMirror.Run(
                        () => Flush("barrkbot export"),
                        () => BarrkBotExport.Write(this),
                        mex => { if (_mirrorThrows++ < 3) ValkyriesCargo.Log.LogError("barrkbot export mirror threw (non-fatal, the sidecar is unaffected): " + mex); });
                }
            }
            catch (Exception ex)
            {
                if (_throws++ < 3) ValkyriesCargo.Log.LogError("director tick threw: " + ex);
            }
        }

        /// <summary>A saved session: adopt it if the engine brought its event back within the window, else let it go.</summary>
        private void Adopt(bool eventIsOurs, double worldTime, float dt)
        {
            if (eventIsOurs)
            {
                var problems = new List<string>();
                string state = _session.Resume(_pendingSessionRow, worldTime, problems);
                _pendingSessionRow = null;
                if (state == null)
                {
                    ValkyriesCargo.Log.LogWarning("saved session row did not parse (" + string.Join("; ", problems.ToArray()) + "); ending the restored event");
                    RandEventSystem.instance.ResetRandomEvent();
                    SweepAfterGivingUp("the saved row did not parse");
                    return;
                }
                Publish(state);
                PublishMarket();
                _dirty = true;
                ValkyriesCargo.Log.LogInfo("visit #" + _session.VisitId + " RESUMED after a restart: pilot " + _session.PilotName + ", " +
                                           _session.Clock.FormatRemaining(worldTime) + " left by the saved clock (the event's own timer corrects it next tick)");
                return;
            }
            _adoptWaited += Mathf.Max(dt, GatherIntervalSeconds);
            if (_adoptWaited < AdoptWindowSeconds) return;
            ValkyriesCargo.Log.LogInfo("saved session row not adopted: the engine did not restore event '" + CargoEvent.Name + "' within " + AdoptWindowSeconds + " s; that visit ended with the restart");
            _pendingSessionRow = null;
            _dirty = true;
            SweepAfterGivingUp("the engine never restored the event");
        }

        /// <summary>
        /// The boot sweep SPARED a merchant because a saved visit was waiting to be adopted. Adoption
        /// has now failed, so that visit is over and he is stranded - persistent, in the world save,
        /// with nothing left to belong to. This is the second half of the boot sweep and it only runs
        /// on the path where the first half deliberately held its hand.
        /// </summary>
        private void SweepAfterGivingUp(string why)
        {
            string swept = Spawner.Sweep(0);
            if (swept != null) ValkyriesCargo.Log.LogInfo(swept + " (" + why + ")");
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

        /// <summary>`cargo dismiss` and VCargo_dismiss: end the event now; the next tick ends the visit with this reason.</summary>
        public string Dismiss(string reason)
        {
            if (!_session.Active) return "no visit to dismiss";
            _pendingEndReason = reason;
            RandEventSystem res = RandEventSystem.instance;
            if (res != null) res.ResetRandomEvent();
            return "visit #" + _session.VisitId + " dismissed (" + reason + ")";
        }

        /// <summary>`cargo reset`: forget every cooldown. The market is not touched.</summary>
        public string ResetCooldowns()
        {
            _scheduler.ApplyCooldowns("cool\t0\t0", 0, null);   // a row with no time left: replaces everything with nothing
            _dirty = true;
            return "cooldowns cleared";
        }

        // ---- deals ----------------------------------------------------------------------------------

        /// <summary>
        /// The wire's entry: settle against the market at the current prices, remember an accepted result as
        /// owed to this player until acked, publish the market. `visit_over` when no visit is running.
        /// </summary>
        public DealResult Settle(Deal deal, string playerKey, string playerName)
        {
            if (deal == null) return DealResult.Refuse(0, DealReason.Malformed);
            if (!_session.Active) return DealResult.Refuse(deal.Nonce, DealReason.VisitOver);
            DealResult r = _market.Settle(deal, deal.CoinsOffered, ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : 0);
            if (r.Ok)
            {
                _ledger.Add(playerKey, r);
                _traders.Record(playerKey, playerName, r);
                _dirty = true;
                PublishMarket();
                ValkyriesCargo.Log.LogInfo("deal " + r.DeliveryId + " with " + playerName + ": " + Describe(r) + "; purse " + _market.Purse);
            }
            else if (r.Reason != DealReason.PriceChanged)
            {
                ValkyriesCargo.Log.LogInfo("deal refused for " + playerName + ": " + r.Reason);
            }
            return r;
        }

        public void Ack(string playerKey, string deliveryId)
        {
            if (_ledger.Ack(playerKey, deliveryId)) _dirty = true;
        }

        public List<DealResult> Owed(string playerKey) => _ledger.For(playerKey);

        public Candidate FindByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (Candidate c in _candidates)
                if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) return c;
            foreach (Candidate c in Gather())
                if (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) return c;
            return null;
        }

        // ---- the visit -------------------------------------------------------------------------------

        private void Begin(Candidate pilot, double worldTime)
        {
            int visitId = _market.NextVisitId;
            _market.StartVisit(visitId, worldTime, _lastCoined);
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
            _dirty = true;
            Publish(state);
            PublishMarket();
            ValkyriesCargo.Log.LogInfo("visit #" + visitId + " begins: pilot " + pilot + " (uid " + Wire.Long(pilot.Uid) + ") at (" + Wire.Float(pilot.X) + ", " +
                                       Wire.Float(pilot.Z) + "), " + Wire.Float(lifespan) + " s, purse " + _market.Purse + ", seed " + seed);

            // P4 (design 3.2): the bird and the merchant are authored here, owned by the pilot, who
            // instantiates and flies them. The event and the clock have already started -- decided
            // 2026-09-06: the visit starts at dispatch, as P3 built it, not at the drop as 3.2 wrote
            // it, so the banner cues the player to look up while the bird is still inbound.
            string flight = Spawner.Author(visitId, pilot.Uid, pilot.X, pilot.Y, pilot.Z, seed);
            ValkyriesCargo.Log.LogInfo("visit #" + visitId + ": " + (flight ?? "NO FLIGHT AND NO MERCHANT: " + Spawner.LastProblem));

            Flush("visit start");
        }

        private void End(string reason, double worldTime)
        {
            _lastTakings = _market.Takings;
            _lastCoined = _market.Coined;
            int id = _session.VisitId;
            string pilot = _session.PilotName;
            // The visit's duration comes from the EVENT CLOCK, not from world time. World time is
            // `ZNet.GetTimeSeconds()`, and `EnvMan.SkipToMorning` drives it forward to the next morning
            // when players sleep -- so a 300 s visit slept through would read as a thousand and more, and
            // the `started_at` derived from it would land before the visit began. It also keeps running
            // while the event is paused with nobody within 96 m, which the clock deliberately does not.
            // `Sync` retargets the clock's end from the event's own remaining seconds, so this is elapsed
            // event time: 300 at the timer, less on a dismiss. Found by review, 2026-09-07.
            double duration = _session.Clock != null
                ? Math.Max(0.0, CargoEvent.Lifespan() - _session.Clock.Remaining(worldTime))
                : 0.0;
            DateTime endedUtc = DateTime.UtcNow;
            _visitHistory.Record(id, pilot, endedUtc.AddSeconds(-duration), endedUtc, duration, _lastTakings, reason);
            Spawner.Clear();          // a bird still in the air when the visit ends is reclaimed and destroyed (P4)
            Publish(_session.End(reason));
            _pendingEndReason = null;
            _dirty = true;
            ValkyriesCargo.Log.LogInfo("visit #" + id + " ended: " + reason + "; takings " + _lastTakings + " coins, purse " + _market.Purse +
                                       ", " + _session.Republishes + " clock republish(es), " + _ledger.Count + " owed deliver" + (_ledger.Count == 1 ? "y" : "ies"));
            Flush("visit end");
        }

        // ---- publish and persist ---------------------------------------------------------------------

        private static void Publish(string visitState) { ModConfig.VisitState.AssignLocalValue(visitState ?? ""); }

        /// <summary>The whole market, after a visit starts and after every accepted deal.</summary>
        public void PublishMarket() { ModConfig.MarketState.AssignLocalValue(_market.Snapshot().Encode()); }

        /// <summary>Write the sidecar now if anything changed (or always, when forced). Never throws.</summary>
        public bool Flush(string why, bool force = false)
        {
            _sinceSave = 0f;
            if (!_dirty && !force) return true;
            if (Store == null || Store.Path == null) return false;
            double now = UnityEngine.Time.time;
            string text = Sidecar.Compose(_market.EncodeState(), _scheduler.EncodeCooldowns(now), _session.EncodeSessionRow(), _ledger.EncodeRows());
            bool ok = Store.Save(text);
            if (ok) _dirty = false;
            return ok;
        }

        private void LogDecision(string reason)
        {
            if (reason == _lastLogged) return;
            _lastLogged = reason;
            ValkyriesCargo.Log.LogInfo("roll: " + reason);
        }

        private static string Describe(DealResult r)
        {
            var parts = new List<string>();
            foreach (DealLine l in r.ItemsToAdd) parts.Add("sold " + l.Count + " " + l.Prefab + " at " + l.UnitPriceSeen);
            foreach (DealLine l in r.ItemsToRemove) parts.Add("bought " + l.Count + " " + l.Prefab + " at " + l.UnitPriceSeen);
            parts.Add("coins " + (r.CoinsDelta > 0 ? "+" : "") + r.CoinsDelta + " to the player");
            return string.Join(", ", parts.ToArray());
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
