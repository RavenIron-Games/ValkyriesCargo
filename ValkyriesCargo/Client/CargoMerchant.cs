using System;
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
        private int _reasserted;
        private bool _calledOut, _vanishing;
        private float _dismissArmedAt = -99f;
        private int _throws;

        private Transform _pin;          // the bird's attach point, while it resolves
        private Vector3 _pinOffset;

        /// <summary>True while the carry pin owns his transform. Read by `Patch_Character_InIntro`.</summary>
        public bool Pinned { get; private set; }

        public int VisitId => _visitId;
        public int State => _state;

        private void Awake()
        {
            try
            {
                _nview = GetComponent<ZNetView>();
                _character = GetComponent<Character>();
                _humanoid = GetComponent<Humanoid>();
                _ai = GetComponent<MonsterAI>();
                _body = GetComponent<Rigidbody>();

                ZDO zdo = _nview != null ? _nview.GetZDO() : null;
                if (zdo == null) { enabled = false; return; }
                _visitId = zdo.GetInt(Spawner.IngvarHash, 0);
                _seed = zdo.GetInt(Spawner.SeedHash, 0);
                _state = zdo.GetInt(Spawner.StateHash, MerchantState.Carried);
                _calledOut = _state >= MerchantState.Trading;

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
                LiveCount++;
                ValkyriesCargo.Log.LogInfo("cargo merchant #" + _visitId + ": awake as " +
                    MerchantPlan.Name(_state) + ", " + (_nview.IsOwner() ? "ours" : "watching") +
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
            if (LiveCount > 0) LiveCount--;
        }

        /// <summary>
        /// Everything vanilla might undo. Owner only: these are all writes to shared state, and a
        /// non-owner writing them is the silent desync the house rule is about.
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
            if (_nview == null || !_nview.IsValid() || !_nview.IsOwner()) return;

            if (_character != null)
            {
                // What makes vanilla treat him as a player ally for aggro and targeting.
                _character.m_faction = Character.Faction.Players;

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
                _ai.m_aggravatable = false;
                _ai.m_passiveAggresive = false;
                _ai.m_alertRange = 0f;
                _ai.m_randomMoveRange = 1.5f;
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
                if (Pinned) PinToTalon();            // physics step: beat the Rigidbody
                if (_nview.IsOwner()) Decide(dt);
            }
            catch (Exception ex)
            {
                if (_throws++ < 3) ValkyriesCargo.Log.LogError("cargo merchant #" + _visitId + " threw: " + ex);
            }
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
            _state = zdo.GetInt(Spawner.StateHash, _state);

            ZDOID carrier = zdo.GetZDOID(Spawner.CarrierKey);
            if (carrier.IsNone() || ZNetScene.instance == null) { Pinned = false; _pin = null; return; }

            GameObject bird = ZNetScene.instance.FindInstance(carrier);
            if (bird == null) { Pinned = false; _pin = null; return; }

            CargoFlight flight = bird.GetComponent<CargoFlight>();
            _pin = flight != null ? flight.AttachPoint : bird.transform;
            _pinOffset = flight != null ? flight.AttachOffset : new Vector3(0f, 0.3f, 0.4f);
            Pinned = MerchantPlan.ShouldPin(_state, true);
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

        /// <summary>Owner only: measure, ask the plan, write what changed.</summary>
        private void Decide(float dt)
        {
            if (_vanishing || _state >= MerchantState.Leaving) return;

            Player near = Player.GetClosestPlayer(transform.position, 9999f);
            float distance = near != null ? Vector3.Distance(transform.position, near.transform.position) : float.MaxValue;
            bool grounded = _character == null || _character.IsOnGround();

            _timeInState += dt;
            if (_state == MerchantState.Trading) _farSeconds = MerchantPlan.AccumulateFar(_farSeconds, distance, dt);

            float approach = ModConfig.ApproachDistance != null ? ModConfig.ApproachDistance.Value : 3.5f;
            MerchantPlan.Step step = MerchantPlan.Next(_state, Pinned, grounded, distance, _timeInState, _farSeconds, approach);

            if (step.Follow && _ai != null)
                _ai.SetFollowTarget(near != null ? near.gameObject : null);

            if (!step.Changed) return;

            _state = step.State;
            _timeInState = 0f;
            _farSeconds = 0f;
            ZDO zdo = _nview.GetZDO();
            if (zdo != null) zdo.Set(Spawner.StateHash, _state);

            if (_state == MerchantState.Trading)
            {
                if (_ai != null) { _ai.SetFollowTarget(null); _ai.SetPatrolPoint(); }
                if (step.CallOut && !_calledOut)
                {
                    _calledOut = true;
                    // Every machine derives the same line from the same seed: no text crosses the wire.
                    Say(Lines.ArrivalFor(_seed), large: true);
                    if (_ingvar != null) _ingvar.Greet();
                }
            }
            ValkyriesCargo.Log.LogInfo("cargo merchant #" + _visitId + ": " + step);
        }

        // ---- speech ------------------------------------------------------------------------------

        /// <summary>An index into `Lines`, never text (design 3.6). Server -> everyone.</summary>
        private void RPC_Say(long sender, int index)
        {
            string text = Lines.Reaction(index);
            if (!string.IsNullOrEmpty(text)) Say(text, large: false);
        }

        private void RPC_Vanish(long sender)
        {
            if (_vanishing) return;
            _vanishing = true;
            _state = MerchantState.Leaving;
            Say(Lines.Farewell, large: true);
            Vanish();
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
            return Lines.Title + Countdown() +
                   "\n[<color=yellow><b>$KEY_Use</b></color>] Trade" +
                   "\n[<color=yellow><b>L Shift + $KEY_Use</b></color>] Send him on his way";
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
