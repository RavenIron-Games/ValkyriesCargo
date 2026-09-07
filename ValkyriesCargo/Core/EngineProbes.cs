using System.Collections.Generic;
using System.Text;

namespace RavenIron.ValkyriesCargo.Core
{
    /// <summary>Where one probe stands. `NotRun` is the starting state and is NEVER a refusal.</summary>
    public enum ProbeState
    {
        /// <summary>Nobody asked. A feature must behave exactly as it did before P10b.</summary>
        NotRun = 0,
        /// <summary>The members are there and say what we assumed.</summary>
        Passed = 1,
        /// <summary>They are not. The one feature that depends on them turns itself off and says why.</summary>
        Failed = 2,
        /// <summary>A method BODY, which no cheap runtime check can see. P10a's offline sweep is the only thing that can.</summary>
        NotProbeable = 3,
    }

    /// <summary>One named engine fact, its risk rank, and what became of it.</summary>
    public sealed class EngineProbe
    {
        public string Name = "";
        /// <summary>The brief's ranking, worst first: 1 RandEventSystem, 2 ZDO authoring, 3 zone maths, 4 the velocity cache, 5 Valkyrie, 6 Character/MonsterAI. Lower is more dangerous.</summary>
        public int Rank;
        /// <summary>What the probe looks at, in a phrase.</summary>
        public string What = "";
        /// <summary>What stops working when it fails, in a phrase. "nothing" is an honest answer.</summary>
        public string Degrades = "";
        public ProbeState State = ProbeState.NotRun;
        public string Message = "";

        /// <summary>A probe is a veto, never a permit: only an outright failure says no.</summary>
        public bool Ok => State != ProbeState.Failed;

        public override string ToString() =>
            Wire.Int(Rank) + " " + Name + " " + State.ToString().ToUpperInvariant() +
            (Message.Length > 0 ? ": " + Message : "");
    }

    /// <summary>
    /// The registry of engine facts the patches rely on (P10b, brief section "Probe before you rely").
    /// Each probe is resolved ONCE at boot by `ValkyriesCargo.EngineCheck`, which owns every line that
    /// touches a game type; this file only holds the answers, the ranking and the words.
    ///
    /// Two rules that are the whole point of it:
    /// - **A missing probe must never disable a feature.** `Ok(name)` answers TRUE for a probe that has
    ///   not run, for a probe that could not be probed, and for a name nobody ever registered. Only a
    ///   recorded FAILURE is a no. A registry that fails closed would turn a bug in this file into a mod
    ///   that does nothing, on every machine, silently.
    /// - **A probe is recorded once.** A second `Record` for the same name is refused and reported, so a
    ///   feature cannot flip a verdict under itself and `cargo status` cannot disagree with the log.
    ///
    /// PURE: no Unity types, no reflection, harness-tested. `Current` is the one the game uses; the
    /// harness builds its own instances, which is why nothing here is static state.
    /// </summary>
    public sealed class EngineProbes
    {
        // ---- the names. Rank 1-6 are the brief's ranking; 7 and below are the rest of the surface. ----

