using System;
using System.Collections.Generic;
using RavenIron.ValkyriesCargo.Config;
using RavenIron.ValkyriesCargo.Client.Terminal;
using RavenIron.ValkyriesCargo.Core;
using RavenIron.ValkyriesCargo.Net;
using RavenIron.ValkyriesCargo.Server;
using UnityEngine;

namespace RavenIron.ValkyriesCargo.Client
{
    /// <summary>
    /// Ingvar (design 3.3), added by `Patch_Humanoid_Awake` to any body whose ZDO carries `VCargo_ingvar`.
    /// It runs on EVERY machine: the carry pin and the speech have to look the same on every screen,
    /// and ownership can pass to a nearer client mid-visit. Only the owner decides anything; the state
    /// lives in `VCargo_state` so the new owner picks the visit up from the number.
    ///
    /// The decisions are all in `Core/MerchantPlan`, pure and proven off-game. This file measures the
    /// world, applies what the plan says, and writes the ZDO. That split is the same one P4 used for
    /// the flight, and for the same reason: the state machine is where the bugs would be.
    ///
    /// THREE THINGS HERE WERE LEARNED FROM OTHER MODS IN THIS WORKSPACE RATHER THAN FROM THE DECOMPILE
    /// (`libs-Tools\IMPLEMENTATIONS\DvergrAllies.md`; see CLAUDE.md's knowledge-base section):
    ///
    /// 1. **The setup is re-applied, not applied once.** `Humanoid.Start` calls `GiveDefaultItems()` --
        /// `Awake` does NOT, so our postfix on Awake runs BEFORE the crossbow is handed out, not after
        /// (decompile-checked 2026-09-07; the earlier comment here had it backwards, and the staggered
        /// re-apply is what makes it work anyway rather than the ordering),
    ///    and the shipped `Dverger` prefab's default items are `DvergerArbalest`, `Dverger_melee` and
    ///    a crossbow suit - measured, not guessed. Other systems re-give and re-alert after our Awake,
    ///    and ZDO sync can revert a flag mid-session. DvergrAllies re-asserts at staggered delays and
    ///    then keeps checking; a one-shot version leaves a merchant holding a crossbow, or turns him
    ///    hostile ten minutes in. We do the same with an accumulator rather than `Invoke`, so nothing
    ///    here owns a timer that outlives the object (house rule 2).
    /// 2. **`Character.SetTamed` does not update `m_tamed` synchronously** - it fires an RPC and only
    ///    the handler assigns the field. So `IsTamed()` is not a usable "did it work" check right
    ///    after `MakeTame()`, and we never read it as one.
    /// 3. **Faction, not just taming.** `m_faction = Character.Faction.Players` is what makes vanilla
    ///    treat him as a player ally for aggro and targeting. Taming alone leaves gaps.
    ///
    /// And one from the master index, which is the reason `Pinned` asks whether the carrier RESOLVED
    /// rather than whether the key is set: **a `ZDOID` is a session handle, not an identity.**
    /// `ZDO.Load` renumbers every id on every world read, so `VCargo_carrier` on this PERSISTENT ZDO is
    /// meaningless after a restart and must never be trusted to still mean the bird.
    /// </summary>
    public sealed class CargoMerchant : MonoBehaviour, Hoverable, Interactable
    {
        /// <summary>How long the dismissal's first press stays armed (design 3.3: Shift+E twice).</summary>
        public const float DismissWindowSeconds = 3f;

        /// <summary>The setup is re-asserted at these ages, and then every `ReassertInterval`.</summary>
        private static readonly float[] ReassertAt = { 0.5f, 1f, 3f };
        public const float ReassertInterval = 5f;

        /// <summary>Cheap gate for the two patches on hot vanilla paths: zero while no visit is running.</summary>
        public static int LiveCount { get; private set; }

        private ZNetView _nview;
        private Character _character;
        private Humanoid _humanoid;
        private MonsterAI _ai;
        private Rigidbody _body;
        private IngvarBody _ingvar;

        private int _visitId, _seed, _state;
        private float _timeInState, _farSeconds, _age, _nextReassert;

        /// <summary>
        /// Metres he has actually covered since this approach began, summed off his own transform.
        /// `BaseAI.HavePath` and `m_path` are protected (house rule 5) and `MoveTo` reports nothing,
        /// so his own displacement is the only honest answer to "did vanilla drive him at all".
        /// </summary>
        private float _approachMoved;
        private Vector3 _lastApproachPos;

        /// <summary>
        /// F5: the distance to the player at the moment THIS approach began, and the pathing-progress
        /// window built from the same per-tick displacement as `_approachMoved`. Both feed
        /// `MerchantPlan.Next`'s scaled timeout and stuck detector, and both are reset exactly when
        /// `_approachMoved` is - on every transition INTO Approaching, wherever it started from.
        /// </summary>
        private float _distanceAtApproachEntry;
        private MerchantPlan.ApproachProgress _progress;

        /// <summary>F5 part 3: the trading leash fires at most once a visit - see `MerchantPlan.Next`.</summary>
        private bool _leashSpent;

        /// <summary>Issue #59: "a terminal is open on him", as last fed to the plan; logged when it flips.</summary>
        private bool _busy;

        private int _reasserted;
        private bool _calledOut, _vanishing;
        private float _dismissArmedAt = -99f;
        private int _throws;

        /// <summary>F9: exactly what THIS instance added to `LiveCount`, so `OnDestroy` gives back only that.</summary>
        private bool _counted;

        private Transform _pin;          // the bird's attach point, while it resolves
        private Vector3 _pinOffset;

