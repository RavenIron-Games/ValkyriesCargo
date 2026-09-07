# Shadows of Midgard v2 — Architecture

**Design lens: shippability.** The organising principle below is *"one structural change per defect class, and nothing else."* Four things change shape: (a) the per-creature awareness record becomes a bounded per-(creature,player) track table, (b) player-intrinsic sensing is hoisted into a per-player profile, (c) the scheduler becomes time-budgeted and local-player-free, (d) `IsAlerted` stops being patched and the real vanilla setter is driven instead. Everything else — the four sensing formulas, the five evaluators, the config keys and defaults, the HUD look, the armor/camo tables — is **ported, not rewritten**, so the diff is reviewable and existing TOMLs keep working.

---

## 0. Build-level fixes (do these first, they are five minutes and they unblock everything)

1. **`ShadowsOfMidgard.csproj`: delete the `<Reference Include="assembly_valheim">` item.** Keep only `assembly_publicizer.dll`. Both share fusion name `assembly_valheim, Version=0.0.0.0`; RAR currently de-dupes and the publicized one wins *by accident*. Rename the file on disk to `assembly_valheim_publicized.dll` and reference exactly one. Once this is explicit, `BehaviorSystem`'s reflection for `SetTargetInfo` / `MoveTo` / `m_targetCreature` is unnecessary — but see §7: we keep soft binding anyway for v1.0 resilience, just as a *fallback ladder* rather than as the only path.
2. **Version unification.** `version.txt` (currently `1.9.1`) drives `AssemblyVersion` and generates `SoMBuild.Version`. Make `ShadowsOfMidgard.ModVersion = SoMBuild.Version` so `[BepInPlugin]`, the assembly, and `ConfigSync.CurrentVersion` cannot disagree. Set `version.txt` to `2.0.0`.
3. **`MinimumRequiredVersion` is hand-edited only.** Never auto-tick it from `version.txt`; the csproj bumps the patch on every build and would hard-kick every client on every rebuild.

---

## 1. File / folder layout

```
ShadowsOfMidgard/
  ShadowsOfMidgard.cs                 Plugin entry: config → bindings → registry → scheduler → patches, in that order.

  Api/
    StealthExemption.cs               FROZEN v1 API. Type name + both IsExempt overloads + ZDOKey const, verbatim.
    SoMInterop.cs                     Additive public API: contract version, key/profile enumeration, Describe(), IsBrainOff().
    SoMKeys.cs                        Every SoM ZDO key string + its cached GetStableHashCode(). Single source of truth.
    Directives.cs                     The `Directives` struct + DirectiveResolver: ZDO keys → resolved per-creature policy.
    ProfileTable.cs                   Named profile bundles ("hunter", "sentinel", …) → Directives overlays.

  Compat/
    LegacyAwareness.cs                AwarenessData (class, all v1 field names) + AwarenessSystem.GetData shim.
    ExternalPin.cs                    Detects third-party writes into AwarenessData and converts them into track floors.

  Core/
    Track.cs                          The per-(creature,player) struct.
    TrackTable.cs                     Fixed-capacity track array: find / insert / evict / prune.
    CreatureState.cs                  Per-creature record: cached components, tracks, aggregate, directives, timers.
    CreatureRegistry.cs               ZDOID → CreatureState. Incremental sweep of BaseAI instances; ownership tracking.
    PlayerProfile.cs                  Per-player intrinsic sensing snapshot (vis/noise/hiding/camo + kinematics).
    PlayerRegistry.cs                 Refreshes PlayerProfiles for all loaded players at a fixed rate.
    Scheduler.cs                      The single MonoBehaviour Update loop. Time-budgeted, cursor-based.
    Budget.cs                         Frame budget: elapsed-ms guard + LOS raycast quota.
    Pools.cs                          Reusable List<T>/Collider[] buffers. No allocation in steady state.

  Sensing/
    SensingMath.cs                    The ONLY per-(creature,player) math. Falloff, FOV, LOS gate, detection integration.
    VisibilitySystem.cs               Ported v1 visibility formula, now producing into PlayerProfile.
    NoiseSystem.cs                    Ported v1 noise formula.
    HidingSystem.cs                   Ported v1 hiding formula.
    CamoSystem.cs                     Ported v1 camo formula (biome computed once).
    VegetationSampler.cs              Grass/bush overlap sampling for EVERY loaded player, not just the local one.

  Brain/
    Decision.cs                       StealthDecision + the four directive structs. Structs, not classes.
    StealthBrain.cs                   Per-creature pass: tracks → aggregate → decision. Orchestrator only.
    StateEvaluator.cs                 Ported verbatim: detection → alertness hysteresis ladder. Now per-track.
    FleeEvaluator.cs                  Ported verbatim. Per-creature (health-driven), aimed at the aggregate target.
    MovementEvaluator.cs              Ported verbatim. Reads the aggregate target's track.
    CombatEvaluator.cs                Ported verbatim.
    GroupEvaluator.cs                 Ported ally-count / ally-call logic, extracted out of StealthBrain.

  Act/
    BehaviorSystem.cs                 Applies a decision to vanilla: target set, MoveTo, search movement.
    AlertDriver.cs                    Drives the REAL vanilla SetAlerted/Alert, debounced. Replaces the IsAlerted patch.
    AllyCall.cs                       Layer-masked, non-allocating nearby-ally alert propagation.

  Net/
    TrackReplication.cs               Writes/reads the replicated detection summary + SoM_Ack. Throttled.

  Bind/
    VanillaBind.cs                    All soft-bound MethodInfo/FieldRef/delegates. Resolved once in Awake.
    PatchInstaller.cs                 Per-patch-class probe + CreateClassProcessor. One missing target ≠ dead mod.
    LayerMasks.cs                     Validated layer masks with logged fallbacks.
    Headless.cs                       SystemInfo.graphicsDeviceType == Null. Never ZNet.IsDedicated().

  Patches/
    P_BaseAI_CanSeeTarget.cs          Prefix only. Per-target from track. Falls through when no track.
    P_BaseAI_CanHearTarget.cs         Same shape.
    P_BaseAI_CanSenseTarget.cs        Same shape.
    P_MonsterAI_UpdateAI.cs           Postfix: apply cached decision. Per-target. Sleep-guarded.
    P_Character_SetMoveDir.cs         Prefix: search-speed scaling. Component-cached guard order.
    P_Humanoid_StartAttack.cs         Prefix: narrow per-target attack gate.
    P_BaseAI_Awake.cs                 Postfix: subscribe to Character.m_onDamaged (owner-side, attacker-aware).
    P_Character_OnDestroy.cs          Prefix: registry + track cleanup.
    P_Player_OnSpawned.cs             Postfix: refresh PlayerRegistry membership promptly.

  Config/
    SOMConfig.cs                      ~40 one-line binds. Locking entry first. CustomSyncedValue for the profile table.
    Binder.cs                         bind + sync + clamp + initial-push + change-push in one call.
    StealthConfigModel.cs             POCO snapshot, same field names/defaults as v1.
    CreatureProfileConfig.cs          Parses the ServerSync'd prefab→profile YAML table.

  UI/
    StealthQuery.cs                   Publishes the local player's threat summary once per pass. HUD reads only this.
    StealthHud.cs                     Lazy canvas built on first Player.m_localPlayer, parented under vanilla Hud.
    StealthGem.cs                     Eye icon. Colour = threat, scale = exposure. Writes only on change.
    NoiseMeter.cs                     Fill bar. Writes only on change.
    SpriteLoader.cs                   Embedded-PNG only. No disk write, no hardcoded plugin folder name.

  Armor/
    ArmorProfile.cs                   Unchanged enums/POCO.
    ArmorProfileSystem.cs             Unchanged classification, cached by item name.
    ArmorUtils.cs                     Equip-hash-cached totals. No per-call GetEquippedItems().

  Util/
    Log.cs                            Gated logging. Every debug string is built inside an `if`.
    EnvironmentUtils.cs               Weather via EnvSetup.m_isWet, with name-matching fallback.
    StealthDebugger.cs                Ported throttled debug sink. All call sites now guard-then-format.
```

---

## 2. Core types (actual declarations)

### 2.1 `Core/Track.cs` — the per-(creature,player) record

```csharp
namespace ShadowsOfMidgard.Core
{
    [System.Flags]
    internal enum TrackFlags : byte
    {
        None      = 0,
        CanSee    = 1 << 0,
        CanHear   = 1 << 1,
        HadLos    = 1 << 2,   // last LOS sample result
        EverSensed= 1 << 3,   // has this pair ever been above threshold
        Pinned    = 1 << 4,   // floored by ExternalPin or a SoM_*Floor directive
        Seeded    = 1 << 5,   // reconstructed from ZDO after ownership handover
    }

    /// <summary>One creature's knowledge of ONE player. 56 bytes, no references, no GC.</summary>
    internal struct Track
    {
        public ZDOID   PlayerId;          // 12 B - the identity key. NOT an instance ref.
        public float   Detection;         // 0..1, integrated per evaluation
        public Vector3 LastKnownPos;      // where this player was last sensed
        public float   TimeSinceSensed;   // seconds since CanSee|CanHear was true
        public float   LastEvalTime;      // Time.time of last evaluation of this pair
        public float   LastLosTime;       // Time.time of last real Physics.Linecast
        public byte    State;             // (byte)VanillaAlertness
        public TrackFlags Flags;

        public bool CanSee  => (Flags & TrackFlags.CanSee)  != 0;
        public bool CanHear => (Flags & TrackFlags.CanHear) != 0;
        public bool Sensed  => (Flags & (TrackFlags.CanSee | TrackFlags.CanHear)) != 0;
    }
}
```

### 2.2 `Core/TrackTable.cs` — bounded storage

