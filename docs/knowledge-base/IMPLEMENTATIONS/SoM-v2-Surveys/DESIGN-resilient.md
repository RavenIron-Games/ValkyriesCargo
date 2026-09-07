# Shadows of Midgard v2 — Architecture (resilience / multiplayer-correctness / contract lens)

**Companion to, and deliberate alternative to, `DESIGN-pragmatic.md`.** Where the two agree I say so briefly and move on; where they differ I argue the difference explicitly. Every vanilla claim is cited into
`c:\WubarrkCODING\libs-Tools\DECOMPILED ASSEMBLY VALHEIM\assembly_valheim.decompiled.cs` as `L####`.

This design optimises for brief goals **#1 (Valheim v1.0 forward-compat)** and **#2 (100 % multiplayer correctness)**, and treats the **tiered cross-mod contract** as a first-class subsystem rather than an appendix. Goal #4 (lower overhead) is met — the cost model in §5 lands at ≈2.3 % of one core at 200 creatures × 10 players — but it is met *second*, and §13 says exactly where I paid for it.

---

## 0. FACT BASE — three corrections that change the design space

Three things in the survey set are stale or wrong. All three were re-verified against source on disk today. Each one moves a load-bearing decision.

### 0.1 MistsofAvalor is 0.1.2, not 0.0.9. The reflection consumer no longer exists.

`RESUME.md`'s "MoA no longer stands down… reflection-writes into `AwarenessData`… `HarmonyBefore("wubarrk.shadowsofmidgard")`" described MoA **0.0.9**. Verified against `c:\WubarrkCODING\MistsofAvalor` as it stands:

- `Compat\AvalorStealthGuards.cs` **does not exist**. `grep -rn "HarmonyBefore\|HarmonyAfter"` over the tree returns **zero hits**.
- `Compat\AvalorStealthCompat.cs` is 244 lines (was 590). The single occurrence of `AwarenessData` / `AwarenessSystem` in the whole mod is a past-tense comment at `AvalorStealthCompat.cs:21` describing the removed mechanism.
- MoA patches **none** of `BaseAI.IsAlerted`, `BaseAI.CanSenseTarget`, `Humanoid.StartAttack`, `MonsterAI.UpdateTarget`, `MonsterAI.UpdateAI`.
- What MoA does now (`AvalorStealthCompat.TendAll`, `:125-159`): every 0.2 s, for every tracked creature it **owns**, re-stamp `SoMStealthExempt = 1` if absent, then `ReassertHunt` — `ai.Alert()` for the Warden, `AvalorMobDirector.MakeItHunt` otherwise. Pure vanilla API. That is the entire integration.
- MoA's presence detector is now `Chainloader.PluginInfos.ContainsKey("wubarrk.shadowsofmidgard")` **and** `AccessTools.TypeByName("ShadowsOfMidgard.StealthExemption")` (`:83-84`), deferred to the first `Update` (`AvalorHuntPinner.Update`, `:231-242`) precisely because BepInEx had not yet *loaded* SoM's assembly during MoA's `Awake`. It gates **nothing** — all three branches produce the same behaviour and differ only in the log line.

`c:\WubarrkCODING\ShadowsOfMidgard\AVALOR_COMPAT_HANDOFF.md` §10 states this from MoA's own side, and adds the sentence this whole design has to answer for:

> "The guards listed in §4b are the **only** thing standing between an Avalor mob and your stealth brain."

**Consequences.**

1. **`DESIGN-pragmatic.md` §2.8 `Compat/ExternalPin.cs` — which that document calls "the single highest-leverage compat decision in v2" — has no live consumer.** Nothing writes into `AwarenessData` any more. I do not adopt it; §9.4 gives the full argument and the alternative.
2. **`DESIGN-pragmatic.md` §5.7's coexistence table describes a mod version that does not ship.** Every row about MoA prefixes at `Priority.First` with `HarmonyBefore(SoM)` is historical. The *doctrine* those rows justify is still right — but it has to be justified on generic third-party grounds and on old-builds-in-the-wild grounds, which is what §6.1 does.
3. **The interop survey's failure scenarios need re-scoring.** §9.5 does this scenario by scenario. Short version: S1/S2/S3/S5/S6/S8/S10 are now about *old builds in the wild and unknown third parties*, not about MoA; **S9 is the one that is still fully live and is about SoM, not about any consumer** — whatever `GetData(Character)` returns must not be "the local player's answer", or the central defect walks back in through the compat door.
4. **What has not changed, and is now more load-bearing, not less:** `AwarenessData` stays a public class with v1 field names; `AwarenessSystem.GetData(Character)` stays static and never returns null; `ShadowsOfMidgard.StealthExemption` and `"SoMStealthExempt"` are frozen; and the exemption guard is now the *mechanism*, with nothing underneath it.

### 0.2 `ZDO.DataRevision` is not a usable cache key for anything on a moving creature.

`ZSyncTransform.CustomFixedUpdate` (L75464) calls `OwnerSync()` (L75476/L75484) every physics tick. `OwnerSync` writes position whenever it changed:

```csharp
// L75161-75171
if (m_syncPosition)
{
    Vector3 position2 = GetPosition();
    if (!m_positionCached.Equals(position2))
    {
        zDO.SetPosition(position2);          // L75166
    }
    ...
}
```

and `ZDO.InternalSetPosition` bumps the revision on the owner:

```csharp
// L62547-62558
public void InternalSetPosition(Vector3 pos)
{
    if (!(m_position == pos))
    {
        m_position = pos;
        SetSector(ZoneSystem.GetZone(m_position));
        if (IsOwner()) { IncreaseDataRevision(); }   // L62555
    }
}
```

So **`DataRevision` increments ~50×/s for any creature that is moving and owned.** `DESIGN-pragmatic.md` §8.6 caches the resolved directives "per creature, invalidated on `ZDO.DataRevision`". For every walking creature that cache **never hits**; the resolver runs in full on every pass, and the documented "fall back to a 1 s re-read" is in fact the primary path. §9.3 replaces it with a policy-triple probe that actually works.

### 0.3 On a machine with no loaded zone, `Physics.Linecast` returns `false`, and `false` means "I can see you".

Dossier L11-L12: a dedicated server has real Heightmaps and colliders only in the few zones around world origin; `ZoneSystem.IsZoneLoaded(Vector3)` (L98760) is the clean test. Vanilla's own sight test is

```csharp
// L4614, inside the static BaseAI.CanSeeTarget
if (Physics.Raycast(eyePoint, vector2.normalized, vector2.magnitude, m_viewBlockMask)) { return false; }
```

— i.e. **a raycast that hits nothing is read as clear line of sight**. In an unloaded zone nothing can ever be hit. The same is true if the layer mask resolves to `0`: `BaseAI.m_viewBlockMask` is `private static int m_viewBlockMask = 0;` (L3953), assigned in `Awake` from `LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "viewblock", "vehicle")` (L4024). A renamed layer in Valheim v1.0 silently produces a partial or zero mask.

So the two most likely LOS-binding failures both fail *toward maximally visible* — the exact floor the brief forbids. §8 makes both of them **refusals** instead: when SoM cannot cast a trustworthy line, it stops having an opinion about sight for that pair and lets vanilla answer with vanilla's own raycast. That is the only fallback that is neither a guess nor a regression.

---

## 1. Organising principle

**Every answer SoM gives must be reconstructible, attributable, and refusable — and "refuse" is the default.** *Reconstructible*: any state whose loss would change behaviour lives in the creature's ZDO, not in a C# dictionary, because `ZDOMan.ReleaseZDOS` (L65301) reassigns the simulator of every creature every two seconds and the dictionary belongs to whichever machine happened to be simulating. *Attributable*: every answer names the exact tuple — which creature, which player, which policy field from which source, which capability — that produced it, and the mod's public self-description is **rendered from that same tuple by a function that is given nothing else**, so SoM is structurally incapable of describing a policy it does not apply. *Refusable*: the gate that every Harmony patch passes through returns a categorised refusal, not a boolean, and refuses by default; anything SoM cannot attribute — no track, a stale track, a missing capability, an unloaded zone, an unreadable stealth factor — is handed straight back to vanilla, per-pair and per-frame, rather than answered from a guess. The failure floor of every ladder in this document is therefore *vanilla*, reached one question at a time rather than by a global kill switch, and every refusal is counted so that "SoM is installed and silently answering nothing" is a one-line diagnosis instead of a two-day bug hunt.

---

## 2. File / folder layout

The tree is organised by **invariant**, not by subsystem. `Contract/`, `Authority/`, `Policy/`, `Gate/` and `Capability/` are the five directories that carry the principle; everything else is ordinary code that must go through them.

```
ShadowsOfMidgard/
  ShadowsOfMidgard.cs               Plugin entry. Fixed order: Diag -> Config -> Capability -> Registry
                                    -> Patches -> Runtime.Enter(mode). Nothing else at Awake.

  Contract/                         PUBLIC SURFACE. Rule: types here may reference vanilla, other
                                    Contract/ types, and ContractHost. NOTHING ELSE. Reviewable alone.
    StealthExemption.cs             FROZEN v1. Type name, ZDOKey const, both IsExempt overloads, verbatim.
    AwarenessData.cs                FROZEN v1 class + field names. One-way projection (see §9.4).
    AwarenessSystem.cs              FROZEN static GetData(Character). Never null. Never throws.
    VanillaAlertness.cs             FROZEN enum member names (Alerted, ...). FleeReason likewise.
    SoMInterop.cs                   Additive API: contract version, keys, profiles, capabilities,
                                    coexistence census, Describe/DescribeTrack, AckIsConclusive.
    SoMKeys.cs                      Every SoM ZDO key string + its cached GetStableHashCode(). One source.
    ContractHost.cs                 The ONLY bridge from Contract/ into the engine. ~12 methods.

  Authority/                        "Reconstructible". Ownership, replication, handover.
    OwnershipState.cs               Authority enum + the per-creature transition machine.
    Digest.cs                       Pack/unpack of the replicated per-player detection digest.
    DigestWriter.cs                 Owner-only, change-gated, consequence-adaptive write.
    DigestReader.cs                 Non-owner read + freshness tracking (gen-bump observation).
    Rehydrate.cs                    Foreign/Orphan -> Owned: seed tracks with a staleness discount.
    OrphanClaim.cs                  Optional, default OFF: ClaimOwnership() on hot orphans.

  Policy/                           "Attributable". One resolver, provenance, no second implementation.
    ResolvedPolicy.cs               readonly struct + Provenance bitfield (who set each field).
    PolicyResolver.cs               THE ONLY resolver. Produces ResolvedPolicy + writes SoM_Ack/Applied/Reject.
    PolicyCache.cs                  Triple-probe invalidation (SoM_Contract, SoM_Rev, SoMStealthExempt).
    ProfileTable.cs                 Named bundles -> overlays. Additive only, never subtractive.
    PolicyRender.cs                 static string Render(in ResolvedPolicy, in Aggregate) -- takes NOTHING
                                    else. This signature IS the one-resolver invariant.

  Gate/                             "Refusable". The single door every patch goes through.
    Gate.cs                         TryOpinion -> Refusal. No patch may reach a CreatureRecord except here.
    Refusal.cs                      Refusal enum + the counted histogram.
    Runtime.cs                      RuntimeMode state machine + the exception watchdog.

  Capability/
    SoMCapability.cs                [Flags] enum. One bit per soft binding.
    BindingSpec.cs                  Ladder + declared Degrade + one-sentence consequence string.
    Caps.cs                         Resolve-once table. The ONLY place a soft MethodInfo may be named.
    VanillaOps.cs                   Facade. Every call site uses TryX(...), never a raw MethodInfo.
    PatchInstaller.cs               Per-group atomic install. Probe -> CreateClassProcessor -> verify.
    Coexistence.cs                  Harmony.GetPatchInfo census at install and at settle.
    LayerMasks.cs                   View-block mask ladder. Mask==0 => refuse, never "nothing blocks".
    Headless.cs                     SystemInfo.graphicsDeviceType == Null. Never ZNet.IsDedicated().

  Model/
    Track.cs                        The per-(creature, playerId) struct. 48 B, no references.
    TrackSet.cs                     Fixed-capacity inline set: find / create / evict / prune / rank.
    CreatureRecord.cs               Per-creature: components, authority, policy, tracks, aggregate, sched.
    CreatureRegistry.cs             Two indexes (BaseAI ref, ZDOID) -> one record. Incremental sweep.
    PlayerView.cs                   Per-player intrinsic snapshot + identity (long PlayerId).
    PlayerRegistry.cs               Refreshes views. Client path and headless path, both bound.

  Sched/
    Wheel.cs                        Bucketed timing wheel. No cursor. Starvation impossible by construction.
    Phases.cs                       Authority phase (unbudgeted) + Perception phase (budgeted).
    Budget.cs                       Time budget + LOS quota + the served/demanded telemetry.
    SelfLimit.cs                    Announced scope reduction when the wheel cannot keep its deadlines.

  Perception/
    PairMath.cs                     The ONLY per-(creature, player) math. Falloff, cone, integration.
    LosSampler.cs                   Zone-gated, mask-validated, quota'd. Refuses rather than guesses.
    Visibility.cs  Noise.cs  Hiding.cs  Camo.cs      Ported v1 formulas, now producing into PlayerView.
    VegetationSampler.cs            Non-alloc overlap sampling, one player per step, all players.

  Brain/
    Aggregate.cs                    The per-creature summary derived from the track set. Derived, not stored.
    StateLadder.cs                  Ported v1 hysteresis, now per-track.
    Flee.cs  Movement.cs  Combat.cs  Group.cs        Ported v1 evaluators. Per-creature, aggregate-aimed.
    Decision.cs                     StealthDecision struct, stored inline on CreatureRecord.

  Act/
    Drive.cs                        Applies a decision to vanilla, entirely through VanillaOps ladders.
    AlertDriver.cs                  Drives the REAL SetAlerted/Alert. Debounced. Replaces the IsAlerted patch.
    AllyCall.cs                     Masked, non-allocating ally propagation.

  Patches/
    P_CanSeeTarget.cs   P_CanHearTarget.cs   P_CanSenseTarget.cs      group Sensing   (required, atomic)
    P_BaseAI_Awake.cs   P_Character_OnDestroy.cs                      group Lifecycle (required, atomic)
    P_MonsterAI_UpdateAI.cs                                           group Drive
    P_Humanoid_StartAttack.cs                                         group AttackGate
    P_Character_SetMoveDir.cs                                         group Movement
    P_Player_OnSpawned.cs                                             group Lifecycle (optional rung)
    Observers/O_Sensing.cs                                            group Diagnostics, sampled, read-only

  Config/
    SOMConfig.cs                    Locking entry FIRST. ModRequired = true. Defaults before any bind.
    Bind.cs                         bind + sync + clamp + initial push + change push, one call, one bound.
    StealthConfigModel.cs           POCO. v1 field names/defaults preserved.
    ProfileTableConfig.cs           CustomSyncedValue<string> YAML prefab -> profile table.

  Hud/
    ThreatQuery.cs                  Publishes {mine, unknownNearby} once per pass. HUD reads only this.
    StealthHud.cs                   Lazy canvas on first local player, parented under vanilla Hud.
    Gem.cs  NoiseMeter.cs           Write on change only. Third visual state for "unknown".
    SpriteLoader.cs                 Embedded only. No disk write, no hardcoded plugin folder name.

  Armor/                            ArmorProfile.cs / ArmorProfileSystem.cs / ArmorUtils.cs -- ported,
                                    equip-hash cached.
  Diag/
    Log.cs                          Gated. Every debug string built inside an `if`. Once-keys are typed.
    Once.cs                         OnceKey(prefab, topic) dedupe with a bounded LRU. No unbounded sets.
    Status.cs                       `somstatus` console command: mode, caps, refusals, budget, contention.
    Selftest.cs                     `somselftest`: the §12 acceptance test, scripted, one line per assert.
```

**Why `Contract/` is isolated.** The frozen surface is the one part of this mod that can never be fixed in a patch release, because other assemblies bind to it by name at runtime. Confining it to a directory whose only outward edge is `ContractHost` means a reviewer can read ~250 lines and know the whole external commitment. `DESIGN-pragmatic.md` scatters the same commitment across `Api/` and `Compat/` with `Compat/LegacyAwareness.cs` reaching directly into `CreatureRegistry`.

---

## 3. Core type declarations

### 3.1 `Model/Track.cs` — the per-(creature, player) record, keyed on the *stable* player id

```csharp
namespace ShadowsOfMidgard.Model
{
    [System.Flags]
    internal enum TrackFlags : byte
    {
        None       = 0,
        CanSee     = 1 << 0,
        CanHear    = 1 << 1,
        LosClear   = 1 << 2,   // last trustworthy LOS sample said "clear"
        LosUsable  = 1 << 3,   // that sample is fresh AND was taken in a loaded zone with a valid mask
        EverSensed = 1 << 4,
        Floored    = 1 << 5,   // a SoM_*Floor directive is holding this track up
        Seeded     = 1 << 6,   // reconstructed from the ZDO digest after a handover
        Remote     = 1 << 7,   // read from a foreign creature's digest; NOT locally simulated
    }

    /// <summary>
    /// One creature's knowledge of ONE player. 48 bytes, no object references, blittable.
    /// KEY IS <see cref="PlayerId"/> - Player.GetPlayerID() (L15982), backed by ZDOVars.s_playerID
    /// ("playerID", L66602). NOT a ZDOID. See the rationale block below.
    /// </summary>
    internal struct Track
    {
        public long       PlayerId;         // 8  - stable across death/respawn, zone unload, handover
        public float      Detection;        // 4  - 0..1, integrated
        public Vector3    LastKnownPos;     // 12
        public float      TimeSinceSensed;  // 4
        public float      LastEvalTime;     // 4  - Time.time, LOCAL clock, never replicated
        public float      LosSampleTime;    // 4
        public float      LastRefreshedAt;  // 4  - for Remote tracks: when the digest gen last bumped
        public byte       State;            // 1  - (byte)VanillaAlertness
        public TrackFlags Flags;            // 1
        // 2 bytes padding

        public bool CanSee  => (Flags & TrackFlags.CanSee)  != 0;
        public bool CanHear => (Flags & TrackFlags.CanHear) != 0;
        public bool Sensed  => (Flags & (TrackFlags.CanSee | TrackFlags.CanHear)) != 0;
    }
}
```