        /// <summary>
        /// True while THIS machine flies the bird carrying him, i.e. this is the pilot's client. Set
        /// from `ResolveCarrier` beside `Pinned`, because it is the same resolution. It is the gate on
        /// the ownership claim below: exactly one machine may claim, or two watchers fight over him.
        /// </summary>
        private bool _flyingCarrier;

        /// <summary>Counted so the claim can say how often it had to fire, without logging per step.</summary>
        private int _reclaims;

        /// <summary>
        /// True when he is standing where the server's sweep would strip an owner's claim (D5). Set in
        /// `EnterState`, reported in the transition line: on a healthy visit the drop is metres from the
        /// pilot and this reads false, so a true here names the cause without anyone reconstructing it.
        /// </summary>
        private bool _inStripBand;

        /// <summary>True while the carry pin owns his transform. Read by `Patch_Character_InIntro`.</summary>
        public bool Pinned { get; private set; }

        public int VisitId => _visitId;
        public int State => _state;

        private void Awake()
        {
            // F9, the FIRST statement, unconditionally - before anything below can throw or bail.
            // `Patch_Character_RPC_Damage` / `Patch_Character_ApplyDamage` (the immortality) and
            // `Patch_Character_InIntro` (the carry's velocity pin) all short-circuit whenever
            // `LiveCount == 0`. The increment used to run last, after `Reassert`; if `Reassert` (or
            // anything above it) threw - which is exactly what the catch below exists for, after a
            // real NRE on the first live visit - the merchant survived (correctly, by that catch's own
            // argument) but `LiveCount` stayed 0 for the whole visit, silently turning both patches off.
            // `_counted` is what lets `OnDestroy` give back exactly what THIS instance added, once: the
            // old unconditional decrement had no memory of whether this instance ever incremented, so
            // an instance that bailed before the old late `LiveCount++` could still steal a live
            // merchant's count on its own destruction (two merchants alive - F4's orphan case - and
            // whichever dies first pays for both).
            LiveCount++;
            _counted = true;

            try
            {
                _nview = GetComponent<ZNetView>();
                _character = GetComponent<Character>();
                _humanoid = GetComponent<Humanoid>();
                _ai = GetComponent<MonsterAI>();
                _body = GetComponent<Rigidbody>();

                ZDO zdo = _nview != null ? _nview.GetZDO() : null;
                if (zdo == null)
                {
                    // Counted anyway (see above), deliberately: this is still a real, physical,
                    // damageable Dverger standing in the world even though we could not read our own
                    // ZDO yet - most likely the F8 class of Awake-ordering race (ZNetView.Awake has not
                    // consumed ZNetView.m_initZDO yet), whose fix belongs to whoever attaches this
                    // component (Patch_Humanoid_Awake), not to this file. `enabled = false` here means
                    // the staggered Reassert can never recover him - a disabled behaviour gets no
                    // FixedUpdate either - so unlike the catch below there is no second chance, which
                    // makes counting him the safer default: the immortality/carry-pin patches pay one
                    // spurious GetComponent call per hit in the world rather than risk leaving a
                    // merchant they cannot identify killable.
                    enabled = false;
                    return;
                }
                _visitId = zdo.GetInt(Spawner.IngvarHash, 0);
                _seed = zdo.GetInt(Spawner.SeedHash, 0);
                _state = zdo.GetInt(Spawner.StateHash, MerchantState.Carried);
                _calledOut = _state >= MerchantState.Trading;
                _lastApproachPos = transform.position;

                if (_character != null) _character.m_name = Lines.Title;

                // Vanilla Dvergr chatter every 30 s would talk straight over Ingvar's own lines. The
                // component is measured present on the shipped prefab (name 'Dvergr', greetRange 10).
                NpcTalk talk = GetComponent<NpcTalk>();
                if (talk != null) talk.enabled = false;

                // The custom body if the bundle is here, the stand-in otherwise. Null is the normal
                // answer on a dedicated server and whenever `CustomBody` is off (P8's contract).
                _ingvar = BodyLoader.Attach(_character);

                if (_nview.IsValid())
                {
                    _nview.Register<int>(Keys.Say, RPC_Say);
                    _nview.Register(Keys.Vanish, RPC_Vanish);
                }

                Reassert(fromAwake: true);
                ValkyriesCargo.Log.LogInfo("cargo merchant #" + _visitId + ": awake as " +
                    MerchantPlan.Name(_state) + ", " + OwnerTag(_nview.GetZDO()) +
                    ", body=" + (_ingvar != null ? "Ingvar" : "the stand-in"));
            }
            catch (Exception ex)
            {
                // NOT `enabled = false`: this component IS the merchant, and switching it off leaves a
                // vanilla Dverger standing in a visit with nothing driving it. Whatever failed here,
                // `Reassert` runs again at 0.5 s, 1 s, 3 s and then every 5 s, and the state machine
                // recovers from a missed setup far better than the visit recovers from no merchant.
                ValkyriesCargo.Log.LogError("cargo merchant: Awake threw, carrying on so the stagger can " +
                                            "recover it: " + ex);
            }
        }

        private void OnDestroy()
        {
            // F9: exactly what this instance added, exactly once - even if OnDestroy somehow ran
            // twice, which Unity does not promise never happens. The `LiveCount > 0` guard stays as a
            // second line of defence against ever going negative, belt and braces.
            if (_counted) { _counted = false; if (LiveCount > 0) LiveCount--; }
        }