```csharp
internal struct TrackTable
{
    public const int Capacity = 8;

    private Track[] _t;          // lazily allocated once per creature, never resized
    public  int     Count;

    public void EnsureAlloc() { if (_t == null) _t = new Track[Capacity]; }
    public ref Track At(int i) => ref _t[i];

    /// <summary>Linear scan over <=8 entries. Faster than any hash for this size.</summary>
    public int IndexOf(ZDOID id)
    {
        for (int i = 0; i < Count; i++) if (_t[i].PlayerId == id) return i;
        return -1;
    }

    /// <summary>Get-or-create. Evicts the least-valuable track when full.</summary>
    public int GetOrCreate(ZDOID id, float now)
    {
        int i = IndexOf(id);
        if (i >= 0) return i;
        if (Count < Capacity)
        {
            i = Count++;
            _t[i] = default;
            _t[i].PlayerId     = id;
            _t[i].LastEvalTime = now;
            _t[i].TimeSinceSensed = 999f;
            return i;
        }
        // Evict: lowest Detection wins; tie-break on staleness.
        int worst = 0; float worstScore = float.MaxValue;
        for (int k = 0; k < Capacity; k++)
        {
            float score = _t[k].Detection * 1000f - (now - _t[k].LastEvalTime);
            if (score < worstScore) { worstScore = score; worst = k; }
        }
        _t[worst] = default;
        _t[worst].PlayerId = id;
        _t[worst].LastEvalTime = now;
        _t[worst].TimeSinceSensed = 999f;
        return worst;
    }

    public void RemoveAt(int i) { _t[i] = _t[--Count]; }

    /// <summary>Drop tracks that are cold AND stale. Called once per creature pass.</summary>
    public void Prune(float now, float idleTtl)
    {
        for (int i = Count - 1; i >= 0; i--)
            if (_t[i].Detection <= 0.001f && (now - _t[i].LastEvalTime) > idleTtl)
                RemoveAt(i);
    }
}
```

### 2.3 `Core/CreatureState.cs`

```csharp
internal sealed class CreatureState
{
    // --- identity & cached components (resolved ONCE, never re-GetComponent) ---
    public ZDOID      Id;
    public Character  Char;
    public BaseAI     Ai;
    public MonsterAI  Monster;      // null for AnimalAI / other BaseAI - see §5.9
    public ZNetView   NView;
    public Humanoid   Humanoid;     // null if not humanoid
    public Transform  Tf;

    // --- ownership & liveness ---
    public bool  Owned;             // NView.IsOwner() as of last visit
    public bool  Valid;             // NView.IsValid() && Char != null
    public float LastSeenSweep;     // Time.time of last registry sweep that saw it

    // --- policy ---
    public Directives Dir;          // resolved from ZDO, revision-cached
    public uint   DirRevision;      // ZDO.DataRevision when Dir was resolved
    public float  DirRecheckAt;     // fallback re-resolve time if DataRevision is unbindable

    // --- per-player knowledge ---
    public TrackTable Tracks;

    // --- aggregate (what non-per-target consumers read) ---
    public VanillaAlertness AggState;
    public float            AggDetection;
    public ZDOID            AggTarget;     // player with the highest detection
    public Vector3          AggLastKnownPos;
    public int              AggTrackIndex; // index into Tracks of AggTarget, -1 if none

    // --- decision (produced by StealthBrain, consumed by patches) ---
    public StealthDecision Decision;
    public float           DecisionTime;   // Time.time when produced

    // --- per-creature (non-per-player) simulation state, ported from v1 ---
    public float SelfHealthPercent;
    public FleeReason CurrentFleeReason;
    public Vector3 FleeDirection;
    public int   NearbyAlliesCount;
    public float NextAllyCallAt;
    public float TimeInCurrentAction;
    public StealthAction CurrentAction;

    // --- scheduling ---
    public float NextEvalAt;
    public float LastEvalTime;
    public byte  Tier;             // 0 near / 1 mid / 2 far / 3 dormant

    // --- alert driving ---
    public float LastAlertTransitionAt;
    public bool  SomWantsAlert;

    // --- replication ---
    public float  LastReplicationAt;
    public byte   ReplicatedState;
    public float  ReplicatedLevel;
    public ZDOID  ReplicatedTarget;

    // --- compat ---
    public AwarenessData Legacy;   // allocated lazily, only if anything touches GetData
}
```

### 2.4 `Core/PlayerProfile.cs` — the hoist that removes the N× cost

```csharp
internal struct PlayerProfile
{
    public ZDOID   Id;
    public Player  Player;          // may be a remote player's local instance
    public bool    Valid;           // !null && !IsDead && nview valid

    // kinematics, refreshed every profile tick
    public Vector3 Position;
    public Vector3 EyePos;
    public Vector3 CenterPos;
    public bool    Crouching;
    public float   Speed;
    public bool    Ghost;           // InGhostMode || InDebugFlyMode -> creature must ignore entirely

    // vanilla replicated scalars (remote-safe, ZDO-backed)
    public float   VanillaStealth;  // Character.GetStealthFactor()
    public float   VanillaNoiseRange;// Character.GetNoiseRange()

    // SoM intrinsic sensing terms - THE FOUR THAT USED TO BE COMPUTED N TIMES
    public float   Visibility;      // VisibilitySystem
    public float   Noise;           // NoiseSystem
    public float   Hiding;          // HidingSystem
    public float   Camo;            // CamoSystem
    public float   AdjustedVis;     // Visibility * (1-Hiding) * (1-Camo), precombined

    // cached sub-terms with their own refresh rates
    public float   ArmorVisPenalty; // recomputed only on equip-hash change
    public float   ArmorNoise;      // ditto
    public int     ArmorCamoCount;  // ditto (+ biome change)
    public int     EquipHash;
    public Heightmap.Biome Biome;   // 2 Hz
    public bool    InShadow;        // 2 Hz, 1 raycast
    public int     GrassHits, BushHits; // ~6 Hz from VegetationSampler

    public float   LastFullRefresh;
}
```

`PlayerRegistry` keeps `PlayerProfile[16]` + `Dictionary<ZDOID,int>`, refreshed by:

```csharp
// PlayerRegistry.Refresh(), called at cfg.ProfileHz (default 10 Hz)
private static readonly List<Player> _buf = new List<Player>(16);
public static void Refresh(float now)
{
    _buf.Clear();
    // Player.GetAllPlayers() returns the LOCAL instance list - which is exactly right:
    // we only simulate creatures we own, and any player relevant to those creatures is loaded.
    var all = Player.GetAllPlayers();
    for (int i = 0; i < all.Count && _buf.Count < 16; i++) if (all[i] != null) _buf.Add(all[i]);
    ...
}
```

**No `Player.m_localPlayer` anywhere outside `UI/`.**

### 2.5 `Api/Directives.cs` — resolved per-creature policy

```csharp
public struct Directives
{
    public bool  BrainOff;
    public byte  AlertFloor;      // 0..3, 255 = unset
    public float DetectFloor;     // 0 = unset
    public float SightRange;      // <=0 = use config
    public float HearRange;
    public float ConeHalfDeg;     // <=0 = use config
    public bool  IgnoreLos;
    public bool  NoFlee;
    public float GiveUpMul;       // 1 = default
    public byte  ContractVersion; // 0 = no v2 keys present
    public bool  LegacyExempt;    // the raw SoMStealthExempt==1 reading
}
```

### 2.6 `Brain/Decision.cs`

```csharp
internal enum StealthAction : byte { Idle, Search, Investigate, Pursue, Attack, Flee }

internal struct CombatDirective   { public bool ShouldAttack; public float AggressionLevel;
                                    public float OptimalAttackRange; public bool ShouldKeepDistance; }
internal struct MovementDirective { public Vector3 SearchTarget; public float SearchRadius; public bool ShouldRun; }
internal struct FleeDirective     { public FleeReason Reason; public Vector3 Direction; }
internal struct GroupDirective    { public bool ShouldCallAllies; public int AllyCount; }

internal struct StealthDecision
{
    public StealthAction     RecommendedAction;
    public ZDOID             TargetPlayer;      // WHO this decision is about. New in v2. Load-bearing.
    public VanillaAlertness  State;
    public float             Detection;
    public Vector3           LastKnownPos;
    public CombatDirective   Combat;
    public MovementDirective Movement;
    public FleeDirective     Flee;
    public GroupDirective    Group;
}
```

`StealthDecision` is a **struct stored inline on `CreatureState`**. v1 allocated one class per evaluation (and one more per cache *hit*). That allocation is gone.

### 2.7 `Compat/LegacyAwareness.cs` — the frozen reflection target

```csharp
namespace ShadowsOfMidgard
{
    /// <summary>
    /// EXTERNAL API. MistsofAvalor reflection-writes CurrentState / DetectionLevel / CanSense /
    /// CanSee / CanHear / CurrentFleeReason / AggressionLevel into this object.
    /// It MUST stay a class, MUST stay public, and these field NAMES must not change.
    /// v2 treats writes into it as an INPUT (see Compat/ExternalPin.cs), not merely as
    /// something to overwrite. The aggregate view is republished after every creature pass.
    /// </summary>
    public sealed class AwarenessData
    {
        public VanillaAlertness CurrentState;
        public float   DetectionLevel;
        public bool    CanSense, CanSee, CanHear;
        public Vector3 LastKnownPosition;
        public float   TimeSinceSeen;
        public FleeReason CurrentFleeReason;
        public float   AggressionLevel;      // ADDED in v2. MoA has probed for it since day one.
        public float   SelfHealthPercent;
        public int     NearbyAlliesCount;
        public bool    IsPartOfGroup;
        public StealthAction CurrentAction;

        // --- external-write detection. Not part of the API; never renamed by a consumer. ---
        internal VanillaAlertness _somState;
        internal float           _somDetection;
        internal bool            _somCanSense;
        internal VanillaAlertness _pinState;
        internal float           _pinDetection;
        internal bool            _pinNoFlee;
        internal float           _pinAggression;
        internal float           _pinUntil;
        internal CreatureState   _owner;
    }

    public static class AwarenessSystem
    {
        /// <summary>FROZEN SIGNATURE. Never returns null. Never throws on null input.</summary>
        public static AwarenessData GetData(Character c)
        {
            if (c == null) return _scratch;                       // v1 threw here. It is public API.
            var st = CreatureRegistry.GetOrCreate(c);
            if (st == null) return _scratch;
            return st.Legacy ?? (st.Legacy = NewLegacy(st));
        }
        public static AwarenessData GetData(BaseAI ai) => GetData(ai?.GetComponent<Character>());
        public static void Clear(Character c) => CreatureRegistry.Remove(c);
        public static void ClearAll() => CreatureRegistry.Clear();
        internal static IEnumerable<KeyValuePair<Character, AwarenessData>> GetAllData() { ... } // kept, unused internally
        private static readonly AwarenessData _scratch = new AwarenessData();
    }
}
```

### 2.8 `Compat/ExternalPin.cs` — turning MoA's writes into a track floor

This is the single highest-leverage compat decision in v2. It means **MistsofAvalor ships unchanged and works better than it does today**, and every one of the interop survey's silent-failure scenarios S2/S3/S4/S6/S8/S9 becomes a non-event, because the fields MoA writes stay real, stay authoritative, and are *read back*.