> **Why `long PlayerId` and not `ZDOID`.** `Game.SpawnPlayer` (L85762-85771) is `Object.Instantiate(m_playerPrefab, ...)` followed by `ZNet.instance.SetCharacterID(component.GetZDOID())`. **Every death and respawn creates a brand-new Player GameObject with a brand-new ZDO and therefore a brand-new `ZDOID`.** `Player.SetPlayerID` (L15973-15980) writes the *profile's* id into `ZDOVars.s_playerID` and `GetPlayerID()` (L15982-15989) reads it back off the ZDO, so it is identical before and after death and readable by any peer off any character ZDO with no instance. Keying tracks on `ZDOID` — which `DESIGN-pragmatic.md` §2.1 does, and which it also uses as the replicated `SoM_DetTgt` — means every player death silently orphans every track about that player across the entire loaded world, and leaves every replicated detection record permanently unmatchable by the player it is about. It is also one wire field instead of two: `ZDO.Set(string, ZDOID)` (L62385-62394) writes **two** entries, `name + "_u"` and `name + "_i"`. `ZDOID` remains in use as a fast local handle for creatures (`CreatureRegistry`'s second index) where the object genuinely dies with its id.

### 3.2 `Model/TrackSet.cs`

```csharp
internal struct TrackSet
{
    public const int Capacity = 8;

    private Track[] _t;
    public  int     Count;
    private int     _rankStamp;      // bumped whenever ranking could have changed

    public void Alloc()          { if (_t == null) _t = new Track[Capacity]; }
    public ref Track At(int i)   => ref _t[i];

    /// <summary>Linear scan over <= 8 longs. Beats any hash at this size and never allocates.</summary>
    public int IndexOf(long playerId)
    {
        for (int i = 0; i < Count; i++) if (_t[i].PlayerId == playerId) return i;
        return -1;
    }

    public int GetOrCreate(long playerId, float now)
    {
        int i = IndexOf(playerId);
        if (i >= 0) return i;
        if (Count < Capacity) { i = Count++; }
        else
        {
            // Evict the least consequential: lowest detection, tie-broken by staleness.
            // A Floored track is never evicted - a consumer directive outranks our own scoring.
            i = 0; float worst = float.MaxValue;
            for (int k = 0; k < Capacity; k++)
            {
                if ((_t[k].Flags & TrackFlags.Floored) != 0) continue;
                float score = _t[k].Detection * 1000f - (now - _t[k].LastEvalTime);
                if (score < worst) { worst = score; i = k; }
            }
        }
        _t[i] = default;
        _t[i].PlayerId        = playerId;
        _t[i].LastEvalTime    = now;
        _t[i].TimeSinceSensed = 999f;
        _rankStamp++;
        return i;
    }

    public void RemoveAt(int i) { _t[i] = _t[--Count]; _rankStamp++; }

    public void Prune(float now, float idleTtl)
    {
        for (int i = Count - 1; i >= 0; i--)
            if (_t[i].Detection <= 0.001f
                && (_t[i].Flags & TrackFlags.Floored) == 0
                && (now - _t[i].LastEvalTime) > idleTtl)
                RemoveAt(i);
    }

    /// <summary>Descending by Detection. Used only when the digest is about to be written,
    /// so slot 0 of the replicated digest is always the dominant track.</summary>
    public void RankInPlace()
    {
        for (int i = 1; i < Count; i++)                     // insertion sort, n <= 8
        {
            Track key = _t[i]; int j = i - 1;
            while (j >= 0 && _t[j].Detection < key.Detection) { _t[j + 1] = _t[j]; j--; }
            _t[j + 1] = key;
        }
    }
}
```

### 3.3 `Model/CreatureRecord.cs`

```csharp
internal enum Authority : byte { Unknown = 0, Foreign = 1, Orphan = 2, Owned = 3 }

internal sealed class CreatureRecord
{
    // --- identity & components, resolved ONCE in the BaseAI.Awake postfix ---
    public ZDOID     Id;
    public Character Char;
    public BaseAI    Ai;
    public MonsterAI Monster;         // null for AnimalAI / bare BaseAI
    public Humanoid  Humanoid;        // null if not humanoid
    public ZNetView  NView;
    public Transform Tf;
    public int       PrefabHash;      // for once-per-prefab diagnostics; never for behaviour

    // --- authority state machine (Authority/OwnershipState.cs) ---
    public Authority Auth;
    public Authority PrevAuth;
    public float     AuthChangedAt;
    public float     NextAuthPollAt;
    public bool      Valid;           // NView.IsValid() && Char != null

    // --- policy (Policy/) ---
    public ResolvedPolicy Policy;
    public int   PolicyContract, PolicyRev, PolicyLegacy;   // the invalidation triple, §9.3
    public float PolicyCeilingAt;                           // hard re-resolve deadline
    public int   AckWritten;                                // SoM_Ack value we last wrote

    // --- per-player knowledge ---
    public TrackSet Tracks;

    // --- aggregate: DERIVED every pass from Tracks, never independently mutated ---
    public byte    AggState;
    public float   AggDetection;
    public long    AggPlayerId;
    public Vector3 AggLastKnownPos;
    public int     AggIndex;          // -1 when there is no dominant track

    // --- per-creature simulation state (properties of the creature, not of a relationship) ---
    public float        SelfHealthPercent;
    public FleeReason   FleeReason;
    public Vector3      FleeDirection;
    public int          NearbyAllies;
    public float        NextAllyCallAt;
    public StealthAction Action;
    public float        ActionSince;

    // --- decision, stored inline: zero allocation per evaluation ---
    public StealthDecision Decision;
    public float           DecisionAt;

    // --- alert driving ---
    public float LastAlertTransitionAt;
    public bool  WantAlert;

    // --- replication (Authority/Digest*) ---
    public long  DigestHeader;        // last header we wrote (owner) or read (non-owner)
    public byte  DigestGen;
    public byte  LastSeenGen;         // non-owner: last gen observed
    public float LastGenChangeAt;     // non-owner: LOCAL time the gen last changed -> freshness
    public float NextDigestWriteAt;
    public readonly long[] DigestSlots = new long[Digest.MaxSlots];

    // --- scheduling (Sched/Wheel.cs) ---
    public int   AuthBucket, PerceptBucket;
    public float NextPerceptAt;
    public float LastPerceptAt;
    public byte  Tier;
    public ushort LateTicks;          // deadline misses, for SelfLimit telemetry

    // --- compat: materialised ONLY if something calls AwarenessSystem.GetData for this creature ---
    public AwarenessData Legacy;
    public bool          LegacyObserved;
}
```

### 3.4 `Policy/ResolvedPolicy.cs` — value plus provenance

```csharp
namespace ShadowsOfMidgard.Policy
{
    /// <summary>Which source supplied each field. Rendered by Describe. Never used for gating.</summary>
    [System.Flags]
    public enum Provenance : ushort
    {
        None        = 0,
        BrainOff    = 1 << 0,  AlertFloor = 1 << 1,  DetectFloor = 1 << 2,
        SightRange  = 1 << 3,  HearRange  = 1 << 4,  ConeHalf    = 1 << 5,
        IgnoreLos   = 1 << 6,  NoFlee     = 1 << 7,  GiveUpMul   = 1 << 8,
    }

    public enum PolicySource : byte { ConfigDefault = 0, GlobalProfileTable = 1, NamedProfile = 2,
                                      ExplicitKey = 3, LegacyFlag = 4 }

    public readonly struct ResolvedPolicy
    {
        public readonly bool  BrainOff;
        public readonly byte  AlertFloor;      // 255 = unset
        public readonly float DetectFloor;
        public readonly float SightRange;      // <= 0 = config
        public readonly float HearRange;
        public readonly float ConeHalfDeg;     // <= 0 = config
        public readonly bool  IgnoreLos;
        public readonly bool  NoFlee;
        public readonly float GiveUpMul;

        public readonly byte  ContractVersion; // 0 = no v2 keys present
        public readonly bool  LegacyExempt;    // raw SoMStealthExempt == 1
        public readonly int   ProfileHash;     // 0 = none
        public readonly Provenance FromProfile;   // bit set = this field came from the profile
        public readonly Provenance FromExplicit;  // bit set = this field came from an explicit key
        public readonly int   AppliedMask;     // bitfield mirrored into SoM_Applied
        public readonly int   RejectedMask;    // bitfield mirrored into SoM_Reject
        // ctor omitted for brevity: one ctor, called from exactly one place.
    }
}
```

### 3.5 `Gate/Refusal.cs` and `Gate/Gate.cs` — the single door, and the counted refusal

```csharp
namespace ShadowsOfMidgard.Gate
{
    /// <summary>Why SoM declined to answer. None == "SoM holds an opinion". Everything else is
    /// a hand-back to vanilla, and every value is COUNTED so that a silent SoM is diagnosable.</summary>
    internal enum Refusal : byte
    {
        None = 0,
        ModeNotFull,      // Booting / Reduced-without-this-capability / Relay / Inert
        NotRegistered,    // no CreatureRecord: BaseAI.Awake patch missing, or a ghost-zone Awake
        NotOwned,         // Foreign or Orphan: not ours to answer for
        Invalid,          // ZNetView invalid / Character gone
        Dead, Tamed,
        BrainOff,         // exemption or SoM_BrainOff
        TargetNotPlayer,  // creature-vs-creature: SoM has, by design, no opinion
        NoPlayerId,       // ZDOVars.s_playerID unreadable or 0 -> cannot key a track
        NoTrack,          // this creature has never evaluated against this player
        TrackStale,       // the track exists but is older than OpinionTtl
        NoLosOpinion,     // sight only: zone unloaded, mask invalid, or LOS sample too old
        NoIntrinsic,      // the player's stealth factor / noise range could not be read
        CapabilityMissing,
        Count
    }

    internal static class Gate
    {
        internal static readonly int[] Counts = new int[(int)Refusal.Count];

        /// <summary>
        /// The ONLY way a patch may reach a CreatureRecord. Refuses by default.
        /// `need` lets a caller demand a capability without re-implementing the check
        /// (sight demands LineOfSight | ViewBlockMask | ZoneLoaded; hearing demands none).
        /// </summary>
        internal static Refusal TryOpinion(BaseAI ai, Character target, SoMCapability need,
                                           out CreatureRecord rec, out int ti)
        {
            rec = null; ti = -1;

            if (Runtime.Mode != RuntimeMode.Full)      return Count(Refusal.ModeNotFull);
            if (target == null || !target.IsPlayer())  return Count(Refusal.TargetNotPlayer);
            if (need != 0 && !Caps.Has(need))          return Count(Refusal.CapabilityMissing);

            rec = CreatureRegistry.Find(ai);           // Dictionary<BaseAI, rec>, ReferenceComparer
            if (rec == null)                           return Count(Refusal.NotRegistered);
            if (!rec.Valid)                            return Count(Refusal.Invalid);
            if (rec.Auth != Authority.Owned)           return Count(Refusal.NotOwned);
            if (rec.Char.IsDead())                     return Count(Refusal.Dead);
            if (rec.Char.IsTamed())                    return Count(Refusal.Tamed);
            if (rec.Policy.BrainOff)                   return Count(Refusal.BrainOff);

            long pid = PlayerRegistry.IdOf(target);    // cached; 0 if unreadable
            if (pid == 0L)                             return Count(Refusal.NoPlayerId);

            ti = rec.Tracks.IndexOf(pid);
            if (ti < 0)                                return Count(Refusal.NoTrack);
            if (Time.time - rec.Tracks.At(ti).LastEvalTime > Runtime.OpinionTtl)
                                                       return Count(Refusal.TrackStale);
            return Refusal.None;
        }

        private static Refusal Count(Refusal r) { Counts[(int)r]++; return r; }
    }
}
```

Two things to notice. First, the guard order is **cheapest-and-most-selective first**: `target.IsPlayer()` (L7419, a virtual returning a constant) rejects roughly 96 % of `FindEnemy`'s calls (L5191-5223 walks every `Character`, of which a handful are players) before anything touches a dictionary. Second, **there is no other path**. `CreatureRegistry.Find` is `internal` to `Gate`'s assembly-visible surface by convention and the review rule is: *a file under `Patches/` that names `CreatureRegistry` fails review*. That is the answer to `AVALOR_COMPAT_HANDOFF.md` §10's "decentralized enforcement" — the exemption can no longer be forgotten at a new patch site, because a new patch site cannot obtain a `CreatureRecord` without passing the exemption check.

### 3.6 `Gate/Runtime.cs` — five modes and a watchdog

```csharp
internal enum RuntimeMode : byte
{
    Booting = 0,   // before Caps.Resolve completes. Gate refuses everything. Vanilla, by construction.
    Full    = 1,
    Reduced = 2,   // a non-required capability is missing; named features off, per-target sensing intact
    Relay   = 3,   // headless: config/ServerSync + authority phase only. No physics, no HUD, no brain.
    Inert   = 4,   // SoM answers nothing for the rest of the session. Byte-for-byte vanilla behaviour.
}

internal static class Runtime
{
    internal static RuntimeMode Mode = RuntimeMode.Booting;
    internal static float OpinionTtl = 3f;

    private static int   _faults;
    private static float _faultWindowStart;

    /// <summary>Every catch in this mod funnels here. There is no bare catch anywhere in v2.</summary>
    internal static void Fault(string site, System.Exception e)
    {
        Log.OnceError(site, e);                       // one line per site, ever
        float now = Time.realtimeSinceStartup;
        if (now - _faultWindowStart > 30f) { _faultWindowStart = now; _faults = 0; }
        if (++_faults >= 20)
        {
            Log.Error($"SoM demoted to Inert: {_faults} faults in 30 s, most recently at {site}. " +
                      "The game is now running vanilla AI. See `somstatus` for the capability report.");
            Enter(RuntimeMode.Inert);
        }
    }

    internal static void Enter(RuntimeMode m) { /* logs the transition once, resets budgets */ }
}
```

`DESIGN-pragmatic.md` has a single `Runtime.Active` bool that is set at startup and never moves again; a runtime exception storm inside its budgeted `Update` has no path to demotion, and v1's `try { } catch { }` at `MonsterAI_StealthBrain_UpdateAI_Patch.cs:29,44` is the specific historical failure this replaces.

---

## 4. The per-player track model, and how tracks survive ownership handover

### 4.1 What is per-track and what is not

Per `(creature, playerId)`: `Detection`, `State`, `LastKnownPos`, `TimeSinceSensed`, the `CanSee`/`CanHear`/`LosClear`/`LosUsable` bits, and the two timestamps. Per *creature*: health, flee reason and direction, ally count, action and its timer, the decision. That split is what lets the four v1 evaluators be ported with their arithmetic untouched — they change inputs, not logic.

The **aggregate** (`AggState`, `AggDetection`, `AggPlayerId`) is recomputed from the track set at the end of every pass and is never independently written. That is deliberate: an independently-mutable aggregate is a second authority for detection state, and §9.4 rejects the same shape in the compat layer for the same reason.

### 4.2 Creation, and why the gate is per-creature not global

```csharp
// Perception/PairMath.cs, called once per creature pass from Sched/Phases.cs
internal static void EvaluateCreature(CreatureRecord rec, float dt, float now)
{
    ref ResolvedPolicy p = ref rec.Policy;
    float sight = p.SightRange > 0f ? p.SightRange : Cfg.MaxVisualRange;
    float hear  = p.HearRange  > 0f ? p.HearRange  : Cfg.MaxHearingRange;
    float gate  = Mathf.Max(sight, hear) + Cfg.TrackCreateMargin;   // +8 m hysteresis
    float gate2 = gate * gate;
    Vector3 pos = rec.Tf.position;

    PlayerView[] views = PlayerRegistry.Views;
    for (int i = 0; i < PlayerRegistry.Count; i++)
    {
        ref PlayerView v = ref views[i];
        if (!v.Valid || v.Ghost) continue;              // InGhostMode/InDebugFlyMode: vanilla parity, L4589
        if (v.PlayerId == 0L) continue;                 // Refusal.NoPlayerId at the source

        if ((v.Position - pos).sqrMagnitude > gate2)
        {
            int e = rec.Tracks.IndexOf(v.PlayerId);
            if (e >= 0) Decay(ref rec.Tracks.At(e), dt, now);   // out of range: decay, do not delete
            continue;
        }
        int ti = rec.Tracks.GetOrCreate(v.PlayerId, now);
        Integrate(rec, ref rec.Tracks.At(ti), in v, in p, dt, now);
    }

    rec.Tracks.Prune(now, Cfg.TrackIdleTtl);
    Aggregate.Recompute(rec);
}
```

The sense radius is read from the **resolved policy**, so a `SoM_SightRange = 60` directive widens track *creation*, not merely the falloff curve. `DESIGN-pragmatic.md` does the same; I note it because it is the one place a Tier-2 key has to reach the scheduler and it is easy to get wrong.

### 4.3 Bounds

- Hard cap 8 tracks. Eviction never removes a `Floored` track — a consumer's declared directive outranks SoM's own scoring.
- Pruned at `Detection <= 0.001` and `LastEvalTime` older than `TrackIdleTtl` (20 s).
- `Track` holds no object references, so a track for a departed player pins nothing and evaporates on its own timer.
- Ceiling: 200 creatures × 8 × 48 B = **75 KB**, allocated once per creature on first use, never churned.

### 4.4 The ownership state machine

```csharp
// Authority/OwnershipState.cs -- runs in the UNBUDGETED authority phase, 4 Hz per creature.
internal static void Poll(CreatureRecord rec, float now)
{
    Authority prev = rec.Auth;
    Authority cur;

    if (rec.NView == null || !rec.NView.IsValid())          cur = Authority.Unknown;
    else {
        ZDO z = rec.NView.GetZDO();
        cur = z == null            ? Authority.Unknown
            : z.IsOwner()          ? Authority.Owned        // L63617, a field read
            : z.HasOwner()         ? Authority.Foreign      // L63622
                                   : Authority.Orphan;
    }
    if (cur == prev) return;

    rec.PrevAuth = prev; rec.Auth = cur; rec.AuthChangedAt = now;

    switch (cur)
    {
        case Authority.Owned:
            // We are now the simulator. Rebuild what the previous simulator knew.
            Rehydrate.FromDigest(rec, now);
            rec.NextDigestWriteAt = now + Cfg.HotDigestPeriod;   // do not stomp the digest instantly
            break;

        case Authority.Foreign:
        case Authority.Orphan:
            // Tracks are RETAINED, not cleared: this creature may be ours again within seconds.
            // They stop being answerable, because Gate refuses on Auth != Owned.
            for (int i = 0; i < rec.Tracks.Count; i++) rec.Tracks.At(i).Flags |= TrackFlags.Remote;
            if (prev == Authority.Owned) Telemetry.HandoverOut(now - rec.LastDigestWriteAt);
            break;

        case Authority.Unknown:
            CreatureRegistry.Remove(rec);
            break;
    }
}
```

**Why the poll and not an event.** Vanilla gives no ownership-change callback. `ZDO.SetOwnerInternal` (L63636-63650) is reached from `ZDOMan.RPC_ZDOData` and computes `Owner = uid == ZDOMan.GetSessionID()` on each peer independently. Patching `SetOwnerInternal` would be a hot, private, save-format-adjacent target — exactly the kind of binding §8 forbids. A 4 Hz poll of two boolean properties costs 800 field reads per second at 200 creatures and is bounded, unbudgeted and impossible to starve.

### 4.5 What actually happens to a chase when `ReleaseNearbyZDOS` fires