        /// <summary>
        /// Everything vanilla might undo, on the machine it must run on (F10, audit 2026-09-07). Splits
        /// into a LOCAL half (every machine) and an OWNED half (owner only) - see each for why.
        ///
        /// F11 note (ghost mode, `Core/Ghost.cs` + `Patches/Patch_BaseAI_IsEnemy.cs`, PR #37, landed
        /// 2026-09-07 alongside this fix): a prefix on the static `BaseAI.IsEnemy(Character, Character)`
        /// now answers "not enemies" outright for any pair with our merchant in it. Verified directly in
        /// the decompile that this makes MOST of what `ReassertLocal` below sets redundant for its
        /// original (F10) purpose: `EnemyHud`'s hostile-colouring reads `BaseAI.IsEnemy(player, target)`
        /// directly (line 38589), `BaseAI.FindEnemy()`'s candidate filter is `if (!IsEnemy(m_character,
        /// item) || ...) continue;` (line ~5205, so Ingvar's own scan can never acquire a hostile target
        /// either), and `MonsterAI.UpdateTarget`'s tame-gated `m_alertRange` clear is immediately
        /// followed, in the same call, by an UNCONDITIONAL `else if (!IsEnemy(m_targetCreature))
        /// m_targetCreature = null;` that fires regardless of tame state. So `m_faction`,
        /// `m_alertRange`, `m_aggravatable` and `m_passiveAggresive` no longer carry the hostility
        /// question ghost mode already answers. Kept here anyway, for three reasons that have nothing to
        /// do with `IsEnemy`: (1) belt and braces if ghost mode's own patch ever fails to apply - see
        /// house rule 3, a patch failing is counted and logged, never silently assumed; (2) it is a real,
        /// if now secondary, correctness fix in its own right - a non-owner's local Character/BaseAI
        /// state SHOULD match the owner's on principle; (3) it costs nothing, four plain field writes.
        /// `m_randomMoveRange` was never an aggro field at all - it bounds idle wander, untouched by
        /// ghost mode either way. This method does not duplicate PR #37: it never touches `IsEnemy` or
        /// any ghost-mode file, only WHEN the pre-existing field writes below run.
        ///
        /// What ghost mode does NOT cover, and is still exactly why the OWNED half below keeps taming:
        /// `BaseAI.AvoidFire(float, Character, bool)` opens `if (m_character.IsTamed()) return false;`
        /// (`asm:4389`) - an UNTAMED Ingvar panics and flees a nearby campfire or hearth instead of
        /// standing still, which has nothing to do with `IsEnemy` and everything to do with him landing
        /// "beside your base". `AvoidFire` only runs from `MonsterAI.UpdateAI`, which is owner-gated
        /// (`BaseAI.UpdateAI`, `asm:4110-4119`: `if (!m_nview.IsOwner()) { ...; return false; }`), so
        /// what matters is that whichever machine currently owns him has `IsTamed() == true` - which the
        /// OWNED half's `SetTamed` call is already exactly for, unchanged by ghost mode landing.
        /// </summary>
        /// <param name="fromAwake">
        /// True only for the call inside `Awake`, where vanilla's own `Awake`s have NOT all run yet.
        /// `MonsterAI.MakeTame()` opens with `m_character.SetTamed(true)` and `BaseAI.m_character` is
        /// assigned in `BaseAI.Awake`, so calling it from here throws a NullReferenceException out of
        /// vanilla -- which the catch in `Awake` then turned into `enabled = false`, killing the merchant
        /// outright. Every visit P5 has ever run did this. Found on the first integrated in-game visit,
        /// 2026-09-07. `m_character` is `protected`, so there is nothing legitimate to test it with
        /// (house rule 5); the honest fix is not to call it before vanilla is up, and to let the stagger
        /// do it 0.5 s later, which is what the stagger is for.
        /// </param>
        private void Reassert(bool fromAwake = false)
        {
            if (_nview == null || !_nview.IsValid()) return;
            ReassertLocal();
            if (_nview.IsOwner()) ReassertOwned(fromAwake);
        }

        /// <summary>
        /// F10: `m_faction`, `MonsterAI.m_alertRange`, `BaseAI.m_aggravatable` / `m_passiveAggresive`
        /// and `m_randomMoveRange` are plain fields with NO replication path at all - no RPC, no ZDO key,
        /// ever touches them (`asm:6891` `Character.m_faction`, `asm:5644` `MonsterAI.m_alertRange`,
        /// `asm:3919`/`3921` `BaseAI.m_aggravatable`/`m_passiveAggresive`, `asm:3883`
        /// `BaseAI.m_randomMoveRange`; line numbers re-verified against this machine's own copy of the
        /// decompile with `grep -n`, not copied from the audit's citations, which sit a few lines off
        /// against this copy). A machine that only ever WATCHES Ingvar keeps its own copy at
        /// whatever a vanilla Dverger initialises it to unless IT sets them too, and ownership can hand
        /// over mid-visit (the boot sweep and the release/claim path the audit's "Ownership through the
        /// visit" section confirms), so the NEW owner needs these already right, not five seconds of
        /// stale state after the handover. See the class-level `Reassert` comment for what ghost mode
        /// (F11, PR #37) already covers of the original reason these existed, and what it does not.
        /// </summary>
        private void ReassertLocal()
        {
            if (_character != null)
                _character.m_faction = Character.Faction.Players;

            if (_ai != null)
            {
                _ai.m_aggravatable = false;
                _ai.m_passiveAggresive = false;
                _ai.m_alertRange = 0f;
                _ai.m_randomMoveRange = 1.5f;
            }
        }