```csharp
internal static class ExternalPin
{
    /// <summary>
    /// Called at the START of every creature pass, before tracks are integrated.
    /// If anything other than SoM has changed the legacy view since we last wrote it,
    /// latch it as a floor with a TTL. MoA re-pins on a 0.2 s timer; TTL 2 s is ample.
    /// </summary>
    internal static void Poll(CreatureState st, float now, float ttl)
    {
        var d = st.Legacy;
        if (d == null) return;

        bool external =
            d.CurrentState   != d._somState ||
            d.CanSense       != d._somCanSense ||
            Mathf.Abs(d.DetectionLevel - d._somDetection) > 0.0001f;

        if (external)
        {
            if ((int)d.CurrentState > (int)d._pinState) d._pinState = d.CurrentState;
            if (d.DetectionLevel > d._pinDetection)     d._pinDetection = d.DetectionLevel;
            if (d.CurrentFleeReason == FleeReason.None) d._pinNoFlee = true;
            if (d.AggressionLevel > 0f)                 d._pinAggression = d.AggressionLevel;
            d._pinUntil = now + ttl;
        }
        else if (now > d._pinUntil)
        {
            d._pinState = VanillaAlertness.Unaware;
            d._pinDetection = 0f; d._pinNoFlee = false; d._pinAggression = 0f;
        }
    }

    /// <summary>Called at the END of the pass. Republish the aggregate and re-stamp the shadows.</summary>
    internal static void Publish(CreatureState st)
    {
        var d = st.Legacy; if (d == null) return;
        d.CurrentState       = st.AggState;
        d.DetectionLevel     = st.AggDetection;
        d.LastKnownPosition  = st.AggLastKnownPos;
        d.CurrentFleeReason  = st.CurrentFleeReason;
        d.SelfHealthPercent  = st.SelfHealthPercent;
        d.NearbyAlliesCount  = st.NearbyAlliesCount;
        d.CurrentAction      = st.CurrentAction;
        int ai = st.AggTrackIndex;
        d.CanSense = ai >= 0 && st.Tracks.At(ai).Sensed;
        d.CanSee   = ai >= 0 && st.Tracks.At(ai).CanSee;
        d.CanHear  = ai >= 0 && st.Tracks.At(ai).CanHear;

        d._somState = d.CurrentState; d._somDetection = d.DetectionLevel; d._somCanSense = d.CanSense;
    }
}
```

The pin is applied inside `StealthBrain` as a floor on **every** track (documented semantics: a pin is per-creature and applies to all targets), which is exactly what MoA wants — its labyrinth mobs should hunt everyone, not just the host. **This alone fixes interop scenario S9 for free.**

---

## 3. The per-player track model

### 3.1 What is stored

Per `(creature, player)` pair, in `Track` (§2.1): the player's `ZDOID`, an integrated `Detection` in 0..1, that pair's `State` (the same `VanillaAlertness` ladder as v1), `LastKnownPos`, `TimeSinceSensed`, cached `CanSee`/`CanHear`/`HadLos` bits, and two timestamps (`LastEvalTime`, `LastLosTime`).

**What is deliberately NOT per-track:** health, flee reason, flee direction, ally count, action timers, movement output. Those are properties of the creature, not of a relationship, and keeping them on `CreatureState` is what lets the four evaluators be ported almost verbatim.

### 3.2 Creation

Tracks are created lazily, only for players that a creature *could plausibly perceive*:

```csharp
// Inside StealthBrain.Evaluate(CreatureState st, float dt, float now)
float senseRadius = Mathf.Max(st.Dir.SightRange > 0 ? st.Dir.SightRange : cfg.MaxVisualRange,
                              st.Dir.HearRange  > 0 ? st.Dir.HearRange  : cfg.MaxHearingRange);
float gate  = senseRadius + cfg.TrackCreateMargin;      // default +8 m hysteresis margin
float gate2 = gate * gate;
Vector3 pos = st.Tf.position;

var profiles = PlayerRegistry.Profiles;                  // PlayerProfile[]
for (int pi = 0; pi < PlayerRegistry.Count; pi++)
{
    ref PlayerProfile pp = ref profiles[pi];
    if (!pp.Valid || pp.Ghost) continue;                 // ghost/fly admins: never tracked (vanilla parity)
    if ((pp.Position - pos).sqrMagnitude > gate2)
    {
        int existing = st.Tracks.IndexOf(pp.Id);
        if (existing >= 0) DecayOnly(ref st.Tracks.At(existing), dt, now);   // out of range: decay, don't delete yet
        continue;
    }
    int ti = st.Tracks.GetOrCreate(pp.Id, now);
    SensingMath.Integrate(st, ref st.Tracks.At(ti), in pp, dt, now);
}
st.Tracks.Prune(now, cfg.TrackIdleTtl);                  // default 20 s
```

On the **first** visit to a creature that has no `CreatureState` yet, the registry seeds one track from replicated ZDO state (§6.3), so a creature does not forget its target when ownership migrates.

### 3.3 Pruning and bounds

- Hard cap **8 tracks** per creature. Eviction favours keeping high-detection tracks.
- A track is pruned when `Detection <= 0.001` and it has not been evaluated for `TrackIdleTtl` (20 s).
- `CreatureState` is removed when: `Character.OnDestroy` fires; or the registry sweep finds `NView.IsValid() == false`; or the state has not been visited for 60 s.
- `Track` holds **no object references**, so a stale track pins nothing and a dead player's track evaporates on its own.
- Total ceiling: 200 creatures × 8 tracks × 56 B = **89 KB**, allocated once, never churned.

### 3.4 How a patch answers a per-target query

```csharp
// Patches/P_BaseAI_CanSeeTarget.cs
[HarmonyPatch(typeof(BaseAI), nameof(BaseAI.CanSeeTarget), new[] { typeof(Character) })]
internal static class P_BaseAI_CanSeeTarget
{
    [HarmonyPrefix, HarmonyPriority(Priority.Low)]
    private static bool Prefix(BaseAI __instance, Character target, ref bool __result, bool __runOriginal)
    {
        if (!__runOriginal) return false;                 // someone above us already decided. Do not fight.
        if (!Gate.TryOpinion(__instance, target, out CreatureState st, out int ti)) return true; // fall through
        __result = st.Tracks.At(ti).CanSee;
        return false;
    }
}
```

and the shared gate — **this is where the v1 "dead `data == null` guard" is finally live**:

```csharp
internal static class Gate
{
    /// <summary>
    /// Returns true ONLY if SoM has a real, current opinion about (ai, target).
    /// Every false path means "SoM has no opinion" and the caller must let vanilla run.
    /// </summary>
    internal static bool TryOpinion(BaseAI ai, Character target, out CreatureState st, out int ti)
    {
        st = null; ti = -1;
        if (!Runtime.Active) return false;                       // config failed / headless-no-physics
        if (target == null || !target.IsPlayer()) return false;  // creature-vs-creature: pure vanilla
        st = CreatureRegistry.Find(ai);                          // dictionary hit, no GetComponent
        if (st == null || !st.Owned || !st.Valid) return false;  // not ours to answer for
        if (st.Char.IsDead() || st.Char.IsTamed()) return false;
        if (st.Dir.BrainOff) return false;                       // exemption / SoM_BrainOff
        ZDOID pid = target.GetZDOID();
        if (pid.IsNone()) return false;
        ti = st.Tracks.IndexOf(pid);
        if (ti < 0) return false;                                // NO TRACK => NO OPINION => vanilla answers
        // A track that has never been evaluated, or is stale, has no opinion either.
        if (Time.time - st.Tracks.At(ti).LastEvalTime > Runtime.OpinionTtl) return false;  // default 3 s
        return true;
    }
}
```

Two consequences worth stating plainly:

1. **A creature the scheduler has never reached behaves exactly like vanilla.** v1's biggest hidden bug — creatures outside the local player's 64 m bubble being permanently blind and pacified — cannot occur, because "no track" now means "vanilla answers", not "answer `false`".
2. **`FindEnemy` becomes correct.** Vanilla iterates every character and asks `CanSenseTarget(item)`. We now answer per-`item` from that item's own track, and for non-players we don't answer at all. Group stealth works because player A's track and player B's track are independent rows.

### 3.5 Where the aggregate is used, and where it must not be

| Consumer | Uses |
|---|---|
| `CanSee/CanHear/CanSense(target)` | **per-track only** |
| `Humanoid.StartAttack` gate | **per-track** (track of `monster.GetTargetCreature()`) |
| `AlertDriver` (vanilla `SetAlerted`) | aggregate (`max` over tracks) — correct, `m_alerted` is not per-target in vanilla either |
| `MovementEvaluator` / `BehaviorSystem` | aggregate target's track (`AggTarget`) |
| Legacy `AwarenessData` view | aggregate |
| HUD | **the local player's own track**, never the aggregate (§8/§11) |

---

## 4. The scheduler

### 4.1 Shape

One `MonoBehaviour` on a single `DontDestroyOnLoad` object (`SoM_Runtime`) with an `Update()`. **No coroutines anywhere** — v1's coroutine soup (`MigrateCacheCoroutine`, the ally coroutine, the vegetation coroutine, the eval loop) is replaced by four accumulators in one method.

```csharp
private void Update()
{
    float now = Time.time, dt = Time.deltaTime;
    if (!Runtime.Active) return;

    Budget.BeginFrame();                                   // starts the stopwatch, resets the LOS quota

    if (now >= _nextSweep)   { CreatureRegistry.Sweep(now);   _nextSweep   = now + cfg.SweepPeriod;   } // 0.5 s
    if (now >= _nextProfile) { PlayerRegistry.Refresh(now);   _nextProfile = now + cfg.ProfilePeriod; } // 0.1 s
    if (now >= _nextVeg)     { VegetationSampler.Step(now);   _nextVeg     = now + cfg.VegPeriod;     } // 0.15 s, 1 player/step

    Scheduler.Tick(now, dt);                               // the main cursor walk, time-budgeted
    StealthQuery.Publish(now);                             // one struct for the HUD
}
```

### 4.2 The cursor walk