The handover sequence is not a single event. `ZDOMan.ReleaseZDOS` (L65301-65314) runs every 2 s and calls `ReleaseNearbyZDOS` once for the server's own reference position and once per peer. Inside (L65330-65354):

```csharp
if (!tempNearObject.Persistent) continue;                                   // L65338
if (tempNearObject.GetOwner() == uid) {
    if (!ZNetScene.InActiveArea(sector, zone, activatedArea)) tempNearObject.SetOwner(0L);
}
else if ((!tempNearObject.HasOwner() || !IsInPeerActiveArea(sector, tempNearObject.GetOwner()))
         && ZNetScene.InActiveArea(sector, zone, activatedArea))
    tempNearObject.SetOwner(uid);
```

Two paths matter:

1. **Release-then-claim.** The departing owner's pass sets owner `0`; a later pass (same sweep, different peer, or up to 2 s later) assigns the new peer. Between them the creature is an **Orphan and nobody simulates it** — `BaseAI.UpdateAI` returns `false` at its owner gate (L4113-4117) on every machine. The creature freezes in place, in vanilla, today, with or without SoM. Latency ≤ ~2.5 s.
2. **Direct steal.** If the current owner is not in its own active area, the `else if` reassigns straight to the new peer with no orphan beat.

SoM must survive both, and must not make the orphan window worse. Three rules:

- **Tracks are retained, not cleared, across `Owned → Foreign/Orphan`.** A player walking a loop past a creature can hand it back within one sweep.
- **No decay while not owned.** Time does not pass for a creature nobody simulates; decaying a track on a machine that is not the simulator would make the answer depend on which client you ask.
- **The replicated digest is the recovery path, and it is written on a cadence set by *consequence*, not by a fixed clock.** While any track is at or above `Alerted` the digest is written at up to 4 Hz; below that, 0.5 Hz; when every track is below `ReplicateFloor` (0.05) it is not written at all. Handover therefore loses at most **250 ms** of detection integration in exactly the case where losing it would be visible, and costs nothing in the case where it would not.

### 4.6 Rehydration, with a staleness discount and a self-write guard

```csharp
// Authority/Rehydrate.cs
internal static void FromDigest(CreatureRecord rec, float now)
{
    if (!Caps.Has(SoMCapability.ZdoDigest)) return;
    ZDO z = rec.NView.GetZDO(); if (z == null) return;

    long hdr = z.GetLong(SoMKeys.HdrHash, 0L);                 // ZDO.GetLong(int,long) L62758
    if (hdr == 0L) return;
    Digest.Header h = Digest.UnpackHeader(hdr);
    if (h.Schema != Digest.Schema) { Log.OnceWarn(rec, "digest.schema", h.Schema); return; }

    // Do not seed from a digest WE wrote: after a brief dual-ownership window the value we would
    // read back is our own, one replication period stale, and re-seeding from it would ratchet.
    if (h.WriterFold == Digest.SelfFold && (now - rec.LastDigestWriteAt) < Cfg.SelfSeedGuard) return;

    int seeded = 0;
    for (int s = 0; s < h.SlotCount && s < Digest.MaxSlots; s++)
    {
        long packed = z.GetLong(SoMKeys.SlotHash[s], 0L);
        if (packed == 0L) continue;
        Digest.Slot d = Digest.UnpackSlot(packed);

        long pid = PlayerRegistry.ResolveFold(d.PlayerFold);   // fold -> full id via loaded players
        if (pid == 0L) continue;                               // that player is not loaded here: skip

        int ti = rec.Tracks.GetOrCreate(pid, now);
        ref Track t = ref rec.Tracks.At(ti);

        // Staleness discount. We do not know how long the digest sat unowned; we know only that it
        // was at most one write period fresh when its owner stopped writing. Discount, never restore
        // verbatim, so a handover can only ever LOWER a creature's knowledge, never raise it.
        t.Detection       = d.Detection * Cfg.SeedDiscount;    // default 0.85
        t.State           = (byte)Mathf.Min(d.State, (int)VanillaAlertness.Alerted);
        t.TimeSinceSensed = Mathf.Max(d.TimeSinceSensed, Cfg.SeedMinTimeSinceSensed);  // 2 s
        t.LastEvalTime    = now;
        t.LosSampleTime   = 0f;                                // never inherit an LOS bit
        t.Flags           = TrackFlags.Seeded | TrackFlags.EverSensed;
        seeded++;
    }

    if (seeded > 0 && Caps.Has(SoMCapability.LastKnownPos))
    {
        Vector3 lkp = z.GetVec3(SoMKeys.LkpHash, rec.Tf.position);
        if (rec.Tracks.Count > 0) rec.Tracks.At(0).LastKnownPos = lkp;
    }
    Telemetry.HandoverIn(seeded);
}
```

Three deliberate asymmetries: the digest can only **lower** knowledge (discount, `Min` against `Alerted`, floor on `TimeSinceSensed`); the LOS bit is **never** inherited, because line of sight is a property of the geometry between two positions on *this* machine and a bit computed elsewhere is not evidence; and a slot whose player is not loaded on this machine is **skipped rather than approximated**, because there is no honest value to invent for a player we cannot see.

`DESIGN-pragmatic.md` §6.3 restores the dominant track's detection verbatim and inherits its state ordinal. In a two-client session where ownership ping-pongs across a zone boundary — which happens continuously when two players walk parallel — verbatim restore plus continued integration is a positive feedback loop: each handover re-injects the peak the *other* client reached. The discount makes handover a strictly lossy operation, which is the correct sign for a stealth mod.

### 4.7 Multi-track handover, not single-track handover

`DESIGN-pragmatic.md` replicates one track (`SoM_DetTgt`/`DetLvl`/`DetSt`). After a handover in a four-player group, the new owner knows about one player and has forgotten the other three; those three re-acquire from zero over the next tier period. Under this design up to `ReplicatedSlots` (default 4, max 8) tracks cross the handover, and **when the loaded-player count is ≤ `ReplicatedSlots` every track replicates**, which covers the overwhelming majority of real sessions. §7 shows this costs *fewer* ZDO fields than the single-track scheme, not more.

---

## 5. The scheduler

### 5.1 Two phases, two budgets, and why they must not be one

```csharp
// Sched/Phases.cs, driven from a single MonoBehaviour Update on one DontDestroyOnLoad object.
private void Update()
{
    if (Runtime.Mode == RuntimeMode.Inert || Runtime.Mode == RuntimeMode.Booting) return;
    float now = Time.time;

    // ---- PHASE A: AUTHORITY. Correctness work. NEVER budgeted, NEVER skipped. ----
    Wheel.DrainAuthority(now);          // ownership poll, policy probe, ack, digest read/write, rehydrate

    if (Runtime.Mode == RuntimeMode.Relay) return;   // headless stops here. No physics, no HUD.

    // ---- PHASE B: PERCEPTION. Quality work. Budgeted. ----
    Budget.BeginFrame();
    if (now >= _nextViews) { PlayerRegistry.Refresh(now); _nextViews = now + Cfg.ViewPeriod; }   // 10 Hz
    if (now >= _nextVeg)   { VegetationSampler.Step(now); _nextVeg   = now + Cfg.VegPeriod;   }  // ~7 Hz
    Wheel.DrainPerception(now, Time.deltaTime);
    ThreatQuery.Publish(now);
    SelfLimit.Observe(now);
}
```

**This split is the clearest expression of the lens in the scheduler.** `DESIGN-pragmatic.md` runs one budgeted cursor over everything, so under load the ownership poll, the handover seeding and the digest write are starved *together with* the LOS casts. That is precisely backwards: the moment the frame is busy — a raid, a boss, thirty creatures in view — is the moment ownership is churning fastest and the moment forgetting a chase is most visible. Phase A is O(1) per creature per period and measured; if Phase A alone ever exceeds its own soft limit that is a bug worth a loud warning, not something to silently absorb.

Phase A cost at 200 creatures and a 0.25 s period: 800 visits/s × (two boolean field reads + three `GetInt(int,int)` + a branch) ≈ **60 ns each ⇒ 48 µs/s**. It is not worth budgeting.

### 5.2 A timing wheel, not a cursor

```csharp
// Sched/Wheel.cs
internal static class Wheel
{
    internal const int   Slots = 64;
    internal const float TickPeriod = 0.05f;          // 20 Hz, matching vanilla's AI cadence (L60996)

    private static readonly List<CreatureRecord>[] _auth    = NewRing();
    private static readonly List<CreatureRecord>[] _percept = NewRing();
    private static readonly List<CreatureRecord>   _carry   = new List<CreatureRecord>(64);
    private static int _cursor;
    private static float _accum;

    internal static void Arm(CreatureRecord r, List<CreatureRecord>[] ring, ref int slotField, float period)
    {
        int steps = Mathf.Clamp(Mathf.CeilToInt(period / TickPeriod), 1, Slots - 1);
        slotField = (_cursor + steps) & (Slots - 1);
        ring[slotField].Add(r);
    }

    internal static void DrainPerception(float now, float dt)
    {
        _accum += Time.deltaTime;
        while (_accum >= TickPeriod)
        {
            _accum -= TickPeriod;
            _cursor = (_cursor + 1) & (Slots - 1);
            var bucket = _percept[_cursor];

            // Anything carried over from a starved tick is served FIRST. A record can never be
            // dropped, only delayed, and the delay is measured.
            Serve(_carry, now, dt, carryOnExhaust: true);
            Serve(bucket, now, dt, carryOnExhaust: true);
            bucket.Clear();
        }
    }

    private static void Serve(List<CreatureRecord> list, float now, float dt, bool carryOnExhaust)
    {
        int i = 0;
        for (; i < list.Count; i++)
        {
            if (Budget.Exhausted) break;
            CreatureRecord r = list[i];
            if (!r.Valid) continue;
            float pairDt = Mathf.Clamp(now - r.LastPerceptAt, dt, Cfg.MaxEvalTimestep);   // 0.5 s cap
            try { Brain.Pass(r, pairDt, now); }
            catch (System.Exception e) { Runtime.Fault("Brain.Pass", e); }
            r.LastPerceptAt = now;
            Arm(r, _percept, ref r.PerceptBucket, TierPeriod(r.Tier));
        }
        if (i < list.Count && carryOnExhaust)
        {
            for (int k = i; k < list.Count; k++) { list[k].LateTicks++; _carry.Add(list[k]); }
            Telemetry.Starved(list.Count - i);
        }
        if (!ReferenceEquals(list, _carry)) return;
        _carry.RemoveRange(0, Mathf.Min(i, _carry.Count));
    }

    private static float TierPeriod(byte tier) => tier switch
    {
        0 => Cfg.PeriodNear,     // 0.10 s  - nearest tracked player <= NearRange (25 m)
        1 => Cfg.PeriodMid,      // 0.30 s  - <= MidRange (50 m)
        2 => Cfg.PeriodFar,      // 1.00 s
        _ => Cfg.PeriodDormant,  // 3.00 s  - no tracked player inside the sense gate
    };
}
```

**Why a wheel and not `DESIGN-pragmatic.md`'s cursor.** A cursor walk that stops on budget exhaustion resumes at the same index next frame, which is fair *on average* — but when the budget is chronically short, the set of creatures served is determined by list order, and list order is insertion order, which is spawn order. The starved subset is therefore **stable and invisible**: the same creatures never update, forever, with no signal. In a group session that reads as "the mobs near player D never react", which is a multiplayer-shaped bug indistinguishable from the one this whole rebuild exists to kill. With a wheel, a record that is due is in exactly one bucket, that bucket must be drained, and anything that does not fit is carried to the next tick with a `LateTicks` increment. Starvation becomes *lateness*, and lateness is a number. Cost: ~40 lines and one `List` add/remove per creature per period (~15 ns) versus zero for a cursor. That is the trade, stated.

`Tier` is recomputed at the end of each pass from the distance to the **nearest tracked player**, never from `Player.m_localPlayer`. `Player.m_localPlayer` (L15457) appears nowhere outside `Hud/`.

### 5.3 Budget, and the LOS quota that refuses rather than guesses

```csharp
// Sched/Budget.cs
internal static class Budget
{
    private static readonly System.Diagnostics.Stopwatch _sw = new System.Diagnostics.Stopwatch();
    private static double _limitMs;
    internal static int LosLeft;
    internal static int LosDemanded, LosServed;      // per second, for SelfLimit + `somstatus`

    internal static void BeginFrame()
    {
        _sw.Restart();
        _limitMs = Cfg.FrameBudgetMs;                // default 1.0 ms
        LosLeft  = Cfg.LosPerFrame;                  // default 24
    }
    internal static bool Exhausted => _sw.Elapsed.TotalMilliseconds >= _limitMs;
    internal static bool TakeLos()
    {
        LosDemanded++;
        if (LosLeft <= 0) return false;
        LosLeft--; LosServed++; return true;
    }
}
```

```csharp
// Perception/LosSampler.cs  -- the single most important refusal in the mod.
internal static void Sample(CreatureRecord rec, ref Track t, in PlayerView v, in ResolvedPolicy p, float now)
{
    if (p.IgnoreLos) { t.Flags |= TrackFlags.LosClear | TrackFlags.LosUsable; return; }

    bool fresh = (now - t.LosSampleTime) < Cfg.LosRecheckPeriod;      // 0.25 s
    if (fresh) return;                                                // reuse the cached bit

    // Refuse rather than guess: an unloaded zone or a zero mask makes Physics report "clear".
    // Vanilla's own gate is `if (Physics.Raycast(...)) return false;` at L4614 - nothing hit
    // means visible. Both failures therefore point at MAXIMALLY VISIBLE, which is forbidden.
    if (!Caps.Has(SoMCapability.LineOfSight | SoMCapability.ViewBlockMask)
        || !VanillaOps.ZoneLoaded(rec.Tf.position)
        || !VanillaOps.ZoneLoaded(v.Position))
    { t.Flags &= ~TrackFlags.LosUsable; return; }

    // Cheap cull: a player who could not be seen even with a perfectly clear line is not worth a cast.
    if (v.AdjustedVis * PairMath.VisFalloff(rec, in v, in p) < Cfg.VisionThreshold * 0.5f)
    { t.Flags &= ~(TrackFlags.LosClear); t.Flags |= TrackFlags.LosUsable; t.LosSampleTime = now; return; }

    // Already Alerted? Vanilla ignores the FOV cone at L4608 and SoM does not need the cast either.
    if (t.State >= (byte)VanillaAlertness.Alerted)
    { t.Flags |= TrackFlags.LosClear | TrackFlags.LosUsable; t.LosSampleTime = now; return; }

    if (!Budget.TakeLos())                                            // starved this frame
    {
        if (now - t.LosSampleTime > Cfg.LosStaleLimit)                // 2 s: the bit is no longer evidence
            t.Flags &= ~TrackFlags.LosUsable;
        return;
    }

    bool clear = !Physics.Linecast(rec.EyePos(), v.AimPoint, LayerMasks.ViewBlock);
    t.Flags = clear ? (t.Flags | TrackFlags.LosClear) : (t.Flags & ~TrackFlags.LosClear);
    t.Flags |= TrackFlags.LosUsable;
    t.LosSampleTime = now;
}
```

and the payoff, in the sight patch:

```csharp
// Patches/P_CanSeeTarget.cs -- the sight patch demands the LOS capability at the GATE.
Refusal r = Gate.TryOpinion(__instance, target,
                SoMCapability.LineOfSight | SoMCapability.ViewBlockMask, out var rec, out int ti);
if (r != Refusal.None) return true;                       // vanilla answers
ref Track t = ref rec.Tracks.At(ti);
if ((t.Flags & TrackFlags.LosUsable) == 0) { Gate.CountNoLos(); return true; }   // vanilla answers
__result = t.CanSee;
return false;
```

**So when the LOS budget is oversubscribed, sight falls back to vanilla's own raycast, per creature, per pair, per frame — not to a stale SoM guess and not to a global kill switch.** Vanilla calls `CanSeeTarget` at most twice per creature per `UpdateTarget` tick (L5920-5921, throttled to 2 s / 6 s at L5850-5854) plus once per candidate in `FindEnemy` every 2-6 s, so the fallback load is bounded by a rate the unmodded game already pays. The behavioural degradation is exact and describable: *"this pair is using vanilla sight this frame"* — which is the declared floor, arrived at one pair at a time.

### 5.4 Self-limiting, announced

```csharp
// Sched/SelfLimit.cs
internal static void Observe(float now)
{
    if (now < _nextCheck) return; _nextCheck = now + 5f;

    float sweep   = Telemetry.MeasuredSweepPeriod();        // real time to visit every due record once
    float losRate = Budget.LosDemanded == 0 ? 1f : (float)Budget.LosServed / Budget.LosDemanded;
    Budget.LosDemanded = Budget.LosServed = 0;

    if (sweep > Cfg.PeriodNear * 2f) _overrun += 5f; else _overrun = 0f;

    if (_overrun >= 30f)
    {
        _overrun = 0f;
        Cfg.EffectiveRange = Mathf.Max(Cfg.PerfRangeFloor, Cfg.EffectiveRange * 0.85f);
        Cfg.LosRecheckPeriod = Mathf.Min(1.0f, Cfg.LosRecheckPeriod * 1.5f);
        Log.Warn($"SoM cannot hold its schedule (sweep {sweep*1000:F0} ms vs target " +
                 $"{Cfg.PeriodNear*1000:F0} ms, LOS served {losRate:P0}). Reducing evaluation range to " +
                 $"{Cfg.EffectiveRange:F0} m and LOS recheck to {Cfg.LosRecheckPeriod:F2} s. " +
                 "Raise FrameBudgetMs or lower EvaluationRange to stop this.");
    }
}
```

`EffectiveRange` may only ever move **down** from the ServerSync'd `EvaluationRange` and never below `PerfRangeFloor`; it is a local perf lever that can shrink SoM's scope but never widen it past what the server agreed. That preserves the config-authority split in §10.4.

### 5.5 Cost model — 200 creatures × 10 players

Worst realistic case: a player-host owning all 200 creatures. Sense radius 50 m. Tier mix 40 near / 60 mid / 60 far / 40 dormant ⇒ **673 creature passes per second**. "Typical" = 2.5 players inside a creature's gate; "worst" = all 10 inside every gate.