        /// <summary>
        /// F10: stays owner-only. `SetTamed` fires an RPC and only the RPC handler assigns `m_tamed`
        /// (the class comment above), so calling it from every machine is redundant chatter at best;
        /// `UnequipAllItems` touches equipment, which `VisEquipment` replicates FROM the owner, so a
        /// non-owner calling it is the silent desync the house rule warns about - a visible flicker that
        /// the owner's next sync overwrites, not a real change. `MakeTame` (see `fromAwake`) manipulates
        /// `MonsterAI`'s own target fields, which nothing but `MonsterAI.UpdateAI` ever reads - and that
        /// is owner-gated too (`BaseAI.UpdateAI`, `asm:4110-4119`). The consume-list swap belongs here
        /// for the same reason: its only reader, `MonsterAI.UpdateConsumeItem`, is called from within
        /// `UpdateAI` (`asm:6048`), downstream of that same owner gate, so clearing it on a machine that
        /// will never evaluate it is a no-op dressed as a fix.
        /// </summary>
        private void ReassertOwned(bool fromAwake)
        {
            if (_character != null)
            {
                // `Character.SetTamed` opens with `m_nview.IsValid()` on the CHARACTER's own ZNetView,
                // which is not ours and is not necessarily assigned yet: this component is added from a
                // `Humanoid.Awake` postfix, and on the first call it threw a NullReferenceException that
                // Mono reported against THIS method because SetTamed is small enough to inline. Seen on
                // the first in-game visit, 2026-09-07. Nothing is lost by skipping it: `Reassert` runs
                // again at 0.5 s, 1 s, 3 s and then every 5 s for exactly this class of reason.
                ZNetView his = _character.GetComponent<ZNetView>();
                if (his != null && his.IsValid())
                    _character.SetTamed(true);      // fires an RPC; do NOT read IsTamed() after it
            }
            if (_ai != null)
            {
                if (!fromAwake) _ai.MakeTame();   // see fromAwake: vanilla's BaseAI.Awake has not run yet

                // The shipped Dverger's own consume list is `CookedMeat, Coins, Sausages,
                // YggdrasilWood`, searched every 10 s inside 10 m - read off the prefab dump
                // (libs-Tools WubarrksEye 2026-09-03, build21981559), not guessed. Two reasons this
                // has to go, and the first is the one that breaks the visit:
                //
                // 1. `MonsterAI.UpdateConsumeItem` returns TRUE while a consumable is in range, and
                //    that return sits ABOVE the follow branch in `UpdateAI`. A merchant who lands
                //    within 10 m of a dropped stack stops approaching the player and walks to the
                //    stack instead - which is a walk-up that times out at 20 s having gone the wrong
                //    way, with nothing in any log to say why.
                // 2. He then EATS it (`ItemDrop.RemoveOne`). Ingvar trades in Coins. A merchant who
                //    eats the customer's coins off the floor is worse than one who does not arrive.
                //
                // A NEW list rather than `Clear()`: the list is a serialized field and clearing it
                // would mutate whatever it turns out to be shared with. Assigning replaces only ours,
                // and `UpdateConsumeItem` returns false on its first line from then on. It runs once:
                // after the swap the count is 0 and the branch never allocates again.
                if (_ai.m_consumeItems != null && _ai.m_consumeItems.Count > 0)
                    _ai.m_consumeItems = new List<ItemDrop>();
            }
            // He arrives holding a crossbow otherwise: the prefab's own default items.
            if (_humanoid != null) _humanoid.UnequipAllItems();
        }

        private void FixedUpdate()
        {
            try
            {
                if (_nview == null || !_nview.IsValid()) return;
                float dt = Time.fixedDeltaTime;
                _age += dt;

                // The staggered re-assert, then a slow steady one. See the class comment.
                if (_reasserted < ReassertAt.Length && _age >= ReassertAt[_reasserted])
                {
                    _reasserted++;
                    Reassert();
                    _nextReassert = _age + ReassertInterval;
                }
                else if (_age >= _nextReassert)
                {
                    _nextReassert = _age + ReassertInterval;
                    Reassert();
                }

                ResolveCarrier();
                HoldTheCarry();
                if (Pinned) PinToTalon();            // physics step: beat the Rigidbody
                if (_nview.IsOwner()) Decide(dt);
            }
            catch (Exception ex)
            {
                if (_throws++ < 3) ValkyriesCargo.Log.LogError("cargo merchant #" + _visitId + " threw: " + ex);
            }
        }