```csharp
internal static class Scheduler
{
    private static int _cursor;

    internal static void Tick(float now, float dt)
    {
        var list = CreatureRegistry.All;                   // List<CreatureState>, stable order
        int n = list.Count; if (n == 0) return;
        int visited = 0;

        while (visited < n && !Budget.Exhausted)
        {
            if (_cursor >= n) _cursor = 0;
            CreatureState st = list[_cursor++];
            visited++;

            if (!st.Valid) continue;
            if (now < st.NextEvalAt) continue;             // not due yet - O(1) skip

            if (!st.Owned)                                 // read-only pass for the HUD
            {
                if (now - st.LastEvalTime >= cfg.RemoteReadPeriod) TrackReplication.ReadRemote(st, now);
                st.NextEvalAt = now + cfg.RemoteReadPeriod;   // 0.5 s
                continue;
            }

            float pairDt = Mathf.Clamp(now - st.LastEvalTime, dt, cfg.MaxEvalTimestep);  // 0.5 s cap, ported
            StealthBrain.Evaluate(st, pairDt, now);
            st.LastEvalTime = now;
            st.NextEvalAt   = now + TierPeriod(st.Tier);
        }
        Budget.RecordRoundTrip(visited, n);
    }

    private static float TierPeriod(byte tier) => tier switch
    {
        0 => cfg.PeriodNear,     // 0.10 s  (<= NearRange, default 25 m to nearest tracked player)
        1 => cfg.PeriodMid,      // 0.33 s  (<= MidRange, default 50 m)
        2 => cfg.PeriodFar,      // 1.00 s
        _ => cfg.PeriodDormant,  // 3.00 s  (no player within sense radius + margin)
    };
}
```

`Tier` is recomputed at the end of each evaluation from the distance to the *nearest tracked player* (not the local player), so tiering is multiplayer-correct.

### 4.3 The budget

```csharp
internal static class Budget
{
    private static readonly System.Diagnostics.Stopwatch _sw = new System.Diagnostics.Stopwatch();
    private static double _limitMs;
    internal static int LosRemaining;

    internal static void BeginFrame()
    {
        _sw.Restart();
        _limitMs      = cfg.FrameBudgetMs;   // default 1.0 ms
        LosRemaining  = cfg.LosPerFrame;     // default 24
    }
    internal static bool Exhausted => _sw.Elapsed.TotalMilliseconds >= _limitMs;
    internal static bool TakeLos()  { if (LosRemaining <= 0) return false; LosRemaining--; return true; }
}
```

**The budget is on cost, not on a count.** v1's `MAX_EVALS_PER_FRAME = 5` was a count and therefore did not adapt; at 20 creatures it silently starved the model to ~10 Hz per creature and at 60 it collapsed to ~3 Hz while `MAX_EVAL_TIMESTEP` clamping quietly ate integration time. Here, over-subscription stretches every creature's interval uniformly and is *observable*: `Budget.RecordRoundTrip` computes the actual full-sweep period and `Runtime` logs a warning (once per 30 s) when it exceeds 2× the near-tier period.

### 4.4 LOS is the only genuinely expensive per-pair term

```csharp
// SensingMath.Integrate, LOS section
bool needLos = !st.Dir.IgnoreLos && trk.State < VanillaAlertness.Alerted;
if (needLos)
{
    if (now - trk.LastLosTime >= cfg.LosRecheckPeriod && Budget.TakeLos())   // 0.25 s
    {
        bool los = !Physics.Linecast(eye, aimPoint, LayerMasks.ViewBlock);
        trk.Flags = los ? (trk.Flags | TrackFlags.HadLos) : (trk.Flags & ~TrackFlags.HadLos);
        trk.LastLosTime = now;
    }
    // else: reuse the cached bit. Stale LOS for <=0.25 s is invisible to players.
}
// If already Alerted, vanilla itself ignores the FOV cone; we skip the cast entirely (v1 cast it and discarded it).
```

Additional gate: skip the cast entirely when `adjustedVis * visFalloff` is already below `VisionThreshold * 0.5` — a player who cannot be seen even with perfect LOS does not need a raycast.

### 4.5 Cost model — 200 creatures × 10 players

Assumptions: player-host owns all 200 creatures (the worst realistic case; on a dedicated-server session each client owns a fraction). Sense radius 50 m. Average 2.5 players within a creature's gate; absolute worst 10.

| Term | Rate | Unit cost | Worst case (10 players in range of all 200) | Typical (2.5) |
|---|---|---|---|---|
| Registry sweep | 2 Hz × ~250 `BaseAI` instances | ~15 ns | 7.5 µs/s | 7.5 µs/s |
| Player profiles | 10 Hz × 10 | ~1.5 µs (cached armor/biome) | 150 µs/s | 150 µs/s |
| Profile expensive tier | 2 Hz × 10 | 1 raycast + 1 `FindHeightmap` | 20 casts/s | 20 casts/s |
| Vegetation sample | 6.6 Hz, 1 player/step | 1 `OverlapSphereNonAlloc(10)` | 7 casts/s | 7 casts/s |
| Creature passes | 200 @ tier ⇒ ≈ 200×3 Hz avg | ~0.4 µs fixed | 240 µs/s | 240 µs/s |
| **Pair integration** | passes × players-in-range | ~45 ns | 600 Hz×10 = 6000 pair/s → **270 µs/s** | 1500 pair/s → 68 µs/s |
| **LOS linecasts** | capped | ~18 µs | **cap 24/frame @60 fps = 1440/s → 26 ms/s (2.6%)** | ~600/s → 11 ms/s |
| Ally calls | ≤ 20 engaged × 1/3 s | masked `OverlapSphereNonAlloc(32)` | 7/s → 0.3 ms/s | 0.3 ms/s |
| ZDO replication | ≤ 2 Hz per *changed* creature | 3 sets | ≤ 400 sets/s | ~120 sets/s |
| Patch answers | `FindEnemy` 200/2 s × ~250 candidates | ~25 ns (dict + ≤8 scan, early-out on non-player) | 25k calls/s → **0.6 ms/s** | 0.6 ms/s |
| **Total CPU** | | | **≈ 28 ms/s ≈ 2.8 % of one core** | **≈ 12 ms/s** |
| **Steady-state allocation** | | | **0 B/s** (target < 2 KB/s incl. debug off) | same |

Compare v1 at C=120/N=20/T=8/P=1: ~15–20 ms/s **and ~1.1 MB/s of garbage**, i.e. roughly one Gen0 per second attributable to SoM. v2 handles 10× the creatures and 10× the players inside the same CPU envelope with essentially no GC.

The dominant term is LOS, it is hard-capped by config, and degrading it degrades *latency of first detection* by at most `LosRecheckPeriod`, never correctness.

### 4.6 Interaction with vanilla's clock

Vanilla ticks AI at a fixed 20 Hz with `dt = 0.05f` (`MonoUpdaters.FixedUpdate`). SoM v2 runs on its own accumulator and integrates with a measured `pairDt`, clamped to `MaxEvalTimestep`. The `MonsterAI.UpdateAI` postfix is **apply-only** — it never evaluates — so the two clocks never race. v1's `Humanoid.StartAttack` fallback that called `Evaluate(monster, p, Time.deltaTime)` from inside an attack prefix, with a different player and a different timebase, is **deleted**; the attack gate now reads the cached decision or falls through.

---

## 5. The patch layer

### 5.1 Doctrine for adversarial coexistence

Four rules, applied uniformly:

1. **Every prefix takes `bool __runOriginal` and returns immediately (touching nothing) when it is `false`.** If any higher-priority prefix already decided to skip the original, SoM does not overwrite their `__result`. v1's `int.MaxValue` prefixes actively fought this.
2. **Prefix priority is `Priority.Low` (200), not `int.MaxValue`.** Lower priority = later. Any mod that wants to win by priority alone wins. `HarmonyBefore("wubarrk.shadowsofmidgard")` continues to resolve because **the Harmony id is unchanged: `"wubarrk.shadowsofmidgard"`**.
3. **No postfixes on sensing methods.** v1 registered prefix + postfix pairs that re-ran the identical guard chain to re-assert the identical value, purely to beat other mods. That is ~50 % of the patch layer's CPU and a genuine sort-order race with MoA's `Priority.Last` + `HarmonyAfter`. v2 cedes the final say. A config toggle `Compat/AssertFinalSay` (default **false**) can re-install a postfix for users with a misbehaving third mod.
4. **"No opinion" always means `return true`.** There is no path in v2 where SoM skips the original and writes a value it is not confident about.

### 5.2 The table

| # | Class | Target | Kind | Priority | Skips vanilla? | Guard order |
|---|---|---|---|---|---|---|
| 1 | `P_BaseAI_CanSeeTarget` | `BaseAI.CanSeeTarget(Character)` | Prefix | `Low` | only with an opinion | `__runOriginal` → `Gate.TryOpinion` |
| 2 | `P_BaseAI_CanHearTarget` | `BaseAI.CanHearTarget(Character)` | Prefix | `Low` | ″ | ″ |
| 3 | `P_BaseAI_CanSenseTarget` | `BaseAI.CanSenseTarget(Character)` | Prefix | `Low` | ″ | ″ |
| 4 | `P_MonsterAI_UpdateAI` | `MonsterAI.UpdateAI(float)` | Postfix | `Low` | n/a | registry lookup → owned → `!IsSleeping` → `!BrainOff` → decision fresh |
| 5 | `P_Character_SetMoveDir` | `Character.SetMoveDir(Vector3)` | Prefix (`ref`) | `Last` (0) | never | `is Player` → registry lookup → `CurrentAction ∈ {Search,Investigate}` → magnitude |
| 6 | `P_Humanoid_StartAttack` | `Humanoid.StartAttack(Character,bool)` | Prefix | `Low` | only with an opinion | `__runOriginal` → registry → owned → `!BrainOff` → target is Player → track exists → track fresh |
| 7 | `P_BaseAI_Awake` | `BaseAI.Awake()` | Postfix | `Normal` | n/a | subscribe `m_onDamaged`; register creature |
| 8 | `P_Character_OnDestroy` | `Character.OnDestroy()` | Prefix | `Normal` | n/a | unregister |
| 9 | `P_Player_OnSpawned` | `Player.OnSpawned(bool)` | Postfix | `Normal` | n/a | poke `PlayerRegistry` |

**Nine patches, down from ten, and two of the ten most damaging ones (`IsAlerted`, both damage postfixes) are gone.**

All targets are declared with **explicit argument-type arrays**. v1's `Humanoid.StartAttack`, `Character.Damage` and `Character.OnDamaged` had none; a single added overload in Valheim 1.0 would throw `AmbiguousMatchException` out of `PatchAll` and silently delete the entire mod.

### 5.3 `P_MonsterAI_UpdateAI` — apply-only, per-target, sleep-guarded