| Term | Rate | Unit | Worst (10 in range) | Typical (2.5) |
|---|---|---|---|---|
| **Phase A** authority poll + policy triple probe | 200 × 4 Hz | ~60 ns | **48 µs/s** | 48 µs/s |
| Registry sweep (reconcile `BaseAIInstances`) | 2 Hz × ~250 | ~15 ns | 7 µs/s | 7 µs/s |
| Player views (kinematics + cached armor/biome) | 10 Hz × 10 | ~1.5 µs | 150 µs/s | 150 µs/s |
| Player views, expensive tier (1 shadow cast + biome) | 2 Hz × 10 | 1 cast | 20 casts/s ≈ 0.3 ms/s | 0.3 ms/s |
| Vegetation sample (1 player/step, non-alloc) | ~7 Hz | 1 overlap | 7 casts/s ≈ 0.1 ms/s | 0.1 ms/s |
| Creature pass, fixed (aggregate, tier, wheel re-arm) | 673/s | ~0.5 µs | **337 µs/s** | 337 µs/s |
| **Pair integration** | passes × in-range players | ~50 ns | 6 730 pair/s ⇒ **337 µs/s** | 1 683 pair/s ⇒ 84 µs/s |
| **LOS linecasts** (quota-bound) | cap 24/frame @ 60 fps | ~15 µs | **1 440/s ⇒ 21.6 ms/s (2.2 %)** | ~700/s ⇒ 10.5 ms/s |
| — LOS *demand* after culls | | | ~4 000/s ⇒ **36 % served** | ~1 100/s ⇒ 64 % served |
| Digest write (hot creatures only) | ~60 × 4 Hz × ≤6 sets | ~80 ns | 1 440 sets/s ⇒ 115 µs/s | ~350 sets/s ⇒ 28 µs/s |
| Digest read (foreign creatures; 0 on a host) | — | — | 0 | 0 |
| Ally calls | ≤20 engaged × 1/3 s | masked overlap(32) | 7/s ⇒ 0.3 ms/s | 0.3 ms/s |
| **Patch answers** — `FindEnemy` 200/2 s × ~250 candidates | 25 000 calls/s | 5 ns non-player early-out; 58 ns with a track | **0.18 ms/s** | 0.18 ms/s |
| Sensing observers (Diagnostics, 1/32 sampled, read-only) | ~780/s | ~40 ns | 31 µs/s | 31 µs/s |
| Refusal accounting | every gate call | ~2 ns | 50 µs/s | 50 µs/s |
| **Total** | | | **≈ 23.3 ms/s ≈ 2.3 % of one core** | **≈ 12 ms/s ≈ 1.2 %** |
| **Steady-state allocation** | | | **0 B/s** | 0 B/s |

Notes on the numbers, so they can be argued with:

- **LOS dominates, and it is hard-capped.** At 200 × 10 the demand is ~2.8× the quota, so the effective recheck period stretches from 0.25 s to ~0.7 s, and every pair whose bit ages past `LosStaleLimit` hands sight back to vanilla. The degradation is bounded, measured (`LosServed/LosDemanded`), and announced by `SelfLimit`.
- **The `FindEnemy` number turns on the early-out.** `BaseAI.FindEnemy` (L5191-5223) walks `Character.GetAllCharacters()` and calls `CanSenseTarget(item)` on every non-dead enemy. In a 250-character neighbourhood ~10 are players, so ~96 % of calls exit at `!target.IsPlayer()` for ~5 ns. If a mod makes many non-players answer `IsPlayer()` true, this term rises to 25 000 × 58 ns = 1.45 ms/s — still bounded, and it is the one term with a genuinely adversarial worst case.
- **My gate is about 2.3× the per-call cost of `DESIGN-pragmatic.md`'s** (58 ns vs its stated ~25 ns) because of the player-id resolution, the capability check and the refusal counter. That is a real, deliberate cost; §13 says what I would cut.
- **Zero steady-state allocation** requires: no LINQ anywhere; no lambda that captures; `Track[]`, `PlayerView[]`, the wheel's `List<>`s and the overlap buffers all preallocated; `Physics.OverlapSphereNonAlloc`; and every interpolated string built **inside** an `if (Log.X)`. v1's ~46 eagerly-built debug strings per evaluation (≈700 KB/s, ~65 % of its GC pressure) are the specific regression this guards against.

### 5.6 Relationship to vanilla's clock

Vanilla ticks AI at a fixed 20 Hz with `dt` hardcoded to `0.05f` (`MonoUpdaters.FixedUpdate`, L60996-60999), not `Time.fixedDeltaTime`. SoM integrates on its own wheel with a **measured** `pairDt` clamped to `MaxEvalTimestep`, and the `MonsterAI.UpdateAI` postfix is **apply-only** — it never evaluates. The two clocks never race. v1's `Humanoid_Attack_Patch.cs:74` fallback, which ran a full sensing pass with `Time.deltaTime` from inside an attack prefix against a different player, is deleted rather than ported.

---

## 6. The patch layer

### 6.1 Coexistence doctrine

Six rules. Rules 1-3 overlap `DESIGN-pragmatic.md`; 4-6 are the additions this lens requires.

**1. Cede first say, always.** Every SoM prefix is `[HarmonyPriority(Priority.Last)]` (0) and takes `bool __runOriginal`. Harmony emits a prefix that declares `__runOriginal` unconditionally, so SoM's prefix still runs after an earlier prefix returned `false` — and immediately returns `false` without touching `__result`. `DESIGN-pragmatic.md` uses `Priority.Low` (200); I go one step further to `Last` because a mod author should not have to know SoM exists in order to win, and because **this rule's own failure mode is also vanilla**: if a future HarmonyX stops emitting `__runOriginal` prefixes unconditionally, SoM's prefix simply does not run, which is identical to SoM having no opinion. Residual: two mods both at `Last` are ordered by install order, which is nondeterministic. Stated, not solved.

**2. No writing postfix on any behavioural method.** v1 paired a `int.MaxValue` prefix with an `int.MinValue` postfix that re-ran the identical four-`GetComponent` guard chain to re-assert the identical value, purely to beat other mods' postfixes. That is ~50 % of the patch layer's CPU and a live sort-order race. SoM v2 does not contest the last word.

**3. Observe the argument you refuse to have.** Ceding the final say means SoM can no longer *detect* that it was overruled — and a stealth mod that has silently stopped applying is the worst outcome in this whole document. So each sensing method carries a **read-only observer postfix** at `Priority.Last` in the `Diagnostics` group, sampled 1-in-32:

```csharp
// Patches/Observers/O_Sensing.cs
[HarmonyPatch(typeof(BaseAI), nameof(BaseAI.CanSeeTarget), new[] { typeof(Character) })]
internal static class O_CanSeeTarget
{
    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    private static void Postfix(BaseAI __instance, Character target, bool __result)
    {
        if (!Cfg.ObserveContention) return;
        if ((++_n & 31) != 0) return;                                  // 1/32 sample
        if (Gate.TryOpinion(__instance, target, 0, out var rec, out int ti) != Refusal.None) return;
        if (rec.Tracks.At(ti).CanSee == __result) return;
        Contention.Note(SoMPatch.CanSeeTarget, rec, __result);         // once-per-(method, prefab)
    }
}
```

`Contention.Note` logs once with the coexistence census for that method attached — *"another mod is answering `BaseAI.CanSeeTarget` after SoM; owners on this method are `com.example.other` (prefix, priority 400)"* — sets `SoMCapability.Contested`, and surfaces in `SoMInterop.ContestedMethods()` and `somstatus`. SoM never wins the argument; it always reports it. Cost measured in §5.5: 31 µs/s. This is the direct application of MoA's own lesson — *a conditional remedy has as many silent failure modes as it has conditions* — to the case where the remedy has been deliberately given up.

**4. Atomic install groups.** Sensing is installed **all three or none**, and this is not fastidiousness. Vanilla's `CanSenseTarget(Character)` forwards to the static ten-argument overload (L4519-4544), which calls the **static** `CanHearTarget`/`CanSeeTarget` (L4534-4542) — *not* the instance overloads SoM patches. So if `CanSenseTarget`'s patch fails to install while `CanSeeTarget`'s succeeds, `BaseAI.FindEnemy` (L5203) gets pure-vanilla answers while `MonsterAI.UpdateTarget` (L5920-5921) gets SoM answers, on the same creature, in the same frame. That is two perception models on one entity — worse than either alone and invisible in a log. If a required group fails, `PatchInstaller` calls `UnpatchSelf()`, reinstalls `Diagnostics` only, and enters `Inert` with one loud line.

**5. Never patch a method reachable through a public one.** `MonsterAI.UpdateTarget` (private, L5846), `BaseAI.FindEnemy` (protected, L5191), `Character.RPC_Damage` (private, L8701), `BaseAI.SetAlerted` (protected virtual, L5350) are **called**, some through soft bindings, but none are patched. Every one is a private/protected member whose signature encodes a design decision and is therefore a Tier-1 v1.0 rename risk (SURVEY-vanilla-api PART 5), and every one is a likely third-party patch target.

**6. Census, twice.** `Harmony.GetPatchInfo(MethodBase)` is public API and returns each patch's `owner`, `priority`, `before` and `after`. `Capability/Coexistence.cs` runs it over every SoM target immediately after install and again ten seconds later (mods that patch from `Start` or lazily are invisible at install time), and writes one table to the log plus `SoMInterop.Coexistence()`. This turns "some other mod is on this method" from a support-forum guessing game into a line the user can paste. Degradation if `GetPatchInfo` is unavailable: `Degrade.Cosmetic`, census skipped, one info line.

### 6.2 The patch table

| # | Class | Target (all with explicit type arrays) | Kind | Priority | Group | Skips vanilla? | Guard order |
|---|---|---|---|---|---|---|---|
| 1 | `P_CanSeeTarget` | `BaseAI.CanSeeTarget(Character)` L4577 | Prefix | `Last` (0) | **Sensing** (req, atomic) | only with an opinion | `__runOriginal` → `Gate.TryOpinion(need: LineOfSight\|ViewBlockMask)` → `LosUsable` |
| 2 | `P_CanHearTarget` | `BaseAI.CanHearTarget(Character)` L4546 | Prefix | `Last` | **Sensing** | ″ | `__runOriginal` → `Gate.TryOpinion(need: 0)` |
| 3 | `P_CanSenseTarget` | `BaseAI.CanSenseTarget(Character)` L4519 | Prefix | `Last` | **Sensing** | ″ | `__runOriginal` → `Gate.TryOpinion(need: 0)` → OR of the two track bits |
| 4 | `P_BaseAI_Awake` | `BaseAI.Awake()` L4013 | Postfix | `Normal` | **Lifecycle** (req, atomic) | n/a | register record; cache components; subscribe `Character.m_onDamaged` (L6875) |
| 5 | `P_Character_OnDestroy` | `Character.OnDestroy()` L7390 | Prefix | `Normal` | **Lifecycle** | n/a | unregister; **unsubscribe the delegate** |
| 6 | `P_MonsterAI_UpdateAI` | `MonsterAI.UpdateAI(float)` L5954 | Postfix | `Last` | Drive | n/a | registry → `Owned` → `!BrainOff` → `!IsDead/!IsTamed` → **`!IsSleeping()`** → decision fresh |
| 7 | `P_Humanoid_StartAttack` | `Humanoid.StartAttack(Character,bool)` L13073 | Prefix | `Last` | AttackGate | only with an opinion | `__runOriginal` → `HardBlockAttacks` → `Gate` on the *current target's* track |
| 8 | `P_Character_SetMoveDir` | `Character.SetMoveDir(Vector3)` L9503 | Prefix (`ref dir`) | `Last` | Movement | never | `is Player` → registry → `Action ∈ {Search, Investigate}` → magnitude |
| 9 | `P_Player_OnSpawned` | `Player.OnSpawned(bool)` L17611 | Postfix | `Normal` | Lifecycle (optional) | n/a | poke `PlayerRegistry`; re-read `GetPlayerID()` |
| O1-O3 | `O_Sensing` ×3 | the three sensing targets | Postfix | `Last` | Diagnostics | never | sampled 1/32, read-only |

Nine functional patches. **`BaseAI.IsAlerted` is not patched. Neither damage patch survives.** Both decisions are argued in §11.

Every target is declared with an explicit `new[] { typeof(...) }` array. v1's `Humanoid.StartAttack`, `Character.Damage` and `Character.OnDamaged` had none; a single added overload in Valheim 1.0 throws `AmbiguousMatchException` out of `PatchAll`, and v1 swallowed that inside `ShadowsOfMidgard.cs:50` while still logging "loaded successfully".

Injected-parameter discipline: `P_Humanoid_StartAttack` injects **only** `__instance` and `__runOriginal`. The second parameter is named `charge` on `Character.StartAttack` (L9675) and `secondaryAttack` on the `Humanoid` override (L13073); Harmony matches injected parameters by name, so naming either is a latent break. `P_Character_SetMoveDir` must inject `ref Vector3 dir` and therefore *does* depend on the parameter name — `Caps` probes the parameter name at bind time and drops the `Movement` group with one warning if it has changed, rather than letting Harmony throw.

### 6.3 `P_MonsterAI_UpdateAI` — apply-only, per-target, sleep-guarded

```csharp
[HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateAI), new[] { typeof(float) })]
internal static class P_MonsterAI_UpdateAI
{
    [HarmonyPostfix, HarmonyPriority(Priority.Last)]
    private static void Postfix(MonsterAI __instance, float dt)
    {
        CreatureRecord rec = CreatureRegistry.Find(__instance);
        if (rec == null || rec.Auth != Authority.Owned || !rec.Valid) return;
        if (rec.Policy.BrainOff) return;
        if (rec.Char.IsDead() || rec.Char.IsTamed()) return;
        if (__instance.IsSleeping()) return;                       // L6508. v1 had NO sleep guard and
                                                                   // could re-drive a sleeping creature
                                                                   // from a 60-frame-old cached decision.
        if (Time.time - rec.DecisionAt > Runtime.OpinionTtl) return;

        // Resolve the decision's subject BY STABLE PLAYER ID. Never Player.m_localPlayer.
        Player subject = PlayerRegistry.ResolvePlayer(rec.Decision.SubjectPlayerId);
        try { Drive.Apply(rec, in rec.Decision, subject, dt); }     // dt is vanilla's 0.05 f, and we use it
        catch (System.Exception e) { Runtime.Fault("Drive.Apply", e); }
        AlertDriver.Apply(rec, Time.time);
    }
}
```

`Drive.Apply` differs from v1's `ExecuteAction` in three ways: it takes the resolved subject rather than the local player; its `Idle` branch clears `m_targetCreature` **only if the current target is the player SoM decided about**, so it can never strip a legitimately-acquired remote target; and every vanilla mutation goes through a `VanillaOps.TryX` ladder (§8) rather than a raw reflected handle.

### 6.4 `AlertDriver` — driving the real setter instead of faking the getter

Not patching `BaseAI.IsAlerted()` repairs, with no further work: vanilla `Alert()` (L5327-5340, guarded by `!IsAlerted()`) stops being a permanent no-op; `m_animator.SetBool("alert", …)` fires again; `ZDOVars.s_alert` replicates so every *other* client's non-owner branch (L4113-4117) stops reading `false` forever; `m_alertedEffects` (the aggro roar) plays; boss `activeBosses` counting works; `m_alertedMessage` shows; the give-up/leash block at L5937-5950 arms; event creatures despawn again (L5988); `UpdateConsumeItem` runs so taming does not stall (L6045); the permanent backstab exploit at L8736 closes; Sneak XP via `InStealthRange` (L5390) is correct; and two clients stop seeing contradictory `EnemyHud` icons (L38631-38636).

**And it matters more now than it did in the survey, not less.** MoA 0.1.2 calls `ai.Alert()` on the Warden every 0.2 s (`AvalorStealthCompat.cs:182`) and `MakeItHunt` — which also calls `Alert()` — on everything else, and it **deleted the `SuppressAlertForce` re-entrancy bracket that used to protect it**. MoA's mobs are normally exempt so SoM's brain does not touch them; but MoA's own comment at `:139-141` documents the window where the stamp has not landed yet (`Character.Awake` runs inside `Instantiate`, before `ZNetView` is ready) and is repaired up to 0.2 s later, or never on a machine that does not own the creature. In that window, a v1-shaped `IsAlerted` force would silently disarm the Warden with no safety net underneath.

```csharp
internal static class AlertDriver
{
    internal static void Apply(CreatureRecord rec, float now)
    {
        if (!Cfg.DriveVanillaAlert) return;
        bool want = rec.AggState >= (byte)VanillaAlertness.Alerted;
        bool have = rec.Ai.IsAlerted();                                    // the REAL field now
        if (want == have) { rec.WantAlert = want; return; }
        if (now - rec.LastAlertTransitionAt < Cfg.AlertDebounce) return;    // 1.0 s, no animator churn

        if (want)
        {
            if (!VanillaOps.TryAlertRaise(rec.Ai)) return;                  // ladder in §8, row `alert.raise`
        }
        else
        {
            if (rec.AggDetection > Cfg.AlertedThreshold * 0.7f) return;     // hysteresis vs vanilla re-raising
            if (!VanillaOps.TryAlertLower(rec.Ai)) return;                  // row `alert.lower`
        }
        rec.LastAlertTransitionAt = now;
        rec.WantAlert = want;
    }
}
```

---

## 7. Replicated ZDO state and write discipline

### 7.1 The keys SoM writes

| Key | ZDO type | Written by | Cadence | Purpose |
|---|---|---|---|---|
| `SoM_DH` | `long` | owner only | on change, ≤4 Hz hot / ≤0.5 Hz cold | Digest header: writer fold, generation, slot count, schema, creature flags. |
| `SoM_D0` … `SoM_D3` | `long` each | owner only | ″ | One packed per-player detection record per slot, ranked by detection descending. |
| `SoM_LKP` | `Vector3` | owner only | ≤1 Hz, only while slot 0 ≥ `Alerted` | Last-known position of the dominant target, so search behaviour survives handover. |
| `SoM_Ack` | `int` | owner only | once per creature per resolution | **Frozen semantics from `SURVEY-interop.md` §5.6.1:** `= ContractVersion`. Written *inside* `PolicyResolver`. |
| `SoM_Applied` | `int` | owner only | with `SoM_Ack` | Bitfield: which Tier-2 keys were **honoured**. Additive; §9.6. |
| `SoM_Reject` | `int` | owner only | with `SoM_Ack` | Bitfield: which Tier-2 keys were present but **rejected**, and why-class. Additive; §9.6. |

Vanilla's `ZDOVars.s_alert` (`"alert"`, L66414) and `s_haveTargetHash` (`"haveTarget"`, L66502) are written by **vanilla itself** again in v2, because `AlertDriver` drives the real setter and `Drive` calls the real `SetTargetInfo`. SoM writes neither directly. That is a strict improvement over v1, where `s_alert` was frozen at `false` for every remote client for the entire session.

### 7.2 The digest encoding