        /// <summary>
        /// D5, the ownership loss during the carry (StormTest 2026-09-07, visit 9; the line PR #50 added
        /// printed `carrier none, 135.7 m from the player, watching` at a drop 13 m from the pilot).
        ///
        /// THE MECHANISM, read out of the decompile rather than inferred. `ZDOMan.Update` runs
        /// `ReleaseZDOS` only `if (ZNet.instance.IsServer())` (`asm:65095`), every 2 s (`asm:65155`), and
        /// for each peer calls `ReleaseNearbyZDOS(peer.m_refPos, peer.m_uid)` (`asm:65164`). That method
        /// skips `!Persistent` ZDOs (`asm:65189`) and then, for one the peer already owns, does
        /// (`asm:65195`):
        ///
        ///     if (!ZNetScene.InActiveArea(sector, zone, m_activeArea - 1)) zdo.SetOwner(0L);
        ///
        /// `m_activeArea` reads 2 live, so the keep-window is `activatedArea = 1` - a 3x3 zone block,
        /// 64 m zones. `Spawner` authors the merchant owned by the pilot at the FLIGHT START, ~90 m out,
        /// which is squarely in that strip band. So within 2 s of every visit beginning the server takes
        /// the pilot's claim away and leaves him owned by NOBODY.
        ///
        /// And it is a one-way door, which is why it never recovered on its own. Only an owner writes a
        /// ZDO's position, so an unowned merchant's SECTOR freezes where the strip caught him; the grant
        /// branch below the strip (`asm:65199`) tests that frozen sector, so it cannot hand him to the
        /// pilot the bird is carrying him toward. He is instead handed over only if somebody physically
        /// walks near the point he froze at. That is every symptom the session recorded: `watching` at
        /// the drop, a distance measured from the frozen point rather than the talon, `Decide` never
        /// running (`FixedUpdate` gates it on `IsOwner`) so the walk-up never started, the late give-ups
        /// on visits 4-8 when the pilot happened to wander back within a zone, the 150-600 m "moved" as
        /// one chord from the frozen point when `ZSyncTransform`'s non-owner path finally snapped him,
        /// and visit 9's outright no-show when nobody ever went there.
        ///
        /// It also explains what did NOT break: the bird flew perfectly on all nine visits because it is
        /// non-persistent and `ReleaseNearbyZDOS` never looks at it at all.
        ///
        /// THE FIX. Take the claim back while the talons hold him. `ZNetView.ClaimOwnership` is
        /// `if (!IsOwner()) m_zdo.SetOwner(ZDOMan.GetSessionID())` (`asm:70222`) and `ZDO.SetOwner` is
        /// itself a no-op when the owner already matches (`asm:63483`), so calling this every physics
        /// step costs nothing on the steps we already own - which is all of them but the one after each
        /// 2 s strip. That beats the treadmill by design: the strip fires at 0.5 Hz and the reclaim at
        /// the physics rate, so his position can freeze for at most a step instead of forever, and once
        /// the bird has carried him inside the pilot's own 3x3 block the strip stops firing entirely.
        /// The staggered `Reassert` (0.5 s, 1 s, 3 s, then every 5 s) is deliberately NOT the place for
        /// this: a 5 s cadence against a 2 s strip is the "claim-then-act-next-tick loop that never
        /// catches an owning tick" the family already paid for once
        /// (`docs/knowledge-base/IMPLEMENTATIONS/ZoneAnchor.md`, the LetItGrow addendum of 2026-08-26).
        ///
        /// Only the pilot's client may do this - `_flyingCarrier` - or every watcher claims him in turn
        /// and they fight. Owning him during the carry is the state this code always believed it had:
        /// the pin runs from FixedUpdate and LateUpdate, and `Patch_Character_InIntro` holds `InIntro`
        /// true while `Pinned` so an owned Rigidbody never accumulates a fall (the physics warning in
        /// ZoneAnchor.md's ownership section).
        /// </summary>
        private void HoldTheCarry()
        {
            if (!Pinned || !_flyingCarrier) return;
            if (_nview.IsOwner()) return;
            _nview.ClaimOwnership();
            _reclaims++;
        }

        /// <summary>
        /// Vanilla's own carry writes the transform from FixedUpdate AND LateUpdate, because the
        /// Rigidbody and `ZSyncTransform` both move him in between. The shipped `Dverger` carries
        /// both (measured). One write per frame is a merchant who visibly sinks through the talon.
        /// </summary>
        private void LateUpdate()
        {
            try { if (Pinned) PinToTalon(); }
            catch (Exception ex) { if (_throws++ < 3) ValkyriesCargo.Log.LogWarning("cargo merchant #" + _visitId + ": pin threw: " + ex.Message); }
        }

        /// <summary>
        /// Find the bird, if the ZDO still names one AND it is really here. `ShouldPin` takes the
        /// resolution rather than the key precisely because the key survives a restart and the id
        /// inside it does not mean anything afterwards.
        /// </summary>
        private void ResolveCarrier()
        {
            ZDO zdo = _nview.GetZDO();
            if (zdo == null) { Pinned = false; return; }
            int was = _state;
            _state = zdo.GetInt(Spawner.StateHash, _state);

            ZDOID carrier = zdo.GetZDOID(Spawner.CarrierKey);
            GameObject bird = (carrier.IsNone() || ZNetScene.instance == null) ? null : ZNetScene.instance.FindInstance(carrier);
            if (bird == null) { Pinned = false; _pin = null; _flyingCarrier = false; }
            else
            {
                CargoFlight flight = bird.GetComponent<CargoFlight>();
                _pin = flight != null ? flight.AttachPoint : bird.transform;
                _pinOffset = flight != null ? flight.AttachOffset : new Vector3(0f, 0.3f, 0.4f);
                Pinned = MerchantPlan.ShouldPin(_state, true);
                _flyingCarrier = flight != null && flight.Flying;   // D5: only the pilot's client claims
            }

            // A state the PLAN did not choose arrived through the ZDO: the bird's drop
            // (CargoFlight.Drop writes Approaching), the server's sweep, or another machine's
            // Decide. It must get the same entry bookkeeping Decide gives its own transitions,
            // or the first walk-up runs on the flight's clock and the carry's displacement
            // (StormTest 2026-09-07, 6/6: `budget scaled from 0 m at entry`). Logged once, with
            // the numbers the session could not give: when it landed relative to waking, whether
            // the talons still held him, how far out he was, and who owns him.
            if (_state != was)
            {
                float distance = EnterState(_state);
                ValkyriesCargo.Log.LogInfo("cargo merchant #" + _visitId + ": " + MerchantPlan.Name(was) + " -> " +
                    MerchantPlan.Name(_state) + " via the ZDO, " + Wire.Float(_age) + " s after waking; carrier " +
                    (carrier.IsNone() ? "none" : (bird != null ? "still instanced" : "gone")) + ", " +
                    Wire.Float(distance) + " m from the player, " + OwnerTag(zdo) +
                    ", " + _reclaims + " reclaim(s) during the carry" +
                    (_inStripBand ? ", IN THE STRIP BAND (the server's sweep would release him here)" : "") +
                    ", grounded " + (_character == null || _character.IsOnGround() ? "yes" : "no") +
                    (_state == MerchantState.Approaching ? "; walk-up budget " + Wire.Float(MerchantPlan.ApproachBudget(distance)) + " s" : ""));
            }
        }