        /// <summary>Rank 1. The vanilla RandomEvent the whole visit rides on.</summary>
        public const string RandEvent = "randevent";
        /// <summary>Rank 1, not probeable. The FixedUpdate clock rule: `m_time` advances only within `m_eventRange`.</summary>
        public const string EventClock = "event_clock";
        /// <summary>Rank 2. Authoring a ZDO the pilot instantiates: prefab, owner, persistence, type.</summary>
        public const string ZdoAuthoring = "zdo_authoring";
        /// <summary>Rank 2, not probeable. `ZNetView.Awake`'s init branch re-applies Type and Distant but never Persistent.</summary>
        public const string ZNetViewAwake = "znetview_awake";
        /// <summary>Rank 2, not probeable. The dedicated server pins its reference position to a million every fixed frame.</summary>
        public const string ServerRefPin = "server_refpin";
        /// <summary>Rank 3. `ZNetScene.InActiveArea` and the zone maths every waypoint is clamped by.</summary>
        public const string ZoneMaths = "zone_maths";
        /// <summary>Rank 4. `ZSyncTransform`'s velocity cache, which is why the owner's own `s_velHash` write survives.</summary>
        public const string VelocityCache = "velocity_cache";
        /// <summary>Rank 5. The Valkyrie fields the flight and the carry read.</summary>
        public const string ValkyrieFields = "valkyrie";
        /// <summary>Rank 6. Character and MonsterAI: the two members read every second, and the three P5 patches.</summary>
        public const string CharacterAi = "character";
        /// <summary>Rank 6. What `Patch_Humanoid_Awake` patches and what `CargoMerchant.Reassert` sequences around.</summary>
        public const string MerchantAwake = "merchant_awake";
        /// <summary>Rank 6, not probeable. The ORDER inside those Awakes, which is a method body and cost a shipped visit once.</summary>
        public const string AwakeOrder = "awake_order";
        /// <summary>Rank 6. Everything else `CargoMerchant` reaches to set Ingvar up, pin him, speak through him and send him away.</summary>
        public const string Merchant = "merchant";
        /// <summary>Rank 7. The three inventory calls a delivery is applied with.</summary>
        public const string InventoryOps = "inventory";
        /// <summary>Rank 8. Comfort and rested, which decide who gets a visit at all.</summary>
        public const string Comfort = "comfort";
        /// <summary>Rank 9. `EnvMan.m_dayLengthSec`, the day the market's drift counts.</summary>
        public const string DayLength = "daylength";
        /// <summary>Rank 10. `ZNet.IsAdmin(string)`, the one game call that decides an admin verb.</summary>
        public const string AdminList = "admin";
        /// <summary>Rank 11. The RPC surface both wires are registered on.</summary>
        public const string Rpc = "rpc";
        /// <summary>Rank 12. `Localization`, which the terminal's item names go through.</summary>
        public const string Localisation = "localization";
        /// <summary>Rank 13. The AssetBundle and PlayableGraph calls Ingvar's body is built with.</summary>
        public const string Body = "body";

        /// <summary>What a `NotProbeable` entry says, every time. `cargo status` repeats it rather than implying a pass.</summary>
        public const string SweepNote = "not probeable: verified by P10a's sweep";

        // ---- the registry ---------------------------------------------------------------------------

        private readonly List<EngineProbe> _order = new List<EngineProbe>();
        private readonly Dictionary<string, EngineProbe> _byName = new Dictionary<string, EngineProbe>();
        private readonly List<string> _problems = new List<string>();

        /// <summary>The one the running mod uses. Everything else builds its own.</summary>
        public static EngineProbes Current { get; } = new EngineProbes();