```csharp
[HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateAI), new[] { typeof(float) })]
internal static class P_MonsterAI_UpdateAI
{
    [HarmonyPostfix, HarmonyPriority(Priority.Low)]
    private static void Postfix(MonsterAI __instance, float dt)
    {
        var st = CreatureRegistry.Find(__instance);
        if (st == null || !st.Owned || !st.Valid) return;
        if (st.Dir.BrainOff) return;
        if (st.Char.IsDead() || st.Char.IsTamed()) return;
        if (__instance.IsSleeping()) return;                       // v1 had no sleep guard
        if (Time.time - st.DecisionTime > Runtime.OpinionTtl) return;

        // Resolve the decision's target BY ZDOID. Never Player.m_localPlayer.
        Player target = PlayerRegistry.Resolve(st.Decision.TargetPlayer);
        BehaviorSystem.Apply(st, in st.Decision, target, dt);      // dt is vanilla's 0.05 f, and we USE it
        AlertDriver.Apply(st, Time.time);
    }
}
```

`BehaviorSystem.Apply` is the ported v1 `ExecuteAction`, with three changes: it takes the resolved target rather than the local player; the `Idle` branch only clears `m_targetCreature` if the current target *is* the player SoM decided about (so it can never strip a legitimately-acquired remote target); and every reflection call is wrapped in a bound-availability check that degrades to `MoveTowards` and then to `SetMoveDir`.

### 5.4 `P_Humanoid_StartAttack` — narrowed

```csharp
[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack), new[] { typeof(Character), typeof(bool) })]
internal static class P_Humanoid_StartAttack
{
    // NOTE: parameter names differ between Character (`charge`) and Humanoid (`secondaryAttack`).
    // We inject NEITHER - only __instance - so a rename cannot break the binding.
    [HarmonyPrefix, HarmonyPriority(Priority.Low)]
    private static bool Prefix(Humanoid __instance, bool __runOriginal)
    {
        if (!__runOriginal) return false;
        if (!cfg.HardBlockAttacks) return true;
        var st = CreatureRegistry.Find(__instance); if (st == null || !st.Owned || !st.Valid) return true;
        if (st.Dir.BrainOff || st.Char.IsTamed() || st.Char.IsDead()) return true;
        if (st.Monster == null) return true;                        // v1 BLOCKED here. That was wrong.
        Character tgt = st.Monster.GetTargetCreature();
        if (tgt == null || !tgt.IsPlayer()) return true;            // non-player target: vanilla decides
        int ti = st.Tracks.IndexOf(tgt.GetZDOID()); if (ti < 0) return true;
        ref Track t = ref st.Tracks.At(ti);
        if (Time.time - t.LastEvalTime > Runtime.OpinionTtl) return true;
        if (t.State >= VanillaAlertness.Alerted) return true;       // allowed
        if (st.CurrentFleeReason != FleeReason.None && !st.Dir.NoFlee) return false;
        Log.Combat(st, "blocked: track below Alerted");
        return false;
    }
}
```

This is now genuinely belt-and-braces: with `AlertDriver` writing the *real* `m_alerted` and per-target `CanSeeTarget` being correct, vanilla's own gate (`num3 && canSeeTarget && IsAlerted()`) already refuses the swing. Narrowing it is a compat win — MoA's `Priority.First` prefix on the same method continues to run ahead of us, and its exempt mobs bail at `BrainOff` in one dictionary lookup.

### 5.5 `P_BaseAI_Awake` — the correct damage hook

This replaces **both** v1 damage patches. `Character.Damage(HitData)` only sends an RPC and runs on the *attacker's* machine. `Character.OnDamaged(HitData)` is overridden by `Humanoid` without chaining, so it never fired for greydwarves, draugr, fulings, skeletons, dvergr or trolls — i.e. most of the game.

```csharp
[HarmonyPatch(typeof(BaseAI), "Awake")]
internal static class P_BaseAI_Awake
{
    [HarmonyPostfix]
    private static void Postfix(BaseAI __instance)
    {
        var c = __instance.GetComponent<Character>(); if (c == null) return;
        var st = CreatureRegistry.GetOrCreate(c); if (st == null) return;
        // Exactly what BaseAI.Awake itself does at vanilla L4028. Public field, owner-side,
        // invoked unconditionally from Character.ApplyDamage for every Character subtype.
        c.m_onDamaged = (System.Action<float, Character>)System.Delegate.Combine(
            c.m_onDamaged, (System.Action<float, Character>)((dmg, attacker) => Damage.OnHurt(st, dmg, attacker)));
    }
}

internal static class Damage
{
    internal static void OnHurt(CreatureState st, float dmg, Character attacker)
    {
        if (!st.Owned || st.Dir.BrainOff) return;                  // v1's damage patches ignored exemption
        if (attacker == null || !attacker.IsPlayer()) return;
        ZDOID pid = attacker.GetZDOID(); if (pid.IsNone()) return;
        float now = Time.time;
        int ti = st.Tracks.GetOrCreate(pid, now);
        ref Track t = ref st.Tracks.At(ti);
        t.Detection = Mathf.Max(t.Detection, cfg.DamageDetectionFloor);   // configured, NOT a magic 0.8
        if (t.Detection >= cfg.EngagedThreshold)      t.State = (byte)VanillaAlertness.Engaged;
        else if (t.State < (byte)VanillaAlertness.Alerted) t.State = (byte)VanillaAlertness.Alerted;
        t.LastKnownPos    = attacker.transform.position;
        t.TimeSinceSensed = 0f;
        t.LastEvalTime    = now;
        t.Flags |= TrackFlags.EverSensed;
    }
}
```

Three v1 defects die here at once: wrong network side, Humanoid non-chaining, and the unconditional `Engaged → Alerted` downgrade caused by `OnDamaged`'s postfix running *inside* `Damage`'s postfix via the synchronous local RPC path.

### 5.6 `AlertDriver` — the replacement for the `IsAlerted` patch

**`BaseAI.IsAlerted()` is not patched in v2.** This single deletion repairs, without any further work: vanilla `Alert()` no longer being a no-op; `m_animator.SetBool("alert")`; `ZDOVars.s_alert` replication to every other client; `m_alertedEffects` (the aggro roar); boss `activeBosses` counting; `m_alertedMessage`; the give-up/leash block in `UpdateTarget`; event-creature despawn; `UpdateConsumeItem` (taming); the backstab exploit; Sneak XP; and the `EnemyHud` icon disagreeing between clients. It also lets MistsofAvalor delete its `SuppressAlertForce` re-entrancy bracket.

```csharp
internal static class AlertDriver
{
    internal static void Apply(CreatureState st, float now)
    {
        if (!cfg.DriveVanillaAlert) return;
        bool want = st.AggState >= VanillaAlertness.Alerted;
        bool have = st.Ai.IsAlerted();                              // the REAL field now
        if (want == have) { st.SomWantsAlert = want; return; }
        if (now - st.LastAlertTransitionAt < cfg.AlertDebounce) return;   // 1.0 s: no animator/effect churn

        if (want)
        {
            st.Ai.Alert();                                          // public; handles owner + non-owner RPC
        }
        else
        {
            // Only de-alert if we are confidently below, to avoid flicker against vanilla re-raising.
            if (st.AggDetection > cfg.AlertedThreshold * 0.7f) return;
            VanillaBind.SetAlerted?.Invoke(st.Ai, false);            // soft-bound; absent => leave vanilla alone
        }
        st.LastAlertTransitionAt = now;
        st.SomWantsAlert = want;
    }
}
```

If `SetAlerted` cannot be bound in Valheim 1.0, SoM loses the ability to *lower* alertness early and vanilla's own 30 s give-up handles it. Degraded, never broken.

### 5.7 Coexistence walk-through with MistsofAvalor (unchanged MoA build)

| MoA patch | v2 behaviour |
|---|---|
| `IsAlerted` prefix `Priority.First` + `HarmonyBefore(SoM)` | We no longer patch it. Their prefix runs against vanilla. Their `SuppressAlertForce` bracket becomes redundant but harmless. |
| `IsAlerted` postfix `Priority.Last` + `HarmonyAfter(SoM)` | No SoM patch to be after. Wins trivially. |
| `CanSenseTarget` prefix `First` + `Before` | Runs first (edge still resolves — Harmony id unchanged). Ours runs at `Low`, sees `BrainOff` for their exempt mobs, returns `true`. Vanilla runs. Their postfix at `Last` writes the final `__result`. **Zero contention.** |
| `Humanoid.StartAttack` prefix `First` + `Before` | Runs first. Ours at `Low` with `__runOriginal` respect. Their exempt mobs short-circuit. |
| `MonsterAI.UpdateTarget` prefix | We still don't patch it. |
| Reflection into `AwarenessData` | **Still resolves, and is now honoured** via `ExternalPin` — including `AggressionLevel`, which v1 silently discarded because the field did not exist. |
| `AwarenessSystem.GetData(Character)` probe | Signature and type name unchanged; still never returns null; now null-safe on null input. |
| `StealthExemption.IsExempt(Character)` probe | Verbatim, same semantics (`GetInt(key,0)==1`), now using a cached hash. |

**MoA needs zero changes to ship against v2.** It *may* then delete its guards file, its reflection surface and its bracket per the interop proposal, but that is a separate, optional PR on their side.

### 5.8 DvergrAllies

Zero changes. It writes `SoMStealthExempt` = `1`/`0`, owner-gated. `DirectiveResolver` keeps `== 1` as the exemption test, so an explicit `0` is not-exempt exactly as intended for wild Dvergr.

### 5.9 `AnimalAI` — a stated non-goal

`AnimalAI` is a sibling of `MonsterAI`, not a subclass. v2's `Gate` requires `st.Monster != null` only where behaviour is applied; the three *sensing* patches will happily answer for an `AnimalAI` if a track exists — but the scheduler only creates `CreatureState` for `MonsterAI` by default. A config flag `IncludeAnimalAI` (default **false**, synced) turns it on. Documented as unsupported in 2.0 to keep the blast radius small.

---

## 6. Replicated state

### 6.1 Keys SoM writes

| Key | Type | Written by | Frequency | Why it must replicate |
|---|---|---|---|---|
| `SoM_DetTgt` | `ZDOID` | owner | ≤ 2 Hz, on change | The player this creature is most aware of. Lets non-owning clients render an honest HUD, and lets the next owner seed a track after handover. |
| `SoM_DetLvl` | `float` | owner | ≤ 2 Hz, on Δ ≥ 0.1 | Detection level for that target. Seeds the new owner's track so a creature does not forget mid-chase. |
| `SoM_DetSt` | `int` | owner | ≤ 2 Hz, on change | Alertness ordinal for that target. Cheap and lets the HUD distinguish Suspicious from Engaged. |
| `SoM_LKP` | `Vector3` | owner | ≤ 1 Hz, only when non-zero | Last-known position, so search behaviour survives handover. Optional; behind `cfg.ReplicateLastKnownPos` (default true). |
| `SoM_Ack` | `int` | owner | once per creature | Interop verification (§8). Written only for creatures that carry at least one `SoM_*`/legacy key. |