        /// <summary>
        /// Who holds him, not just whether it is us (asked for on PR #50: "print `zdo.GetOwner()` beside
        /// `ours|watching`"). `ZDO.GetOwner` answers 0 for an unowned ZDO (`asm:63464` returns 0 unless
        /// `Owned`), and 0 is the whole of D5 - it distinguishes "the server's strip released him" from
        /// "another peer took him", which the bare `watching` could not.
        /// </summary>
        private string OwnerTag(ZDO zdo)
        {
            long owner = zdo != null ? zdo.GetOwner() : 0L;
            return (_nview.IsOwner() ? "ours" : "watching") +
                   " (owner " + owner + (owner == 0L ? " - nobody" : "") + ")";
        }

        /// <summary>
        /// The one place a state is entered from, whichever way it arrived: the plan's own step in
        /// Decide, or a value read off the ZDO in ResolveCarrier. Resets the clocks the plan judges
        /// by and, on entry to Approaching, starts THIS approach's budget and stuck window from where
        /// he actually is now. Returns the distance to the nearest player, measured here so both
        /// callers use the same number.
        /// </summary>
        private float EnterState(int state)
        {
            Player near = Player.GetClosestPlayer(transform.position, 9999f);
            float distance = near != null ? Vector3.Distance(transform.position, near.transform.position) : float.MaxValue;

            // D5: is he standing where the server's 2 s sweep would take a claim away? Measured against
            // the nearest player rather than the pilot's `m_refPos`, which no client can read - close
            // enough to diagnose, since it is the pilot the bird is flying toward. `m_activeArea` is read
            // live because the scene overrides the compiled default (it reads 2, the default is 1).
            int activeArea = ZoneSystem.instance != null ? ZoneSystem.instance.m_activeArea : ZoneOwnership.LiveActiveArea;
            _inStripBand = near != null && ZoneOwnership.WouldStripClaim(
                transform.position.x, transform.position.z,
                near.transform.position.x, near.transform.position.z, activeArea);

            _timeInState = 0f;
            _farSeconds = 0f;
            _approachMoved = 0f;
            _lastApproachPos = transform.position;      // the carry's displacement is not a walk
            if (state == MerchantState.Approaching)
            {
                _distanceAtApproachEntry = distance;
                _progress = default;
            }
            return distance;
        }

        private void PinToTalon()
        {
            if (_pin == null) { Pinned = false; return; }
            Vector3 at = _pin.position - _pin.TransformVector(_pinOffset);
            transform.position = at;
            transform.rotation = _pin.rotation;
            if (_body != null)
            {
                _body.position = at;
                _body.velocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
            }
        }

        /// <summary>The server's count for THIS visit, off the VisitState channel; 0 for any other visit or none.</summary>
        private int TerminalsOpenOnHim()
        {
            VisitSnapshot v = CargoRpc.Visit;
            return v != null && v.Active && v.VisitId == _visitId ? v.TerminalsOpen : 0;
        }

        /// <summary>
        /// Issue #59: is a terminal open on him, here or anywhere? The local terminal answers at once;
        /// the server's count covers every other peer, including one this client has not instanced.
        /// </summary>
        private bool Busy()
        {
            bool local = CargoTerminalHost.Instance != null && CargoTerminalHost.Instance.IsOpen;
            return local || TerminalsOpenOnHim() > 0;
        }

        /// <summary>Owner only: measure, ask the plan, write what changed.</summary>
        private void Decide(float dt)
        {
            if (_vanishing || _state >= MerchantState.Leaving) return;

            Player near = Player.GetClosestPlayer(transform.position, 9999f);
            float distance = near != null ? Vector3.Distance(transform.position, near.transform.position) : float.MaxValue;
            bool grounded = _character == null || _character.IsOnGround();

            // One displacement measurement feeds BOTH the diagnostic total (_approachMoved, unwindowed)
            // and F5's windowed stuck detector (_progress) - the same number CargoMerchant already took
            // every tick before F5, just handed to a second accumulator too.
            float movedSinceLastTick = Vector3.Distance(transform.position, _lastApproachPos);
            _lastApproachPos = transform.position;

            // Issue #59 (2026-09-08): he never walks while a terminal is open on him. The server counts
            // the open terminals on the deal wire and carries the count in VisitState (for the player at
            // the terminal this client may never have instanced); this machine's own terminal counts at
            // once, ahead of the round trip.
            bool busy = Busy();
            if (busy != _busy)
            {
                _busy = busy;
                ValkyriesCargo.Log.LogInfo("cargo merchant #" + _visitId + ": " + (busy
                    ? "a terminal is open on him (" + TerminalsOpenOnHim() + " on the wire): the leash holds"
                    : "no terminal open on him: the leash is armed again"));
            }

            _timeInState += dt;
            if (_state == MerchantState.Trading) _farSeconds = MerchantPlan.AccumulateFar(_farSeconds, distance, dt, busy);
            if (_state == MerchantState.Approaching)
            {
                _approachMoved += movedSinceLastTick;
                _progress = MerchantPlan.AccumulateProgress(_progress, movedSinceLastTick, dt);
            }

            float approach = ModConfig.ApproachDistance != null ? ModConfig.ApproachDistance.Value : 3.5f;
            MerchantPlan.Step step = MerchantPlan.Next(_state, Pinned, grounded, distance, _timeInState, _farSeconds,
                approach, _distanceAtApproachEntry, _progress.StuckSeconds, _leashSpent, busy);

            if (step.Follow && _ai != null)
                _ai.SetFollowTarget(near != null ? near.gameObject : null);

            if (!step.Changed) return;

            // Taken before the reset below eats the numbers the diagnosis needs.
            string diagnosis = (step.TimedOut || step.Stuck) ? WalkDiagnosis(distance) : null;

            _state = step.State;
            if (step.LeashFired) _leashSpent = true;      // F5 part 3: spent, never re-arms this visit
            EnterState(_state);                           // the same bookkeeping as the ZDO path (D1)
            ZDO zdo = _nview.GetZDO();
            if (zdo != null) zdo.Set(Spawner.StateHash, _state);

            if (_state == MerchantState.Trading)
            {
                if (_ai != null) { _ai.SetFollowTarget(null); _ai.SetPatrolPoint(); }
                if (step.CallOut && !_calledOut)
                {
                    _calledOut = true;
                    // F3: owner -> every screen, INCLUDING this one. `ZNetView.InvokeRPC(Everybody, ...)`
                    // delegates to `ZRoutedRpc.InvokeRoutedRPC(0L, m_zdo.m_uid, ...)`, whose
                    // `targetPeerID == 0L` branch calls `HandleRoutedRPC()` locally and SYNCHRONOUSLY
                    // before the packet is ever routed out (decompile-confirmed:
                    // `ZRoutedRpc.InvokeRoutedRPC(long, ZDOID, string, object[])`; the same guarantee
                    // CLAUDE.md's knowledge-base section now documents in words: "`Everybody` (0L) also
                    // invokes the handler locally on the caller"). RPC_Say below is therefore what draws
                    // the bubble and plays Greet() everywhere, including here; calling Say/Greet
                    // directly on this path too would double-draw on the owner's own screen.
                    if (_nview.IsValid())
                        _nview.InvokeRPC(ZNetView.Everybody, Keys.Say, Lines.ArrivalIndexFor(_seed));
                }
            }
            ValkyriesCargo.Log.LogInfo("cargo merchant #" + _visitId + ": " + step);
            if (diagnosis != null)
                ValkyriesCargo.Log.LogWarning("cargo merchant #" + _visitId + ": the walk-up did not finish. " + diagnosis);
        }