```csharp
// Authority/Digest.cs
internal static class Digest
{
    internal const int MaxSlots = 4;      // config `ReplicatedSlots`, 1..8
    internal const int Schema   = 1;

    internal struct Header
    {
        public uint WriterFold;   // bits 0..31  low-32 fold of ZDOMan.GetSessionID() (L65750)
        public byte Gen;          // bits 32..39 monotone, wraps; a reader watches this for freshness
        public byte SlotCount;    // bits 40..43
        public byte Schema;       // bits 44..47
        public ushort Flags;      // bits 48..63 creature-level: BrainOff, Contested, Reduced, Relay
    }

    internal struct Slot
    {
        public uint  PlayerFold;      // bits 0..31  (uint)(playerId ^ (playerId >> 32))
        public float Detection;       // bits 32..39 quantised /255
        public byte  State;           // bits 40..41 VanillaAlertness ordinal
        public byte  Bits;            // bits 42..47 CanSee|CanHear|LosClear|Floored|Seeded|EverSensed
        public float TimeSinceSensed; // bits 48..55 deciseconds, saturating at 25.5 s
                                      // bits 56..63 reserved, must be written 0
    }

    internal static long PackSlot(in Track t)
    {
        uint fold = (uint)(t.PlayerId ^ (t.PlayerId >> 32));
        long v = fold;
        v |= (long)Mathf.Clamp(Mathf.RoundToInt(t.Detection * 255f), 0, 255) << 32;
        v |= (long)(t.State & 0x3)                                            << 40;
        v |= (long)(SlotBits(t.Flags) & 0x3F)                                 << 42;
        v |= (long)Mathf.Clamp(Mathf.RoundToInt(t.TimeSinceSensed * 10f), 0, 255) << 48;
        return v;
    }
}
```

Four decisions worth defending:

- **No timestamp in the digest.** There is no clock shared between peers that is cheap and reliable, so freshness is measured by the *reader*: each peer keeps `LastSeenGen` and `LastGenChangeAt` (local `Time.time`) per foreign creature, and a digest whose generation has not moved for `DigestFreshWindow` (3 s) is **stale by definition**, whatever it says. That works if the owner left, if the owner is a vanilla client, if replication is configured off, and if the network is simply slow — one mechanism for four failure modes.
- **One `long` per slot, not a field per attribute.** `DESIGN-pragmatic.md` writes `SoM_DetTgt` (a `ZDOID`, which `ZDO.Set(string, ZDOID)` at L62385-62394 expands into **two** entries) plus `SoM_DetLvl` (float), `SoM_DetSt` (int) and `SoM_LKP` (Vector3) — six ZDO entries carrying **one** player's state. This design carries **four** players' state in five entries. It is cheaper on the wire *and* strictly more informative, and each slot's fields cannot disagree with each other because they are one value.
- **Slot 0 is always the dominant track** (`TrackSet.RankInPlace` before packing), so a consumer that only wants "who is this creature most aware of" reads exactly one `long`.
- **When `PlayerRegistry.Count <= ReplicatedSlots`, every track replicates.** With the default of 4, every player in a ≤4-player session always sees their own track from every creature. Above that, at most `Count - 4` players can be missing from a given creature's digest, and their HUD correctly shows **unknown** rather than **quiet** (§7.5).

The `PlayerFold` is a 32-bit fold of a 64-bit id, so collisions are possible in principle. `PlayerRegistry.ResolveFold` resolves a fold against the **loaded players on this machine** (typically ≤10) and returns 0 if two of them collide, which downgrades to "no information" rather than to "the wrong player". A 32-bit collision between two players in the same session is ~1 in 4 × 10⁹ per pair; the handling is there because "unlikely" is not "impossible" and the failure would otherwise be a silent identity swap.

### 7.3 Write discipline

```csharp
// Authority/DigestWriter.cs -- called from PHASE A only.
internal static void Write(CreatureRecord rec, float now)
{
    if (rec.Auth != Authority.Owned) return;                 // rule 1
    if (!Cfg.ReplicateDetection || !Caps.Has(SoMCapability.ZdoDigest)) return;
    if (now < rec.NextDigestWriteAt) return;

    bool hot = rec.AggDetection >= Cfg.AlertedThreshold;
    rec.NextDigestWriteAt = now + (hot ? Cfg.HotDigestPeriod : Cfg.ColdDigestPeriod);   // 0.25 s / 2 s
    if (rec.AggDetection < Cfg.ReplicateFloor && rec.DigestGen == 0) return;            // never started

    rec.Tracks.RankInPlace();
    int n = Mathf.Min(rec.Tracks.Count, Cfg.ReplicatedSlots);

    bool changed = false;
    ZDO z = rec.NView.GetZDO(); if (z == null) return;
    for (int s = 0; s < Digest.MaxSlots; s++)
    {
        long packed = s < n ? Digest.PackSlot(in rec.Tracks.At(s)) : 0L;
        if (packed == rec.DigestSlots[s]) continue;                                     // rule 3
        rec.DigestSlots[s] = packed;
        z.Set(SoMKeys.SlotHash[s], packed);                  // ZDO.Set(int, long) L62508 - absolute
        changed = true;
    }
    if (!changed) return;

    unchecked { rec.DigestGen++; }
    z.Set(SoMKeys.HdrHash, Digest.PackHeader(Digest.SelfFold, rec.DigestGen, (byte)n, rec.Flags()));
    rec.LastDigestWriteAt = now;

    if (hot && Cfg.ReplicateLastKnownPos && rec.AggIndex >= 0)
        z.Set(SoMKeys.LkpHash, rec.AggLastKnownPos);
}
```

Five rules, in force everywhere:

1. **Owner only.** SURVEY-vanilla-api PART 3 item 18 establishes that non-owner writes *do* propagate — `IncreaseDataRevision` (L62635-62642) enqueues `ClientChanged`, and `RPC_ZDOData` accepts anything with `num4 > zDO.DataRevision` (L65533). That makes non-owner writes *possible*, not *correct*: two peers writing the same key race and ping-pong by revision. Static declarations (`SoMStealthExempt`, the Tier-2 directives) may be written by any peer once, because they are idempotent constants — which is exactly why MoA's and DvergrAllies' stamps work from a non-owning peer. **Mutable brain state may be written only by the owner.** This is a documented clause of the contract (§9.7), not just an internal rule.
2. **Idempotent absolute set, never read-modify-write.** Every value is computed from local state and assigned. A replicated field accumulated on more than one peer compounds — `Character.SetMaxHealth` (dossier L33) is the canonical vanilla example of the bug.
3. **Change-gated per slot.** Unchanged slots are not rewritten; the generation only bumps when something did change, which is what makes the reader's freshness test meaningful.
4. **Cadence follows consequence, not a clock.** Hot 4 Hz, cold 0.5 Hz, silent below `ReplicateFloor`. A creature nobody has noticed costs zero replication forever.
5. **Never flush on ownership loss.** By the time the authority poll observes `Owned → Foreign`, `IsOwner()` is already false and a write would race the new owner. The correct engineering answer is to keep the replicated view continuously fresh so the last written value is at most one hot period old — hence rule 4 — and to *measure* the gap: `Telemetry.HandoverOut(now - rec.LastDigestWriteAt)` feeds a p99 in `somstatus`.

Load estimate: at 200 creatures with ~60 hot, ≈1 440 `ZDO.Set` calls/s worst case, each bumping `DataRevision` on a ZDO that `ZSyncTransform` is already bumping 50×/s (§0.2). SoM's replication is a rounding error against the transform traffic the session already carries.

### 7.4 The residual this design accepts

`ZDO.SetOwnerInternal` (L63636) computes `Owner = uid == ZDOMan.GetSessionID()` on each peer from replicated ownership data with independent arrival times, so during propagation two peers can both believe they own a creature. Both will write the digest; last-writer-by-revision wins; the loser's write is discarded at L65533. **Consequence: a brief digest flicker affecting only non-owning HUDs and handover seeding, self-correcting within one hot period (250 ms).** I am not building a consensus protocol for this. The only mitigation is the `SelfSeedGuard` in `Rehydrate` (§4.6), which stops a peer re-seeding from a digest it wrote itself moments earlier — the one way the flicker could ratchet instead of settle.

### 7.5 What a non-owning client may legitimately render

This is the question the HUD has always got wrong, so the rule is explicit. For each creature within `HudRange` of the local player, the local client classifies it:

| Situation | Classification | HUD |
|---|---|---|
| `Owned` by us, a track exists for the local player | **authoritative** | full colour |
| `Foreign`/`Orphan`, digest generation moved within `DigestFreshWindow`, a slot's fold matches our player id | **authoritative-remote** | full colour |
| `Foreign`/`Orphan`, digest fresh, **no** slot matches us | **negative — real information** (the owner has no track for us) | quiet |
| `Foreign`/`Orphan`, no digest or generation stale | **unknown** | quiet **plus the unknown indicator** |
| `Owned` by us, no track for the local player | **negative** | quiet |

A non-owning client may **not** synthesise a track locally. That would be a second implementation of the brain producing different answers on different machines — the one-resolver invariant applied across the network. And it may not render another player's threat.

The load-bearing addition is the third visual state. v1 and `DESIGN-pragmatic.md` both collapse **unknown** into **quiet**, which is the silent lie: a player standing next to a creature owned by someone who has replication off, or by a vanilla client, or whose digest has not arrived, is told "you are not detected" when the truthful answer is "I do not know". `Hud/Gem.cs` therefore draws a thin desaturated ring whenever `ThreatQuery.UnknownNearby` is true. It costs one boolean in the published struct and is the most user-visible expression of "fail safe and loud" in the whole design.

```csharp
// Hud/ThreatQuery.cs -- published once per pass, read by Update(), which does nothing else.
internal struct LocalThreat
{
    public VanillaAlertness State;     // max over authoritative + authoritative-remote tracks about ME
    public float            Detection;
    public int              Sources;   // how many creatures currently have a track on me
    public bool             UnknownNearby;
    public float            Exposure;  // VisibilitySystem for the local player: purely local, always correct
}
```

---

## 8. The soft-binding / capability model

### 8.1 Shape

```csharp
// Capability/SoMCapability.cs
[System.Flags]
public enum SoMCapability : uint
{
    None            = 0,
    SenseSee        = 1u << 0,   SenseHear     = 1u << 1,   SenseCombined = 1u << 2,
    Lifecycle       = 1u << 3,   Drive         = 1u << 4,   AttackGate    = 1u << 5,
    MoveScale       = 1u << 6,   DamageHook    = 1u << 7,
    AlertRaise      = 1u << 8,   AlertLower    = 1u << 9,
    TargetWrite     = 1u << 10,  TargetInfoWrite = 1u << 11,
    PathMove        = 1u << 12,  StepMove      = 1u << 13,
    CreatureRoster  = 1u << 14,  PlayerRoster  = 1u << 15,  PlayerIdentity = 1u << 16,
    StealthFactor   = 1u << 17,  NoiseRange    = 1u << 18,
    LineOfSight     = 1u << 19,  ViewBlockMask = 1u << 20,  ZoneLoaded     = 1u << 21,
    Biome           = 1u << 22,  Light         = 1u << 23,  Weather        = 1u << 24,
    Vegetation      = 1u << 25,  Equipment     = 1u << 26,
    ZdoDigest       = 1u << 27,  LastKnownPos  = 1u << 28,
    Census          = 1u << 29,  Hud           = 1u << 30,
    Contested       = 1u << 31,  // NOT a binding: set at runtime by the observers (§6.1 rule 3)
}

// Capability/BindingSpec.cs
internal enum Degrade : byte
{
    Cosmetic,          // a display term goes neutral. Simulation identical.
    FeatureOff,        // one named feature stops. Per-target sensing intact.
    SubsystemVanilla,  // the affected question is handed back to vanilla, per-pair, permanently.
    ModeReduced,       // SoM demotes to Reduced for the session.
    ModeInert,         // SoM demotes to Inert for the session.
}

internal readonly struct BindingSpec
{
    public readonly SoMCapability Cap;
    public readonly string   Id;            // "alert.lower"
    public readonly string[] Ladder;        // in order; the last rung is always the declared floor
    public readonly Degrade  OnExhausted;
    public readonly string   Consequence;   // one sentence, printed verbatim in the capability report
}
```

**The enforcement rule, and it is a review rule with teeth:** `Capability/Caps.cs` is the only file in the assembly permitted to name a `MethodInfo`, `FieldRef`, `AccessTools.*` call or `LayerMask.GetMask`. Every consumer calls a `VanillaOps.TryX(...)` façade whose body *is* the ladder and whose return value is a `bool` the caller must handle. A grep for `AccessTools` outside `Capability/` failing review is the mechanism; there is no way to consume a binding without having gone past its declared degradation.

Resolution happens **once, explicitly, in plugin `Awake`**, never from a static constructor. v1's `BehaviorSystem`'s static constructor (`BehaviorSystem.cs:25`) ran on first touch from inside a Harmony patch with no ordering guarantee, and swallowed a `MissingFieldException` that left `CallNearbyAllies` inert for the entire life of the mod.

### 8.2 The ladders

Every row: the binding, its ordered fallback rungs, what SoM does when the ladder is exhausted, and the sentence printed to the user. **Nothing in this table degrades toward "maximally visible".**

#### Harmony targets (probed before patching; a failure fails the whole group)

| Cap | Target | Ladder | On exhausted | Consequence printed |
|---|---|---|---|---|
| `SenseSee` | `BaseAI.CanSeeTarget(Character)` L4577 | 1. `AccessTools.Method(typeof(BaseAI),"CanSeeTarget",[Character])` · 2. scan `BaseAI` public instance methods returning `bool` with exactly one `Character` parameter whose name contains "See" · 3. — | **ModeInert** (Sensing is required + atomic) | "SoM could not bind vanilla sight; the mod is inert this session and the game is running vanilla AI." |
| `SenseHear` | `BaseAI.CanHearTarget(Character)` L4546 | same shape, "Hear" | **ModeInert** | ″ |
| `SenseCombined` | `BaseAI.CanSenseTarget(Character)` L4519 | same shape, "Sense" | **ModeInert** — *not* optional: L4519-4544 forwards to the **static** overload which calls the **static** see/hear, so a partial install gives `FindEnemy` vanilla answers and `UpdateTarget` SoM answers on the same creature | ″ |
| `Lifecycle` | `BaseAI.Awake()` L4013 + `Character.OnDestroy()` L7390 | 1. `AccessTools.Method` by name · 2. `Character.Awake()` L7304 as the registration hook · 3. — | **ModeInert** | "SoM could not hook creature creation; without it there is no registry and no damage hook." |
| `Drive` | `MonsterAI.UpdateAI(float)` L5954 | 1. by name+`[float]` · 2. `BaseAI.UpdateAI(float)` L4107 with a `is MonsterAI` guard · 3. — | `ModeReduced` | "SoM cannot apply movement or targeting decisions; sensing is unaffected and vanilla drives behaviour." |
| `AttackGate` | `Humanoid.StartAttack(Character,bool)` L13073 | 1. by name+`[Character,bool]` · 2. — | `FeatureOff` | "SoM's attack gate is off; vanilla's own gate (`num3 && canSeeTarget && IsAlerted()`, L6148) still applies and is now fed correct per-target values." |
| `MoveScale` | `Character.SetMoveDir(Vector3)` L9503 **and** its parameter still being named `dir` | 1. method + parameter-name check · 2. — | `FeatureOff` | "Search movement runs at full speed instead of `SearchSpeedFactor`." |
| `DamageHook` | `Character.m_onDamaged` public delegate L6875 | 1. `Delegate.Combine` in the `BaseAI.Awake` postfix, exactly as vanilla does at L4028 · 2. `MonsterAI.OnDamaged(float,Character)` L5797 via Harmony postfix · 3. — | `FeatureOff` | "Being hit no longer raises SoM detection directly; vanilla still calls `SetAlerted(true)` on damage (L5800) and SoM now reads that honestly." |

#### Called members