Everything else stays client-local. In particular **no per-track array is replicated** — the cost/benefit does not justify it, and the single dominant track is what drives every observable behaviour.

Vanilla `ZDOVars.s_alert` and `s_haveTargetHash` are written by **vanilla** again in v2 (because `AlertDriver` drives the real setter and `BehaviorSystem` calls the real `SetTargetInfo`). SoM writes neither directly. That is a strict improvement over v1, where `s_alert` was frozen forever at `false`.

### 6.2 Write discipline

```csharp
internal static class TrackReplication
{
    internal static void Write(CreatureState st, float now)
    {
        if (!st.Owned || !cfg.ReplicateDetection) return;
        if (now - st.LastReplicationAt < cfg.ReplicationPeriod) return;      // 0.5 s floor

        byte  s = (byte)st.AggState;
        float l = st.AggDetection;
        if (s == st.ReplicatedState &&
            Mathf.Abs(l - st.ReplicatedLevel) < cfg.ReplicationDelta &&      // 0.1
            st.AggTarget == st.ReplicatedTarget) return;                     // nothing changed

        var zdo = st.NView.GetZDO(); if (zdo == null) return;
        zdo.Set(SoMKeys.DetTgt, st.AggTarget);
        zdo.Set(SoMKeys.DetLvlHash, l);
        zdo.Set(SoMKeys.DetStHash, (int)s);
        if (cfg.ReplicateLastKnownPos && st.AggTrackIndex >= 0)
            zdo.Set(SoMKeys.LkpHash, st.AggLastKnownPos);

        st.ReplicatedState = s; st.ReplicatedLevel = l;
        st.ReplicatedTarget = st.AggTarget; st.LastReplicationAt = now;
    }
}
```

**Idempotent set, never read-modify-write.** Replicated fields that are accumulated on more than one peer compound; every SoM write is an absolute assignment computed from local state by the sole owner.

Load estimate: 60 creatures actively changing state × 2 Hz × 3–4 fields = ~400 ZDO sets/s worst case, each bumping `DataRevision`. That is well inside what a Valheim session already does for transforms.

### 6.3 Handover seeding

```csharp
// CreatureRegistry, on first visit to a creature with no prior state (or on Owned false->true)
internal static void SeedFromZdo(CreatureState st, float now)
{
    var zdo = st.NView.GetZDO(); if (zdo == null) return;
    ZDOID tgt = zdo.GetZDOID(SoMKeys.DetTgt);
    if (tgt.IsNone()) return;
    float lvl = zdo.GetFloat(SoMKeys.DetLvlHash, 0f);
    if (lvl <= 0.01f) return;
    int ti = st.Tracks.GetOrCreate(tgt, now);
    ref Track t = ref st.Tracks.At(ti);
    t.Detection       = lvl;
    t.State           = (byte)zdo.GetInt(SoMKeys.DetStHash, 0);
    t.LastKnownPos    = zdo.GetVec3(SoMKeys.LkpHash, st.Tf.position);
    t.TimeSinceSensed = cfg.SeedTimeSinceSensed;   // 2 s: assume slightly stale
    t.LastEvalTime    = now;
    t.Flags |= TrackFlags.Seeded | TrackFlags.EverSensed;
}
```

`ZDOMan.ReleaseNearbyZDOS` reassigns ownership every ~2 s as players move; without this, every handover reset detection to zero and the creature forgot who it was chasing. Three ZDO reads, once per handover, fix the 80 % case. Secondary tracks are lost by design and re-acquire within one tier period.

---

## 7. Soft-binding layer

### 7.1 `Bind/VanillaBind.cs`