        /// <summary>
        /// One line naming every gate on `MonsterAI.UpdateAI`'s follow branch that we are allowed to
        /// read, written when the approach gives up (F5: timed out OR stuck). It exists because both
        /// fallbacks are SILENT: he calls out from where he stands and the visit carries on looking
        /// healthy, so without this the log of a merchant who never moved is identical to the log of
        /// one who walked up perfectly (CLAUDE.md, "Debugging discipline").
        ///
        /// `moved` is the number that splits the field in two. Near zero and vanilla never drove him
        /// at all - look at the follow target and `tamed`. Tens of metres with no arrival and it drove
        /// him somewhere else, or the terrain has no path to the player and `MoveTo` kept calling
        /// `StopMoving`. Everything here is public on the real assembly (checked against the
        /// non-publicized decompile, house rule 5): `GetFollowTarget`, `IsTamed`, `IsAlerted` and
        /// `GetTargetCreature`. `HavePath` is protected, which is why `moved` is measured rather
        /// than asked for.
        /// </summary>
        private string WalkDiagnosis(float distance)
        {
            string follow = "?", tamed = "?", alerted = "?", target = "?";
            try
            {
                if (_ai != null)
                {
                    GameObject f = _ai.GetFollowTarget();
                    follow = f == null ? "NONE" : f.name;
                    alerted = _ai.IsAlerted() ? "yes" : "no";
                    Character c = _ai.GetTargetCreature();
                    target = c == null ? "none" : c.name;
                }
                if (_character != null) tamed = _character.IsTamed() ? "yes" : "NO";
            }
            catch (Exception ex)
            {
                if (_throws++ < 3) ValkyriesCargo.Log.LogWarning("cargo merchant #" + _visitId + ": the diagnosis threw: " + ex.Message);
            }
            // The budget actually in force, not a flat constant (F5): it is scaled from where THIS
            // approach began, so the log has to recompute it from the same number Next used rather than
            // quote the old fixed ApproachTimeoutSeconds, which is now only the floor.
            float budget = MerchantPlan.ApproachBudget(_distanceAtApproachEntry);
            return "walked " + Wire.Float(_timeInState) + " s of a " + Wire.Float(budget) +
                   " s budget (scaled from " + Wire.Float(_distanceAtApproachEntry) +
                   " m at entry), moved " + Wire.Float(_approachMoved) +
                   " m; stuck " + Wire.Float(_progress.StuckSeconds) +
                   " s of the last window, and stopped " + Wire.Float(distance) +
                   " m away; follow target " + follow + ", tamed " + tamed + ", alerted " + alerted +
                   ", AI target " + target +
                   ", grounded " + (_character == null || _character.IsOnGround() ? "yes" : "no") + ".";
        }

        // ---- speech ------------------------------------------------------------------------------

        /// <summary>
        /// An index into `Lines`, never text (design 3.6). Owner -> everyone, including the owner
        /// itself (see `Decide`'s comment on the arrival callout). One unified index space
        /// (`Lines.Say`/`Lines.Says`, F3) carries both the seeded arrival line and every other reaction;
        /// `IsArrival` is what tells them apart so only the arrival plays large and waves.
        /// </summary>
        private void RPC_Say(long sender, int index)
        {
            try
            {
                string text = Lines.Reaction(index);
                if (string.IsNullOrEmpty(text)) return;
                bool arrival = Lines.IsArrival(index);
                Say(text, large: arrival);
                // The wave: only the arrival line plays it, matching what this path replaced (Decide
                // used to call Greet() only from its own CallOut branch, never for a plain reaction).
                if (arrival && _ingvar != null) _ingvar.Greet();
            }
            catch (Exception ex)
            {
                // N4 (audit 2026-09-07): a throw here unwinds into ZRoutedRpc's own dispatch loop. Dead
                // while this handler was never invoked (F3); wiring it (this PR) is what makes N4 apply.
                if (_throws++ < 3) ValkyriesCargo.Log.LogWarning("cargo merchant #" + _visitId + ": RPC_Say threw: " + ex.Message);
            }
        }