| Cap | Member | Ladder | On exhausted | Consequence printed |
|---|---|---|---|---|
| `AlertRaise` | `BaseAI.Alert()` **public** L5327 | 1. direct call · 2. `SetAlerted(true)` via `AlertLower`'s handle · 3. — | `FeatureOff` | "SoM cannot raise alertness; vanilla raises it from damage (L5800) and from its own sensing, which SoM now answers correctly, so most cases still work." |
| `AlertLower` | `BaseAI.SetAlerted(bool)` protected virtual L5350 | 1. direct (publicized asm) · 2. `AccessTools.Method` → `Action<BaseAI,bool>` · 3. — (**not** a raw `"alert"` ZDO write: `SetAlerted` also drives the animator, the effects and the boss counter, and writing only the ZDO desynchronises them) | `FeatureOff` | "SoM cannot lower alertness early; creatures de-alert on vanilla's 30 s give-up (L5943) instead of on SoM's decay." |
| `TargetWrite` | `MonsterAI.m_targetCreature` private L5727 | 1. direct field (publicized) · 2. `AccessTools.FieldRefAccess<MonsterAI,Character>` — **note `MonsterAI`, not `BaseAI`; v1 named the wrong declaring type and the resulting `MissingFieldException` was swallowed, leaving `CallNearbyAllies` inert forever** · 3. `MonsterAI.SetTarget(Character)` private L5805 · 4. — | `FeatureOff` | "SoM cannot hand vanilla a target; vanilla's own `FindEnemy` acquires, and with per-target sensing correct it now acquires the right one." |
| `TargetInfoWrite` | `BaseAI.SetTargetInfo(ZDOID)` protected L5453 | 1. direct · 2. `AccessTools.Method` · 3. `zdo.Set("haveTarget".GetStableHashCode(), hasTarget)` — the body is literally `Set(s_haveTargetHash, !targetID.IsNone())` (L5455), so the ZDO write is behaviourally complete · 4. — | `Cosmetic` | "The `EnemyHud` 'aware' icon may lag by one AI tick." |
| `PathMove` | `BaseAI.MoveTo(float,Vector3,float,bool)` protected L4771 | 1. direct · 2. `AccessTools.Method` · 3. `BaseAI.MoveTowards(Vector3,bool)` **public** L4672 — the primitive `MoveTo` itself calls at L4821 · 4. `Character.SetMoveDir` **public** L9503 | `FeatureOff` | "Search and investigate movement steps toward the target instead of pathing around obstacles." |
| `StepMove` | `BaseAI.MoveTowards(Vector3,bool)` public L4672 | 1. direct · 2. `Character.SetMoveDir` L9503 | `FeatureOff` | (as above) |
| `CreatureRoster` | `BaseAI.BaseAIInstances` public static L4011 | 1. property · 2. `BaseAI.GetAllInstances()` L5476 (note: `Awake`/`OnDestroy`-scoped, so it includes disabled AIs — filter) · 3. `Character.GetAllCharacters()` L10316 + `GetBaseAI()` L10587 · 4. SoM's own registry from the `BaseAI.Awake` postfix | none — rung 4 always exists because `Lifecycle` is required | "SoM is enumerating creatures from its own registry." |
| `PlayerRoster` | `Player.GetPlayersInRange(Vector3,float,List<Player>)` L20388 | 1. direct (non-allocating) · 2. `Player.GetAllPlayers()` L20441 + distance filter · 3. `ZNet.instance.GetAllCharacterZDOS()` L68699 (positions only; the headless path) | `ModeReduced` | "SoM cannot enumerate players and is limited to the local player's own exposure display." |
| `PlayerIdentity` | `Player.GetPlayerID()` L15982 / `ZDOVars.s_playerID` = `"playerID"` L66602 | 1. `GetPlayerID()` · 2. `zdo.GetLong("playerID".GetStableHashCode(), 0)` · 3. `Character.GetZDOID()` folded — **session-local only; disables digest matching across peers** | `FeatureOff` (replication) | "Detection cannot be replicated between clients; your HUD will show 'unknown' for creatures another player is simulating." |
| `StealthFactor` | `Character.GetStealthFactor()` L10094 / `Player` override L21842 | 1. direct (already replicated via `ZDOVars.s_stealth` = `"Stealth"`, L66670 — a creature's owner can read **every** nearby player's true stealth with no new RPC) · 2. `zdo.GetFloat("Stealth".GetStableHashCode(), 1f)` · 3. — | **`SubsystemVanilla`, per pair** | "SoM cannot read a player's stealth and hands sight for that player back to vanilla." |
| `NoiseRange` | `Character.GetNoiseRange()` L10132 (`ZDOVars.s_noise` = `"noise"`, L66576) | 1. direct · 2. `zdo.GetFloat("noise".GetStableHashCode(), 0f)` · 3. — | **`SubsystemVanilla`, per pair** | "SoM cannot read a player's noise and hands hearing for that player back to vanilla." |
| `LineOfSight` | `Physics.Linecast` + a loaded zone | 1. `Linecast` with the resolved mask, gated on `ZoneLoaded` for **both** endpoints · 2. — | **`SubsystemVanilla`, per pair** | "SoM has no trustworthy line-of-sight test here and is letting vanilla do the raycast." |
| `ViewBlockMask` | `BaseAI.m_viewBlockMask` **private static** L3953 | 1. static field (publicized) · 2. `AccessTools.StaticFieldRefAccess` · 3. `LayerMask.GetMask("Default","static_solid","Default_small","piece","terrain","viewblock","vehicle")` — the seven names from L4024 · 4. — · **a resolved mask of `0` is treated as rung failure, never as "nothing blocks sight"** | **`SubsystemVanilla`, sight** | "SoM could not determine which layers block sight; vanilla is doing the raycast." |
| `ZoneLoaded` | `ZoneSystem.IsZoneLoaded(Vector3)` L98760 | 1. direct · 2. `IsZoneLoaded(Vector2i)` L98766 with `ZoneSystem.GetZone` · 3. `Heightmap.FindHeightmap(pos) != null` · 4. **assume NOT loaded** | **`SubsystemVanilla`, sight** | "SoM cannot tell whether terrain is loaded and is not raycasting; vanilla is doing the raycast." |
| `Biome` | `Heightmap.FindBiome(Vector3)` | 1. `FindBiome` · 2. `WorldGenerator.instance.GetBiome` L132281 (pure math, works headless) · 3. constant `Meadows` **and** camo term contributes 0 | `FeatureOff` | "Camouflage is not being applied." |
| `Light` | `EnvMan.instance.m_dirLight` | 1. `m_dirLight` · 2. `RenderSettings.sun` (**never assigned by Valheim**; kept only as a rung for modded scenes) · 3. `EnvMan.IsDaylight()` as a boolean · 4. shadow term contributes **0**, not max | `FeatureOff` | "The shadow bonus is not being applied." |
| `Weather` | `EnvSetup.m_isWet` | 1. `m_isWet` · 2. v1's environment-name matching (**note: `"ThunderStorm"` contains neither `"rain"` nor `"snow"` and v1 misses it**) · 3. contributes 0 | `Cosmetic` | "Weather is not affecting visibility." |
| `Vegetation` | `Physics.OverlapSphereNonAlloc` + a valid vegetation mask | 1. masked non-alloc overlap · 2. contributes 0 | `Cosmetic` | "Grass and bushes are not affecting hiding." |
| `Equipment` | `Inventory.GetEquippedItems()` (vanilla L57488-57499 allocates a fresh `List` per call) | 1. direct, cached on an equip hash and recomputed only on change · 2. `Humanoid.m_visEquipment` item names · 3. armour penalty 0 | `FeatureOff` | "Armour is not affecting your visibility or noise." |
| `ZdoDigest` | `ZDO.Set(int,long)` L62508 / `GetLong(int,long)` L62758 | 1. int-hash overloads · 2. `Set(string,long)` L62503 / `GetLong(string,long)` L62753 · 3. replication off | `FeatureOff` | "Detection is not replicating; HUD coverage for creatures other players simulate is unavailable and chases reset on ownership handover." |
| `LastKnownPos` | `ZDO.Set(int,Vector3)` L62435 / `GetVec3` L62673 | 1. int-hash · 2. string · 3. off | `Cosmetic` | "Search behaviour restarts at the creature's own position after an ownership handover." |
| `Census` | `Harmony.GetPatchInfo(MethodBase)` | 1. direct · 2. skip | `Cosmetic` | "SoM cannot report which other mods share its patch targets." |
| `Hud` | `Hud.instance` + a scene `EventSystem` | 1. parent under vanilla `Hud.instance` on first `Player.m_localPlayer` · 2. own `Canvas` with `raycastTarget=false` and **no `EventSystem` creation, ever** · 3. no HUD | `Cosmetic` | "The stealth HUD is unavailable." |

### 8.3 The capability report

One consolidated block at startup, and the same content from `somstatus`:

```
[SoM] mode=Full  contract=2  build=2.0.0
[SoM] bound   28/31 : SenseSee SenseHear SenseCombined Lifecycle Drive AttackGate MoveScale
                      DamageHook AlertRaise TargetWrite TargetInfoWrite PathMove StepMove
                      CreatureRoster PlayerRoster PlayerIdentity StealthFactor NoiseRange
                      LineOfSight ViewBlockMask ZoneLoaded Biome Weather Vegetation Equipment
                      ZdoDigest LastKnownPos Census
[SoM] MISSING  AlertLower  (rung 2/2 failed: BaseAI.SetAlerted(bool) not found)
              -> SoM cannot lower alertness early; creatures de-alert on vanilla's 30 s give-up.
[SoM] MISSING  Light       (rung 3/4 failed: EnvMan.m_dirLight, RenderSettings.sun, IsDaylight all absent)
              -> The shadow bonus is not being applied.
[SoM] MISSING  Hud         (headless)
[SoM] patches  installed 9/9  groups Sensing=OK Lifecycle=OK Drive=OK AttackGate=OK Movement=OK
[SoM] coexist  BaseAI.CanSeeTarget      : SoM(prefix,0) com.example.other(prefix,400)
               Humanoid.StartAttack     : SoM(prefix,0)
               MonsterAI.UpdateAI       : SoM(postfix,0) wubarrk.dvergrallies(prefix,400)
```

Three of these lines do not exist in v1 or in `DESIGN-pragmatic.md`: the *rung that failed* for each missing capability, the *sentence* describing the consequence, and the coexistence census. Together they are the difference between "SoM isn't working" and a bug report you can act on.

---

## 9. The cross-mod compat surface

### 9.1 Frozen surface, verbatim

```csharp
namespace ShadowsOfMidgard
{
    /// FROZEN. Type name, ZDOKey, both overloads, and the `== 1` reading are API.
    /// Consumers probe this TYPE's existence by name (MoA 0.1.2, AvalorStealthCompat.cs:84).
    /// Semantics are and remain "does this ZDO carry SoMStealthExempt == 1" -- NOT
    /// "is the brain off". The resolved question is SoMInterop.IsBrainOff (§9.2).
    public static class StealthExemption
    {
        public const string ZDOKey = "SoMStealthExempt";
        public static bool IsExempt(Character c);
        public static bool IsExempt(BaseAI ai);
    }

    /// FROZEN as a public CLASS with v1 field NAMES. One-way projection; see §9.4.
    public sealed class AwarenessData
    {
        public float            DetectionLevel;
        public Vector3          LastKnownPosition;
        public float            TimeSinceSeen;
        public bool             CanSense, CanSee, CanHear;
        public VanillaAlertness CurrentState;
        public AIBehaviorAction CurrentAction;
        public Vector3          DesiredDirection;     public float DesiredSpeed;
        public bool             ShouldRun;            public Vector3 FleeDirection;
        public int              NearbyAlliesCount;    public float TimeUntilNextAllyCall;
        public bool             IsPartOfGroup;
        public TargetPriority   TargetPriority;       public float TargetDistance;
        public float            PlayerHealthPercent;  public float SelfHealthPercent;
        public FleeReason       CurrentFleeReason;    public float FleeStartHealth;
        public float            TimeSpentFleeing;
        public float            TimeInCurrentState;   public float TimeInCurrentAction;
        public int              FramesSinceLastEval;  public float LastEvalTime;
        public string           LastDecisionReason;   public float LastConfidence;
        public float            AggressionLevel;      // ADDED in v2: MoA 0.0.9 probed for it and it
                                                      // never existed (AvalorStealthCompat.cs:260).
    }

    public static class AwarenessSystem
    {
        /// FROZEN: static, exactly one Character parameter, NEVER null, NEVER throws on null input.
        /// Returns the AGGREGATE over all of this creature's per-player tracks -- never the local
        /// player's track. See §9.5 scenario S9 for why that distinction is the live one.
        public static AwarenessData GetData(Character c);
        public static AwarenessData GetData(BaseAI ai);
        public static void Clear(Character c);
        public static void ClearAll();
        public static IEnumerable<KeyValuePair<Character, AwarenessData>> GetAllData();
    }

    /// FROZEN member NAMES. MoA 0.0.9 called Enum.Parse(alertness,"Alerted") and
    /// Enum.Parse(flee,"None") with no try/catch, from a per-frame Update. Old builds are in the wild.
    public enum VanillaAlertness { Unaware = 0, Suspicious = 1, Alerted = 2, Engaged = 3 }
    public enum FleeReason { None = 0, HealthCritical, StrategicRetreat, Overwhelming, Grouping }
    public enum AIBehaviorAction { Idle, Patrol, Search, Pursue, Flee, Attack, CallForHelp,
                                   Investigate, StrategicRetreat }
    public enum TargetPriority { None, Secondary, Confirmed, Immediate }
}
```

**Also frozen, and no longer merely conventional:** the BepInEx plugin GUID `"wubarrk.shadowsofmidgard"` and the Harmony instance id (the same string). MoA 0.1.2 looks the GUID up in `Chainloader.PluginInfos` (`AvalorStealthCompat.cs:83`), and old MoA builds resolve `HarmonyBefore`/`HarmonyAfter` edges against the Harmony id. Both are contract.

`StealthExemption.IsExempt` keeps its exact semantics and gains one non-observable change: `"SoMStealthExempt".GetStableHashCode()` is cached into a `static readonly int` in `SoMKeys` and the call uses `ZDO.GetInt(int, int)` (L62718) instead of `GetInt(string, int)` (L62713). v1 re-hashed a 16-character string at seven patch sites, per creature, per tick.

### 9.2 Additive surface

```csharp
namespace ShadowsOfMidgard
{
    /// Additive. Every parameter and return is a primitive, a string, or a VANILLA type.
    /// No SoM type crosses the boundary, so a consumer needs no assembly reference.
    public static class SoMInterop
    {
        public const int ContractVersion = 2;

        // --- discovery. Methods, not consts: a const read via reflection works, but a consumer
        // --- that took a COMPILE-TIME reference would silently bake in an old value.
        public static int      GetContractVersion();
        public static string[] SupportedKeys();
        public static string[] SupportedProfiles();
        public static string[] RetiredKeys();          // recognised, no longer honoured, with successors
        public static bool     Supports(string keyOrProfile);

        // --- capability. What SoM actually managed to bind THIS session, not merely that it exists.
        public static string[] Capabilities();         // e.g. "sense.see", "alert.lower", "los.cast"
        public static bool     HasCapability(string id);
        public static string[] MissingCapabilities();  // id + the rung that failed + the consequence

        // --- coexistence. What else is on SoM's patch targets, and where SoM has been overruled.
        public static string[] Coexistence();          // "BaseAI.CanSeeTarget|SoM:prefix:0|other:prefix:400"
        public static string[] ContestedMethods();     // methods where an observer saw divergence

        // --- the resolved questions
        public static bool IsBrainOff(Character c);

        /// THE PROBE. Rendered from the same ResolvedPolicy the patches gate on, by a function
        /// (PolicyRender.Render) whose signature admits NOTHING ELSE. Structurally cannot become
        /// a second implementation.
        public static string Describe(Character c);
        /// Per-player. Null-safe on `p` -- falls back to creature level, so it works headless.
        public static string DescribeTrack(Character c, Player p);

        /// Is `SoM_Ack == 0` conclusive evidence that SoM did not resolve this creature?
        /// TRUE only when ModRequired is on and no mixed-mode peer has been observed. If this
        /// returns false, ack==0 may simply mean "a vanilla client owns that creature". §9.8.
        public static bool AckIsConclusive();
    }
}
```

`Describe` output, stable and append-only:

```
som=2;mode=full;src=zdo;brain=on;legacy_exempt=0;profile=hunter(P);alertfloor=2(E);
detectfloor=0.50(P);sight=60.0(E);hear=60.0(P);cone=180.0(P);los=ignore(P);flee=off(P);
giveup=1.0(D);applied=0x1F;rejected=0x00;ack=2;auth=owned;tracks=3;agg=Engaged/0.91;
caps_missing=alert.lower
```

The `(D)`/`(P)`/`(E)` suffixes are the `Provenance` fields — Default / Profile / Explicit. `SURVEY-interop.md`'s proposed format had no provenance, so *"why is this creature's sight 60 m"* was unanswerable without reading the ZDO by hand.

### 9.3 Tiered keys

**Tier 1 (frozen forever).** `SoMStealthExempt`, int, `1` = exempt, read as `zdo.GetInt(hash, 0) == 1`. DvergrAllies' explicit `0` for wild Dvergr (`DvergrGenetics.cs:40`) is correct today and correct forever: any move to an existence check must still read present-and-0 as not-exempt.

**Tier 2 (contract v2).** Adopted verbatim from `SURVEY-interop.md` §5.2 — `SoM_Contract`, `SoM_BrainOff`, `SoM_AlertFloor`, `SoM_DetectFloor`, `SoM_SightRange`, `SoM_HearRange`, `SoM_ConeHalf`, `SoM_IgnoreLoS`, `SoM_NoFlee`, `SoM_GiveUpMul`, `SoM_Profile`, `SoM_Ack`. Same types, ranges, defaults and semantics. It is the agreed standard and re-spelling it would be churn.

**Tier 2a — one addition, and it exists because of §0.2.**

| Key | Type | Absent ⇒ | Semantics |
|---|---|---|---|
| `SoM_Rev` | int | 0 | Directive revision. **Optional.** A consumer that mutates directives after spawn increments it; a consumer that writes once at spawn never touches it. SoM re-resolves immediately when it changes. |

```csharp
// Policy/PolicyCache.cs -- runs in PHASE A, ~4 Hz per creature, ~30 ns.
internal static bool NeedsResolve(CreatureRecord rec, ZDO z, float now)
{
    int contract = z.GetInt(SoMKeys.ContractHash, 0);       // ZDO.GetInt(int,int) L62718
    int rev      = z.GetInt(SoMKeys.RevHash,      0);
    int legacy   = z.GetInt(SoMKeys.LegacyHash,   0);

    if (contract == rec.PolicyContract && rev == rec.PolicyRev && legacy == rec.PolicyLegacy)
        return now >= rec.PolicyCeilingAt;                  // hard ceiling, default 5 s

    rec.PolicyContract = contract; rec.PolicyRev = rev; rec.PolicyLegacy = legacy;
    return true;
}
```

Three hashed lookups against a triple that **does not churn**, plus a 5 s ceiling that catches a Tier-2 key changed without `SoM_Rev`. This replaces `DESIGN-pragmatic.md`'s `ZDO.DataRevision` invalidation, which §0.2 shows is a no-op cache for any moving creature. The documented contract clause is: *"a directive change is visible within `PolicyRecheckPeriod` (5 s); a consumer that needs it sooner increments `SoM_Rev`."* That is an **optional** consumer-side key, so it does not violate the zero-conditions axiom — a consumer that never mutates never writes it.

**Tier 3 — SoM-written, machine-readable diagnostics.** All owner-only, all read-never-written by consumers.

| Key | Type | Semantics |
|---|---|---|
| `SoM_Ack` | int | **Frozen from `SURVEY-interop.md` §5.6.1:** `= ContractVersion` once SoM has resolved this creature. Written **inside `PolicyResolver`**, at the moment the policy is produced, so `ack != 0` *provably* means the resolver ran. |
| `SoM_Applied` | int | Bitfield: which Tier-2 keys were **honoured** — bit per key in `SupportedKeys()` order. |
| `SoM_Reject` | int | Bitfield: which Tier-2 keys were present but **rejected** (out of range, unknown profile, no `SoM_Contract`, retired). |
| `SoM_DH`, `SoM_D0..3`, `SoM_LKP` | long / Vector3 | The replicated detection digest (§7). Consumers may read; the encoding is documented and versioned by the header's schema nibble. |

**`SoM_Applied` and `SoM_Reject` are the genuinely new mechanism, and they close a real gap in the survey's proposal.** The survey specifies loud logging for a malformed directive. But a consumer's malformed write is very often made on a *different machine* from the one that resolves it, and on a dedicated-server session frequently on a machine whose log nobody reads. Writing the rejection back onto the creature's ZDO makes the diagnosis **travel with the creature**: the consumer can see, at the point of writing and with no reflection, whether SoM took its directive, ignored it, or refused it. `ack >= 2 && (applied & MyBit) != 0` is the whole verification, and it is strictly stronger than `ack >= 2` alone, which only proves SoM *looked*.

### 9.4 Precedence, and the one-resolver invariant

Precedence is adopted from `SURVEY-interop.md` §5.4 unchanged, because its reasoning is correct and re-litigating it would break the standard:

1. `SoM_Contract >= 2` present → resolve Tier 2. `SoMStealthExempt` is ignored for gating and reported by `Describe` as `legacy_exempt=1 (superseded)`. INFO once per prefab.
2. Else `SoMStealthExempt == 1` → brain off (frozen v1 behaviour).
3. Else → config defaults, brain on.
4. Any `SoM_*` key present without `SoM_Contract` → WARN once per prefab, set the corresponding `SoM_Reject` bit, ignore Tier 2, fall through to 2/3.

Within (1): explicit keys beat `SoM_Profile`; the profile fills only what was not explicitly set; config defaults fill the rest. Rule (1) inverting the obvious precedence is what lets a consumer write `SoMStealthExempt = 1` **and** the Tier-2 keys unconditionally, forever — an old SoM stands aside, a new SoM takes over, no SoM and both are inert ints. Zero conditions on the consumer, zero mod knowledge in SoM.

The invariant is enforced by a **signature**, not a comment:

```csharp
// Policy/PolicyRender.cs
internal static class PolicyRender
{
    /// The ONLY renderer. It is given the resolved policy and the aggregate and NOTHING ELSE -
    /// no Character, no ZDO, no registry, no config. It is therefore structurally incapable of
    /// describing a policy that the patches do not apply, because it has no other source to
    /// describe. This signature is the invariant. Do not add a parameter to it.
    internal static string Render(in ResolvedPolicy p, in Aggregate a, in CapSnapshot c);
}
```

`SoMInterop.Describe(Character)` is `PolicyResolver.Current(c)` → `PolicyRender.Render(...)`. There is no second path. `SURVEY-interop.md` §5.6.5 asks for this and proposes enforcing it "with a comment at the top of the resolver"; a comment is exactly the enforcement mechanism that MoA's own post-mortem indicts.

### 9.5 The compat surface after MoA 0.1.2 — scenario by scenario

`SURVEY-interop.md` PART 4's ten scenarios were scored against MoA 0.0.9. Re-scored against what ships:

| # | Restructure | Live consumer today? | This design's position |
|---|---|---|---|
| S1 | `AwarenessSystem` renamed | **No.** MoA 0.1.2's detector is `Chainloader.PluginInfos` + `StealthExemption` (`:83-84`). Live only for MoA ≤ 0.1.1 builds in the wild. | Keep the type name and `GetData(Character)` static + never-null. Cheap, and old builds exist. |
| S2 | `GetData` signature change | No live consumer. | Frozen. |
| S3 | Field rename | No live consumer. | Frozen. |
| S4 | Fields demoted to a cache | **This design does exactly this** (§9.6). Under MoA 0.1.2 it is a non-event. Under 0.0.9 it is the survey's "worst, pure-silent" case — **but it is now benign**, because 0.0.9 also stamps `SoMStealthExempt = 1`, precedence rule 2 turns the brain off for those creatures entirely, and 0.0.9's own Harmony guards force `IsAlerted`/`CanSenseTarget` true against vanilla. **The frozen exemption flag is what makes the `AwarenessData` restructure safe for old builds.** | Proceed, and say so. |
| S5 | `VanillaAlertness` loses `Alerted` | No live consumer; catastrophic for 0.0.9 (`Enum.Parse` with no try/catch, from `Update`). | Member names frozen. Free. |
| S6 | `AwarenessData` → struct | No live consumer. | Stays a class. Free. |
| S7 | `GetData` → instance | — | Stays static. |
| S8 | `GetData` returns null | No live consumer. | Never null, and now also null-**input**-safe (v1 would NRE on `GetData(null)` at `AwarenessSystem.cs:13`; it is public API). |
| **S9** | **`GetData` returns the LOCAL player's track** | **Fully live, and it is about SoM, not any consumer.** | **`GetData` returns the aggregate over all tracks.** The per-player question has its own API (`DescribeTrack`). This is the one scenario where getting it wrong would let the central defect back in through the compat door. |
| S10 | `IsExempt` shape change | The one restructure 0.0.9 handled correctly. | Frozen anyway. |

**And the position on `Compat/ExternalPin.cs`.** `DESIGN-pragmatic.md` §2.8 reads third-party writes into `AwarenessData` back as an authoritative floor with a TTL, and calls it "the single highest-leverage compat decision in v2". Its entire stated justification is MoA's reflection surface. **That surface no longer exists** — the only mention of `AwarenessData` in the whole MoA tree is a past-tense comment. Three reasons I do not adopt it even as speculative generality:

1. **It creates a second authority for detection state.** Honouring an external write means a value SoM does not compute participates in SoM's own simulation, and it does so with a *guessed* interpretation: pragmatic guesses "they mean a floor, applied to every track, with a 2 s TTL". Nothing in an unknown mod's `SetValue` says any of that. If the guess is wrong the divergence is silent and unfalsifiable — the exact failure class the whole rebuild exists to kill, and the direct opposite of the one-resolver invariant.
2. **It costs O(all creatures), forever, for a hypothetical.** The pin polls at the start of every creature pass and republishes at the end, for every creature that has ever had a `AwarenessData`. Under §9.6 the compat layer instead costs **zero** in a session with no legacy consumer.
3. **It teaches the ecosystem to keep using a surface we want to retire.** A reflection write that silently works is a reflection write that will still be there in 2028.

What I do instead is **detect the write and refuse it, loudly, with the remedy named** (§9.6). If a user genuinely has a broken third mod that only speaks reflection, `Compat/HonourLegacyWrites` (default **false**, local, not synced) turns pragmatic's behaviour on. The mechanism exists; it is never the default, and it is never silent.

### 9.6 `AwarenessData` as a one-way, lazily-materialised projection

```csharp
// Contract/AwarenessSystem.cs
public static AwarenessData GetData(Character c)
{
    if (c == null) return _scratch;                        // public API: must not throw. v1 did.
    CreatureRecord rec = ContractHost.FindOrCreate(c);
    if (rec == null) return _scratch;
    if (rec.Legacy == null)
    {
        rec.Legacy         = new AwarenessData();
        rec.LegacyObserved = true;                          // <- from now on, this creature publishes
        ContractHost.NoteLegacyConsumer(rec);                // one INFO, once per session, naming the caller
        Publish(rec);                                       // populate immediately: a caller reading the
    }                                                       //   returned object in the same statement wins
    return rec.Legacy;
}
```

- **Lazy.** In a session where nothing calls `GetData`, `rec.Legacy` stays null and the entire compat layer costs **nothing per creature per pass**. v1 allocated an `AwarenessData` for every creature ever loaded, including exempt ones (`Character_Damage_Patch.cs:25-43` called `GetData` unconditionally by design), and never freed them.
- **One-way.** `Publish(rec)` copies aggregate → projection at the end of each pass, only for `LegacyObserved` creatures. Nothing is ever read back.
- **Loud on external writes.** `Publish` first compares the projection against the shadow it last wrote (three fields: `CurrentState`, `DetectionLevel`, `CanSense`); on divergence it logs **once per (prefab, field)**:

  > `[SoM][WARN] Something wrote AwarenessData.CurrentState on Greydwarf(Clone) between SoM passes. SoM v2 does not read this field back — it is a one-way projection. To influence SoM, write ZDO keys instead: SoM_Contract=2 plus SoM_AlertFloor=2 (see SoMInterop.SupportedKeys()). Set Compat/HonourLegacyWrites=true to restore the v1 reflection behaviour. This message appears once per prefab.`

  and sets a `LegacyContested` flag that surfaces in `Describe` and `somstatus`. The one-line remedy in the message is the difference between a warning and a support ticket.
- **`AggressionLevel` is added.** MoA 0.0.9 probed `AccessTools.Field(data, "AggressionLevel")` (`AvalorStealthCompat.cs:260`) against a field that never existed; the `?.SetValue` was a silent no-op from the day it was written. Adding it costs four bytes and closes a contract that drifted with zero signal on either side — the cleanest single argument in the whole survey for making this surface explicit.

### 9.7 Consumer-side write rules (documented as part of the contract)

1. **Static declarations may be written by any peer.** `SoMStealthExempt` and every Tier-2 directive are idempotent constants; a non-owner write propagates (L62635-62642, accepted at L65533) and is why MoA's and DvergrAllies' stamps have always worked. Owner-gating them is still *recommended* — MoA does it (`AvalorStealthCompat.cs:137`), DvergrAllies does it (`DvergrGenetics.cs:58,132`) — because two peers writing different values race.
2. **Mutable brain state may be written only by the owner.** `SoM_D*`, `SoM_DH`, `SoM_LKP`, `SoM_Ack`, `SoM_Applied`, `SoM_Reject` are SoM's; consumers read them and never write them.
3. **Write `SoM_Contract` last.** All other keys must be present when SoM first sees `SoM_Contract >= 2`, because rule 1 of precedence resolves the whole Tier-2 set in one pass.
4. **The `Character.Awake`-inside-`Instantiate` race is real and the repair belongs to the consumer.** `Character.Awake` runs before `ZNetView` is initialised, so a spawn-time stamp can silently miss. MoA's 0.2 s re-stamp loop (`AvalorStealthCompat.cs:148-153`) is the correct pattern and this contract does not replace it. SoM's `SoM_Ack` is how a consumer knows whether the repair was needed.

### 9.8 What each consumer does, concretely

**DvergrAllies: nothing.** It writes only Tier 1, never `SoM_Contract`, and always resolves through precedence rule 2 or 3. Its explicit-`0` discipline and its `SetTamed`-parameter correctness (`DvergrGenetics.cs:119-133`) are unaffected. Note its live patch: `MonsterAI.UpdateAI` void **prefix** at default priority (`AllyPrefabManager.cs:498`) forcing `m_passiveAggresive`/`m_attackPlayerObjects`/`m_avoidFire`/`m_fleeIfNotAlerted` false on wild Dvergr. SoM's postfix at `Priority.Last` does not contend, and the coexistence census will name it in the log so nobody has to re-derive that.

**MistsofAvalor 0.1.2: nothing required.** Its stamp is Tier 1, honoured verbatim. Two *optional* upgrades, and I want to be honest about their value:

- **Reflection-free verification.** `VerifyAll`'s current re-stamp loop can add three lines — `int ack = zdo.GetInt("SoM_Ack", 0);` and the two branches — and get a genuine, independent signal that SoM reached each maze mob, replacing the `AccessTools.TypeByName` probe that today only proves SoM's assembly is loaded. Small, real, and it costs MoA nothing.
- **Tier 2 instead of exemption.** `SURVEY-interop.md` §5.7 imagined MoA writing `SoM_Profile = "sentinel"/"hunter"` and collapsing three `isWarden` branch sites into one declarative value. **That is a strictly larger change than the survey implied**, because MoA's mobs are currently *exempt*, so SoM's brain does not run on them and Tier-2 directives are moot. Adopting Tier 2 means MoA switching from "opt out and drive vanilla myself on a 0.2 s timer" to "opt in and let SoM drive declaratively" — deleting `ReassertHunt`, `MakeItHunt`'s periodic half, and `AvalorHuntPinner`, and trusting SoM's model in 6 m corridors where MoA has already been burned once. The contract must **support** that migration and must never **require** it. It does: MoA can write both keys unconditionally and forever, and precedence rule 1 decides.

**Any third mod:** the whole contract is usable with **no reflection at all** — plain `zdo.Set` on write, plain `zdo.GetInt` on verify. Reflection into `SoMInterop` is needed only for version discovery and for `Describe`, and both gate nothing.

### 9.9 When `SoM_Ack == 0` is and is not conclusive

`SoM_Ack == 0` means "no SoM-aware simulator has resolved this creature". That is *conclusive* only if every peer is guaranteed to be SoM-aware — which is exactly what `ModRequired = true` buys (§10.2). With `ModRequired = false`, a vanilla client can own a creature and `ack == 0` means "either SoM never reached it, or its current owner is running vanilla", which are very different bugs. `SoMInterop.AckIsConclusive()` returns `ModRequired && !MixedModeObserved`, and the interop documentation says plainly: **the ack's meaning depends on `ModRequired`.** No survey or competing design states this, and a consumer that treats `ack == 0` as a hard failure in a mixed session will emit false alarms forever.

---

## 10. Config and ServerSync

### 10.1 One binder, one bound, one initial push

```csharp
// Config/Bind.cs
internal static class Bind
{
    internal static ConfigEntry<float> F(ConfigFile file, ConfigSync sync, string section, string key,
                                         float def, float min, float max, string desc, Action<float> apply)
    {
        var e = file.Bind(section, key, def,
            new ConfigDescription(desc, new AcceptableValueRange<float>(min, max)));
        if (sync != null) sync.AddConfigEntry(e);        // sync == null is the EXPLICIT, greppable "local"
        void Push() => apply(Sanitize(e.Value, min, max));
        e.SettingChanged += (_, __) => Push();
        Push();                                          // structurally impossible to forget
        return e;
    }
    internal static ConfigEntry<bool> B(...);
    internal static ConfigEntry<int>  I(...);

    private static float Sanitize(float v, float min, float max)
        => (float.IsNaN(v) || float.IsInfinity(v)) ? min : Mathf.Clamp(v, min, max);
}
```

v1 wrote 3 lines per entry × 34 entries = 102 of the file's 224 lines, with the min/max literals **duplicated on every pair** (68 chances for the clamp to disagree with itself between the initial read and the change handler), zero `ConfigDescription`s (so ConfigurationManager rendered all 43 entries as free-text boxes with no sliders and no enforced range), and no structural guarantee that the initial value was ever pushed.

### 10.2 `Init`, in the order that matters

```csharp
private static readonly ConfigSync Sync = new ConfigSync(ShadowsOfMidgard.ModGUID)
{
    DisplayName            = ShadowsOfMidgard.ModName,
    CurrentVersion         = ShadowsOfMidgard.ModVersion,   // == SoMBuild.Version, single source
    MinimumRequiredVersion = "2.0.0",                       // HAND-BUMPED ONLY, never auto-ticked
    ModRequired            = true,
};

public static void Init(ConfigFile file)
{
    Active = new StealthConfigModel();                      // DEFAULTS LAND BEFORE ANYTHING CAN THROW
    UI     = new UiConfigModel();

    ServerConfigLocked = file.Bind("0 - General", "Lock Configuration", true,
        new ConfigDescription("If on, the server's values overwrite every client's and only server " +
                              "admins may change them."));
    Sync.AddLockingConfigEntry(ServerConfigLocked);          // BEFORE any synced entry

    try { BindLocal(file); }   catch (Exception e) { Runtime.Fault("cfg.local", e); }
    try { BindSynced(file); }  catch (Exception e) { Runtime.Fault("cfg.synced", e); }

    CreatureProfiles = new CustomSyncedValue<string>(Sync, "creatureprofiles", "", priority: 10);
    CreatureProfiles.ValueChanged += ProfileTableConfig.Reload;
}
```

**The locking entry.** `AddLockingConfigEntry` is never called in v1, so `IsLocked` (`ServerSync.cs:135-139`) is permanently `false`: the server's admin gate (`ServerSync.cs:347-356`) is dead code, `serverLockedSettingChanged` never marks anything `ReadOnly`, and — the live exploit — `AddConfigEntry` installs `SettingChanged += … Broadcast(ZRoutedRpc.Everybody, …)` (`ServerSync.cs:192-198`), so **any non-admin client editing its own TOML broadcasts that value to every player on the server**. A client can set `MaxVisualRange = 5` server-wide. It must be bound **first**, before any synced entry, so `ReadOnly` flags are already correct on ConfigurationManager's first render.

**`ModRequired = true`, and the asymmetry that forces it.** With `false`: `MinimumRequiredVersion` degrades to `"0.0.0"` (`ServerSync.cs:1142-1146`), the client skips sending its version (`:1351-1354`), and `IsVersionOk()` returns `true` when nothing was received (`:1211-1214`) — a vanilla client can join. That is not cosmetic here. **The owning client simulates** (`AIAuthority.IsAuthoritative` → `nview.IsOwner()`), and ownership migrates to whoever is nearest every 2 s. A mixed session is therefore not "SoM with one player missing a HUD"; it is a session where the perception rules for a given creature change depending on who happens to be closest. It also makes `SoM_Ack == 0` ambiguous (§9.9). If mixed mode is ever wanted it needs a design, not a flag default.

**The failure floor.** v1 assigned `Active` on the **last line** of `LoadGameplayConfig`, so any throw in between left it null — and `VisibilitySystem.GetVisibility` returned **`1f`** for a null config (`VisibilitySystem.cs:24-25`), i.e. *maximally visible*. A config failure meant every creature saw you perfectly, forever, silently, with no UI to signal it. Here `Active` is constructed first, each bind group is individually faulted, and a fault demotes `Runtime.Mode`, never leaves a null. `ServerSync.cs:191`'s undefended reflection on the BepInEx-internal `"<Tags>k__BackingField"` is exactly the throw this defends against.

### 10.3 Synced vs local

**Synced (gameplay — changes what a creature perceives):** every v1 entry in sections `1 - Systems` … `8 - Detection Ranges`, same section names, same keys, same defaults, so existing `wubarrk.shadowsofmidgard.cfg` files load unchanged. New: `EvaluationRange`, `MaxEvalTimestep`, `TrackIdleTtl`, `TrackCreateMargin`, `NearbyAllyRadius`, `AllyCallCooldown`, `DamageDetectionFloor`, `HardBlockAttacks`, `DriveVanillaAlert`, `AlertDebounce`, `ReplicateDetection`, `ReplicatedSlots`, `ReplicateFloor`, `HotDigestPeriod`, `ColdDigestPeriod`, `SeedDiscount`, `SeedMinTimeSinceSensed`, `IncludeAnimalAI`.

> `ReplicatedSlots` and the digest periods are **synced** even though they look like perf knobs, because they determine what every *other* client can see about a creature this client owns. A client that quietly set `ReplicatedSlots = 1` would blind its team-mates' HUDs. That is a gameplay value wearing a performance costume, and it is the kind of misclassification the locking entry exists to prevent.

**Local (perf and presentation — cannot change what any creature perceives):** `FrameBudgetMs`, `LosPerFrame`, `LosRecheckPeriod`, `LosStaleLimit`, `PeriodNear/Mid/Far/Dormant`, `NearRange`, `MidRange`, `ViewPeriod`, `AuthPollPeriod`, `SweepPeriod`, `VegPeriod`, `OpinionTtl`, `PolicyRecheckPeriod`, `PerfRangeFloor`, `HudRange`, `ObserveContention`, `ClaimOrphans`, `HonourLegacyWrites`, all `UI/*`, all `9 - Debug/*`.

`PerfRangeFloor` is the one interesting case: `SelfLimit` may shrink `EffectiveRange` **down** from the synced `EvaluationRange` toward this floor and never past it, and never upward. A local machine may spend less than the server allows; it may never spend more.

**`CustomSyncedValue<string> creatureprofiles`** carries a YAML prefab → profile table (`Greydwarf: ambusher`). A `ConfigEntry` cannot express a table; `CustomSyncedValue` is the native mechanism (`ServerSync.cs:91-115`), fragments at 250 KB (`:664-690`) and Deflate-compresses (`:400-413`) for free, and `priority: 10` makes `AddCustomValue`'s descending-priority sort (`:226`) land it before the scalars.

### 10.4 The authority caveat, stated in the code

ServerSync's authority is the **server**; SoM's simulation authority is the **owning client**. ServerSync guarantees every peer agrees on the *numbers*. It guarantees nothing about the *simulation*. That is what per-player tracks, the honest `SetAlerted`, the replicated digest and `ModRequired = true` are for. This paragraph is a comment at the top of `SOMConfig.cs`, because "we added ServerSync" being mistaken for "multiplayer is consistent" is a one-sentence mistake that costs a release.

### 10.5 Version unification