        public EngineProbes()
        {
            // Declared in rank order and enumerated in declaration order, so `cargo engine` and
            // `Encode()` read worst-first without sorting anything at print time.
            Declare(RandEvent, 1, "RandEventSystem.m_events / instance / SetRandomEventByName / ResetRandomEvent / GetCurrentRandomEvent / HaveEvent, and the RandomEvent fields the definition sets",
                                   "the event is not registered and no visit can start");
            Declare(EventClock, 1, "RandEventSystem.FixedUpdate advances m_time only while a player is within m_eventRange, and broadcasts every 2 s",
                                   "the visit clock; a body change here is silent", ProbeState.NotProbeable);
            Declare(ZdoAuthoring, 2, "ZDO.Persistent / Distant / Type setters, SetPrefab, SetOwner, GetHashZDOID, ZDOMan.CreateNewZDO(Vector3,int)",
                                     "the flight is not authored (Spawner)");
            Declare(ZNetViewAwake, 2, "ZNetView.Awake's init branch re-applies Type and Distant but never Persistent",
                                      "an authored ZDO's persistence; a body change here is silent", ProbeState.NotProbeable);
            Declare(ServerRefPin, 2, "a dedicated server pins its reference position to (1000000, 0, 1000000) every fixed frame, so it instantiates nothing of ours",
                                     "the whole authored-ZDO design; a body change here is silent", ProbeState.NotProbeable);
            Declare(ZoneMaths, 3, "ZNetScene.InActiveArea's three static overloads, ZoneSystem.GetZone, m_zoneSize, m_activeArea",
                                  "the flight's block clamp (Spawner, FlightPlan)");
            Declare(VelocityCache, 4, "ZSyncTransform.m_velocityCached and ZDOVars.s_velHash",
                                      "the glide on every screen but the pilot's (CargoFlight)");
            Declare(ValkyrieFields, 5, "Valkyrie.m_attachPoint / m_attachOffset / m_dropHeight / m_speed / m_turnRate",
                                       "the carry point and the drop height (CargoFlight, P5's carry)");
            // RPC_Damage, not Damage: `Character.Damage` is a thin sender on the ATTACKER's machine and
            // nothing of ours depends on it, while the PRIVATE `Character.RPC_Damage` is what
            // `Patch_Character_RPC_Damage` actually patches. Probing the sender was green while the
            // thing it stood for could be broken - `docs/P10B-PROBE-GAP.md`, fixed here.
            Declare(CharacterAi, 6, "Character.GetAllCharacters / GetSEMan / InIntro / RPC_Damage, MonsterAI.MakeTame, BaseAI.IsEnemy",
                                    "Ingvar is mortal (RPC_Damage), the carry does not hold him still (InIntro) and he is never tamed (MakeTame)");
            Declare(MerchantAwake, 6, "Humanoid.Awake / Start / GiveDefaultItems, BaseAI.Awake, BaseAI.m_character, MonsterAI.Awake, Character.SetTamed, ZNetView.IsValid",
                                      "Patch_Humanoid_Awake finds no target and no merchant is ever built (P5)");
            Declare(AwakeOrder, 6, "BaseAI.Awake is what assigns m_character, MonsterAI.MakeTame dereferences it on its FIRST line, and Humanoid.Start - not Awake - is what calls GiveDefaultItems",
                                   "the merchant dies inside his own Awake; a body change here is silent", ProbeState.NotProbeable);
            Declare(Merchant, 6, "Character.m_name / m_faction / Faction.Players / IsOnGround, Humanoid.UnequipAllItems, MonsterAI.SetFollowTarget / m_alertRange, BaseAI.SetPatrolPoint / m_aggravatable / m_passiveAggresive / m_randomMoveRange, Player.GetClosestPlayer, ZNetScene.FindInstance / GetPrefab, ZDO.GetZDOID(hashPair), Chat.SetNpcText, Odin.m_despawn, EffectList.Create",
                                 "the merchant cannot be set up, pinned, spoken through or sent away (CargoMerchant)");
            Declare(InventoryOps, 7, "Inventory.RemoveItem(string,int,int,bool) / AddItem(GameObject,int) / CanAddItem(GameObject,int) / CountItems, ObjectDB.GetItemPrefab",
                                     "a delivery cannot be applied (DealApplier)");
            Declare(Comfort, 8, "Player.GetComfortLevel, SEMan.s_statusEffectRested / HaveStatusEffect, ZDOVars.s_baseValue / s_dead / s_playerName",
                                "the client's eligibility report (ComfortReporter, Scheduler)");
            Declare(DayLength, 9, "EnvMan.instance, EnvMan.m_dayLengthSec (long), EnvMan.IsDay()",
                                  "the market's drift half-life falls back to the compiled 1200 s");
            Declare(AdminList, 10, "ZNet.IsAdmin(string), ZNet.GetUID / GetWorldUID / IsDedicated",
                                   "nothing: AdminGate already fails closed on its own");
            Declare(Rpc, 11, "ZRpc.Register<T> and Register(string,RpcMethod.Method), ZNet.GetServerRPC, ZRoutedRpc.Register<T,U>",
                             "the deal wire and the admin wire (DealWire, AdminRpc)");
            Declare(Localisation, 12, "Localization.instance and Localize(string)",
                                      "the terminal shows raw $item_ tokens instead of names");
            Declare(Body, 13, "AssetBundle.LoadFromStream / LoadAsset<T> / LoadAllAssets<T>, the three PlayableGraph Create calls, and the Material/Renderer calls the donor material is copied with",
                              "Ingvar's body is not loaded; the stand-in is kept (BodyLoader)");
        }

        private void Declare(string name, int rank, string what, string degrades, ProbeState state = ProbeState.NotRun)
        {
            var p = new EngineProbe
            {
                Name = name,
                Rank = rank,
                What = what,
                Degrades = degrades,
                State = state,
                Message = state == ProbeState.NotProbeable ? SweepNote : "",
            };
            _order.Add(p);
            _byName[name] = p;
        }

        // ---- reading --------------------------------------------------------------------------------

        /// <summary>Every probe, worst rank first, in a stable order.</summary>
        public IList<EngineProbe> All => _order;