        private void RPC_Vanish(long sender)
        {
            try
            {
                if (_vanishing) return;
                _vanishing = true;
                _state = MerchantState.Leaving;

                // F3: only the owner's write replicates (house rule). Every machine that receives this
                // RPC already latches `_vanishing` above on its own, but the ZDO is what a client
                // instancing him for the first time AFTER this reads back in `Awake`, and what anything
                // outside this component - the terminal, the director - reads to know the visit is
                // over; `_vanishing` is private to this instance and nobody else can see it.
                if (_nview != null && _nview.IsValid() && _nview.IsOwner())
                {
                    ZDO zdo = _nview.GetZDO();
                    if (zdo != null) zdo.Set(Spawner.StateHash, _state);
                }

                Say(Lines.Farewell, large: true);
                Vanish();   // owner-gated inside (the effect rule): only the owner creates vfx_odin_despawn
            }
            catch (Exception ex)
            {
                // N4, same reasoning as RPC_Say above.
                if (_throws++ < 3) ValkyriesCargo.Log.LogWarning("cargo merchant #" + _visitId + ": RPC_Vanish threw: " + ex.Message);
            }
        }

        /// <summary>
        /// `Chat.SetNpcText` is unguarded client UI - it draws a bubble and nothing else, so it is
        /// safe on any machine and meaningless on a server with no Chat instance.
        /// </summary>
        private void Say(string text, bool large)
        {
            if (Chat.instance == null || string.IsNullOrEmpty(text)) return;
            Chat.instance.SetNpcText(gameObject, Vector3.up * 2f, 30f, 8f, "", text, large);
            if (_ingvar != null) _ingvar.Talk();
        }

        /// <summary>
        /// The Odin vanish (design 3.6, locked). `Odin.m_despawn`'s one entry is `vfx_odin_despawn`
        /// and it CARRIES a ZNetView - measured - so by the effect rule the owner creates it and
        /// vanilla replicates it to every screen. Creating it on every client would stack it.
        ///
        /// The `Odin` COMPONENT is never added: its `m_ttl` on the shipped prefab is 60, not the 300
        /// design 3.6 quotes, and a merchant carrying it would delete himself a fifth of the way
        /// into the visit. Only the EffectList is borrowed.
        /// </summary>
        private void Vanish()
        {
            try
            {
                if (_nview == null || !_nview.IsValid() || !_nview.IsOwner()) return;
                if (ZNetScene.instance == null) return;
                GameObject odin = ZNetScene.instance.GetPrefab("odin");
                Odin o = odin != null ? odin.GetComponent<Odin>() : null;
                if (o != null && o.m_despawn != null) o.m_despawn.Create(transform.position, transform.rotation);
            }
            catch (Exception ex)
            {
                if (_throws++ < 3) ValkyriesCargo.Log.LogWarning("cargo merchant #" + _visitId + ": the vanish threw: " + ex.Message);
            }
        }

        // ---- hover and interaction (design 3.3) --------------------------------------------------

        public string GetHoverName() => Lines.Title;

        public string GetHoverText()
        {
            if (_state < MerchantState.Trading) return Lines.Title;
            string text = Lines.Title + Countdown() +
                          "\n[<color=yellow><b>$KEY_Use</b></color>] Trade" +
                          "\n[<color=yellow><b>L Shift + $KEY_Use</b></color>] Send him on his way";
            // A Hoverable localises its own text (Container.GetHoverText does); Hud does not do it for us.
            // The playtest of 2026-09-08 saw the literal `$KEY_Use` (issue #59, item 3).
            return Localization.instance != null ? Localization.instance.Localize(text) : text;
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold) return false;
            try
            {
                if (alt)
                {
                    // Twice within the window, so a stray Shift+E never ends a visit.
                    if (Time.time - _dismissArmedAt > DismissWindowSeconds)
                    {
                        _dismissArmedAt = Time.time;
                        Say(Lines.DismissFirst, large: false);
                        return true;
                    }
                    _dismissArmedAt = -99f;
                    CargoRpc.Dismiss(_visitId);
                    return true;
                }
                if (_state < MerchantState.Trading) return false;
                if (CargoTerminalHost.Instance != null) CargoTerminalHost.Instance.Open(gameObject, _visitId);
                CargoRpc.Open(_visitId);
                return true;
            }
            catch (Exception ex)
            {
                if (_throws++ < 3) ValkyriesCargo.Log.LogError("cargo merchant #" + _visitId + ": interact threw: " + ex);
                return false;
            }
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        /// <summary>
        /// The visit clock as the hover shows it. `EndWorldTime` is in the SERVER's world seconds and
        /// `ZNet.GetTimeSeconds()` is the shared clock both sides read, so this is the same number on
        /// every screen without anything of ours crossing the wire to keep it there.
        /// </summary>
        private static string Countdown()
        {
            VisitSnapshot v = CargoRpc.Visit;
            if (v == null || !v.Active || ZNet.instance == null) return "";
            double left = v.Remaining(ZNet.instance.GetTimeSeconds());
            if (left <= 0.0) return "";
            int total = (int)left;
            return "  " + (total / 60) + ":" + (total % 60).ToString("00");
        }
    }
}