`version.txt` is the single source of truth and generates `SoMBuild.Version`. `ShadowsOfMidgard.ModVersion = SoMBuild.Version`, so `[BepInPlugin]`, the assembly version and `ConfigSync.CurrentVersion` cannot disagree. v1 hardcoded `ModVersion = "2.0.0"` while `version.txt` read `1.9.1`, so the ServerSync handshake advertised a build that has never existed. `MinimumRequiredVersion` is **never** derived from `version.txt` — the csproj ticks the patch component on every build, and a min-version tied to that hard-kicks every client on every rebuild.

---

## 11. What is deleted from v1, and what is deliberately not rewritten

### 11.1 Deleted

| Deleted | Why |
|---|---|
| **`BaseAI_StealthBrain_IsAlerted_Patch`** (prefix + postfix) | Forcing the getter disabled the setter guarded by it. `Alert()` (L5327) is `if (m_nview.IsValid() && !IsAlerted())`, so it became a permanent no-op; `SetAlerted` (L5350) never ran; `m_alerted` stayed false on the owner; `ZDOVars.s_alert` never replicated, so every other client's non-owner branch (L4113-4117) read `false` forever; the animator's `"alert"` bool, `m_alertedEffects`, boss `activeBosses` counting and `m_alertedMessage` all died; the give-up/leash block (L5937-5950) never armed; event creatures never despawned (L5988); `UpdateConsumeItem` was skipped so taming stalled (L6045); backstab became a permanent exploit (L8736); Sneak XP was corrupted via `InStealthRange` (L5390); and two clients saw contradictory `EnemyHud` icons (L38631-38636). Replaced by `AlertDriver` calling the real setter through a declared ladder. |
| **`Character_Damage_Patch`** | `Character.Damage(HitData)` (L8692-8699) only does `m_nview.InvokeRPC("RPC_Damage", hit)` — it runs on the **attacker's** machine, and the patch then demanded ownership, so in multiplayer the awareness bump was lost on every hit against a creature the attacker did not own. It also fired for hits `RPC_Damage` subsequently rejected (L8721), demoted an `Engaged` creature to `Alerted` unconditionally, and ignored `StealthExemption` entirely. |
| **`Character_OnDamaged_Patch`** | `Humanoid.OnDamaged(HitData)` (L13249-13252) overrides without chaining, so the patch **never fired for greydwarves, draugr, fulings, skeletons, dvergr or trolls** — most of the game. Also ignored exemption. |
| **both replaced by** `P_BaseAI_Awake` + `Character.m_onDamaged` (L6875) | Public delegate, invoked unconditionally on the owner at L8871-8873 for every `Character` subtype, and it carries the attacker. It is exactly what `BaseAI.Awake` itself subscribes to at L4028. |
| **All six sensing postfixes** | Re-ran the identical four-`GetComponent` guard chain to re-assert the identical value the prefix had already written, purely to beat other mods. ~50 % of the patch layer's CPU and a live sort-order race. Replaced by sampled read-only observers that report contention instead of fighting it. |
| **`_harmony.PatchAll(...)`** | All-or-nothing inside a swallow-everything try/catch: one renamed method in Valheim v1.0 silently removes every patch after the failure point while the plugin still logs "loaded successfully" (`ShadowsOfMidgard.cs:48,50`). Replaced by per-group atomic install. |
| **`StealthOvermind`** | `Player.m_localPlayer`-centred with a 64 m gate, so **creatures this client owned but that sat near a remote player were permanently `Unaware`, blind and pacified**; it also halted entirely for every creature the client owned whenever the local player died. |
| **`StealthBrain._evalCache` + `StableAIKey` + `MigrateCacheCoroutine`** | The key was `((ulong)zdo.m_uid.UserID, GetInstanceID())` — `UserID` is the *spawning peer's* id and identical for everything that peer spawned, so the key degenerated to a per-process Unity handle that cannot survive ownership migration by construction. The migration coroutine leaked an iterator plus two closures on **every** evaluation while `NetworkUid == 0`. Decisions now live inline on `CreatureRecord`. |
| **`Dictionary<Character, AwarenessData>`** | Keyed on a `UnityEngine.Object`, pruned only by `OnDestroy` — which per dossier L10 also fires on zone **unload**, so walking away wiped a creature's stealth state — and walked in full every frame by the HUD through a **boxed** enumerator (`GetAllData` returns `IEnumerable<KeyValuePair<…>>`). |
| **Every coroutine** (`CoroutineManager`, `EvaluateLoop`, `MigrateCacheCoroutine`, `UpdateNearbyAlliesCoroutine`) | Beyond the cost: `UpdateNearbyAlliesCoroutine` had a `while (true)` in which every `continue` skipped the per-AI `yield return null` and the only other yield was gated on `allAIs.Count == 0`, so a non-empty but entirely-skippable tracked list spun forever — **a hard game freeze**, documented and fixed in `AVALOR_COMPAT_HANDOFF.md` §10. Replaced by one `Update` and the timing wheel. |
| **`AIAuthority`'s server fallback** (`nview == null → ZNet.IsServer()`) | Contradicted by dossier L9: there are no creature instances near players on a dedicated server, so the server cannot be the simulator. The rule is `ZNetView.IsOwner()` and nothing else. |
| **Every unconditional interpolated debug string** (`StealthBrain:270`, `VisibilitySystem:40`, `NoiseSystem:21`, `HidingSystem:34`, `CamoSystem:55`, `BehaviorSystem:209,215`) | Arguments are evaluated at the call site, so on `net472` the `string.Format` plus boxing ran with debug **off** — ~46 allocations per evaluation, ≈700 KB/s, roughly 65 % of v1's GC pressure. |
| **`StealthUIRoot`'s `EventSystem` creation** | Ran during the BepInEx chainloader, before FejdStartup's scene existed, so the guard always passed and SoM's `DontDestroyOnLoad` instance permanently owned `EventSystem.current` (uGUI takes element `[0]`, the first enabled). Valheim's own EventSystem was inert for the session: gamepad navigation and the key-rebinding dialog broke, a stock `StandaloneInputModule` polled legacy `Input.*` axes against Valheim's InputManager asset, and `IsPointerOverGameObject()` returned true over two invisible widgets on the character-select screen (L84781). |
| **`SpriteLoader`'s disk path and disk write** | Hardcoded the plugin folder name `"ShadowsOfMidgard"`, which is wrong under a Gale or Hexium profile, and silently `File.WriteAllBytes` into the user's plugins tree on first sprite load. |
| **`BehaviorSystem`'s static constructor** | Ran on first touch from inside a Harmony patch with no ordering guarantee, and swallowed the `MissingFieldException` from `AccessTools.FieldRefAccess<BaseAI, Character>("m_targetCreature")` — the field is declared on `MonsterAI` (L5727), a *derived* type, which `AccessTools` never searches — leaving `CallNearbyAllies` silently inert for the life of the mod. |

### 11.2 Deliberately not rewritten

- **The four sensing formulas.** `Clamp01(light − shadow − grass − weather + movement + armor)` and its three siblings are ported term for term into `PlayerView`. Their known quirks — grass double-counted, `Clamp01` saturating armour tiers, weapons counted as armour, `RenderSettings.sun` never assigned by Valheim — are **documented, logged where detectable, and fixed in 2.1**. One behavioural variable at a time; a release that changes both the data model and the numbers is unreviewable.
- **The five evaluators.** `StateEvaluator`'s hysteresis ladder, `FleeEvaluator`'s thresholds, `MovementEvaluator`'s deliberate `Vector3.zero` for Alerted/Engaged (vanilla owns pursuit), `CombatEvaluator`'s scoring and the ally logic keep their exact arithmetic. Only their inputs change: `(ref Track, in PlayerView, in ResolvedPolicy, CreatureRecord)` instead of `(AwarenessData, Player)`.
- **Config section names, key names and defaults.** Existing TOMLs load unchanged; new entries only.
- **The HUD's visual design.** Same gem, same meter, same colours — plus one new state (§7.5). Data source, lifecycle and write discipline change; look does not.
- **`ArmorProfileSystem` / `CamoSystem` tables.** Same keywords, tiers and biome map, including rows that probably match no vanilla item token. Correcting them is a balance change.
- **`MonsterAI.UpdateTarget`, `BaseAI.FindEnemy`, `MonsterAI.SetTarget`, `Character.RPC_Damage`, `BaseAI.SetAlerted` stay unpatched.** Every one is private or protected, every one is a Tier-1 rename risk, and every effect SoM needs is reachable through a public member they already call.
- **`AnimalAI` is a stated non-goal for 2.0.** It is a sibling of `MonsterAI`, not a subclass (L3714), with its own `m_target` (L3724) and its own `UpdateAI` (L3742). The sensing patches would answer for it if a track existed, but the registry only creates records for `MonsterAI` unless `IncludeAnimalAI` (synced, default **false**) is on. Documented as unsupported to keep the blast radius small.
- **Per-track replication beyond `ReplicatedSlots`.** A full packed blob for eight tracks would need a `byte[]` ZDO field and a schema negotiation. Four slots covers every session up to four players completely and every larger session's dominant relationships; the rest degrade to **unknown**, which the HUD now says out loud.

---

## 12. Acceptance test

One scripted scenario, run by `somselftest` with one assert per line, on a two-client session with a **dedicated server**. It is deliberately not solo-playable, because every defect this design exists to fix is invisible in a solo playtest.

> **Setup.** Dedicated server. Client A and client B. Three greydwarves spawned near a wall, plus one MoA-flagged mob (`SoMStealthExempt = 1`) and one mob carrying `SoM_Contract = 2; SoM_Profile = "sentinel"; SoM_AlertFloor = 2`. B engages the three greydwarves. A crouches two metres from them, behind the wall from one of them. A then **walks away for 30 seconds and returns**, so that `ZDOMan.ReleaseNearbyZDOS` hands ownership of at least one greydwarf from A to B and back.

| # | Assertion | What it proves |
|---|---|---|
| 1 | A's gem is **blue, small and quiet**; B's is red. | Per-player tracks exist and the HUD reads A's own track. v1 returns `Engaged` here. |
| 2 | On one greydwarf, in one frame: `CanSeeTarget(A) == false` **and** `CanSeeTarget(B) == true`. | The patches answer per-target, not per-creature. |
| 3 | `FindEnemy` continues to select B, not A, though A is closer. | The `CanSenseTarget` patch feeds vanilla's acquisition loop correctly per candidate. |
| 4 | A's backstab lands at full multiplier; B's does not. | `IsAlerted()` is the real vanilla field (L8736 reads it directly). |
| 5 | A gains Sneak XP; B does not. | `InStealthRange` (L5390) sees honest alert state. |
| 6 | **A's and B's `EnemyHud` show the same alert icon on the same greydwarf.** | `ZDOVars.s_alert` is being written by vanilla's `SetAlerted` and is replicating (L38631-38636 runs on every client). |
| 7 | For the greydwarf **A does not own**: A's gem still reflects that creature's awareness of A, and `SoMInterop.DescribeTrack` on A names A's own player id. | The replicated digest works, and non-owning clients render honestly. |
| 8 | With `ReplicateDetection = false` on B, A's gem shows the **unknown** ring rather than going quiet. | The HUD distinguishes "no threat" from "no information". |
| 9 | Across the handover in step 3 of the setup, the chasing greydwarf's `Detection` for B drops by no more than `1 − SeedDiscount` (15 %) and its `State` never resets to `Unaware`. | Tracks survive ownership migration through the ZDO, and handover is lossy-but-bounded rather than a reset. |
| 10 | During the handover, `somstatus` reports a non-zero `handover-out` gap ≤ `HotDigestPeriod` and a non-zero `handover-in seeded` count. | The loss is measured, not assumed. |
| 11 | The MoA-flagged mob's `Describe` reads `brain=off … legacy_exempt=1`, it **attacks on sight**, and `MoA`'s `ai.Alert()` on it visibly works. | The frozen Tier-1 path is intact and `Alert()` is not neutered. |
| 12 | The Tier-2 mob's `Describe` reads `alertfloor=2(E);profile=sentinel(P);applied=…`, its ZDO carries `SoM_Ack = 2` and `SoM_Applied` with the alert-floor bit set, and `SoM_Reject == 0`. | The resolver ran, honoured the directives, and reported both facts back onto the ZDO. |
| 13 | Add `SoM_Sight = 60` (a typo for `SoM_SightRange`) to a fourth mob: one WARN naming the key, that mob's `SoM_Reject` has the unknown-key bit, and its behaviour is otherwise unchanged. | Malformed input is refused loudly and machine-readably, never clamped-and-forgotten. |
| 14 | Kill an unresolvable capability (rename `BaseAI.SetAlerted` in a test build): startup prints `MISSING AlertLower` with the failed rung and the consequence sentence, `Mode` stays `Full`, and everything else in this table still passes. | The ladder degrades one capability without taking the mod down. |
| 15 | Force `LayerMasks.ViewBlock == 0`: sight for every pair hands back to vanilla (`Refusal.NoLosOpinion` climbs in `somstatus`), and **no creature becomes more perceptive than vanilla**. | The failure floor is vanilla, never "maximally visible". |
| 16 | The dedicated server's log shows `mode=Relay`, zero `Physics.*` calls from SoM, no HUD, and a working ServerSync handshake that rejects a vanilla client. | Server/client asymmetry and `ModRequired` are right. |
| 17 | Over five minutes at 200 creatures, `somstatus` reports 0 B/s steady-state allocation attributable to SoM and a p99 wheel lateness under one tier period. | The cost model holds. |

Assertions 1-6 are `DESIGN-pragmatic.md`'s test, kept because it is a good test. **7-17 are the ones this design exists to pass**, and 8, 10, 13, 14, 15 and 16 are all failures that would be invisible in v1 and in a shippability-lensed v2.

---

## 13. Where this design loses

Stated plainly, against the two alternatives.

### 13.1 Against a throughput-lensed design

- **The gate is ~2.3× more expensive per call.** 58 ns vs ~25 ns, because of the player-id resolution, the capability mask test and the refusal counter. On the `FindEnemy` path the `!IsPlayer()` early-out hides almost all of it (§5.5), but a throughput design would strip the capability test to a cached bool, drop the refusal histogram entirely, and key tracks on a `ZDOID` it can read straight off `Character.GetZDOID()` without a registry lookup.
- **The two-phase scheduler and the timing wheel cost bookkeeping a cursor does not.** One `List` add and one remove per creature per period, plus a second ring. A throughput design runs one budgeted cursor over everything and is measurably faster in the common case; it pays for it only when the budget is chronically short, which is the case I am optimising for and it is not.
- **The digest is five ZDO fields per hot creature where one would do.** A throughput design would replicate one `float` and be done, accept that only the dominant relationship survives handover, and save the pack/unpack, the header, the generation tracking and the fold resolution.
- **Provenance is dead weight in the hot path.** Two `ushort`s carried on every `ResolvedPolicy` and copied on every resolve, read only by `Describe` and `somstatus`. A throughput design deletes them and renders `Describe` from the values alone.
- **`SoM_Applied` / `SoM_Reject` are two extra ZDO ints written on every flagged creature** for diagnostics that most sessions never read.
- **The observer postfixes are three extra Harmony patches on the hottest methods in the mod** to buy a warning. 31 µs/s measured, but three more entries in every `PatchInfo` chain and three more things another mod's transpiler could trip over.

### 13.2 Against a shippability-lensed design (`DESIGN-pragmatic.md`)

- **This is more code.** Roughly 30-35 % more than pragmatic's, concentrated in `Capability/` (the ladder table and its façade), `Authority/` (the state machine, the digest codec, rehydration) and `Diag/`. That is more surface to review, more to get wrong, and a longer road to the first playable build.
- **The capability table is a maintenance obligation.** Thirty-one rows, each with a ladder, a declared degradation and a user-facing sentence, all of which must be re-checked when Valheim v1.0 lands. Pragmatic's `VanillaBind` is a flat list of nullable delegates with the fallbacks described in prose; it will be wrong sooner, but it will never be *stale in a way that lies*, because it does not claim as much.
- **Five runtime modes is four more than pragmatic's `Runtime.Active` bool.** Every mode is a state a tester has to reach, and `Reduced` in particular is a family of states (one per missing non-required capability) rather than a single one.
- **`SoM_Rev` is a new key in a contract the survey already agreed.** It is optional and consumers can ignore it forever, but it is a line item in a standard that was supposed to be settled, and its justification (§0.2) requires the reader to accept a decompilation argument about `DataRevision` churn.
- **I decline `ExternalPin`, and if a third mod is out there reflection-writing `AwarenessData`, that mod breaks under my design and works under pragmatic's.** I have argued (§9.5) that no such mod is known, that honouring an unknown write is a guess about intent, and that the config toggle covers the case — but the honest statement is that pragmatic is *more compatible with mods nobody has audited*, and I have traded that for not having a second authority for detection state. If the ecosystem turns out to be wider than the two known consumers, that trade was wrong and the remedy is a config default flip.
- **Deferring to `Priority.Last` is strictly more deferential than pragmatic's `Priority.Low`, which means SoM loses more arguments.** Every argument it loses is one where a user installed a stealth mod and got someone else's answer. My mitigation is to *report* it rather than win it, which is the right call for a mod that must coexist — but it is a worse experience than winning, and users do not read logs.
- **The self-limiting scheduler changes gameplay values at runtime.** `SelfLimit` shrinks `EffectiveRange` and stretches `LosRecheckPeriod` on a struggling machine, so two players in the same session can be running measurably different perception ranges. It is announced, it only ever reduces, and it is bounded by `PerfRangeFloor` — but a shippability design would simply drop frames and keep the numbers identical everywhere, which is easier to support.
- **The whole thing is harder to explain.** Pragmatic's organising principle — *one structural change per defect class* — fits in a sentence and makes the diff reviewable against v1. Mine requires the reader to accept that refusing to answer is a feature.

### 13.3 What I would cut first, in order, under schedule pressure

1. **`Diag/` beyond the capability report and `somstatus`.** `Selftest`, the contention observers, the coexistence census. ~2 days, no behavioural change, and it is where the "fail loud" thesis is weakest per line of code.
2. **`OrphanClaim`.** Already default-off and speculative. Delete it rather than ship a disabled feature.
3. **`SoM_Applied` / `SoM_Reject`.** Keep `SoM_Ack` (frozen by the survey). The other two are the best-value diagnostics in the contract, but they are additive and can ship in 2.1 without breaking anyone.
4. **`Provenance`.** Render `Describe` from the values alone and drop the `(D)/(P)/(E)` suffixes.
5. **The timing wheel**, reverting to a cursor walk **with the lateness counter retained**. The counter is 90 % of the value for 10 % of the code; without it, do not make this cut.
6. **Multi-slot replication**, reverting to one slot. This is the last cut and I would fight it: it re-introduces the "three of four players are forgotten on handover" hole, and it is the difference between correct group play and correct duo play.

Everything above the line stays: per-player tracks keyed on the stable player id, the `Gate` with its refusal categories, atomic patch groups, the capability ladders with declared degradations, the `PolicyRender` signature invariant, the digest with its staleness discount, the locking config entry, `ModRequired = true`, and the HUD's third state. Those are the design; the rest is instrumentation.