        /// <summary>The probe by name, or null. An unknown name is not an error here; it is an answer.</summary>
        public EngineProbe Find(string name)
        {
            EngineProbe p;
            return name != null && _byName.TryGetValue(name, out p) ? p : null;
        }

        /// <summary>
        /// May the feature that depends on this fact run? TRUE unless the probe RAN and FAILED. A probe
        /// that has not run, could not be probed, or was never registered answers true: a bug in the
        /// registry must never be the thing that turns the mod off.
        /// </summary>
        public bool Ok(string name)
        {
            EngineProbe p = Find(name);
            return p == null || p.State != ProbeState.Failed;
        }

        /// <summary>Why a probe said no, for the one log line the feature writes. "" when it did not.</summary>
        public string Reason(string name)
        {
            EngineProbe p = Find(name);
            return p != null && p.State == ProbeState.Failed ? p.Message : "";
        }

        /// <summary>Probes that actually ran (passed or failed). Not-probeable notes are not probes that ran.</summary>
        public int Run
        {
            get { int n = 0; foreach (EngineProbe p in _order) if (p.State == ProbeState.Passed || p.State == ProbeState.Failed) n++; return n; }
        }

        public int Passed
        {
            get { int n = 0; foreach (EngineProbe p in _order) if (p.State == ProbeState.Passed) n++; return n; }
        }

        public int NotProbeable
        {
            get { int n = 0; foreach (EngineProbe p in _order) if (p.State == ProbeState.NotProbeable) n++; return n; }
        }

        /// <summary>The failures, worst rank first.</summary>
        public IList<EngineProbe> Failed
        {
            get
            {
                var list = new List<EngineProbe>();
                foreach (EngineProbe p in _order) if (p.State == ProbeState.Failed) list.Add(p);
                return list;
            }
        }

        /// <summary>Anything the registry itself refused: an unknown name, a second recording. Never silent.</summary>
        public IList<string> Problems => _problems;

        // ---- writing --------------------------------------------------------------------------------

        /// <summary>
        /// Record one probe's answer. Refused (and reported) for an unknown name and for a probe that has
        /// already been recorded - including a not-probeable note, which nothing may promote to a pass.
        /// Returns whether the answer was taken.
        /// </summary>
        public bool Record(string name, bool ok, string message)
        {
            EngineProbe p = Find(name);
            if (p == null) { _problems.Add("probe '" + (name ?? "") + "' is not registered; its answer was dropped"); return false; }
            if (p.State != ProbeState.NotRun)
            {
                _problems.Add("probe '" + p.Name + "' was already " + p.State.ToString().ToLowerInvariant() + "; the second answer was dropped");
                return false;
            }
            p.State = ok ? ProbeState.Passed : ProbeState.Failed;
            p.Message = message ?? "";
            return true;
        }

        // ---- words ----------------------------------------------------------------------------------

        /// <summary>
        /// The `cargo status` half: `probes 15/15 ok, 4 not probeable[, FAILED: a, b]`. One line, in the
        /// existing one-line-per-source style, and stable enough for the harness to pin.
        /// </summary>
        public string Encode()
        {
            int run = Run;
            var sb = new StringBuilder(64);
            if (run == 0) sb.Append("probes not run");
            else sb.Append("probes ").Append(Wire.Int(Passed)).Append('/').Append(Wire.Int(run)).Append(" ok");

            int notProbeable = NotProbeable;
            if (notProbeable > 0) sb.Append(", ").Append(Wire.Int(notProbeable)).Append(" not probeable");

            IList<EngineProbe> failed = Failed;
            if (failed.Count > 0)
            {
                sb.Append(", FAILED: ");
                for (int i = 0; i < failed.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(failed[i].Name);
                }
            }
            return sb.ToString();
        }

        /// <summary>Every probe, one line each, for `cargo engine`. Worst rank first.</summary>
        public List<string> Report()
        {
            var lines = new List<string>(_order.Count);
            foreach (EngineProbe p in _order)
                lines.Add("[" + Wire.Int(p.Rank) + "] " + p.Name + ": " + p.State.ToString().ToUpperInvariant() +
                          (p.Message.Length > 0 ? " - " + p.Message : "") +
                          "; checks " + p.What + "; on failure " + p.Degrades);
            return lines;
        }
    }
}