Resolved **once, explicitly, in `Awake`** — never from a static constructor reached from inside a Harmony patch (v1's `BehaviorSystem` did exactly that, with no ordering guarantee).

```csharp
internal static class VanillaBind
{
    // --- Harmony targets (probed before patching, see PatchInstaller) ---
    internal static MethodBase CanSeeTarget, CanHearTarget, CanSenseTarget,
                               MonsterUpdateAI, SetMoveDir, StartAttack,
                               BaseAiAwake, CharacterOnDestroy, PlayerOnSpawned;

    // --- called members, each with a documented fallback ladder ---
    internal static Action<BaseAI, bool>            SetAlerted;      // fallback: none (Alert() covers "true")
    internal static Action<BaseAI, ZDOID>           SetTargetInfo;   // fallback: direct ZDO bool write
    internal static Func<BaseAI, float, Vector3, float, bool, bool> MoveTo;
                                                                    // fallback: MoveTowards -> SetMoveDir
    internal static Action<BaseAI, Vector3, bool>   MoveTowards;     // public, near-certain
    internal static AccessTools.FieldRef<MonsterAI, Character> TargetCreature;
                                                                    // NOTE: declaring type is MonsterAI, not BaseAI.
                                                                    // v1 bound it on BaseAI -> MissingFieldException ->
                                                                    // swallowed -> CallNearbyAllies was inert forever.
    internal static Func<ZDO, uint> DataRevision;                    // fallback: 1 s time-based re-resolve

    internal static readonly List<string> Missing = new List<string>();

    internal static void Resolve()
    {
        CanSeeTarget    = Probe(typeof(BaseAI),   "CanSeeTarget",   typeof(Character));
        CanHearTarget   = Probe(typeof(BaseAI),   "CanHearTarget",  typeof(Character));
        CanSenseTarget  = Probe(typeof(BaseAI),   "CanSenseTarget", typeof(Character));
        MonsterUpdateAI = Probe(typeof(MonsterAI),"UpdateAI",       typeof(float));
        SetMoveDir      = Probe(typeof(Character),"SetMoveDir",     typeof(Vector3));
        StartAttack     = Probe(typeof(Humanoid), "StartAttack",    typeof(Character), typeof(bool));
        BaseAiAwake     = Probe(typeof(BaseAI),   "Awake");
        CharacterOnDestroy = Probe(typeof(Character), "OnDestroy");
        PlayerOnSpawned = Probe(typeof(Player),   "OnSpawned",      typeof(bool));

        SetAlerted    = Bind<Action<BaseAI,bool>>(typeof(BaseAI), "SetAlerted", typeof(bool));
        SetTargetInfo = Bind<Action<BaseAI,ZDOID>>(typeof(BaseAI), "SetTargetInfo", typeof(ZDOID));
        MoveTowards   = Bind<Action<BaseAI,Vector3,bool>>(typeof(BaseAI), "MoveTowards", typeof(Vector3), typeof(bool));
        MoveTo        = Bind<Func<BaseAI,float,Vector3,float,bool,bool>>(typeof(BaseAI), "MoveTo",
                            typeof(float), typeof(Vector3), typeof(float), typeof(bool));
        try { TargetCreature = AccessTools.FieldRefAccess<MonsterAI, Character>("m_targetCreature"); }
        catch { Missing.Add("MonsterAI.m_targetCreature"); }
        BindDataRevision();
        Log.Capabilities();   // ONE consolidated line listing everything bound and everything missing
    }
}
```

### 7.2 `Bind/PatchInstaller.cs` — one missing target must not delete the mod

```csharp
internal static class PatchInstaller
{
    internal static readonly List<string> Installed = new List<string>();
    internal static readonly List<string> Skipped   = new List<string>();

    internal static void InstallAll(Harmony h)
    {
        Try(h, typeof(P_BaseAI_CanSeeTarget),   VanillaBind.CanSeeTarget);
        Try(h, typeof(P_BaseAI_CanHearTarget),  VanillaBind.CanHearTarget);
        Try(h, typeof(P_BaseAI_CanSenseTarget), VanillaBind.CanSenseTarget);
        Try(h, typeof(P_MonsterAI_UpdateAI),    VanillaBind.MonsterUpdateAI);
        Try(h, typeof(P_Character_SetMoveDir),  VanillaBind.SetMoveDir);
        Try(h, typeof(P_Humanoid_StartAttack),  VanillaBind.StartAttack);
        Try(h, typeof(P_BaseAI_Awake),          VanillaBind.BaseAiAwake);
        Try(h, typeof(P_Character_OnDestroy),   VanillaBind.CharacterOnDestroy);
        Try(h, typeof(P_Player_OnSpawned),      VanillaBind.PlayerOnSpawned);

        // Hard requirement: without per-target sensing there is no mod. Degrade to fully inert.
        if (!Installed.Contains(nameof(P_BaseAI_CanSeeTarget)) &&
            !Installed.Contains(nameof(P_BaseAI_CanSenseTarget)))
        { Runtime.Active = false; Log.Error("core sensing patches unavailable - SoM is inert this session."); }
    }

    private static void Try(Harmony h, Type patchClass, MethodBase target)
    {
        if (target == null) { Skipped.Add(patchClass.Name); Log.Warn($"skip {patchClass.Name}: target not found"); return; }
        try { h.CreateClassProcessor(patchClass).Patch(); Installed.Add(patchClass.Name); }
        catch (Exception e) { Skipped.Add(patchClass.Name); Log.Warn($"skip {patchClass.Name}: {e.Message}"); }
    }
}
```

`PatchAll` is **never called**. v1 used it inside a single swallow-everything try/catch, so one renamed method in Valheim 1.0 would silently remove every patch after the failure point while still logging "loaded successfully".

### 7.3 Other hardening

- **`Runtime.Active`** is a single master switch. Set `false` if config binding failed, if core patches are missing, or if `Headless.NoGraphics` and `cfg.DisableOnHeadless` (default true). When `false`, every `Gate.TryOpinion` returns `false` and the game is byte-for-byte vanilla. **The failure floor is vanilla, never "maximally visible"** — v1's `VisibilitySystem.GetVisibility` returned `1f` when config was null, so a config failure meant every creature saw you perfectly, forever, silently.
- **`Headless.NoGraphics = SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null`.** Never `ZNet.IsDedicated()` — it is hardcoded `false` in the reference assembly SoM compiles against.
- **`LayerMasks`** validates every `LayerMask.GetMask` result: a mask of `0` means a renamed layer and degrades to "nothing blocks sight", so we log an error and fall back to a hardcoded literal mask, then to `Physics.DefaultRaycastLayers`. The view-block mask is first attempted by reflecting `BaseAI.m_viewBlockMask` (private static), with the seven-name literal as fallback — so SoM stays in lockstep with vanilla if 1.0 edits the list.
- **`ZDO.GetInt(int hash, int)`** everywhere; every key string is hashed once into a `static readonly int` in `SoMKeys`. v1 re-hashed `"SoMStealthExempt"` (16 chars) on every one of seven patch sites, per creature, per tick.
- **Weather** via `EnvSetup.m_isWet` (soft-bound) with the v1 name-matching as fallback — `"ThunderStorm"` contains neither `"rain"` nor `"snow"` and is currently missed.
- **`Heightmap.FindBiome(point)`** preferred over `GetBiome(point)`; the latter has optional parameters, so the call site bakes in today's defaults and would throw `MissingMethodException` if 1.0 changes them.
- **`RenderSettings.sun`** is never assigned by Valheim. Bind `EnvMan.instance.m_dirLight` first, fall back to `RenderSettings.sun`, fall back to `EnvMan.IsDaylight()` as a boolean. Logged at capability time.
- **Creature enumeration** ladder: `BaseAI.BaseAIInstances` → `BaseAI.GetAllInstances()` → `Character.GetAllCharacters()`.

---

## 8. Cross-mod compat API

### 8.1 Frozen surface (byte-identical semantics)

```csharp
namespace ShadowsOfMidgard
{
    public static class StealthExemption
    {
        public const string ZDOKey = "SoMStealthExempt";
        public static bool IsExempt(Character c);   // == "the legacy key is exactly 1". NOT "the brain is off".
        public static bool IsExempt(BaseAI ai);
    }
    public sealed class AwarenessData { /* §2.7 */ }
    public static class AwarenessSystem { public static AwarenessData GetData(Character c); /* … */ }
    public enum VanillaAlertness { Unaware = 0, Suspicious = 1, Alerted = 2, Engaged = 3 }
    public enum FleeReason { None = 0, /* … v1 members verbatim … */ }
}
```

Three invariants, and they are the whole contract: `StealthExemption` keeps its type name, both overloads and its `== 1` reading; `AwarenessData` stays a **public class** with the v1 field names; `AwarenessSystem.GetData(Character)` stays a **static** method with exactly one `Character` parameter that **never returns null**. `AggressionLevel` is *added*, closing the long-standing dead probe.

`VanillaAlertness.Alerted` and `FleeReason.None` must keep those exact member names — MoA calls `Enum.Parse` on them with no try/catch, so removing them throws every frame from its pinner.

### 8.2 Additive surface

```csharp
public static class SoMInterop
{
    public const int ContractVersion = 2;

    public static int      GetContractVersion();
    public static string[] SupportedKeys();       // e.g. { "SoM_Contract", "SoM_BrainOff", "SoM_AlertFloor", … }
    public static string[] SupportedProfiles();   // { "default","vanilla","sentinel","hunter","ambusher","oblivious" }
    public static string[] RetiredKeys();         // keys this build recognises but no longer honours
    public static bool     Supports(string keyOrProfile);
    public static string[] Capabilities();        // which soft bindings resolved: "alert.set", "ai.moveto", …

    public static bool   IsBrainOff(Character c);            // the RESOLVED question
    public static string Describe(Character c);              // rendered from ResolveDirectives - never a 2nd impl
    public static string DescribeTrack(Character c, Player p);// null-safe p => falls back to creature level
}
```

`Describe` output format (stable, append-only):

```
som=2;src=zdo;brain=on;legacy_exempt=0;profile=hunter;alertfloor=2;detectfloor=0.50;
sight=60.0;hear=60.0;cone=180.0;los=ignore;flee=off;giveup=1.0;tracks=3;agg=Engaged/0.91
```

### 8.3 Tier-2 ZDO keys

| Key | Type | Range | Absent ⇒ | Semantics |
|---|---|---|---|---|
| `SoM_Contract` | int | ≥ 2 | v1 mode | **Mandatory** whenever any other `SoM_*` key is written. |
| `SoM_BrainOff` | int | 0/1 | 0 | v2 spelling of exemption. |
| `SoM_AlertFloor` | int | 0..3 | unset | Floors **every** track's alertness. `2` opens the attack gate. |
| `SoM_DetectFloor` | float | 0..1 | 0 | Floors every track's detection. |
| `SoM_SightRange` | float | > 0 | `cfg.MaxVisualRange` | Per-creature visual range. |
| `SoM_HearRange` | float | > 0 | `cfg.MaxHearingRange` | Per-creature hearing range. |
| `SoM_ConeHalf` | float | 0..180 | `cfg.VisionConeHalfAngle` | `180` = omnidirectional. |
| `SoM_IgnoreLoS` | int | 0/1 | 0 | Skip the LOS requirement (also skips the raycast — a perf directive too). |
| `SoM_NoFlee` | int | 0/1 | 0 | Suppress `FleeEvaluator`. |
| `SoM_GiveUpMul` | float | > 0 | 1.0 | Multiplier on search/give-up durations. |
| `SoM_Profile` | string | see below | unset | Named bundle. **Only ever adds; never subtracts.** |
| `SoM_Ack` | int | — | — | **SoM writes, consumers read.** |

Profiles: `default` (nothing), `vanilla` (`BrainOff=1`), `sentinel` (`AlertFloor=2, DetectFloor=0.5, IgnoreLoS=1, NoFlee=1`), `hunter` (sentinel + `Sight=60, Hear=60, Cone=180, GiveUpMul=99`), `ambusher` (`Cone=30, Sight=15, GiveUpMul=0.25`), `oblivious` (`Sight=1, Hear=1`).

### 8.4 Precedence — total order, no conditionals on the consumer side

1. `SoM_Contract >= 2` present → resolve Tier 2. `SoMStealthExempt` is ignored for gating and reported by `Describe` as `legacy_exempt=1 (superseded)`. INFO once per prefab.
2. Else `SoMStealthExempt == 1` → brain off (frozen v1 behaviour).
3. Else → config defaults, brain on.
4. Any `SoM_*` key present *without* `SoM_Contract` → WARN once per prefab, ignore Tier 2, fall through to 2/3.

Within (1), explicit keys beat `SoM_Profile`; the profile fills only what was not explicitly set.

**Why (1) inverts the obvious precedence:** it lets a consumer write `SoMStealthExempt=1` **and** the Tier-2 keys, unconditionally, forever. An old SoM sees the exempt flag and stands aside (today's known-good behaviour). A new SoM sees `SoM_Contract=2` and honours the directives instead. No SoM at all and both are inert ints. **Zero conditions on the consumer, zero mod knowledge in SoM.**

### 8.5 `SoM_Ack` — reflection-free verification

When SoM resolves a creature carrying at least one interop key and owns the ZDO, it writes `SoM_Ack = ContractVersion` once (read-compare guarded). Consumers then verify with three lines and no reflection:

```csharp
int ack = zdo.GetInt("SoM_Ack", 0);
if (ack == 0) { /* SoM never resolved this creature */ }
if (ack < 2)  { /* older SoM; my v2 directives are ignored */ }
```

This is strictly stronger than probing `IsExempt`, because it proves SoM *reached* the creature, and it is independent of anything the consumer patches.

### 8.6 The one-resolver invariant

`DirectiveResolver.Resolve(CreatureState) → Directives` is the only implementation. The patches gate on it; `Describe` renders it. There is no second code path that could describe a policy the brain does not actually apply. This is the direct application of MoA's own v0.0.9 lesson: a self-description computed by a parallel implementation is worse than none.

Resolution is cached per creature and invalidated on `ZDO.DataRevision` (soft-bound; if the member is unbindable, fall back to a 1 s re-read). Cold path is two hashed lookups (`SoM_Contract`, `SoMStealthExempt`); if both are default, the creature short-circuits to config defaults and nothing else is read — cheaper than v1's seven `IsExempt` calls per creature per tick.

---

## 9. Config / ServerSync layer

### 9.1 `Config/Binder.cs`

```csharp
internal static class Binder
{
    internal static ConfigEntry<float> F(ConfigFile file, ConfigSync sync, string section, string key,
                                         float def, float min, float max, string desc, Action<float> apply)
    {
        var e = file.Bind(section, key, def, new ConfigDescription(desc, new AcceptableValueRange<float>(min, max)));
        if (sync != null) sync.AddConfigEntry(e);          // sync == null is the EXPLICIT, greppable "local only"
        void Push() => apply(Sanitize(e.Value, min, max));
        e.SettingChanged += (_, __) => Push();
        Push();                                            // initial value, structurally impossible to forget
        return e;
    }
    internal static ConfigEntry<bool> B(...);  internal static ConfigEntry<int> I(...);
    private static float Sanitize(float v, float min, float max)
        => (float.IsNaN(v) || float.IsInfinity(v)) ? min : Mathf.Clamp(v, min, max);
}
```

102 lines of triplicated boilerplate collapse to ~40 one-liners, each min/max written **once** (v1 wrote them twice per entry, 68 chances for the clamp to disagree with itself), and every entry gains a real `AcceptableValueRange` so ConfigurationManager renders sliders instead of free-text boxes.

### 9.2 `SOMConfig.Init` — order is load-bearing

```csharp
private static readonly ConfigSync Sync = new ConfigSync(ShadowsOfMidgard.ModGUID)
{
    DisplayName            = ShadowsOfMidgard.ModName,
    CurrentVersion         = ShadowsOfMidgard.ModVersion,   // == SoMBuild.Version
    MinimumRequiredVersion = "2.0.0",                       // HAND-BUMPED ONLY
    ModRequired            = true,                          // owner-client simulates; a vanilla client owning
                                                            // a mob would run vanilla perception on it
};

public static void Init(ConfigFile file)
{
    Active = new StealthConfigModel();                       // defaults land BEFORE anything can throw
    UI     = new UiConfigModel();

    ServerConfigLocked = file.Bind("0 - General", "Lock Configuration", true,
        new ConfigDescription("If on, the server's values overwrite every client's and only admins may change them."));
    Sync.AddLockingConfigEntry(ServerConfigLocked);           // BEFORE any synced entry, so ReadOnly is right on first render

    BindLocal(file);                                         // UI + debug + perf, sync: null
    BindSynced(file, Active);                                // ~40 Binder one-liners
    CreatureProfiles = new CustomSyncedValue<string>(Sync, "creatureprofiles", "", priority: 10);
    CreatureProfiles.ValueChanged += CreatureProfileConfig.Reload;
}
```

Two v1 defects fixed here. **No locking entry** meant `IsLocked` was permanently `false`, the admin gate was dead, and *any non-admin client editing its own TOML broadcast that value to every player on the server* — a client could set `MaxVisualRange = 5` server-wide. **`ModRequired = false`** meant the client skipped sending its version entirely and a vanilla client could join.

Each `BindSynced` group is individually try/caught, so a single bad entry cannot take down `Active`.

### 9.3 Synced vs local

**Synced (gameplay):** everything in v1's sections `1 - Systems` … `8 - Detection Ranges` (same section names, same keys, same defaults — existing TOMLs keep working), plus new gameplay-affecting entries: `MaxEvalTimestep`, `NearbyAllyRadius`, `AllyCallCooldown`, `TrackIdleTtl`, `TrackCreateMargin`, `EvaluationRange`, `DamageDetectionFloor`, `ExternalPinTtl`, `HardBlockAttacks`, `DriveVanillaAlert`, `AlertDebounce`, `ReplicateDetection`, `ReplicationDelta`, `IncludeAnimalAI`.

**Local (perf & presentation):** `FrameBudgetMs`, `LosPerFrame`, `LosRecheckPeriod`, `PeriodNear/Mid/Far/Dormant`, `NearRange`, `MidRange`, `ProfilePeriod`, `SweepPeriod`, `VegPeriod`, `RemoteReadPeriod`, `PerfRangeCap` (may only *lower* the synced `EvaluationRange`, never raise it), `OpinionTtl`, `DisableOnHeadless`, all `UI/*`, all `9 - Debug/*`, `Compat/AssertFinalSay`.

**`CustomSyncedValue<string> creatureprofiles`** carries a YAML prefab→profile table (`Greydwarf: ambusher`), which ServerSync fragments and Deflate-compresses for free. A `ConfigEntry` cannot express a table; this is the native mechanism.

### 9.4 Stated caveat

ServerSync's authority is the **server**; SoM's simulation authority is the **owning client**. ServerSync guarantees every peer agrees on the *numbers*. It guarantees nothing about the *simulation*. That is what per-player tracks, honest `SetAlerted`, and `SoM_Det*` replication are for.

---

## 10. What is deleted from v1, and why

| Deleted | Why |
|---|---|
| **`BaseAI_StealthBrain_IsAlerted_Patch`** (prefix + postfix) | Neutered `Alert()` (guarded by the getter it forced), froze `ZDOVars.s_alert` at `false` for all remote clients, killed the alert animator bool and the aggro roar, broke boss counting and alert messages, broke the give-up/leash block, made event creatures immortal after raids, stalled taming via `UpdateConsumeItem`, gifted a permanent backstab exploit, corrupted Sneak XP, and made two clients see contradictory `EnemyHud` icons. Replaced by `AlertDriver` calling the real setter. |
| **`Character_Damage_Patch`** | Postfixes the RPC *sender*, which runs on the attacker's machine, then requires ownership — so the awareness bump was lost in every real multiplayer hit. Also demoted `Engaged → Alerted` unconditionally, and ignored `StealthExemption` entirely. |
| **`Character_OnDamaged_Patch`** | `Humanoid.OnDamaged(HitData)` overrides without chaining, so it never fired for greydwarves, draugr, fulings, skeletons, dvergr or trolls. Also ignored exemption. |
| **Both replaced by** `P_BaseAI_Awake` + `Character.m_onDamaged` | Public delegate, invoked unconditionally on the owner for every `Character` subtype, and carries the attacker. It is what `BaseAI.Awake` itself subscribes to. |
| **All six sensing postfixes** | Re-ran the identical 4-`GetComponent` guard chain to re-assert the identical value the prefix already wrote. ~50 % of the patch layer's CPU, and a live sort-order race against MoA's `Priority.Last` + `HarmonyAfter`. |
| **`_harmony.PatchAll(...)`** | All-or-nothing. One unresolvable target aborts the lot, the exception is swallowed, and the plugin still logs "loaded successfully". Replaced by per-class probe + `CreateClassProcessor`. |
| **`StealthBrain._evalCache` + `StableAIKey` + `MigrateCacheCoroutine`** | The key was `(ZDOID.UserID, GetInstanceID())`; `UserID` is the *spawning peer's* id, identical for everything that peer spawned, so the key degenerated to a per-process handle. The migration coroutine was a no-op rename that leaked an iterator + two closures on **every** evaluation while `NetworkUid == 0`. Decisions now live inline on `CreatureState`. |
| **`Dictionary<Character, AwarenessData>`** | Keyed on a `UnityEngine.Object`, pruned only by `OnDestroy`, walked in full every frame by the HUD. Replaced by `Dictionary<ZDOID, CreatureState>`. |
| **`StealthOvermind`** (the coroutine) | `Player.m_localPlayer`-centred, O(all loaded characters) per frame with no spatial gate, allocated a class per tracked creature per frame, tiered in *frames*, capped by a *count* not a cost, and halted entirely when the local player died. |
| **`CoroutineManager.UpdateNearbyAlliesCoroutine`** | Copied the whole awareness dictionary's keys per outer lap, `GetComponent<MonsterAI>()` twice per candidate, one creature per frame, and a `Collider[30]` that truncated silently in exactly the dense case group logic exists for. Folded into `GroupEvaluator`, run per-creature on the ally-call cooldown with a masked, adequately-sized buffer. |
| **`CoroutineManager.StopManagedCoroutine`, `StealthBrain.ClearCache/ClearAllCaches`, `AwarenessSystem.ClearAll` (as dead code), `BehaviorSystem.ExecuteCombat/GetCombatCooldown/SetCombatStance`, `RaycastUtils.SkyVisible/SampleOcclusion/InTallGrass`, `EnvironmentUtils.GetAmbientLight`, `HidingSystem.VegetationMask`** | Zero call sites. `SampleOcclusion` allocated a `Vector3[6]` per call. |
| **Every unconditional interpolated debug string** — `StealthBrain:270`, `VisibilitySystem:40`, `NoiseSystem:21`, `HidingSystem:34`, `CamoSystem:55`, `BehaviorSystem:209,215` | Arguments are evaluated at the call site, so the `net472` `string.Format` + boxing ran with debug **off**. ~46 allocations per evaluation, ≈700 KB/s — about 65 % of v1's total GC pressure. Every call site is now `if (Log.AiDebug) { … }`. |
| **`SpriteLoader`'s disk path and disk write** | Hardcoded the plugin folder name `"ShadowsOfMidgard"` (wrong under a Gale/Hexium profile), and silently `File.WriteAllBytes` into the user's plugins tree. Embedded-only now, addressed as `"ShadowsOfMidgard.Assets.eye.png"` with the manifest scan as fallback. |
| **`StealthUIRoot`'s `EventSystem` creation** | Ran during the chainloader, so it always won `EventSystem.current` and permanently disabled Valheim's own — breaking gamepad navigation and the key-rebinding dialog, feeding legacy `Input.*` axes through a stock `StandaloneInputModule`, and making `IsPointerOverGameObject()` return true over two invisible widgets on the character-select screen. v2 builds the canvas lazily on first `Player.m_localPlayer`, parents it under vanilla's `Hud`, and keeps `raycastTarget = false` except in an explicit reposition mode. |
| **`StealthUIController.Update`'s per-frame work** | `GetVisibility` + `GetNoise` (2 `GetEquippedItems()` lists, 4 `ToLowerInvariant()`, a 20 m raycast, 2 eager debug strings) plus a boxed dictionary enumerator and an O(all-creatures) distance walk, every frame — duplicating work the brain had already done. Replaced by reading one `StealthQuery` struct published once per scheduler pass, and writing `color`/`localScale`/`sizeDelta` **only on change** (v1 rebuilt its canvas batch every frame, forever). |
| **`AIAuthority`'s server fallback** (`nview == null → ZNet.IsServer()`) | Contradicted by the dedicated-server dossier: there are no creature instances near players on a dedicated server, so the server cannot be the simulator. Now `false`. |
| **`cfg.GrassBonus` (visibility)** as dead config | Bound and ServerSync'd, never read — `VisibilitySystem` hardcoded `0.15f`/`0.6f`. v2 reads the entry. |
| **The duplicate `assembly_valheim` csproj reference** | §0. |

### What is deliberately **NOT** rewritten

- **The four sensing formulas.** `Clamp01(light − shadow − grass − weather + movement + armor)` and its three siblings are ported term-for-term into `PlayerProfile`. Their known quirks (grass double-counted, `Clamp01` saturating armor tiers, weapons counting as armor, `RenderSettings.sun` possibly always null) are **documented, logged where detectable, and fixed in 2.1** — not in the release that changes the whole data model. One behavioural variable at a time.
- **The five evaluators.** `StateEvaluator`'s hysteresis ladder, `FleeEvaluator`'s thresholds, `MovementEvaluator`'s deliberate `Vector3.zero` for Alerted/Engaged (vanilla owns pursuit), `CombatEvaluator`'s priority scoring and `GroupEvaluator`'s ally logic keep their exact arithmetic. Only their *inputs* change: `(ref Track, in PlayerProfile, in Directives, CreatureState)` instead of `(AwarenessData, Player)`.
- **Config section names, key names and defaults.** Existing `wubarrk.shadowsofmidgard.cfg` files load unchanged. New entries only.
- **The HUD's visual design.** Same gem, same meter, same colours. Only its data source, its lifecycle and its write discipline change.
- **`ArmorProfileSystem` / `CamoSystem` tables.** Same keywords, same tiers, same biome map — including the rows that probably match no vanilla item token. Correcting them is a balance change, not an architecture change.
- **`MonsterAI.UpdateTarget`, `FindEnemy`, `MonsterAI.SetTarget`, `Character.RPC_Damage`** stay unpatched. v2 achieves the same effects through the public methods they already call, which is both more v1.0-resilient and far less likely to collide with another mod.
- **`m_targetCreature`, `m_timeSinceSensedTargetCreature`, `m_updateTargetTimer`** are read but never *written* except through `BehaviorSystem.SetTarget` on the existing, already-soft-bound path. No new private-field writes are introduced.
- **Per-track ZDO replication of *all* tracks.** Only the dominant track replicates. The cost/benefit does not justify a packed blob in 2.0.

---

## 11. Acceptance test

One scenario decides whether the whole redesign works end to end:

> Player A crouches two metres from three greydwarves that are actively fighting player B.
>
> - A's gem is **blue, small and quiet**. (v1 returns `Engaged` here.)
> - The greydwarves' `CanSeeTarget(A)` is `false` and `CanSeeTarget(B)` is `true`, **from the same creature, in the same frame**.
> - `FindEnemy` continues to select B, not A, even though A is closer.
> - A's backstab lands at full multiplier; B's does not.
> - Both A's and B's `EnemyHud` show the **same** alert icon on the same greydwarf.
> - A gains Sneak XP; B does not.

If that reads correctly, per-player tracks, honest `SetAlerted`, per-target patch answers and the HUD rewrite are all verified simultaneously. If it does not, none of them are.