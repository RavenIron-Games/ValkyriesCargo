using System;
using System.Collections.Generic;
using System.Globalization;
using RavenIron.ValkyriesCargo.Client;
using RavenIron.ValkyriesCargo.Config;
using RavenIron.ValkyriesCargo.Core;
using RavenIron.ValkyriesCargo.Net;
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
        /// <summary>How many refused deals get a log line before the rest are only counted.</summary>
        public const int RefusalsLogged = 3;

        private readonly Scheduler _scheduler;
        private Market _market;   // rebuilt by SwapCatalogue (2026-09-07); otherwise fixed for the director's life
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
        /// <summary>F3's grace period: the visit id `End` sent Keys.Vanish for and is waiting to reclaim. 0 = nothing pending.</summary>
        private int _pendingVanishVisitId;
        private double _pendingClearAt;
        /// <summary>The visit whose departure finished last tick; its sweep runs THIS tick, after
        /// ZDOMan.Update has actually removed what Clear destroyed (DestroyZDO only queues; D3,
        /// docs/AUDIT-STORMTEST-2026-09-07.md §2). 0 = nothing pending.</summary>
        private int _pendingSweepFor;
        private float _adoptWaited;
        private bool _orphanLogged;
        private bool _dirty;
        private int _throws;
        private int _mirrorThrows;
        /// <summary>The `ModConfig.CatalogueVersion` the live market was built from; a newer one is a swap waiting to happen.</summary>
        private int _catalogueVersion;
        private string _catalogueWaiting;
        private bool _catalogueWaitingLogged;
        private bool _shelfWaitingLogged;   // the rotating shelf (2026-09-08): a due roll that waits logs once

        public Scheduler Scheduler => _scheduler;
        public Market Market => _market;
        /// <summary>Why a changed catalogue has not been applied yet ("visit #3 is running"), or null when nothing waits. `cargo status` prints it.</summary>
        public string CatalogueWaiting => _catalogueWaiting;
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
        /// <summary>Deals this session refused, for every reason. Only the first few are logged; see Settle.</summary>
        public int Refusals { get; private set; }

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
            // The backpack add-on: looked up BEFORE the market is sized, so the shelf is built at its
            // scaled size and does not re-roll one tick after "director up". Every plugin has loaded by now.
            BackpackMod.Detect(ModConfig.BackpackModGuid.Value);
            MarketRules mr = ModConfig.BuildMarketRules(day, problems);
            SchedulerRules sr = ModConfig.BuildSchedulerRules(problems);

            string salt = "w0";
            try { salt = "w" + znet.GetWorldUID().ToString("x", CultureInfo.InvariantCulture); }
            catch (Exception ex) { problems.Add("world uid unreadable (" + ex.GetType().Name + "); delivery ids salted 'w0'"); }

            string unknown;
            Catalogue catalogue = KnownEntries(ModConfig.CatalogueParsed, out unknown);
            if (unknown != null) ValkyriesCargo.Log.LogWarning("catalogue: dropped, no item prefab of that name in this game: " + unknown);
            var d = new VisitDirector(catalogue, mr, sr, znet.GetTimeSeconds(), salt) { DayLengthFromEngine = fromEngine, _catalogueVersion = ModConfig.CatalogueVersion };
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
            string swept = Spawner.Sweep(keep, "boot");
            if (swept != null) ValkyriesCargo.Log.LogInfo(swept);

            ValkyriesCargo.Log.LogInfo("director up: salt " + salt + ", day " + Wire.Double(day) + " s (" + (fromEngine ? "EnvMan.m_dayLengthSec" : "ASSUMED, no EnvMan") +
                                       "), catalogue " + d._market.Count + " entries, purse " + d._market.Purse + ", next visit #" + d._market.NextVisitId +
                                       ", roll every " + Wire.Float(sr.IntervalSeconds) + " s at " + Wire.Float(sr.ChancePercent) + "%, first roll one interval from now; sidecar " +
                                       (d.Store.Path != null ? System.IO.Path.GetFileName(d.Store.Path) + " (" + (d.Loaded > 0 ? d.Loaded + " rows loaded" : "fresh world") +
                                       (d._pendingSessionRow != null ? ", a saved visit waits for its event" : "") + ")" : "NOT AVAILABLE: " + d.Store.Detail) +
                                       "; " + d._market.DescribeShelf(znet.GetTimeSeconds()) +
                                       (problems.Count > 0 ? "; problems: " + d.Problems : ""));
            // The names too (StormTest 2026-09-08: the first boot on the shelf printed the count and the period and
            // left the reader computing the twenty by hand), on their own line so the director-up line stays readable.
            if (d._market.Rotating)
                ValkyriesCargo.Log.LogInfo("shelf now: " + string.Join(", ", d._market.ShelfNames()));
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
                BackpackMod.Detect(ModConfig.BackpackModGuid.Value);   // one dictionary read; a live knob change lands here
                ModConfig.FillMarketRules(_market.Rules, null);
                ModConfig.FillSchedulerRules(_scheduler.Rules, null);

                // A changed Server.Catalogue (2026-09-07), by whatever route it arrived: applied here, between visits.
                if (ModConfig.CatalogueVersion != _catalogueVersion) SwapCatalogue(worldTime);
                RollShelfIfDue(worldTime);

                // D3 (docs/AUDIT-STORMTEST-2026-09-07.md §2): a visit that finished its departure LAST
                // tick gets its sweep THIS tick, once ZDOMan.Update has actually removed what Clear
                // destroyed (DestroyZDO only queues the removal). This drain runs first, above the
                // _pendingClearAt handling below, so a FinishDeparture call further down THIS tick
                // (the immediate branch in End, or the grace-period branch right below) sets
                // _pendingSweepFor for NEXT tick to find, never this one -- the whole point of the
                // deferral is that it cannot drain in the same call that set it.
                if (_pendingSweepFor != 0)
                {
                    int endedId = _pendingSweepFor;
                    _pendingSweepFor = 0;
                    int liveNow = _session.Active ? _session.VisitId : 0;
                    string swept = Spawner.Sweep(liveNow, "after visit #" + endedId);
                    if (swept != null) ValkyriesCargo.Log.LogInfo(swept);
                }

                // F3's grace period (Spawner.VanishGraceSeconds after End sent Keys.Vanish): independent
                // of everything below, on purpose, so a RandEventSystem hiccup or "no visit active" never
                // delays reclaiming a merchant who has already had his moment to say goodbye.
                if (_pendingVanishVisitId != 0 && now >= _pendingClearAt)
                {
                    int ended = _pendingVanishVisitId;
                    _pendingVanishVisitId = 0;
                    FinishDeparture(ended);
                }

                RandEventSystem res = RandEventSystem.instance;
                if (res == null) return;
                RandomEvent current = res.GetCurrentRandomEvent();
                bool ours = current != null && current.m_name == CargoEvent.Name;

                if (_pendingSessionRow != null) Adopt(ours, worldTime, dt);

                if (_session.Active)
                {
                    if (!ours)
                    {
                        End(_pendingEndReason ?? (current == null ? "timer" : "displaced by event '" + current.m_name + "'"), now, worldTime);
                    }
                    else
                    {
                        string republish = _session.Sync(worldTime, CargoEvent.Remaining(res));
                        if (republish != null) Publish(republish);

                        // Issue #59 (2026-09-08): how many terminals are open on him rides in VisitState, so
                        // the merchant's owner can hold the leash for a player it may not have instanced.
                        // The wire counts one per peer and forgets a peer that drops; this only copies it.
                        string busy = _session.SetTerminalsOpen(DealWire.OpenTerminals);
                        if (busy != null) Publish(busy);

                        // The server never flies anything: it watches the pilot's `VCargo_dropped` flag and
                        // moves the visit's phase and drop point to follow (P4).
                        string flightState;
                        string note = Spawner.Tick(_session, GatherIntervalSeconds, out flightState);
                        if (flightState != null) { Publish(flightState); _dirty = true; }
                        if (note != null) ValkyriesCargo.Log.LogInfo(note);

                        // The event's area follows him once he is down (visit 21, 2026-09-08): fixed at the
                        // drop point it paused the clock beside a trading player after the leash walk,
                        // republished VisitState every tick and had the deal wire refuse his dismiss.
                        Vector3 him;
                        if (_session.Phase != VisitPhase.Flying && VisitAnchor.MerchantAt(out him)) CargoEvent.Follow(res, him);

                        if (_session.Clock.OneMinuteWarningDue(worldTime))
                            ValkyriesCargo.Log.LogInfo("visit #" + _session.VisitId + ": one minute left");   // P5: VCargo_say the line
                    }
                }
                else if (ours && _pendingSessionRow == null)
                {
                    // Our event is running and we did not start it. Until 2026-09-07 this branch called
                    // `ResetRandomEvent` and the event died inside a second, which is why the VANILLA
                    // console command looked broken: `event valkyries_cargo` is a legal way to start a
                    // visit -- our event is registered in `RandEventSystem.m_events` on every machine, so
                    // the command finds it and tab-completes it -- and the visit it started was killed by
                    // us before anyone saw anything. The only trace was a warning about a "leftover".
                    //
                    // So adopt it instead. Vanilla's `event` command passes the CALLER's own position
                    // (`Player.m_localPlayer.transform.position`), which makes the nearest player to the
                    // event the admin who typed it.
                    //
                    // Eligibility is deliberately NOT re-checked. `event` is cheat-gated and
                    // `onlyServer`, so the caller has already said what they want with more authority
                    // than `cargo visit` needs; refusing here would be the old bug wearing a new coat.
                    // The roll's gates exist to decide when a visit is a nice surprise, not to argue
                    // with an admin who asked for one.
                    Candidate host = Scheduler.NearestTo(Gather(), current.m_pos.x, current.m_pos.z);
                    if (host != null)
                    {
                        ValkyriesCargo.Log.LogInfo("event '" + CargoEvent.Name + "' started outside the director (the vanilla `event` console command, or another mod); adopting it onto " + host + " and authoring the visit");
                        _orphanLogged = false;
                        Begin(host, worldTime);
                    }
                    else
                    {
                        // Nobody to give it to: an empty server, or every candidate refused. Now it
                        // really is a leftover, and the old behaviour is the right one.
                        if (!_orphanLogged) { _orphanLogged = true; ValkyriesCargo.Log.LogWarning("event '" + CargoEvent.Name + "' is running with nobody to receive it (no live candidate near " + Wire.Float(current.m_pos.x) + ", " + Wire.Float(current.m_pos.z) + "); ending it"); }
                        res.ResetRandomEvent();
                    }
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

                // F4 (docs/AUDIT-P4P5-2026-09-07.md): Author() never ran for this visit in THIS process,
                // so Spawner's Bird/Merchant/VisitId/Dropped/Active were never bound to it -- without
                // this, End() -> Spawner.Clear() would reclaim nothing and the merchant (his ZDO is
                // PERSISTENT) would outlive the visit forever, immortal and interactable, with the next
                // visit authoring a second one beside him. The boot sweep already spared his ZDO under
                // this same visit id; find it again and bind Spawner to it.
                string rebound = Spawner.Rebind(_session.VisitId);
                if (rebound != null) ValkyriesCargo.Log.LogInfo(rebound);
                else ValkyriesCargo.Log.LogWarning("visit #" + _session.VisitId + " resumed: no merchant ZDO found to rebind " +
                                                   "(looked for VCargo_ingvar=" + _session.VisitId + "); Clear() will have nothing " +
                                                   "of its own to reclaim when this visit ends (the restart sweep is still the backstop)");
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
            string swept = Spawner.Sweep(0, "boot, after giving up on the carry");
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
        ///
        /// THE COINS THE CLIENT CLAIMS ARE ADVISORY, and that is a decision, not an oversight (P11's
        /// authority audit, 2026-09-07; DESIGN section 8). `deal.CoinsOffered` is a field the client
        /// writes, and it is passed straight through as `playerCoins`. The server cannot check it and
        /// never will be able to: vanilla keeps the inventory on the client-owned player ZDO and puts
        /// no part of it on the wire, so there is no server-side count of anyone's coins to compare
        /// against, and none of the player's items either -- a modified client can equally offer him
        /// goods it does not carry. `coins_short` is therefore a courtesy to an honest client, not a
        /// guard.
        ///
        /// What actually bounds a lying client is the MARKET's own numbers, every one of them the
        /// server's (Core/Market.Settle):
        ///   - `sold_out`: he cannot be bought out past his shelf, so a free buy costs him stock, and
        ///     stock is what it was going to be after an honest deal anyway.
        ///   - `over_max`: a phantom sale cannot overflow the shelf past the catalogue's MaxStock.
        ///   - `purse_empty`: a phantom sale cannot draw a coin more than the purse holds, so the
        ///     worst a lying client takes from one visit is the whole purse (PurseCoins plus the
        ///     carry, capped at three purses) and no more.
        ///   - the nonce ring and the visit id: neither replayed nor carried across visits.
        /// The exposure is bounded and per-visit, and it is bounded by numbers the SERVER owns. Nothing
        /// here reads the client's number for anything except refusing an honest client early.
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
            else
            {
                Refusals++;
                // A refusal is free for the sender -- a malformed, duplicate or stale deal costs one
                // packet and leaves the market untouched -- so this line is the one thing a client can
                // flood. Capped like a patch body's, and the count is kept for `cargo status`.
                if (r.Reason != DealReason.PriceChanged && Refusals <= RefusalsLogged)
                    ValkyriesCargo.Log.LogInfo("deal refused for " + playerName + ": " + r.Reason +
                                               (Refusals == RefusalsLogged ? "; further refusals are counted, not logged" : ""));
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

        private void End(string reason, double now, double worldTime)
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

            // The session is inactive from HERE on, before the departure below reads it: FinishDeparture
            // (whether called immediately or after the grace period) tells a live NEW visit apart from
            // "nothing running" by asking `_session.Active`/`.VisitId`, and both must already reflect
            // this visit having ended, not the visit itself.
            string endedState = _session.End(reason);
            DealWire.ClearOpen();   // whatever was open was open on him; the next visit starts at 0

            // F3, the server half (docs/AUDIT-P4P5-2026-09-07.md): ask him to vanish before he's gone.
            // `CargoMerchant.RPC_Vanish` already exists and already does the right thing on receive --
            // the farewell line, `Leaving`, the Odin despawn effect created by the owner -- but nothing
            // had ever sent it. Give it `Spawner.VanishGraceSeconds` to actually play before `Clear`
            // destroys the ZDO the RPC was targeted at; `Clear` (via `FinishDeparture`) stays the
            // backstop and runs regardless, whether now or after the grace. Nothing bound (MerchantEnabled
            // was off, or an adopted visit's Rebind found no merchant) -> nothing to wait for.
            if (!Spawner.Merchant.IsNone())
            {
                Spawner.SendVanish();
                _pendingVanishVisitId = id;
                _pendingClearAt = now + Spawner.VanishGraceSeconds;
            }
            else
            {
                FinishDeparture(id);
            }

            Publish(endedState);
            _pendingEndReason = null;
            _dirty = true;
            ValkyriesCargo.Log.LogInfo("visit #" + id + " ended: " + reason + "; takings " + _lastTakings + " coins, purse " + _market.Purse +
                                       ", " + _session.Republishes + " clock republish(es), " + _ledger.Count + " owed deliver" + (_ledger.Count == 1 ? "y" : "ies"));
            Flush("visit end");
        }

        /// <summary>
        /// F3's backstop, fired whether `Spawner.Clear()` runs right now (nothing was bound at `End`)
        /// or after the grace period (`Tick`, above). `ClearIfStillOurs` only reclaims if nothing has
        /// re-authored a NEW visit's bird and merchant since `endedVisitId` was the one ending.
        ///
        /// D3 (docs/AUDIT-STORMTEST-2026-09-07.md §2): the belt-and-braces sweep used to run HERE, in
        /// the same synchronous call as the reclaim. `Spawner.Reclaim` calls `ZDOMan.DestroyZDO`, which
        /// in 0.221.12 only adds the id to `m_destroySendList` -- actual removal from the sector and id
        /// tables happens in `HandleDestroyedZDO`, reached from `SendDestroyed` on the NEXT
        /// `ZDOMan.Update`. So a sweep run right here always found the merchant this call just
        /// destroyed still in the table, still tagged `VCargo_ingvar`, and reported him as stranded --
        /// `restart sweep: 1 stranded merchant(s) destroyed` at every one of StormTest's six visit
        /// ends, 2026-09-07, none of them a restart. The reclaim itself was never wrong. The sweep now
        /// waits one director tick (`_pendingSweepFor`, drained at the top of `Tick`), by which time
        /// `ZDOMan.Update` has actually removed what `Clear` destroyed, so a sweep line after this
        /// point means something really was left behind.
        /// </summary>
        private void FinishDeparture(int endedVisitId)
        {
            bool reclaimed = Spawner.ClearIfStillOurs(endedVisitId);
            ValkyriesCargo.Log.LogInfo("visit #" + endedVisitId + ": " + (reclaimed
                ? "merchant and bird reclaimed and their destroy queued (it lands on the next ZDOMan.Update)"
                : "nothing bound to reclaim"));
            _pendingSweepFor = endedVisitId;
        }

        // ---- publish and persist ---------------------------------------------------------------------

        private static void Publish(string visitState) { ModConfig.VisitState.AssignLocalValue(visitState ?? ""); }

        /// <summary>The whole market, after a visit starts and after every accepted deal.</summary>
        public void PublishMarket() { ModConfig.MarketState.AssignLocalValue(_market.Snapshot().Encode()); }

        // ---- the catalogue swap (2026-09-07) --------------------------------------------------------

        /// <summary>
        /// Apply a changed `Server.Catalogue` to the live market: `cargo catalogue add|remove|reset`, an
        /// admin's Configuration Manager, a listen host's own file - every route ends in
        /// `ModConfig.CatalogueVersion` moving, and the tick calls this when it has. BETWEEN VISITS ONLY:
        /// `Market.WithCatalogue` carries everything the sidecar carries and nothing else, and the nonce
        /// ring is not in the sidecar, so a swap mid-visit could settle a deal twice. While a visit runs,
        /// a saved one waits for its event, or a departure is still finishing, the change waits - once in
        /// the log, always in `cargo status` - and the first idle tick applies it. The new shelf is
        /// published and saved at once, so a client's `cargo stock`, the sidecar and the log agree.
        /// Returns one sentence for whoever asked.
        /// </summary>
        /// <summary>
        /// The rotating shelf (the owner, 2026-09-08; issue #56). The period moved, or an admin changed
        /// `Server.ShelfSize` live: re-roll and republish the market so every terminal's panes follow. The
        /// same busy rule as the catalogue swap - never under a running visit, a saved one waiting for its
        /// event, or a merchant still departing - so the pane never changes under an open terminal; the wait
        /// is logged once. Nothing is persisted: the same salt, period and catalogue roll the same shelf on
        /// every boot, which is why a restart mid-period shows the same twenty.
        /// </summary>
        private void RollShelfIfDue(double worldTime)
        {
            if (!_market.ShelfDue(worldTime)) return;
            string busy = _session.Active ? "visit #" + _session.VisitId + " is running"
                        : _pendingSessionRow != null ? "a saved visit is waiting for its event"
                        : _pendingVanishVisitId != 0 ? "visit #" + _pendingVanishVisitId + " is still departing"
                        : null;
            if (busy != null)
            {
                if (!_shelfWaitingLogged)
                {
                    _shelfWaitingLogged = true;
                    ValkyriesCargo.Log.LogInfo("shelf roll waits: " + busy + "; it rolls as soon as no visit is running");
                }
                return;
            }
            _shelfWaitingLogged = false;
            if (!_market.UpdateShelf(worldTime)) return;
            PublishMarket();
            ValkyriesCargo.Log.LogInfo("shelf rolled: " + _market.DescribeShelf(worldTime) + ": " + string.Join(", ", _market.ShelfNames()));
        }

        public string SwapCatalogue(double worldTime)
        {
            if (ModConfig.CatalogueVersion == _catalogueVersion) return "catalogue unchanged";
            string busy = _session.Active ? "visit #" + _session.VisitId + " is running"
                        : _pendingSessionRow != null ? "a saved visit is waiting for its event"
                        : _pendingVanishVisitId != 0 ? "visit #" + _pendingVanishVisitId + " is still departing"
                        : null;
            if (busy != null)
            {
                _catalogueWaiting = busy;
                string waiting = "catalogue change waits: " + busy + "; it applies as soon as no visit is running";
                if (!_catalogueWaitingLogged) { _catalogueWaitingLogged = true; ValkyriesCargo.Log.LogInfo(waiting); }
                return waiting;
            }
            string unknown;
            Catalogue catalogue = KnownEntries(ModConfig.CatalogueParsed, out unknown);
            string summary;
            _market = _market.WithCatalogue(catalogue, worldTime, out summary);
            _catalogueVersion = ModConfig.CatalogueVersion;
            _catalogueWaiting = null;
            _catalogueWaitingLogged = false;
            if (unknown != null) summary += "; dropped, no item prefab of that name in this game: " + unknown;
            int refused = ModConfig.CatalogueProblems.Count;
            if (refused > 0) summary += "; " + refused + " entr" + (refused == 1 ? "y" : "ies") + " did not parse (cargo status names the first)";
            _dirty = true;
            PublishMarket();
            Flush("catalogue");
            ValkyriesCargo.Log.LogInfo(summary);
            return summary;
        }

        /// <summary>
        /// True when this game has a prefab of that exact name and it is an item (`ItemDrop`), which is
        /// what a deal can put in an inventory. `why` says which test failed. Public `ZNetScene.GetPrefab`,
        /// a null for a name it does not know. Without a scene - too early, or off-game - nothing is known.
        /// </summary>
        public static bool IsItemPrefab(string prefab, out string why)
        {
            why = null;
            ZNetScene scene = ZNetScene.instance;
            if (scene == null) { why = "no scene to ask (is the world loaded?)"; return false; }
            if (!Catalogue.IsPrefabName(prefab)) { why = "'" + prefab + "' is not a prefab name (letters, digits and underscores only)"; return false; }
            GameObject go;
            try { go = scene.GetPrefab(prefab); }
            catch (Exception ex) { why = "ZNetScene.GetPrefab threw " + ex.GetType().Name; return false; }
            if (go == null) { why = "this game has no prefab named '" + prefab + "' (names are exact, and case matters)"; return false; }
            if (go.GetComponent<ItemDrop>() == null) { why = "'" + prefab + "' is a prefab but not an item (no ItemDrop), so it could never be delivered"; return false; }
            return true;
        }

        /// <summary>The catalogue minus every entry `IsItemPrefab` refuses; `unknown` names them, or is null. With no scene to ask, the catalogue as it is.</summary>
        private static Catalogue KnownEntries(Catalogue catalogue, out string unknown)
        {
            unknown = null;
            if (catalogue == null || ZNetScene.instance == null) return catalogue;
            var drop = new List<string>();
            foreach (CatalogueEntry e in catalogue.Entries)
            {
                string why;
                if (!IsItemPrefab(e.Prefab, out why)) drop.Add(e.Prefab);
            }
            if (drop.Count == 0) return catalogue;
            unknown = string.Join(", ", drop.ToArray());
            return catalogue.Without(drop);
        }

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
                    // The identity, not the session (D2, docs/AUDIT-STORMTEST-2026-09-07.md §3): the
                    // cooldown is keyed on this; `Uid` stays the handle the admin wire speaks in.
                    // Probed at boot beside s_playerName (EngineCheck.CheckComfort).
                    PlayerId = zdo.GetLong(ZDOVars.s_playerID, 0L),
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
                   (_scheduler.OnPlayerCooldown(c.CooldownKey, now) ? " on cooldown" : "") + (_scheduler.NearBaseCooldown(c.X, c.Z, now) ? " near a base on cooldown" : "");
        }
    }
}
