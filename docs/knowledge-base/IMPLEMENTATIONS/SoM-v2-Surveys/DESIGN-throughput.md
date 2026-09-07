# Shadows of Midgard v2 — Architecture (throughput & scale lens)

**Companion documents:** `DESIGN-pragmatic.md` (shippability lens) and a third resilience/compat-lensed design.
This one optimises brief goal #4 — *far more monsters simultaneously in multiplayer, at lower overhead* — and
says explicitly, in §13, where that costs more than it is worth.

All vanilla claims are cited as `AV:LINE` against
`c:\WubarrkCODING\libs-Tools\DECOMPILED ASSEMBLY VALHEIM\assembly_valheim.decompiled.cs`.
Runtime-capability claims are cited to a binary/reflection probe of
`C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\Managed\`.

---

## 0. Organising principle

**Stop simulating creatures; start transforming columns.** Every per-creature and per-(creature,player) value
lives in a flat, index-addressed array — never in a class instance, never behind an object reference, never in
a hash table on a hot path — and the frame's work is expressed as a small number of phase-separated passes,
each of which walks one dense roster of `int` row indices in ascending order and touches only the columns that
phase needs. Object identity is replaced by a 32-bit `CreatureHandle` (row + generation); creature→row and
player→slot resolution on the Harmony hot path is a direct-mapped instance-ID probe, not a dictionary; every
per-call predicate the patch layer used to recompute (owner? valid? tamed? exempt? has an opinion?) is hoisted
into a single precomputed `byte` of flags refreshed on a 2 Hz sweep, so a patch answer is *one array read, one
AND, one compare*. Creatures are partitioned into tier rosters rather than carrying a per-creature
`NextEvalAt`, so a dormant creature costs literally zero per frame instead of one pointer-chase and one float
compare. And the one term that genuinely dominates — line-of-sight — is lifted off the main thread entirely by
batching it into `UnityEngine.RaycastCommand.ScheduleBatch`, the same API Valheim itself uses at `AV:115123`.
The result is a design whose main-thread cost is dominated by things that are not arithmetic, whose arithmetic
is measured in nanoseconds per pair, and whose scaling wall is vanilla's own `FindEnemy`, not SoM's brain.

---

## 1. File / folder layout

```
ShadowsOfMidgard/
  ShadowsOfMidgard.cs               Plugin entry. Config → Bind → Store alloc → Patches → Driver. In that order.

  Api/                              THE FROZEN SURFACE. Nothing in here may be reshaped for performance.
    StealthExemption.cs             Verbatim v1 type: ZDOKey const + IsExempt(Character) + IsExempt(BaseAI).
    LegacyAwareness.cs              AwarenessData (public class, v1 field NAMES AND TYPES) + AwarenessSystem.
    SoMInterop.cs                   Additive: ContractVersion, SupportedKeys/Profiles, IsBrainOff, Describe.
    SoMKeys.cs                      Every SoM ZDO key string + its precomputed GetStableHashCode().

  Data/                             THE STORE. No logic, only layout and O(1) access.
    Handles.cs                      CreatureHandle / PlayerSlot. Row+generation packing.
    CreatureStore.cs                All per-creature columns. Grow-by-doubling. Free list + generations.
    TrackStore.cs                   All per-(creature,player) columns. row*K + k addressing.
    PlayerStore.cs                  Per-player intrinsic profile columns. Fixed 16 slots.
    RowIndex.cs                     Direct-mapped instanceID→row cache + dictionary backstop.
    CellGrid.cs                     Hashed uniform grid over creature rows. Buckets, no allocation.
    Rosters.cs                      Tier rosters (sorted int[]), rebuilt on the sweep.

  Pipeline/                         THE FRAME. One file per phase, plus the driver.
    Driver.cs                       The single MonoBehaviour. Update(). No coroutines anywhere.
    P0_Sweep.cs                     2 Hz: reconcile BaseAI.BaseAIInstances → rows; refresh flag bytes; rosters.
    P1_Profiles.cs                  10 Hz: refresh PlayerStore columns for every loaded player.
    P2_Gather.cs                    Per stripe: read transforms, rebucket cells, resolve directives on revision.
    P3_Broadphase.cs                Per stripe: grid → candidate players → open/close/decay tracks.
    P4_Los.cs                       Emit RaycastCommands; consume last epoch's results. Off-main-thread.
    P5_Integrate.cs                 Per stripe: the branch-free pair kernel over the track columns.
    P6_Decide.cs                    Per stripe: ladder → aggregate → action. Per creature, not per pair.
    P7_Apply.cs                     Change-gated writes into vanilla + replication + legacy publish.
    Stripe.cs                       Stripe sizing, EWMA cost tracking, worst-case round-trip accounting.

  Kernels/                          PURE MATH. No Unity API, no allocation, no branches worth mispredicting.
    Tuning.cs                       Config → precomputed derived constants (squared ranges, cos, gain table).
    SenseKernel.cs                  Falloff, FOV, threshold, detection integration.
    LadderKernel.cs                 Branch-free alertness ladder, exactly equivalent to v1 StateEvaluator.
    ActionKernel.cs                 State+flee+group → StealthAction. Ported arithmetic, table-driven.
    FleeKernel.cs                   Ported FleeEvaluator arithmetic.
    GroupKernel.cs                  Ally counting and ally-call propagation over the grid (no Physics).

  Jobs/
    RaycastBatch.cs                 NativeArray pools + soft-bound RaycastCommand/ScheduleBatch ladder.
    OverlapBatch.cs                 Same for OverlapSphereCommand (vegetation). Optional, degrades to sync.

  Sensing/                          Producers for PlayerStore columns. v1 formulas, term for term.
    VisibilityTerms.cs
    NoiseTerms.cs
    HidingTerms.cs
    CamoTerms.cs
    EquipCache.cs                   Armor totals, invalidated by Inventory.m_onChanged (AV:56894). Never polled.
    VegetationSampler.cs            Per-player overlap sample; auto-disarms after N consecutive zero samples.

  Act/
    Behavior.cs                     Apply an action to vanilla. Every write change-gated.
    AlertDriver.cs                  Drives the REAL BaseAI.Alert()/SetAlerted(false). Debounced.
    DamageHook.cs                   Character.m_onDamaged handlers, one permanently-bound delegate per ROW.

  Net/
    TrackBlob.cs                    Pack/unpack the multi-track replication blob. One ZDO byte[] field.
    Replication.cs                  Owner-only, delta-gated, idempotent-set write discipline.

  Bind/
    VanillaBind.cs                  Soft bindings resolved once in Awake. One consolidated capability log line.
    UnityBind.cs                    RaycastCommand / QueryParameters / GetPositionAndRotation probes + ladders.
    PatchInstaller.cs               Per-class probe + CreateClassProcessor. PatchAll is never called.
    LayerMasks.cs                   Validated masks; mirrors BaseAI.m_viewBlockMask with a literal fallback.
    Headless.cs                     SystemInfo.graphicsDeviceType == Null. Never ZNet.IsDedicated().

  Patches/                          THIN. Each patch body is < 15 lines and does no work it can hoist.
    P_BaseAI_CanSeeTarget.cs
    P_BaseAI_CanHearTarget.cs
    P_BaseAI_CanSenseTarget.cs
    P_MonsterAI_UpdateAI.cs
    P_Character_SetMoveDir.cs
    P_Humanoid_StartAttack.cs
    P_Character_OnDestroy.cs        Latency optimisation only. The sweep is the authority.
    P_BaseAI_Awake.cs               Latency optimisation only. The sweep is the authority.
    P_Player_OnSpawned.cs           Latency optimisation only. The sweep is the authority.

  Config/
    SOMConfig.cs                    Locking entry first, then local, then synced. Failure floor = vanilla.
    Binder.cs                       bind + sync + clamp + initial push + change push, in one call.
    StealthConfigModel.cs           v1 field names and defaults, verbatim. Existing TOMLs keep working.
    PerfConfig.cs                   LOCAL-ONLY perf knobs. Never synced. Never able to change gameplay.

  UI/
    ThreatQuery.cs                  One struct published per round from the local player's track column.
    StealthHud.cs / StealthGem.cs / NoiseMeter.cs / SpriteLoader.cs

  Armor/
    ArmorProfile.cs / ArmorProfileSystem.cs / ArmorUtils.cs    Tables unchanged; call sites now event-driven.

  Util/
    Log.cs                          Every debug string is constructed INSIDE an if. No exceptions.
    EnvironmentUtils.cs             EnvSetup.m_isWet (AV:81641) first, v1 name matching as fallback.
```

---

## 2. Core type declarations

### 2.1 `Data/Handles.cs`

```csharp
using System.Runtime.CompilerServices;

namespace ShadowsOfMidgard.Data
{
    /// <summary>
    /// Row index + generation, packed into one int. NOT an object reference.
    /// Rows are permanent for the life of a creature and are recycled through a free list;
    /// the generation counter makes a stale handle detectable in one compare.
    /// 16 bits of row = 65 535 simultaneously registered BaseAI. 16 bits of generation.
    /// </summary>
    internal readonly struct CreatureHandle
    {
        public readonly int Packed;
        public CreatureHandle(int row, int gen) { Packed = (gen << 16) | (row & 0xFFFF); }

        public int  Row  { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => Packed & 0xFFFF; }
        public int  Gen  { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => (Packed >> 16) & 0xFFFF; }
        public bool IsNone => Packed == 0;

        public static readonly CreatureHandle None = default;
    }

    /// <summary>Dense 0..15 index into PlayerStore. One byte, so it fits in the hot track column.</summary>
    internal readonly struct PlayerSlot
    {
        public const byte NoneValue = 0xFF;
        public readonly byte Value;
        public PlayerSlot(byte v) { Value = v; }
        public bool IsNone => Value == NoneValue;
    }

    [System.Flags]
    internal enum RowFlags : byte
    {
        None       = 0,
        Registered = 1 << 0,  // row is live and its component refs are resolved
        Simulated  = 1 << 1,  // MonsterAI, not tamed, not BrainOff -> the brain runs
        Owned      = 1 << 2,  // NView.IsOwner() as of the last sweep
        Valid      = 1 << 3,  // NView.IsValid() && Char != null && !IsDead()
        HasOpinion = 1 << 4,  // at least one non-stale track exists -> patches may answer
        Sleeping   = 1 << 5,  // MonsterAI.IsSleeping() as of the last sweep
        Searching  = 1 << 6,  // CurrentAction in {Search, Investigate} -> SetMoveDir scaling applies
        PosFresh   = 1 << 7,  // position column refreshed within PerfConfig.PosStaleMax
    }

    [System.Flags]
    internal enum TrackFlags : byte
    {
        None       = 0,
        CanSee     = 1 << 0,
        CanHear    = 1 << 1,
        HadLos     = 1 << 2,   // cached result of the last real cast
        LosPending = 1 << 3,   // a RaycastCommand for this pair is in flight
        EverSensed = 1 << 4,
        Pinned     = 1 << 5,   // floored by SoM_AlertFloor / SoM_DetectFloor
        Seeded     = 1 << 6,   // reconstructed from the replication blob after handover
        Damaged    = 1 << 7,   // opened by m_onDamaged, not by perception
    }
}
```

### 2.2 `Data/CreatureStore.cs` — the columns

```csharp
namespace ShadowsOfMidgard.Data
{
    /// <summary>
    /// Structure of arrays. Every field of the old CreatureState is a column here.
    /// Splitting is by ACCESS FREQUENCY, not by semantics: the HOT block is everything
    /// touched on every pass of every creature, and it is deliberately small enough that
    /// one creature's hot footprint is a single 64-byte cache line.
    /// </summary>
    internal static class CreatureStore
    {
        internal const int MinCapacity = 256;
        internal static int Capacity;   // grows by doubling; never shrinks during a session
        internal static int HighWater;  // highest row ever handed out (bounds every full scan)

        // ---- HOT: read on every pass. 40 bytes per row. ----
        internal static byte[]  Flags;      // RowFlags
        internal static byte[]  Tier;       // 0 hot / 1 warm / 2 cold / 3 dormant
        internal static byte[]  TrackCount; // 0..TrackStore.K
        internal static byte[]  AggState;   // (byte)VanillaAlertness, max over tracks
        internal static float[] PosX, PosY, PosZ;
        internal static float[] FwdX, FwdZ; // creature facing, XZ only; the FOV test is planar
        internal static float[] AggDet;
        internal static int[]   Cell;       // packed grid cell id
        internal static float[] PosStamp;   // Time.time of the last transform read
        internal static float[] LastPass;   // Time.time of the last full pass

        // ---- WARM: read on most passes, written rarely. ~77 bytes per row. ----
        internal static float[]      SelfHealth;
        internal static byte[]       Action;        // (byte)StealthAction
        internal static byte[]       FleeReason;    // (byte)FleeReason
        internal static byte[]       AggTrack;      // index of the dominant track, 0xFF = none
        internal static byte[]       AllyCount;
        internal static float[]      NextAllyCallAt, NextAllyCountAt;
        internal static Vector3[]    FleeDir;
        internal static float[]      LastAlertAt;
        internal static bool[]       WantsAlert;
        internal static uint[]       DirRevision;   // ZDO.DataRevision when Dir was resolved (AV:62322)
        internal static Directives[] Dir;
        internal static ushort[]     Gen;           // generation, bumped on free

        // ---- REPLICATION shadow: what we last wrote, so we can delta-gate. ----
        internal static byte[]  ReplState;
        internal static float[] ReplDet;
        internal static float[] ReplAt;
        internal static ZDOID[] ReplTarget;

        // ---- APPLY shadow: what we last pushed into vanilla, so we can change-gate. ----
        internal static ZDOID[] AppliedTarget;
        internal static byte[]  AppliedAction;

        // ---- REFERENCE columns. The ONLY GC-traceable arrays in the store. ----
        internal static Character[] Char;
        internal static BaseAI[]    Ai;
        internal static MonsterAI[] Monster;
        internal static ZNetView[]  NView;
        internal static Transform[] Tf;
        internal static Humanoid[]  Hum;

        // ---- Per-row permanently-bound damage delegate. Allocated ONCE per row, ever. ----
        internal static System.Action<float, Character>[] OnDamaged;

        // ---- Legacy compat: allocated ONLY for rows a third party has actually observed. ----
        internal static AwarenessData[] Legacy;

        private static int[] _free; private static int _freeCount;

        internal static void Init(int capacity) { Capacity = 0; Grow(capacity); }

        internal static int Alloc(Character c, BaseAI ai, ZNetView nv)
        {
            int row;
            if (_freeCount > 0) { row = _free[--_freeCount]; Gen[row]++; }
            else { if (HighWater >= Capacity) Grow(Capacity * 2); row = HighWater++; Gen[row] = 1; }
            Char[row] = c; Ai[row] = ai; NView[row] = nv;
            Monster[row] = ai as MonsterAI; Hum[row] = c as Humanoid; Tf[row] = c.transform;
            Flags[row] = RowFlags.Registered;
            TrackCount[row] = 0; AggTrack[row] = 0xFF; AggState[row] = 0; AggDet[row] = 0f;
            Tier[row] = 3; Dir[row] = Directives.Defaults; DirRevision[row] = uint.MaxValue;
            AppliedTarget[row] = ZDOID.None; ReplTarget[row] = ZDOID.None;
            if (OnDamaged[row] == null) OnDamaged[row] = DamageHook.MakeForRow(row); // once per row, forever
            return row;
        }

        internal static void Free(int row)
        {
            DamageHook.Unsubscribe(row);
            Char[row] = null; Ai[row] = null; Monster[row] = null;
            NView[row] = null; Tf[row] = null; Hum[row] = null; Legacy[row] = null;
            Flags[row] = RowFlags.None; TrackCount[row] = 0;
            RowIndex.Forget(row);
            if (_freeCount == _free.Length) System.Array.Resize(ref _free, _free.Length * 2);
            _free[_freeCount++] = row;
        }

        internal static CreatureHandle HandleOf(int row) => new CreatureHandle(row, Gen[row]);
        internal static bool Alive(CreatureHandle h) => Gen[h.Row] == h.Gen && Flags[h.Row] != RowFlags.None;

        private static void Grow(int cap) { /* Array.Resize every column; log once at INFO */ }
    }
}
```

### 2.3 `Data/TrackStore.cs` — the per-(creature,player) columns

```csharp
namespace ShadowsOfMidgard.Data
{
    /// <summary>
    /// Track k of creature row r lives at index r*K + k. One allocation per column for the
    /// WHOLE WORLD, so consecutive creature rows have consecutive tracks and the integration
    /// pass walks memory forward. Split hot/cold: the pair kernel touches ONLY the four hot
    /// columns (7 bytes per track), so ten tracks fit in 70 bytes - one to two cache lines.
    /// </summary>
    internal static class TrackStore
    {
        /// <summary>Ten, not eight: ServerSync caps SoM at 10 tracked players, so a creature in a
        /// full 10-player session never evicts, and eviction becomes dead code in the common case.</summary>
        internal const int K = 10;

        // ---- HOT: the pair kernel's entire working set. ----
        internal static float[] Det;    // 0..1 integrated detection
        internal static byte[]  State;  // (byte)VanillaAlertness for THIS pair
        internal static byte[]  Flags;  // TrackFlags
        internal static byte[]  Slot;   // PlayerSlot. One byte. NOT a ZDOID.

        // ---- COLD: touched on transition, on replication, and by the patch layer. ----
        internal static Vector3[] Lkp;          // last known position of that player
        internal static float[]   SinceSensed;
        internal static float[]   LastLos;
        internal static float[]   LastEval;
        internal static ZDOID[]   Pid;          // 6 bytes (AV:64584/64586). Identity, not a hot key.

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int Base(int row) => row * K;

        /// <summary>Find the track for a player SLOT. Byte compares over <=10 entries: ~4 ns.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int IndexOfSlot(int row, byte slot)
        {
            int b = row * K, n = CreatureStore.TrackCount[row];
            for (int k = 0; k < n; k++) if (Slot[b + k] == slot) return b + k;
            return -1;
        }

        /// <summary>Open a track. Eviction is the lowest-detection entry; only reachable when K is
        /// exceeded, which requires more than 10 players in a creature's gate.</summary>
        internal static int Open(int row, byte slot, ZDOID pid, float now)
        {
            int i = IndexOfSlot(row, slot); if (i >= 0) return i;
            int b = row * K, n = CreatureStore.TrackCount[row], t;
            if (n < K) { t = b + n; CreatureStore.TrackCount[row] = (byte)(n + 1); }
            else
            {
                t = b; float worst = float.MaxValue;
                for (int k = 0; k < K; k++) { float s = Det[b + k]; if (s < worst) { worst = s; t = b + k; } }
            }
            Det[t] = 0f; State[t] = 0; Flags[t] = TrackFlags.None; Slot[t] = slot; Pid[t] = pid;
            Lkp[t] = default; SinceSensed[t] = 999f; LastLos[t] = 0f; LastEval[t] = now;
            return t;
        }

        internal static void CloseAt(int row, int t)
        {
            int b = row * K, last = b + CreatureStore.TrackCount[row] - 1;
            if (t != last)
            {
                Det[t]=Det[last]; State[t]=State[last]; Flags[t]=Flags[last]; Slot[t]=Slot[last];
                Lkp[t]=Lkp[last]; SinceSensed[t]=SinceSensed[last];
                LastLos[t]=LastLos[last]; LastEval[t]=LastEval[last]; Pid[t]=Pid[last];
            }
            CreatureStore.TrackCount[row]--;
        }
    }
}
```

### 2.4 `Data/PlayerStore.cs` — the hoist that keeps cost at P, not N×P

```csharp
namespace ShadowsOfMidgard.Data
{
    /// <summary>
    /// SURVEY-sensing §0 is the load-bearing fact: all four sensing entry points are 100%
    /// player-intrinsic. They are computed P times per profile tick and read N×P times per
    /// second at zero marginal cost. 16 slots, fixed, never grown.
    /// </summary>
    internal static class PlayerStore
    {
        internal const int Cap = 16;
        internal static int Count;

        internal static Player[]  Obj      = new Player[Cap];
        internal static ZDOID[]   Id       = new ZDOID[Cap];
        internal static bool[]    Valid    = new bool[Cap];
        internal static bool[]    Ghost    = new bool[Cap];   // InGhostMode() || InDebugFlyMode(): vanilla parity, AV:4556

        internal static float[]   PosX     = new float[Cap];
        internal static float[]   PosY     = new float[Cap];
        internal static float[]   PosZ     = new float[Cap];
        internal static Vector3[] AimPoint = new Vector3[Cap]; // crouch ? GetCenterPoint() : m_eye.position (AV:4612)
        internal static bool[]    Crouch   = new bool[Cap];
        internal static float[]   Speed    = new float[Cap];

        // vanilla replicated scalars - remote-safe, already throttled
        internal static float[]   Stealth  = new float[Cap];  // Character.GetStealthFactor()  AV:21842
        internal static float[]   NoiseRng = new float[Cap];  // Character.GetNoiseRange()     AV:10132

        // SoM intrinsic terms
        internal static float[]   Vis      = new float[Cap];
        internal static float[]   Noise    = new float[Cap];
        internal static float[]   Hiding   = new float[Cap];
        internal static float[]   Camo     = new float[Cap];
        /// <summary>Vis*(1-Hiding)*(1-Camo), PRE-COMBINED. The pair kernel never recomputes it.</summary>
        internal static float[]   AdjVis   = new float[Cap];

        // sub-terms with their own refresh policy
        internal static float[]   ArmorVis = new float[Cap];  // event-driven: Inventory.m_onChanged AV:56894
        internal static float[]   ArmorNoi = new float[Cap];
        internal static int[]     ArmorCamoCount = new int[Cap];
        internal static Heightmap.Biome[] Biome = new Heightmap.Biome[Cap]; // 2 Hz, Heightmap.FindBiome AV:111647
        internal static bool[]    InShadow = new bool[Cap];   // 2 Hz, one BATCHED raycast
        internal static int[]     Grass    = new int[Cap];
        internal static int[]     Bush     = new int[Cap];

        internal static byte SlotOf(Character c) => RowIndex.PlayerSlotOf(c);   // direct-mapped probe
    }
}
```

### 2.5 `Data/RowIndex.cs` — the patch layer's fast path

```csharp
namespace ShadowsOfMidgard.Data
{
    /// <summary>
    /// Object -> row resolution for the Harmony hot path. A direct-mapped cache keyed on
    /// UnityEngine.Object.GetInstanceID(), with a Dictionary&lt;int,int&gt; as the collision backstop.
    ///
    /// Why not Dictionary&lt;ZDOID, T&gt; (what DESIGN-pragmatic uses): ZDOID.GetHashCode() is
    /// `GetUserID(UserKey).GetHashCode() ^ ID.GetHashCode()` (AV:64697), and GetUserID is a
    /// STATIC LIST INDEX (AV:64612). Every dictionary probe therefore costs a list bounds
    /// check + indirection BEFORE the bucket walk, and building the key at all requires
    /// Character.GetZDOID() -> m_nview.IsValid() -> GetZDO().m_uid (AV:9689), three derefs.
    /// The direct-mapped probe is one multiply, one shift, two array reads.
    /// </summary>
    internal static class RowIndex
    {
        private const int Bits = 13, Size = 1 << Bits, Mask = Size - 1;   // 8192 slots, 64 KB
        private static readonly int[] _key = new int[Size];
        private static readonly int[] _val = new int[Size];
        private static readonly System.Collections.Generic.Dictionary<int, int> _overflow =
            new System.Collections.Generic.Dictionary<int, int>(512);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Bucket(int id) => (int)(((uint)id * 2654435761u) >> (32 - Bits)) & Mask;

        /// <summary>Returns the row, or -1. Never allocates. Never touches a ZDO.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int Row(UnityEngine.Object o)
        {
            if (ReferenceEquals(o, null)) return -1;
            int id = o.GetInstanceID(), b = Bucket(id);
            if (_key[b] == id) return _val[b];
            return _overflow.TryGetValue(id, out int r) ? r : -1;
        }

        internal static void Bind(int instanceId, int row)
        {
            int b = Bucket(instanceId);
            if (_key[b] == 0 || _key[b] == instanceId) { _key[b] = instanceId; _val[b] = row; }
            else _overflow[instanceId] = row;                 // collision: backstop, still O(1)
        }

        internal static void Forget(int row) { /* clear the direct slot + overflow entries for that row */ }

        internal static byte PlayerSlotOf(Character c) { /* same shape, 16-entry table */ return 0xFF; }
    }
}
```

**Correctness note, stated because it is the sharpest edge in the design:** `GetInstanceID()` is unique among
*live* objects but is recycled after destruction. A stale `_key` entry is therefore only dangerous if we forget
to `Forget()`. Two independent defences: (a) `Forget()` runs from both `P_Character_OnDestroy` *and* the 2 Hz
sweep's `!NView.IsValid()` reconciliation, so losing the patch does not lose the invalidation; and (b) every
consumer re-validates with `ReferenceEquals(CreatureStore.Char[row], instance)` before trusting the row — one
pointer compare, ~1 ns, and it makes an aliased instance ID structurally impossible to act on.

### 2.6 `Data/CellGrid.cs` — the spatial index

```csharp
namespace ShadowsOfMidgard.Data
{
    /// <summary>
    /// Hashed uniform grid over creature ROWS. Not a dense array: Valheim's world is ~20 km
    /// across and only ~200 m of it is ever loaded, so a dense grid would be 99.99% empty.
    /// Buckets are singly-linked through Next[], head-inserted, so insertion is O(1) and there
    /// is never an allocation after Init.
    /// </summary>
    internal static class CellGrid
    {
        internal const float CellSize = 32f;
        private const int Buckets = 2048, BMask = Buckets - 1;

        private static readonly int[] _head = new int[Buckets];  // row, or -1
        private static int[] _next;                              // per-row link, sized with the store

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int CellId(float x, float z)
        {
            int cx = Mathf.FloorToInt(x * (1f / CellSize));
            int cz = Mathf.FloorToInt(z * (1f / CellSize));
            return (cx & 0xFFFF) | (cz << 16);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Bucket(int cell) => (int)(((uint)cell * 2654435761u) >> 21) & BMask;

        internal static void Rebuild(int[] roster, int count) { /* clear heads, head-insert each row */ }

        /// <summary>Append every row in the 3x3 cell block around (x,z) to `into`. Callers filter by
        /// squared distance; the grid only bounds the candidate set. No allocation: `into` is pooled.</summary>
        internal static void Query3x3(float x, float z, System.Collections.Generic.List<int> into) { }
    }
}
```

### 2.7 `Kernels/Tuning.cs` — config becomes derived constants, once

```csharp
namespace ShadowsOfMidgard.Kernels
{
    /// <summary>
    /// Rebuilt on any ConfigEntry.SettingChanged (i.e. also on every ServerSync push) and
    /// never read from a ConfigEntry again. The hot loop does no division, no Mathf.Cos, no
    /// range squaring, and no null check on a config object.
    /// </summary>
    internal struct Tuning
    {
        internal float VisRangeSq, HearRangeSq, VisRangeInv, HearRangeInv;
        internal float CosConeHalf;
        internal float VisionThreshold, HearingThreshold;
        internal float PeripheralMul;             // v1's hard-coded 0.1f, now a named local knob

        /// <summary>Indexed by (canSee ? 2 : 0) | (canHear ? 1 : 0). Replaces v1's if/else-if chain.
        /// { -DetectionDecay, +SuspicionGain, +AlertGain, +AlertGain }</summary>
        internal float G0, G1, G2, G3;

        /// <summary>Ascend triple: Suspicious, Alerted, Engaged.</summary>
        internal float Up0, Up1, Up2;
        /// <summary>Descend triple, SORTED ASCENDING at build time: 0.02, Susp*0.5, Alert*0.7.
        /// v1 applied these as a sequential cascade, which oscillates if a user inverts the
        /// thresholds. Sorting here makes the ladder total and monotone for every config.</summary>
        internal float Dn0, Dn1, Dn2;

        internal float TierNearSq, TierMidSq, TierFarSq;
        internal float LosRecheckPeriod, LosVisPreGate;
        internal float OpinionTtl, TrackIdleTtl, TrackMarginSq;

        internal static Tuning Build(StealthConfigModel c)
        {
            var t = default(Tuning);
            t.VisRangeSq  = c.MaxVisualRange  * c.MaxVisualRange;
            t.HearRangeSq = c.MaxHearingRange * c.MaxHearingRange;
            t.VisRangeInv = 1f / Mathf.Max(0.01f, c.MaxVisualRange);
            t.HearRangeInv= 1f / Mathf.Max(0.01f, c.MaxHearingRange);
            t.CosConeHalf = Mathf.Cos(c.VisionConeHalfAngle * Mathf.Deg2Rad);
            t.VisionThreshold = c.VisionThreshold; t.HearingThreshold = c.HearingThreshold;
            t.PeripheralMul = 0.1f;
            t.G0 = -c.DetectionDecay; t.G1 = c.SuspicionGain; t.G2 = c.AlertGain; t.G3 = c.AlertGain;
            t.Up0 = c.SuspiciousThreshold; t.Up1 = c.AlertedThreshold; t.Up2 = c.EngagedThreshold;
            float a = 0.02f, b = c.SuspiciousThreshold * 0.5f, d = c.AlertedThreshold * 0.7f;
            Sort3(ref a, ref b, ref d);
            t.Dn0 = a; t.Dn1 = b; t.Dn2 = d;
            return t;
        }
        private static void Sort3(ref float a, ref float b, ref float c) { /* 3 compare-swaps */ }
    }
}
```

### 2.8 `Kernels/SenseKernel.cs` + `LadderKernel.cs` — the inner loop

```csharp
namespace ShadowsOfMidgard.Kernels
{
    internal static class SenseKernel
    {
        /// <summary>
        /// One (creature, player) pair. No branch that a predictor can miss on a hot fight,
        /// no Unity API, no allocation, no config deref. Reads: 5 creature columns, 6 player
        /// columns, 4 hot track columns. Writes: 4 hot track columns (+2 cold on a sense edge).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Integrate(int row, int t, byte slot, in Tuning T, float dt, float now)
        {
            float dx = PlayerStore.PosX[slot] - CreatureStore.PosX[row];
            float dy = PlayerStore.PosY[slot] - CreatureStore.PosY[row];
            float dz = PlayerStore.PosZ[slot] - CreatureStore.PosZ[row];
            float d2 = dx * dx + dy * dy + dz * dz;              // SQUARED. No sqrt on this path.

            // Falloff needs the linear distance, but only when it can matter. One reciprocal
            // sqrt covers both falloffs and the FOV normalisation.
            float invD = d2 > 1e-6f ? 1f / Mathf.Sqrt(d2) : 0f;
            float dist = d2 * invD;

            float visFall  = 1f - dist * T.VisRangeInv;  visFall  = visFall  > 0f ? (visFall  < 1f ? visFall  : 1f) : 0f;
            float hearFall = 1f - dist * T.HearRangeInv; hearFall = hearFall > 0f ? (hearFall < 1f ? hearFall : 1f) : 0f;

            // Planar FOV: creature facing is stored as (FwdX, FwdZ) already normalised.
            float dot = (dx * CreatureStore.FwdX[row] + dz * CreatureStore.FwdZ[row]) * invD;
            float coneMul = dot >= T.CosConeHalf
                ? 1f
                : Mathf.Clamp01((dot + 1f) / (T.CosConeHalf + 1f)) * T.PeripheralMul;

            float adjVis = PlayerStore.AdjVis[slot] * visFall * coneMul;
            float adjNoi = PlayerStore.Noise[slot]  * hearFall;

            byte tf = TrackStore.Flags[t];
            bool alerted  = TrackStore.State[t] >= (byte)VanillaAlertness.Alerted;
            bool ignoreLos = alerted || CreatureStore.Dir[row].IgnoreLos;
            bool los = ignoreLos || (tf & (byte)TrackFlags.HadLos) != 0;

            bool see  = los && adjVis > T.VisionThreshold;
            bool hear = adjNoi > T.HearingThreshold;

            int k = (see ? 2 : 0) | (hear ? 1 : 0);
            float gain = k == 0 ? T.G0 : (k == 1 ? T.G1 : (k == 2 ? T.G2 : T.G3));
            float det = TrackStore.Det[t] + gain * dt;
            det = det < 0f ? 0f : (det > 1f ? 1f : det);

            float floor = CreatureStore.Dir[row].DetectFloor;
            TrackStore.Det[t] = det > floor ? det : floor;

            tf = (byte)(tf & ~(byte)(TrackFlags.CanSee | TrackFlags.CanHear));
            if (see)  tf |= (byte)TrackFlags.CanSee;
            if (hear) tf |= (byte)TrackFlags.CanHear;
            if (see | hear)
            {
                tf |= (byte)TrackFlags.EverSensed;
                TrackStore.Lkp[t] = new Vector3(PlayerStore.PosX[slot], PlayerStore.PosY[slot], PlayerStore.PosZ[slot]);
                TrackStore.SinceSensed[t] = 0f;
            }
            else TrackStore.SinceSensed[t] += dt;
            TrackStore.Flags[t] = tf;
            TrackStore.LastEval[t] = now;

            TrackStore.State[t] = LadderKernel.Step(TrackStore.State[t], TrackStore.Det[t], in T,
                                                    CreatureStore.Dir[row].AlertFloor);
        }
    }

    internal static class LadderKernel
    {
        /// <summary>
        /// Branch-free equivalent of v1 StateEvaluator (Systems/StealthBrain/StateEvaluator.cs:13-33).
        /// Verified equal on the whole default parameter space; see §12 for the parity test.
        ///   ascend  : monotone-with-floor, state = max(state, crossings of {Susp, Alert, Eng})
        ///   descend : cascading, state = min(state, crossings of the sorted descend triple)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static byte Step(byte state, float det, in Tuning T, byte alertFloor)
        {
            int up = (det >= T.Up0 ? 1 : 0) + (det >= T.Up1 ? 1 : 0) + (det >= T.Up2 ? 1 : 0);
            int s  = up > state ? up : state;
            int dn = (det >= T.Dn0 ? 1 : 0) + (det >= T.Dn1 ? 1 : 0) + (det >= T.Dn2 ? 1 : 0);
            s = dn < s ? dn : s;
            return (byte)(s > alertFloor ? s : alertFloor);       // SoM_AlertFloor, free
        }
    }
}
```

### 2.9 `Api/LegacyAwareness.cs` — the frozen surface, unchanged types

```csharp
namespace ShadowsOfMidgard
{
    /// <summary>
    /// EXTERNAL API, FROZEN. Field NAMES *and TYPES* are v1-verbatim
    /// (Systems/UnifiedStealthTypes.cs:59-106). In particular CurrentAction stays
    /// AIBehaviorAction, not a new enum: a reflection consumer that does
    /// FieldInfo.SetValue with an AIBehaviorAction box would throw ArgumentException
    /// against a renamed enum type even if the member names matched.
    ///
    /// This object is NOT part of the simulation. It is a VIEW, materialised lazily and
    /// refreshed on read. See §9.2 for why v2 does not push into it every pass.
    /// </summary>
    public class AwarenessData
    {
        public float DetectionLevel = 0f;
        public Vector3 LastKnownPosition = Vector3.zero;
        public float TimeSinceSeen = 0f;
        public bool CanSense = false, CanSee = false, CanHear = false;
        public VanillaAlertness CurrentState = VanillaAlertness.Unaware;
        public AIBehaviorAction CurrentAction = AIBehaviorAction.Idle;
        public Vector3 DesiredDirection = Vector3.zero;
        public float DesiredSpeed = 0f;
        public bool ShouldRun = false;
        public Vector3 FleeDirection = Vector3.zero;
        public int NearbyAlliesCount = 0;
        public float TimeUntilNextAllyCall = 0f;
        public bool IsPartOfGroup = false;
        public TargetPriority TargetPriority = TargetPriority.None;
        public float TargetDistance = float.MaxValue;
        public float PlayerHealthPercent = 1f;
        public float SelfHealthPercent = 1f;
        public FleeReason CurrentFleeReason = FleeReason.None;
        public float FleeStartHealth = 1f;
        public float TimeSpentFleeing = 0f;
        public float TimeInCurrentState = 0f;
        public float TimeInCurrentAction = 0f;
        public int FramesSinceLastEval = 0;
        public float LastEvalTime = 0f;
        public string LastDecisionReason = "";
        public float LastConfidence = 0f;

        /// <summary>ADDED in v2. MoA 0.0.9 probed AccessTools.Field(data,"AggressionLevel") and got
        /// null because the field lived on CombatDirective. Adding it costs 4 bytes and closes a
        /// dead probe that may still exist in an old build in the wild.</summary>
        public float AggressionLevel = 0f;

        internal int  _row;        // owning row, for refresh-on-read
        internal uint _stamp;      // publish counter, so a second read in the same pass is free
    }

    public static class AwarenessSystem
    {
        /// <summary>FROZEN SIGNATURE. Static. Never returns null. Never throws on null input
        /// (v1 threw ArgumentNullException from Dictionary.TryGetValue(null) - and this is public API).</summary>
        public static AwarenessData GetData(Character c)
        {
            if (ReferenceEquals(c, null) || c == null) return _scratch;
            int row = Registry.RowFor(c);
            if (row < 0) return _scratch;
            var d = CreatureStore.Legacy[row];
            if (d == null) { d = CreatureStore.Legacy[row] = new AwarenessData { _row = row }; Registry.MarkObserved(row); }
            LegacyView.RefreshOnRead(row, d);        // pull, not push. See §9.2.
            return d;
        }

        public static AwarenessData GetData(BaseAI ai) => GetData(ai == null ? null : ai.GetComponent<Character>());
        public static void Clear(Character c) { int r = Registry.RowFor(c); if (r >= 0) Registry.Release(r); }
        public static void ClearAll() => Registry.ClearAll();
        public static System.Collections.Generic.IEnumerable<Character> GetAllTrackedCharacters() { /* kept */ }
        public static System.Collections.Generic.IEnumerable<
            System.Collections.Generic.KeyValuePair<Character, AwarenessData>> GetAllData() { /* kept */ }

        private static readonly AwarenessData _scratch = new AwarenessData();
    }
}
```

### 2.10 `Api/Directives.cs`

```csharp
namespace ShadowsOfMidgard
{
    /// <summary>Resolved per-creature policy. A STRUCT stored in a column, so resolving it costs
    /// no allocation and reading a field of it costs no indirection.</summary>
    public struct Directives
    {
        public bool  BrainOff;
        public byte  AlertFloor;      // 0..3
        public float DetectFloor;     // 0..1
        public float SightRangeSq;    // pre-squared at resolve time
        public float HearRangeSq;
        public float CosConeHalf;     // pre-cosined at resolve time
        public bool  IgnoreLos;
        public bool  NoFlee;
        public float GiveUpMul;
        public byte  ContractVersion;
        public bool  LegacyExempt;

        public static readonly Directives Defaults = new Directives { GiveUpMul = 1f, AlertFloor = 0 };
    }
}
```

---

## 3. The per-player track model and its memory bound

### 3.1 What a track is

One row of `TrackStore` = one creature's knowledge of one player. It stores **nothing** that is a property of
the creature alone (health, flee reason, ally count, action timers) — those are creature columns, so the
per-player dimension multiplies only the four hot bytes-per-track that actually vary per relationship.

| Column | Bytes | Block | Read by |
|---|---|---|---|
| `Det` | 4 | hot | pair kernel, ladder, aggregate, replication |
| `State` | 1 | hot | pair kernel (LOS skip), patches, aggregate |
| `Flags` | 1 | hot | pair kernel, `CanSee`/`CanHear` patch answers |
| `Slot` | 1 | hot | pair kernel — **the identity key on the hot path is one byte** |
| `Lkp` | 12 | cold | movement/search, replication |
| `SinceSensed` | 4 | cold | give-up, movement |
| `LastLos` | 4 | cold | LOS recheck gate |
| `LastEval` | 4 | cold | patch freshness gate |
| `Pid` (`ZDOID`) | 6→8 | cold | patch lookup, replication, handover seeding |

**Hot footprint per creature: 10 tracks × 7 B = 70 B.** The pair-integration pass therefore touches ~1–2 cache
lines per creature. `DESIGN-pragmatic`'s `Track` struct is 56 B with all fields interleaved, so ten tracks span
560 B ≈ 9 cache lines, and its `Track[8]` array is a **separate heap allocation per creature**, so consecutive
creatures land at unrelated addresses. At 200 creatures that is the difference between a ~14 KB working set
that fits in L1/L2 and a ~112 KB scattered set that does not.

### 3.2 Lifecycle

- **Open** when a player enters `max(sightRange, hearRange) + TrackCreateMargin` (default +8 m hysteresis), or
  when `m_onDamaged` fires with a player attacker (`TrackFlags.Damaged`), or when the replication blob is
  decoded after an ownership handover (`TrackFlags.Seeded`).
- **Decay-only** when the player leaves the gate: the track keeps integrating `-DetectionDecay` so the creature
  visibly loses interest instead of instantly forgetting. It is not closed.
- **Close** when `Det <= 0.001` **and** `now - LastEval > TrackIdleTtl` (default 20 s), or when the player slot
  is released. Closing is a swap-with-last into the same contiguous block: no shuffle, no allocation.
- **Evict** only when a creature has more than `K = 10` players in gate at once. Lowest `Det` loses. Because
  ServerSync caps `PlayerStore` at 16 and `K` is 10, this is unreachable in a ≤10-player session — which is the
  brief's target — so eviction is cold code, not a steady-state behaviour.

### 3.3 The memory bound, exactly

Per row, with `K = 10`:

| Block | Bytes/row |
|---|---|
| Creature hot columns | 40 |
| Creature warm columns | 77 |
| Replication + apply shadows | 24 |
| Component reference columns (6 × 8 B on x64) | 48 |
| Track hot (10 × 7) | 70 |
| Track cold (10 × 32) | 320 |
| **Subtotal** | **579** |

| Fixed structures | Bytes |
|---|---|
| Rows at `Capacity = 1024` | 592 KB |
| `RowIndex` direct-mapped tables (2 × 8192 × 4) | 64 KB |
| `CellGrid` heads + links | 12 KB |
| `PlayerStore` (16 slots) | 2 KB |
| Per-row damage delegates (lazy, ≤1024 × ~128 B) | ≤128 KB |
| `NativeArray<RaycastCommand>` 512 × 44 B (`Allocator.Persistent`, **unmanaged**) | 22 KB |
| `NativeArray<RaycastHit>` 512 × ~76 B (**unmanaged**) | 39 KB |
| Pooled `List<int>` / `List<Player>` scratch | < 8 KB |
| **Total, allocated once** | **≈ 867 KB** (≈ 806 KB managed, ≈ 61 KB unmanaged) |

At the realistic 200-creature load the store is 4 % occupied and still costs 592 KB, because it is pre-sized.
**That is a deliberate trade: half a megabyte of RSS in exchange for a hard zero-allocation guarantee and a
flat, predictable access pattern.** The default `MinCapacity` is 256 (148 KB) and it doubles on demand, so a
single-player session never pays for the multiplayer ceiling.

GC characteristics: of ~30 array objects in the store, only **6** contain references (`Char`, `Ai`, `Monster`,
`NView`, `Tf`, `Hum`) plus `Legacy` and `OnDamaged`. A Gen2 mark therefore traces 8 contiguous arrays. The
comparable object graph in `DESIGN-pragmatic` is one `CreatureState` + one `Track[]` + one `AwarenessData` per
creature — 600 scattered objects at N=200, each a mark-stack push and a probable cache miss.

---

## 4. The scheduler

### 4.1 Shape: one `Update()`, phase-separated, striped

```csharp
// Pipeline/Driver.cs
internal sealed class Driver : MonoBehaviour
{
    private float _nextSweep, _nextProfile, _nextVeg;

    private void Update()
    {
        if (!Runtime.Live) return;
        float now = Time.time, frameDt = Time.deltaTime;

        if (now >= _nextSweep)   { P0_Sweep.Run(now);    _nextSweep   = now + Perf.SweepPeriod; }   // 0.5 s
        if (now >= _nextProfile) { P1_Profiles.Run(now); _nextProfile = now + Perf.ProfilePeriod; } // 0.1 s
        if (now >= _nextVeg)     { VegetationSampler.Step(now); _nextVeg = now + Perf.VegPeriod; }  // 0.33 s

        P4_Los.Consume();                                   // read LAST epoch's results. Never blocks.

        for (int tier = 0; tier < 4; tier++)
        {
            Stripe s = Stripe.Next(tier, now, frameDt);     // contiguous [begin,end) into Rosters.Of(tier)
            if (s.Count == 0) continue;
            P2_Gather   .Run(in s, now);
            P3_Broadphase.Run(in s, now);
            P5_Integrate.Run(in s, now);                    // P4 emit happens inside P3/P5
            P6_Decide   .Run(in s, now);
            P7_Apply    .Run(in s, now);
        }

        P4_Los.Schedule();                                  // fire this epoch's batch, if due
        ThreatQuery.Publish(now);                           // one struct for the HUD
    }
}
```

Six named phases, each a `for` loop over `int[] roster` in ascending order. No coroutines: v1's four
(`EvaluateLoop`, `UpdatePlayerVegetationCoroutine`, `UpdateNearbyAlliesCoroutine`, `MigrateCacheCoroutine`) are
all deleted, and with them the iterator state machines, the `WaitUntil` closures that pinned `MonsterAI`
references forever (`StealthBrain.cs:127-130`), and the "yield inside the foreach so `DistanceToPlayer` is
stale on resume" hazard (`StealthOvermind.cs:128`).

### 4.2 Tier rosters, not per-creature timers

```csharp
// Data/Rosters.cs
internal static class Rosters
{
    internal static readonly int[][] Roster = { new int[256], new int[512], new int[1024], new int[1024] };
    internal static readonly int[]   Count  = new int[4];

    /// <summary>Rebuilt on the 2 Hz sweep from Tier[]. Ascending row order is deliberate:
    /// every phase then walks the columns forward, which is what the hardware prefetcher wants.</summary>
    internal static void Rebuild() { /* single ascending scan of rows 0..HighWater */ }
}

// Pipeline/Stripe.cs
internal struct Stripe { internal int[] Rows; internal int Begin, Count; internal float Dt; }

internal static class Stripe
{
    private static readonly int[]   _cursor = new int[4];
    private static readonly float[] _ewmaNs = { 400f, 400f, 400f, 400f };   // per-pass cost estimate

    /// <summary>
    /// Contiguous range stripe. Size is chosen so that ONE FULL ROUND of this tier completes in
    /// exactly its period, given the measured per-pass cost - so the round-trip time is a
    /// DESIGN PARAMETER, not an emergent property of a budget running out.
    /// </summary>
    internal static Stripe Next(int tier, float now, float frameDt)
    {
        int n = Rosters.Count[tier]; if (n == 0) return default;
        float period = Perf.TierPeriod[tier];                       // 0.10 / 0.33 / 1.00 / 4.00 s
        int want = Mathf.CeilToInt(n * frameDt / period);           // rows this frame owes
        int cap  = Mathf.Max(1, (int)(Perf.TierBudgetNs[tier] / _ewmaNs[tier]));
        int take = Mathf.Min(want, cap);                            // hard ceiling, but only under real load
        ...
    }

    /// <summary>Called after each stripe with Stopwatch ticks. Feeds the EWMA and the overrun warning.</summary>
    internal static void Record(int tier, int rows, long ticks) { /* EWMA; WARN once/30 s if round > 2x period */ }
}
```

Why this instead of `DESIGN-pragmatic`'s "walk a `List<CreatureState>` cursor until a 1 ms budget is
exhausted, skipping entries whose `NextEvalAt` has not arrived":

1. **A dormant creature costs zero, not one pointer-chase.** Pragmatic's loop must load the `CreatureState`
   reference from the list, dereference it (a probable cache miss — these are scattered heap objects), read
   `Valid`, read `NextEvalAt`, and compare, *before* it can `continue`. At N=200 with 35 dormant that is 35
   wasted cache misses per lap; at N=1000 with 700 dormant it is 700, and the wasted work is now larger than
   the useful work. Mine never enumerates a dormant row: it is in roster 3, which is visited at 0.25 Hz.
2. **Skipped creatures consume the budget that due creatures needed.** In pragmatic's `Tick`, `visited++`
   happens for skipped entries and `Budget.Exhausted` can fire mid-lap, so under load the cursor stalls at
   whatever offset it reached and the creatures past it wait a full extra lap. Mine sizes each stripe from the
   tier's own population and period, so the round-trip time is `period` by construction, and the *only* thing
   that can stretch it is `TierBudgetNs`, which is measured and logged.
3. **Cache locality is an output, not an accident.** A contiguous range of a sorted roster maps to a
   contiguous range of every column. `Rosters.Rebuild()` sorting ascending is what makes the whole SoA layout
   pay off; a modulo stripe or a shuffled list would throw that away.

`Tier` is assigned at the end of `P6_Decide` from the **squared** distance to the nearest *tracked* player
(never `Player.m_localPlayer`), with 20 % hysteresis on the band edges so a creature pacing a boundary does not
thrash rosters.

### 4.3 LOS: the one term that matters, and where it goes

```csharp
// Jobs/RaycastBatch.cs
internal static class RaycastBatch
{
    internal const int Cap = 512;
    private static NativeArray<RaycastCommand> _cmd;
    private static NativeArray<RaycastHit>     _hit;
    private static readonly int[] _pairRow  = new int[Cap];
    private static readonly int[] _pairTrk  = new int[Cap];
    private static int _pending;
    private static JobHandle _handle;
    private static bool _inFlight;

    /// <summary>
    /// Every type here ships with the game and is ALREADY referenced by ShadowsOfMidgard.csproj:
    ///   Unity.Collections.NativeArray&lt;T&gt;, Unity.Collections.Allocator, Unity.Jobs.JobHandle
    ///        -> UnityEngine.CoreModule.dll
    ///   UnityEngine.RaycastCommand, UnityEngine.QueryParameters, RaycastHit.colliderInstanceID
    ///        -> UnityEngine.PhysicsModule.dll
    /// Verified by loading both assemblies out of valheim_Data\Managed and reflecting them, and
    /// corroborated by VALHEIM ITSELF using this exact call at AV:115123:
    ///     RaycastCommand.ScheduleBatch(m_raycastCommands, m_raycastResults, 16).Complete();
    /// with NativeArray fields declared at AV:114764-114766 and allocated at AV:114795-114796.
    /// There is no Unity.Burst.dll in the install, so a job runs as plain managed IL on a worker
    /// thread. That is exactly what we need here (the work is native raycasting) and exactly why
    /// we do NOT put the brain kernels in a job - see §4.5.
    /// </summary>
    internal static void Init()
    {
        _cmd = new NativeArray<RaycastCommand>(Cap, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        _hit = new NativeArray<RaycastHit>    (Cap, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
    }

    /// <summary>Called from the integrate phase. Returns false if the batch is full; the caller
    /// then reuses the cached LOS bit for another epoch. Degradation is latency, never correctness.</summary>
    internal static bool Emit(int row, int t, Vector3 from, Vector3 to)
    {
        if (_pending >= Cap) return false;
        Vector3 d = to - from; float len = d.magnitude; if (len < 1e-3f) return false;
        _cmd[_pending] = UnityBind.MakeRaycast(from, d * (1f / len), len, LayerMasks.ViewBlock);
        _pairRow[_pending] = row; _pairTrk[_pending] = t;
        TrackStore.Flags[t] |= (byte)TrackFlags.LosPending;
        _pending++;
        return true;
    }

    internal static void Schedule()
    {
        if (_inFlight || _pending == 0) return;
        _handle = RaycastCommand.ScheduleBatch(_cmd, _hit, Perf.MinCommandsPerJob, default(JobHandle));
        _inFlight = true;
    }

    /// <summary>Consumed at the TOP of the next epoch's Update, so Complete() finds the job already
    /// finished and returns without blocking. One epoch of latency (default 4 frames = 67 ms) is
    /// well inside LosRecheckPeriod, so it is invisible.</summary>
    internal static void Consume()
    {
        if (!_inFlight) return;
        _handle.Complete();
        for (int i = 0; i < _pending; i++)
        {
            int t = _pairTrk[i];
            // A MISS is colliderInstanceID == 0. Do NOT read .collider: that property is a managed
            // Object.FindObjectFromInstanceID lookup, ~30 ns each, and we do not need the collider.
            bool blocked = _hit[i].colliderInstanceID != 0;
            byte f = (byte)(TrackStore.Flags[t] & ~(byte)TrackFlags.LosPending);
            TrackStore.Flags[t] = blocked ? (byte)(f & ~(byte)TrackFlags.HadLos)
                                          : (byte)(f |  (byte)TrackFlags.HadLos);
            TrackStore.LastLos[t] = Time.time;
        }
        _pending = 0; _inFlight = false;
    }
}
```

The emit gate — this is where most casts are removed before they are ever created:

```csharp
// inside P5_Integrate, before the kernel call
bool needLos =
      !ignoreLos                                                    // already Alerted, or SoM_IgnoreLoS: vanilla
                                                                    // ignores the FOV cone too (AV:4608)
   && (TrackStore.Flags[t] & (byte)TrackFlags.LosPending) == 0
   && now - TrackStore.LastLos[t] >= T.LosRecheckPeriod             // 0.20 s
   && d2 <= dirSightSq                                              // squared, no sqrt
   && preVis > T.LosVisPreGate;                                     // VisionThreshold * 0.5
if (needLos) RaycastBatch.Emit(row, t, eye, PlayerStore.AimPoint[slot]);
```

`preVis` is `AdjVis[slot] * visFall` — already computed for the kernel. A player who could not clear the vision
threshold *even with perfect line of sight* never generates a cast. v1 cast unconditionally
(`SensingEvaluator.cs:16`) and then discarded the result whenever `ignoreLoS` was true (`:37`).

**`UnityBind.MakeRaycast` is a soft-bound ladder** (§8): `RaycastCommand(from, dir, QueryParameters, distance)`
→ legacy `RaycastCommand(from, dir, distance, layerMask, maxHits)` → if `RaycastCommand` itself is unresolvable,
`Perf.BatchedLos = false` and `P4_Los` degrades to a synchronous `Physics.Linecast` with a per-frame quota,
i.e. exactly `DESIGN-pragmatic`'s scheme. **The fast path is an optimisation, not a dependency.**

### 4.4 Cost model at 200 creatures × 10 players

Modelled unit costs (x86-64, ~3.5 GHz). These are estimates, stated so they can be argued with; the
`Physics.Linecast` figure is taken from `DESIGN-pragmatic` unchanged so the two models are comparable.

| Operation | Cost |
|---|---|
| ALU op / L1 hit | 0.3 ns |
| `Transform.position` / `.forward` getter (native interop) | 20 ns |
| `GetComponent<T>()` | 150 ns |
| `Dictionary<int,int>` probe | 8 ns |
| `Dictionary<ZDOID,T>` probe (incl. `GetUserID` list indirection, AV:64612/64697) | 25 ns |
| `ZDO.GetInt(int,int)` → `ZDOExtraData` side-table (AV:62718) | 40 ns |
| `"SoMStealthExempt".GetStableHashCode()` (16 chars) | 40 ns |
| `Physics.Linecast`, 40 m, 7 layers, main thread | 18 µs |
| `Physics.OverlapSphereNonAlloc`, 20 m, masked | 35 µs |
| `RaycastCommand` fill / result read | 20 ns / 5 ns |
| `RaycastCommand.ScheduleBatch` call overhead | 10 µs |
| `ZDO.Set(int, byte[])` incl. revision bump + side-table insert | 150 ns |

**Scenario S-worst — the brief's literal case.** All 200 creatures in tier HOT (period 0.10 s ⇒ 10 passes/s
each), all 10 players inside every creature's gate, `C = 300` loaded `Character`s.

Passes per second: `200 × 10 = 2 000`.
Pairs per second: `2 000 × 10 = 20 000`.

*Per-pass fixed cost:*

| Term | Detail | ns |
|---|---|---|
| Roster read + flag test + row loads | 6 L1 reads + 2 compares | 3 |
| `GetPositionAndRotation` (one interop for both) | soft-bound; else 2 interops = 40 | 20 |
| Write pos/fwd columns, rebucket cell | 5 stores + hash + compare | 4 |
| `ZDO.DataRevision` compare (directive cache) | property read + compare (AV:62322) | 4 |
| Broadphase: 10 players × sqrDist | 10 × 4 ns | 40 |
| **Integrate: 10 pairs × 25 ns** | falloff + FOV + gain table + ladder + flag store | **250** |
| LOS emit (0.9 casts per pass, see below) | 0.9 × 20 ns | 18 |
| Aggregate over 10 tracks | 10 × 3 ns | 30 |
| Decide (action table + flee + tier) | ~40 ops | 12 |
| Ally count via grid, throttled to 2 Hz ⇒ 0.2/pass | 0.2 × 150 ns | 30 |
| Apply, change-gated (no change ⇒ 2 compares) | | 6 |
| **Total per pass** | | **≈ 417** |

`2 000 × 417 ns` = **0.83 ms/s**.

*LOS volume.* Of 20 000 pairs/s: ~40 % are already `>= Alerted` and skip (12 000 left); ~30 % of those survive
the `d2 <= sightSq && preVis > threshold*0.5` pre-gate (3 600 left); the 0.20 s recheck gate against a 10 Hz
revisit halves it ⇒ **1 800 casts/s**.

| LOS path | Main-thread cost |
|---|---|
| Synchronous (`DESIGN-pragmatic`'s scheme) | 1 800 × 18 µs = **32.4 ms/s** — and pragmatic hard-caps at 24/frame = 1 440/s = 25.9 ms/s, *truncating the remaining 360 casts/s* |
| Batched (this design) | fill 1 800 × 20 ns = 36 µs + schedule 15 epochs/s × 10 µs = 150 µs + read 1 800 × 5 ns = 9 µs = **195 µs/s** |

Worker threads absorb the 32.4 ms/s of actual casting, spread across Unity's job workers.
**166× less main-thread time, and no truncation, so first-detection latency is bounded by
`LosRecheckPeriod` (0.20 s) rather than by how many other creatures wanted a cast this frame.**

*Patch layer.* `MonsterAI.UpdateTarget` runs `FindEnemy` every 2 s per creature when a player is within 50 m
(`AV:5850-5853`), and `FindEnemy` calls `CanSenseTarget(item)` once per loaded `Character` (`AV:5191-5203`):
`200 / 2 × 300 = 30 000` calls/s.

| Call site | Rate | Unit | µs/s |
|---|---|---|---|
| `CanSenseTarget`, player target | 1 000/s | probe 3 + flags 2 + slot 3 + track scan 4 + read 2 = **14 ns** | 14 |
| `CanSenseTarget`, creature target (v2.0 default: `IsPlayer()` then fall through) | 29 000/s | 3 ns | 87 |
| `CanSeeTarget`/`CanHearTarget` on the current target, 20 Hz (`AV:5919-5921`) | 8 000/s | 14 ns | 112 |
| `MonsterAI.UpdateAI` postfix, all `BaseAI.Instances` at 20 Hz | 5 000/s | 5–15 ns | 50 |
| `Character.SetMoveDir` prefix | 10 600/s | 7 ns | 74 |
| **Patch total** | | | **337** |

For scale: v1's equivalent, from `SURVEY-patches` §6.1–6.2, is prefix **and** postfix each running a
4-`GetComponent` + `GetStableHashCode` + `ZDO.GetInt` guard chain ≈ 1.1 µs per `CanSenseTarget` call ⇒
**33 ms/s from this family alone**, ~98× the whole v2 patch layer.

*Everything else:*

| Term | Rate | µs/s |
|---|---|---|
| Player profiles, cheap tier | 10 Hz × 10, ~220 ns each | 22 |
| Player profiles, `Heightmap.FindBiome` (AV:111647, linear scan) | 2 Hz × 10, ~2 µs | 40 |
| Player shadow raycast | folded into the LOS batch, 20/s | 0.5 |
| Armor totals (`Inventory.m_onChanged`, AV:56894) | event-driven | ~0 |
| Vegetation sampler (auto-disarms after 200 zero samples) | ≤ 30/s | 0–15 |
| `P0_Sweep` over `BaseAI.BaseAIInstances` (AV:4011) | 2 Hz × 250 × 45 ns | 22 |
| Roster rebuild | 2 Hz × 200 × 3 ns | 1 |
| Replication (delta-gated; assume all 200 changing) | 400 writes/s × 230 ns | 92 |
| HUD publish | 60/s | 2 |

**S-worst, main thread:**

| Block | µs/s |
|---|---|
| Creature passes | 830 |
| LOS batch (main-thread share) | 195 |
| Patch layer | 337 |
| Player profiles | 63 |
| Sweep + rosters | 23 |
| Replication | 92 |
| HUD | 2 |
| **TOTAL** | **≈ 1 542 µs/s ≈ 1.54 ms/s ≈ 0.15 % of one core** |
| Worker threads (raycasting) | ≈ 32 ms/s, parallel |
| **Steady-state managed allocation** | **≈ 1.4 KB/s** (replication blobs only — see §7.4) |

Per frame at 60 fps that is **26 µs**, against a 16 667 µs frame budget: **0.15 %**.

**Comparison.** `DESIGN-pragmatic` §4.5 states ≈ 28 ms/s for the same 200 × 10 case, of which 26 ms/s is
synchronous LOS. v1 measured at *one twentieth* of that load (C = 120, N = 20, P = 1) was 15–20 ms/s and
1.1 MB/s of garbage (`SURVEY-brain` §7).

| | v1 @ N=20,P=1 | pragmatic @ N=200,P=10 | this @ N=200,P=10 |
|---|---|---|---|
| Main-thread CPU | 15–20 ms/s | ≈ 28 ms/s | **≈ 1.5 ms/s** |
| Steady-state garbage | ≈ 1.1 MB/s | "0 B/s (< 2 KB/s)" | **≈ 1.4 KB/s** |
| LOS ceiling | uncapped, 5 evals/frame | 24/frame, **truncating** | 512/epoch, off-thread |
| Dormant-creature per-frame cost | O(all loaded characters) | 1 deref + 1 compare each | **0** |

**Scenario S-typical** (tier mix 25 hot / 60 warm / 80 cold / 35 dormant, 2.5 players in gate on average):
519 passes/s, 1 298 pairs/s, ~205 casts/s ⇒ creature passes 155 µs/s, LOS 160 µs/s, patches ~200 µs/s,
everything else ~180 µs/s ⇒ **≈ 0.70 ms/s**.

**Where the wall actually is.** Push to N = 1 000, C = 1 100. My creature passes rise to ~0.5 ms/s (most
creatures fall into cold/dormant rosters). But `FindEnemy` becomes `1000/2 × 1100 = 550 000` `CanSenseTarget`
calls/s, and *vanilla's own body* for a far-away rejected candidate costs ~156 ns — `m_character.m_eye.position`
(interop), `ZoneSystem.GetGlobalKey` (`AV:4531`), then `Vector3.Distance` twice (`AV:4560`, `AV:4602`), each of
which reads two transforms across the interop boundary, plus `Character.InInterior` (`AV:4562`). That is
**86 ms/s of pure vanilla cost**, 55× everything SoM does. **At scale the bottleneck is vanilla's O(N·C) target
acquisition, not SoM's brain**, and no amount of SoA in the brain touches it.

Which is why the design includes one opt-in lever aimed squarely at it (§5.4): SoM already holds a cached
position for every registered `BaseAI`, so the `CanSenseTarget` prefix can answer `false` for
creature-vs-creature pairs beyond `max(hearRange, viewRange)` **without any transform read at all**, which is
*provably* the same answer vanilla computes (`AV:4560-4570`, `AV:4602-4608`; a non-player's
`GetStealthFactor()` is the base virtual returning `1f`, `AV:10094`). Cost 10 ns, saves ~146 ns, on ~95 % of
550 000 calls ⇒ **≈ 76 ms/s recovered, for 5.5 ms/s spent**.

### 4.5 What explicitly does NOT go off the main thread, and why

**Cannot, at all:** `GetComponent`, any `UnityEngine.Object` dereference or `==` comparison,
`Transform.position` / `.forward` / `.rotation`, immediate-mode `Physics.Raycast` / `Linecast` /
`OverlapSphere`, every `ZDO` / `ZDOMan` / `ZNetView` access (`ZDOExtraData` is a plain static dictionary set,
`AV:63922-63983`, with no synchronisation), `Time.time`, `Character.GetStealthFactor()` / `GetNoiseRange()`
(both dereference `m_nview`, `AV:21842`, `AV:10132`). Every one of these is confined to `P1`, `P2` and `P7`.

**Can, and does:** `RaycastCommand.ScheduleBatch` — the sanctioned escape hatch, and the only physics work in
the whole design that is worth escaping.

**Could, and deliberately does not: the brain kernels.** `P5_Integrate` at S-worst is 500 µs/s of pure float
math over managed arrays. It is a legal `IJobParallelFor` candidate on paper. It is rejected because:

1. **There is no Burst.** The install contains no `Unity.Burst.dll`, so a job is plain managed IL on a worker
   thread — no SIMD, no auto-vectorisation. The win is thread-parallelism only.
2. **The workload is smaller than the scheduling overhead.** A `Schedule` + `Complete` round trip is ~10–20 µs.
   The entire per-frame integration work at S-worst is ~8 µs. You would spend more scheduling than computing.
3. **`NativeArray` is the only job-safe container available.** `Unity.Collections`' `NativeList`/`NativeHashMap`
   are not shipped. Moving the store to `NativeArray` would mean unmanaged memory for every column, manual
   disposal on domain reload, and no `Character[]` at all — a very large tax for a term that is already 0.05 %
   of a frame.
4. **The failure mode is a hard crash, not a slowdown.** Touching a managed Unity object from a job is not an
   exception, it is an editor/player abort. The brief says *never hard-crash*.

Stated plainly: **the arithmetic was never the bottleneck, so parallelising the arithmetic is not the win.**
The win is (a) not doing the arithmetic for creatures nobody can perceive, (b) not paying `GetComponent` and
`GetStableHashCode` 30 000 times a second in the patch layer, and (c) getting raycasts off the main thread.

---

## 5. The patch layer

### 5.1 Doctrine for coexisting with arbitrary third-party mods

Four rules, applied without exception. They are written for *any* mod that patches these methods, not for a
named sibling — see §9.1 on why that distinction matters.

1. **Every prefix takes `bool __runOriginal` and returns immediately, touching nothing, when it is `false`.**
   If a higher-priority prefix has already decided to skip the original, SoM does not overwrite its `__result`.
   v1's `[HarmonyPriority(int.MaxValue)]` prefixes actively fought this.
2. **Prefix priority is `Priority.Low` (200), never `int.MaxValue`.** Lower priority runs later, so any mod
   that wants to win by priority alone wins by default. **The Harmony id stays `"wubarrk.shadowsofmidgard"`
   verbatim**, so existing `HarmonyBefore`/`HarmonyAfter` edges in any third-party build — shipping today or
   still in the wild — keep resolving.
3. **No postfix on any sensing method.** v1 registered prefix + postfix pairs that re-ran the identical
   4-`GetComponent` guard chain to re-assert the identical value, purely to win a sort-order race. That is
   ~50 % of the patch layer's CPU and an unwinnable argument with anyone else's `Priority.Last`. v2 cedes the
   final say. `Compat.AssertFinalSay` (local, default **false**) can reinstate a postfix for a user with a
   genuinely misbehaving third mod.
4. **"No opinion" always means `return true`.** There is no path where SoM skips the original and writes a
   value it is not confident in. v1's `if (data == null) return true` guards were unreachable because
   `GetData` never returned null (`SURVEY-patches` §3), so *every* creature the Overmind had not reached was
   answered "blind and pacified". In v2 the equivalent condition is "no track, or a stale track", and it is
   reached constantly and correctly.

### 5.2 The table

| # | Class | Target (explicit arg types, always) | Kind | Priority | Skips vanilla? | Guard order (cheapest discriminator first) |
|---|---|---|---|---|---|---|
| 1 | `P_BaseAI_CanSeeTarget` | `BaseAI.CanSeeTarget(Character)` | Prefix | `Low` (200) | only with an opinion | `__runOriginal` → `Runtime.Live` → `target.IsPlayer()` → `RowIndex.Row(__instance)` → `ReferenceEquals` revalidate → `Flags & AnswerMask` → `PlayerSlotOf` → `IndexOfSlot` → freshness |
| 2 | `P_BaseAI_CanHearTarget` | `BaseAI.CanHearTarget(Character)` | Prefix | `Low` | ″ | ″ |
| 3 | `P_BaseAI_CanSenseTarget` | `BaseAI.CanSenseTarget(Character)` | Prefix | `Low` | ″ | ″, then the optional creature-vs-creature early-out (§5.4) |
| 4 | `P_MonsterAI_UpdateAI` | `MonsterAI.UpdateAI(float)` | Postfix | `Low` | n/a | `Runtime.Live` → row → revalidate → `Flags & ApplyMask` (includes `!Sleeping`) → decision freshness → change-gated apply |
| 5 | `P_Character_SetMoveDir` | `Character.SetMoveDir(Vector3)` — `ref Vector3 dir` | Prefix | `Last` (0) | never | `__instance is Player` → row → revalidate → `Flags & Searching` → `dir.sqrMagnitude > 1e-6` |
| 6 | `P_Humanoid_StartAttack` | `Humanoid.StartAttack(Character, bool)` | Prefix | `Low` | only with an opinion | `__runOriginal` → `cfg.HardBlockAttacks` → row → revalidate → `Flags & ApplyMask` → `GetTargetCreature()` is player → track → freshness → gate |
| 7 | `P_BaseAI_Awake` | `BaseAI.Awake()` | Postfix | `Normal` | n/a | **latency optimisation only** — claim a row now instead of within 0.5 s |
| 8 | `P_Character_OnDestroy` | `Character.OnDestroy()` | Prefix | `Normal` | n/a | **latency optimisation only** — release the row and `RowIndex.Forget` now |
| 9 | `P_Player_OnSpawned` | `Player.OnSpawned(bool)` | Postfix | `Normal` | n/a | **latency optimisation only** — refresh `PlayerStore` membership now |

**Nine patches, down from ten, and the three most damaging ones are gone:**
`BaseAI_StealthBrain_IsAlerted_Patch`, `Character_Damage_Patch`, `Character_OnDamaged_Patch` (§11).

**Patches 7–9 are not load-bearing.** `P0_Sweep` reconciles against `BaseAI.BaseAIInstances` (`AV:4011`) every
0.5 s and is the sole authority for row lifetime; `Character.m_onDamaged` (`AV:6875`) is a **public field** that
can be subscribed from the sweep just as well as from an `Awake` postfix. If Valheim 1.0 renames or removes any
of `BaseAI.Awake`, `Character.OnDestroy` or `Player.OnSpawned`, SoM logs one warning and keeps working with a
worst-case 0.5 s registration latency. `DESIGN-pragmatic` makes `P_BaseAI_Awake` the *only* damage hook, so
losing that one target loses damage-driven aggro entirely.

### 5.3 The gate, and why it is one AND

```csharp
// Patches/Gate.cs
internal static class Gate
{
    // Every predicate the guard chain used to recompute per call, precomputed on the 0.5 s sweep.
    internal const byte AnswerMask  = (byte)(RowFlags.Registered | RowFlags.Simulated | RowFlags.Owned |
                                             RowFlags.Valid      | RowFlags.HasOpinion);
    internal const byte ApplyMask   = (byte)(RowFlags.Registered | RowFlags.Simulated | RowFlags.Owned |
                                             RowFlags.Valid);
    internal const byte SleepBit    = (byte)RowFlags.Sleeping;

    /// <summary>
    /// True only when SoM has a real, current opinion about (ai, playerTarget).
    /// ~14 ns. Compare DESIGN-pragmatic's TryOpinion: Runtime.Active, target null, IsPlayer(),
    /// Dictionary&lt;ZDOID&gt; find (which first needs Character.GetZDOID() -> 3 derefs), st.Owned,
    /// st.Valid, IsDead(), IsTamed(), Dir.BrainOff, GetZDOID() again, IndexOf over 12-byte ZDOIDs,
    /// LastEvalTime - twelve checks and four pointer chases, 25 000 times a second.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool TryAnswer(BaseAI ai, Character target, out int row, out int t)
    {
        row = -1; t = -1;
        if (!Runtime.Live) return false;
        if (ReferenceEquals(target, null) || !target.IsPlayer()) return false;   // creature-vs-creature: vanilla
        int r = RowIndex.Row(ai); if (r < 0) return false;
        if (!ReferenceEquals(CreatureStore.Ai[r], ai)) return false;             // stale instance-ID guard
        if ((CreatureStore.Flags[r] & AnswerMask) != AnswerMask) return false;
        byte slot = RowIndex.PlayerSlotOf(target); if (slot == PlayerSlot.NoneValue) return false;
        int ti = TrackStore.IndexOfSlot(r, slot); if (ti < 0) return false;      // NO TRACK => NO OPINION
        if (Time.time - TrackStore.LastEval[ti] > Runtime.OpinionTtl) return false;  // 3 s
        row = r; t = ti; return true;
    }
}

// Patches/P_BaseAI_CanSeeTarget.cs
[HarmonyPatch(typeof(BaseAI), nameof(BaseAI.CanSeeTarget), new[] { typeof(Character) })]
internal static class P_BaseAI_CanSeeTarget
{
    [HarmonyPrefix, HarmonyPriority(Priority.Low)]
    private static bool Prefix(BaseAI __instance, Character target, ref bool __result, bool __runOriginal)
    {
        if (!__runOriginal) return false;
        if (!Gate.TryAnswer(__instance, target, out _, out int t)) return true;
        __result = (TrackStore.Flags[t] & (byte)TrackFlags.CanSee) != 0;
        return false;
    }
}
```

Two consequences worth stating plainly, both of which the arithmetic above depends on:

1. **A creature the scheduler has never reached behaves exactly like vanilla.** v1's worst hidden bug —
   creatures outside the local player's 64 m bubble being permanently blind *and* permanently pacified
   (`SURVEY-patches` §3.2) — is structurally impossible, because "no track" now routes to vanilla.
2. **`FindEnemy` becomes correct** (`AV:5191-5212`). It asks per candidate; we answer per candidate from that
   candidate's own track. Group stealth works because player A's track and player B's track are independent
   rows in `TrackStore`, integrated in the same pass.

Note the explicit `new Type[] { typeof(Character) }` on every target. v1's `Humanoid.StartAttack`,
`Character.Damage` and `Character.OnDamaged` had none; one added overload in Valheim 1.0 throws
`AmbiguousMatchException` out of `PatchAll` and, via the swallow-everything `catch` at
`ShadowsOfMidgard.cs:50`, silently deletes the mod while still logging "loaded successfully".

### 5.4 The creature-vs-creature early-out (`Perf.CreatureTargetEarlyOut`)

This is the throughput-lensed patch that no shippability-lensed design would propose, and it is the single
largest lever on "more monsters".

```csharp
// inside P_BaseAI_CanSenseTarget.Prefix, AFTER the player path has declined
if (!Perf.CreatureTargetEarlyOut) return true;
int ro = RowIndex.Row(__instance);  if (ro < 0) return true;
int rt = RowIndex.Row(target);      if (rt < 0) return true;
if (!ReferenceEquals(CreatureStore.Ai[ro], __instance) ||
    !ReferenceEquals(CreatureStore.Char[rt], target))    return true;

float now = Time.time;
float ageO = now - CreatureStore.PosStamp[ro], ageT = now - CreatureStore.PosStamp[rt];
if (ageO > Perf.PosStaleMax || ageT > Perf.PosStaleMax) return true;      // 1.0 s

float dx = CreatureStore.PosX[rt] - CreatureStore.PosX[ro];
float dy = CreatureStore.PosY[rt] - CreatureStore.PosY[ro];
float dz = CreatureStore.PosZ[rt] - CreatureStore.PosZ[ro];
float reach = Mathf.Max(__instance.m_hearRange, __instance.m_viewRange)
            + (ageO + ageT) * Perf.MaxCreatureSpeed;                       // 12 m/s, provably conservative
if (dx*dx + dy*dy + dz*dz > reach * reach) { __result = false; return false; }
return true;
```

**The equivalence argument, from the vanilla body.** `CanSenseTarget` returns `true` only if `CanHearTarget`
or `CanSeeTarget` does (`AV:4529-4544`). `CanHearTarget` returns `false` when `dist > hearRange`
(`AV:4562-4566`; `InInterior` only ever *lowers* the range to 12 m, `AV:10669` is a `y > 3000f` test).
`CanSeeTarget` returns `false` when `dist > viewRange` (`AV:4596-4600`), and again when
`dist > viewRange * stealthFactor` (`AV:4602-4606`) — and for a non-`Player` target `GetStealthFactor()` is the
base virtual returning exactly `1f` (`AV:10094`), so `viewRange` is the hard ceiling. Therefore **`dist > max(hearRange, viewRange)` ⇒ vanilla
answers `false`.** This is not an approximation; the only approximation is position staleness, and the
`(ageO + ageT) × MaxCreatureSpeed` margin bounds it by physics. Rows staler than `PosStaleMax` never
participate.

Default **on**, with `Perf.CreatureTargetEarlyOut` as a single local kill switch. §13 names this as the one
place I expect a bug report the pragmatic design cannot get.

### 5.5 `P_MonsterAI_UpdateAI` — apply-only, per-target, change-gated

```csharp
[HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateAI), new[] { typeof(float) })]
internal static class P_MonsterAI_UpdateAI
{
    [HarmonyPostfix, HarmonyPriority(Priority.Low)]
    private static void Postfix(MonsterAI __instance, float dt)
    {
        if (!Runtime.Live) return;
        int r = RowIndex.Row(__instance); if (r < 0) return;
        if (!ReferenceEquals(CreatureStore.Monster[r], __instance)) return;
        byte f = CreatureStore.Flags[r];
        if ((f & Gate.ApplyMask) != Gate.ApplyMask) return;
        if ((f & Gate.SleepBit) != 0) return;                          // v1 had NO sleep guard (AV:5961)
        if (Time.time - CreatureStore.LastPass[r] > Runtime.OpinionTtl) return;

        Behavior.Apply(r, dt);                                         // uses vanilla's own dt, 0.05 f
        AlertDriver.Apply(r, Time.time);
    }
}
```

`Behavior.Apply` resolves the target **by `ZDOID` out of the dominant track** — never `Player.m_localPlayer`.
v1 force-wrote `m_targetCreature = Player.m_localPlayer` on every owned creature at 20 Hz
(`MonsterAI_StealthBrain_UpdateAI_Patch.cs:25,38`), a permanent aggro magnet pointed at the host.
Every write is change-gated against `AppliedTarget[row]` / `AppliedAction[row]`, so the steady-state cost of a
creature already chasing the right player is two compares — and, importantly, so SoM stops churning
`SetTargetInfo` (`AV:5453`, a ZDO write) 20 times a second per creature for no change.

`Behavior.Apply`'s `Idle` branch only clears `m_targetCreature` when the current target *is* the player SoM
decided about, so it can never strip a legitimately-acquired remote-player target.

### 5.6 `AlertDriver` — the `IsAlerted` patch is deleted, not reimplemented

**`BaseAI.IsAlerted()` is not patched.** This single deletion repairs, with no further work: `Alert()` being a
no-op because it is guarded by the getter v1 forced (`AV:5327-5340`); `m_animator.SetBool("alert")`;
`ZDOVars.s_alert` replication to every other client (`AV:5358`, `AV:66414`); `m_alertedEffects`; boss
`activeBosses` counting; `m_alertedMessage`; the give-up/leash block (`AV:5937`); event-creature despawn
(`AV:5988`); `UpdateConsumeItem` and therefore taming (`AV:6045`); the permanent backstab exploit (`AV:8736`);
Sneak XP (`AV:5390`); and two clients seeing contradictory `EnemyHud` icons (`AV:38631-38636`).

```csharp
internal static class AlertDriver
{
    internal static void Apply(int r, float now)
    {
        if (!Cfg.DriveVanillaAlert) return;
        bool want = CreatureStore.AggState[r] >= (byte)VanillaAlertness.Alerted;
        bool have = CreatureStore.Ai[r].IsAlerted();                    // the REAL field
        if (want == have) { CreatureStore.WantsAlert[r] = want; return; }
        if (now - CreatureStore.LastAlertAt[r] < Cfg.AlertDebounce) return;   // 1.0 s, no animator churn

        if (want) CreatureStore.Ai[r].Alert();                          // public; handles owner + RPC (AV:5327)
        else
        {
            if (CreatureStore.AggDet[r] > Cfg.AlertedThreshold * 0.7f) return;
            VanillaBind.SetAlerted?.Invoke(CreatureStore.Ai[r], false);  // soft-bound (AV:5350); absent => leave vanilla
        }
        CreatureStore.LastAlertAt[r] = now;
        CreatureStore.WantsAlert[r] = want;
    }
}
```

If `SetAlerted` cannot be bound under Valheim 1.0, SoM loses only the ability to *lower* alertness early and
vanilla's own 30 s give-up handles it. Degraded, never broken. I reach the same conclusion as
`DESIGN-pragmatic` here and say so rather than manufacture a difference: `SURVEY-patches` §4 forces it.

### 5.7 Damage: one delegate per row, allocated once, forever

```csharp
internal static class DamageHook
{
    /// <summary>
    /// The closure captures ONLY the row index, which is stable for the lifetime of the array
    /// slot. So this delegate is allocated at most 1024 times in a session (once per row ever
    /// used) and is reused verbatim every time the row is recycled - zero steady-state allocation
    /// even under continuous spawn/despawn churn.
    /// </summary>
    internal static System.Action<float, Character> MakeForRow(int row) => (dmg, attacker) => OnHurt(row, dmg, attacker);

    internal static void Subscribe(int row)
    {
        var c = CreatureStore.Char[row];
        // Exactly what BaseAI.Awake itself does at AV:4028. m_onDamaged is a PUBLIC field
        // (AV:6875) invoked unconditionally on the OWNER from Character.ApplyDamage (AV:8871-8874)
        // for every Character subtype - so it works for Humanoids, which Character.OnDamaged
        // does not (Humanoid overrides without chaining, AV:13249).
        c.m_onDamaged = (System.Action<float, Character>)System.Delegate.Combine(
            c.m_onDamaged, CreatureStore.OnDamaged[row]);
    }

    internal static void Unsubscribe(int row)
    {
        var c = CreatureStore.Char[row]; if (c == null) return;
        c.m_onDamaged = (System.Action<float, Character>)System.Delegate.Remove(c.m_onDamaged,
            CreatureStore.OnDamaged[row]);
    }

    private static void OnHurt(int row, float dmg, Character attacker)
    {
        byte f = CreatureStore.Flags[row];
        if ((f & (byte)(RowFlags.Owned | RowFlags.Simulated)) != (byte)(RowFlags.Owned | RowFlags.Simulated)) return;
        if (attacker == null || !attacker.IsPlayer()) return;
        byte slot = RowIndex.PlayerSlotOf(attacker); if (slot == PlayerSlot.NoneValue) return;
        float now = Time.time;
        int t = TrackStore.Open(row, slot, attacker.GetZDOID(), now);
        float floor = Cfg.DamageDetectionFloor;                        // CONFIGURED, not a magic 0.5/0.8
        if (TrackStore.Det[t] < floor) TrackStore.Det[t] = floor;
        byte want = TrackStore.Det[t] >= Cfg.EngagedThreshold ? (byte)VanillaAlertness.Engaged
                                                              : (byte)VanillaAlertness.Alerted;
        if (TrackStore.State[t] < want) TrackStore.State[t] = want;    // NEVER a downgrade (v1 downgraded)
        TrackStore.Lkp[t] = attacker.transform.position;
        TrackStore.SinceSensed[t] = 0f; TrackStore.LastEval[t] = now;
        TrackStore.Flags[t] |= (byte)(TrackFlags.EverSensed | TrackFlags.Damaged);
        CreatureStore.Flags[row] |= (byte)RowFlags.HasOpinion;
    }
}
```

Four v1 defects die at once: wrong network side (`Character.Damage` is only an RPC *sender*, `AV:8692`);
`Humanoid.OnDamaged` not chaining (`AV:13249`) so the hook never fired for greydwarves, draugr, fulings,
skeletons, dvergr or trolls; the unconditional `Engaged → Alerted` downgrade caused by `OnDamaged`'s postfix
running *inside* `Damage`'s postfix on the synchronous local-RPC path (`AV:71198-71201`); and the missing
exemption check on both damage patches.

---

## 6. Replicated ZDO state and write discipline

### 6.1 One key, one revision bump, all tracks

`ZDO` has **no** `Set(int hash, ZDOID)` overload and **no** `GetZDOID(int)`. The only integer-keyed ZDOID
storage is `Set(KeyValuePair<int,int>, ZDOID)` (`AV:62390`), which writes the ZDOID as **two separate fields**
(a `long` user id and a `uint` id). `DESIGN-pragmatic`'s `zdo.Set(SoMKeys.DetTgt, st.AggTarget)` does not
compile as written, and once corrected it costs 2 of its 5 ZDO fields.

`ZDO.Set(int hash, byte[])` (`AV:62521`) and `GetByteArray(int, byte[])` (`AV:62788`) do exist. So:

| Key | Type | Writer | Cadence | Contents |
|---|---|---|---|---|
| `SoM_T` | `byte[]` | **owner only** | ≤ 2 Hz, delta-gated | The whole track summary: dominant track with full identity, plus every other track hashed. |
| `SoM_Ack` | `int` | owner only | once per creature, read-compare guarded | Interop verification (§9.3). Written only for creatures carrying at least one `SoM_*` or legacy key. |

Vanilla's `ZDOVars.s_alert` (`AV:66414`) and `s_haveTargetHash` (`AV:66502`) are written **by vanilla again**,
because `AlertDriver` drives the real setter and `Behavior` calls the real `SetTargetInfo`. SoM writes neither
directly. In v1 `s_alert` was frozen at `false` forever for every remote client.

### 6.2 The blob

```csharp
// Net/TrackBlob.cs
internal static class TrackBlob
{
    internal const byte Version = 2;
    // header 4 + dominant 20 + 5 per extra track. K=10 -> 69 B max; 3 tracks -> 34 B typical.
    private static readonly byte[] _scratch = new byte[4 + 20 + 5 * (TrackStore.K - 1)];

    /// <summary>
    /// Layout:
    ///   [0]      version
    ///   [1]      trackCount (0..K)
    ///   [2]      flags: bit0 = dominant present, bit1 = LKP present
    ///   [3]      reserved
    ///   [4..9]   dominant player ZDOID: ushort UserKey + uint ID  (AV:64584/64586 - the struct is
    ///            6 bytes, Pack=2; we write the two components explicitly rather than trusting layout)
    ///   [10]     dominant: state in bits 0-1, TrackFlags.EverSensed in bit 2
    ///   [11]     dominant: detection quantised to 1/255
    ///   [12..23] dominant LKP (3 floats), present only if flags bit1
    ///   then 5 bytes per remaining track:
    ///     int32 playerIdHash   (mix of UserKey+ID; used only for HUD attribution)
    ///     byte  state&lt;&lt;6 | det quantised to 1/63
    /// </summary>
    internal static int Pack(int row, byte[] into) { /* ... */ return 0; }
    internal static void Unpack(int row, byte[] blob, float now) { /* opens tracks; unmatched hashes dropped */ }
}
```

Why the dominant track gets a full 6-byte `ZDOID` and the rest get a 4-byte hash: only the dominant track
drives *behaviour* after a handover (who the creature is chasing, where it last saw them), so it must be
resolvable exactly. The remainder exist so a non-owning client's HUD can answer "is any of this about **me**",
which a hash match answers with a collision probability of ~3 × 10⁻⁸ across ≤16 players.

### 6.3 Write discipline

```csharp
internal static class Replication
{
    internal static void Write(int row, float now)
    {
        if ((CreatureStore.Flags[row] & (byte)RowFlags.Owned) == 0) return;   // OWNER ONLY. Always.
        if (!Cfg.ReplicateDetection) return;
        if (now - CreatureStore.ReplAt[row] < Cfg.ReplicationPeriod) return;  // 0.5 s floor

        byte  s = CreatureStore.AggState[row];
        float l = CreatureStore.AggDet[row];
        ZDOID tgt = CreatureStore.AggTrack[row] == 0xFF
                  ? ZDOID.None : TrackStore.Pid[row * TrackStore.K + CreatureStore.AggTrack[row]];
        if (s == CreatureStore.ReplState[row] &&
            Mathf.Abs(l - CreatureStore.ReplDet[row]) < Cfg.ReplicationDelta &&   // 0.1
            tgt == CreatureStore.ReplTarget[row]) return;                          // nothing changed

        var nv = CreatureStore.NView[row]; if (nv == null || !nv.IsValid()) return;
        var zdo = nv.GetZDO(); if (zdo == null) return;
        int len = TrackBlob.Pack(row, _scratch);
        var bytes = new byte[len];                     // see §7.4 on why this one allocation exists
        System.Buffer.BlockCopy(_scratch, 0, bytes, 0, len);
        zdo.Set(SoMKeys.TrackBlobHash, bytes);         // ONE field, ONE DataRevision bump (AV:62521)

        CreatureStore.ReplState[row] = s; CreatureStore.ReplDet[row] = l;
        CreatureStore.ReplTarget[row] = tgt; CreatureStore.ReplAt[row] = now;
    }
}
```

Three invariants:

1. **Owner-only for mutable state.** `SURVEY-vanilla-api` §3.18 proves that a non-owner write *does* propagate
   (`CreateSyncList` pushes `m_clientChangeQueue` regardless of ownership, `AV:65631-65642`) and is accepted
   last-writer-by-revision (`AV:65533`). That is why a third party's one-shot `SoMStealthExempt` stamp works —
   and exactly why brain state must never be written by anyone but the owner, or two peers ping-pong.
2. **Idempotent set, never read-modify-write.** Every value is an absolute assignment computed from local state.
   `SURVEY-vanilla-api` §3.17: a replicated field accumulated on more than one peer compounds.
3. **Delta-gated.** `ZDO.Set` calls `IncreaseDataRevision()` (`AV:62635`) and inserts into the `ZDOExtraData`
   side-table; both are real costs and both feed the wire. One field, not five.

### 6.4 Handover seeding

```csharp
// P0_Sweep, on a row whose Owned bit transitioned false -> true, or a brand-new row that already has a blob
internal static void SeedFromZdo(int row, float now)
{
    var zdo = CreatureStore.NView[row].GetZDO(); if (zdo == null) return;
    byte[] blob = zdo.GetByteArray(SoMKeys.TrackBlobHash, null);      // AV:62788
    if (blob == null || blob.Length < 4 || blob[0] != TrackBlob.Version) return;
    TrackBlob.Unpack(row, blob, now);
}
```

`ZDOMan.ReleaseNearbyZDOS` reassigns ownership every ~2 s as players move
(`VALHEIM-DEDICATED-SERVER-FACTS.md` L24), and in v1 every handover reset detection to zero because
`AwarenessData` lived only in a client-local `Dictionary<Character, AwarenessData>`. Because the blob carries
**all** tracks and not just the dominant one, group stealth survives handover too: a creature that had
independent tracks on players A, B and C keeps all three, so it does not suddenly notice the sneaking player
the instant ownership moves.

---

## 7. Zero steady-state allocation — the accounting

| Allocation source in v1 | v2 |
|---|---|
| `new StealthDecision()` per `Evaluate`, incl. on a cache **hit** (`StealthBrain.cs:53,73`) | Decision is a set of columns on the row. **Gone.** |
| `new EvaluatedCharacter{}` per tracked creature per frame (`StealthOvermind.cs:78`) | Rosters are `int[]`. **Gone.** |
| Unconditional `$"..."` in `StealthBrain:270`, `VisibilitySystem:40`, `NoiseSystem:21`, `HidingSystem:34`, `CamoSystem:55`, `BehaviorSystem:209,215` — ~46 allocations per evaluation, ≈ 700 KB/s, ~65 % of v1's total GC | Every call site is `if (Log.X) { … }`. On `net472` there is no `DefaultInterpolatedStringHandler`, so `$"…"` lowers to `string.Format(string, object[])` with boxing; the guard is the only fix. **Gone.** |
| `GetEquippedItems()` × 3 per evaluation (`AV:57488` allocates a `List` and walks the inventory) | `EquipCache`, invalidated by `Inventory.m_onChanged` (`AV:56894`). **Gone.** |
| 9–10 `ToLowerInvariant()` per evaluation | Computed once per player per 2 Hz tier, cached in `PlayerStore`. **Gone.** |
| `Physics.OverlapSphere` (allocating overload) in `CallNearbyAllies` (`BehaviorSystem.cs:180`) + `List<string>` + `Object.name` marshal per ally + `string.Join` | `GroupKernel` reads `CellGrid`. **No physics, no strings, no allocation.** |
| `new List<Character>(AwarenessSystem.GetAllTrackedCharacters())` per outer lap (`CoroutineManager.cs:95`) | Coroutine deleted. **Gone.** |
| `MigrateCacheCoroutine` iterator + 2 `WaitUntil` + 2 closures on **every** evaluation while `NetworkUid == 0` (`StealthBrain.cs:127-130`) | Cache and coroutine both deleted. **Gone.** |
| `CleanupStaleCache`'s `new List<StableAIKey>()` per sweep | Free list. **Gone.** |
| Boxed dictionary enumerator + O(all-creatures) walk per frame in the HUD (`StealthUIController.cs:69-83`) | One published `ThreatQuery` struct. **Gone.** |
| `colBuffer[i].name.ToLowerInvariant()` × 2 per collider at 12 Hz ≈ 240 strings/s (`CoroutineManager.cs:75`) | Compares against a precomputed `int[]` of `GetInstanceID()`-keyed name hashes; and the sampler auto-disarms. **Gone.** |

**Remaining steady-state allocation: exactly one site.** `ZDO.Set(int, byte[])` stores the array reference into
`ZDOExtraData`, so it cannot take a pooled buffer. At S-worst with all 200 creatures changing state, that is
400 writes/s × ~34 B = **13.6 KB/s**; at a realistic ~60 creatures/s actually crossing the delta gate it is
**≈ 1.4 KB/s** — roughly one Gen0 collection every eight minutes attributable to SoM, against v1's ~one per
second. *Implementation note:* if `ZDOExtraData.Set` turns out to copy rather than retain, the buffer becomes
poolable and the figure goes to zero; verify at implementation time rather than assuming either way.

Non-steady-state, bounded, and deliberate:
- `CreatureStore.OnDamaged[row]` — one delegate + closure per **row**, ≤ 1024 for the session.
- Column growth — `Array.Resize` on doubling; 256 → 512 → 1024 is at most two events per session.
- `NativeArray` × 2, `Allocator.Persistent`, disposed in `OnDestroy`. **Unmanaged — invisible to the GC.**

---

## 8. Soft binding and v1.0 resilience

### 8.1 `Bind/VanillaBind.cs` — resolved once, in `Awake`, never from a static constructor

```csharp
internal static class VanillaBind
{
    // Harmony targets, probed BEFORE any patch is applied.
    internal static MethodBase CanSeeTarget, CanHearTarget, CanSenseTarget, MonsterUpdateAI,
                               SetMoveDir, StartAttack, BaseAiAwake, CharacterOnDestroy, PlayerOnSpawned;

    // Called members, each with a documented fallback ladder.
    internal static Action<BaseAI, bool>  SetAlerted;      // fallback: none; Alert() covers the "true" direction
    internal static Action<BaseAI, ZDOID> SetTargetInfo;   // fallback: zdo.Set(s_haveTargetHash, hasTarget)
    internal static Func<BaseAI, float, Vector3, float, bool, bool> MoveTo;   // fallback: MoveTowards -> SetMoveDir
    internal static Action<BaseAI, Vector3, bool> MoveTowards;                // public, near-certain (AV:4672)

    // Declaring type is MonsterAI (AV:5727), NOT BaseAI. v1 bound it on BaseAI, AccessTools does not
    // search DERIVED types, the MissingFieldException was swallowed, _targetCreatureRef stayed null
    // forever, and CallNearbyAllies never assigned a target to anything (SURVEY-vanilla-api §4.1).
    internal static AccessTools.FieldRef<MonsterAI, Character> TargetCreature;

    internal static Func<Character, float> StealthFactor;  // AV:21842; fallback: constant 1f
    internal static readonly List<string> Missing = new List<string>();
    internal static void Resolve() { /* probe each; one consolidated capability log line */ }
}
```

### 8.2 `Bind/UnityBind.cs` — the fast path must be optional

```csharp
internal static class UnityBind
{
    internal static bool HasQueryParams, HasRaycastCommand, HasGetPosRot;
    private static QueryParameters _qp;

    internal static void Resolve()
    {
        // Both RaycastCommand ctors exist in the shipped PhysicsModule (verified by reflection):
        //   (Vector3 from, Vector3 direction, QueryParameters queryParameters, float distance)
        //   (Vector3 from, Vector3 direction, float distance, int layerMask, int maxHits)   [legacy]
        // Prefer the first; fall back to the second; fall back to synchronous Physics.Linecast.
        HasRaycastCommand = AccessTools.TypeByName("UnityEngine.RaycastCommand") != null
                         && AccessTools.TypeByName("Unity.Jobs.JobHandle")       != null;
        HasQueryParams    = AccessTools.TypeByName("UnityEngine.QueryParameters") != null;
        HasGetPosRot      = AccessTools.Method(typeof(Transform), "GetPositionAndRotation",
                                new[] { typeof(Vector3).MakeByRefType(), typeof(Quaternion).MakeByRefType() }) != null;
        if (HasQueryParams)
            _qp = new QueryParameters(LayerMasks.ViewBlock, false, QueryTriggerInteraction.Ignore, false);
        Perf.BatchedLos = HasRaycastCommand && Perf.BatchedLosRequested;
        Log.Capabilities();
    }

    internal static RaycastCommand MakeRaycast(Vector3 from, Vector3 dir, float dist, int mask)
        => HasQueryParams ? new RaycastCommand(from, dir, _qp, dist)
                          : new RaycastCommand(from, dir, dist, mask, 1);
}
```

### 8.3 `Bind/PatchInstaller.cs` — `PatchAll` is never called

```csharp
internal static class PatchInstaller
{
    internal static readonly List<string> Installed = new List<string>(), Skipped = new List<string>();

    internal static void InstallAll(Harmony h)
    {
        Try(h, typeof(P_BaseAI_CanSeeTarget),   VanillaBind.CanSeeTarget);
        Try(h, typeof(P_BaseAI_CanHearTarget),  VanillaBind.CanHearTarget);
        Try(h, typeof(P_BaseAI_CanSenseTarget), VanillaBind.CanSenseTarget);
        Try(h, typeof(P_MonsterAI_UpdateAI),    VanillaBind.MonsterUpdateAI);
        Try(h, typeof(P_Character_SetMoveDir),  VanillaBind.SetMoveDir);
        Try(h, typeof(P_Humanoid_StartAttack),  VanillaBind.StartAttack);
        Try(h, typeof(P_BaseAI_Awake),          VanillaBind.BaseAiAwake);        // optional
        Try(h, typeof(P_Character_OnDestroy),   VanillaBind.CharacterOnDestroy); // optional
        Try(h, typeof(P_Player_OnSpawned),      VanillaBind.PlayerOnSpawned);    // optional

        // Without per-target sensing there is no mod. Degrade to fully inert, loudly.
        if (!Installed.Contains(nameof(P_BaseAI_CanSeeTarget)) &&
            !Installed.Contains(nameof(P_BaseAI_CanSenseTarget)))
        { Runtime.Live = false; Log.Error("core sensing patches unavailable - SoM is inert this session."); }
    }

    private static void Try(Harmony h, Type cls, MethodBase target)
    {
        if (target == null) { Skipped.Add(cls.Name); Log.Warn($"skip {cls.Name}: target not found"); return; }
        try { h.CreateClassProcessor(cls).Patch(); Installed.Add(cls.Name); }
        catch (Exception e) { Skipped.Add(cls.Name); Log.Warn($"skip {cls.Name}: {e.Message}"); }
    }
}
```

### 8.4 The degradation ladder, in full

| Missing under Valheim 1.0 | Effect |
|---|---|
| `RaycastCommand` / `JobHandle` | `Perf.BatchedLos = false`; synchronous `Physics.Linecast` with a per-frame quota. Costs main-thread time, changes nothing else. |
| `QueryParameters` | Legacy `RaycastCommand` ctor. Identical semantics. |
| `Transform.GetPositionAndRotation` | Two separate interop reads. +20 ns per pass. |
| `BaseAI.SetAlerted` | Cannot lower alertness early; vanilla's 30 s give-up handles it. |
| `BaseAI.MoveTo` | `MoveTowards` → `SetMoveDir`. Three-level ladder, every level publicly reachable (`AV:4672`, `AV:9503`). |
| `BaseAI.SetTargetInfo` | Write `ZDOVars.s_haveTargetHash` directly; the observable effect (`HaveTarget()`, `AV:5458`) is what matters. |
| `MonsterAI.m_targetCreature` | Read through `GetTargetCreature()` (`AV:6401`, a public virtual — the authors' own abstraction seam); writing degrades to no-op with one warning. |
| `BaseAI.Awake` / `Character.OnDestroy` / `Player.OnSpawned` | Registration latency rises from ~0 to 0.5 s. **Nothing else changes.** The sweep is the authority. |
| `BaseAI.BaseAIInstances` | Ladder: `BaseAIInstances` (`AV:4011`) → `GetAllInstances()` (`AV:5476`) → `Character.GetAllCharacters()` (`AV:10316`) filtered by `GetBaseAI()`. |
| `EnvSetup.m_isWet` (`AV:81641`) | v1 name matching. Note `"ThunderStorm"` (`AV:81796`) contains neither `"rain"` nor `"snow"`, so this fallback is *worse*, and it says so in the log. |
| `Heightmap.FindBiome` (`AV:111647`) | `FindHeightmap` + `GetBiome`, accepting the optional-parameter binary-compat risk, with a final fallback of `Biome.None` (camo contributes 0). |
| Any `LayerMask.GetMask` name | `GetMask` returns `0` **silently**, which degrades to "nothing blocks sight". `LayerMasks` validates for non-zero, logs an error, falls back to the `BaseAI.m_viewBlockMask` literal (`AV:4024`) and then to `Physics.DefaultRaycastLayers`. |
| Config binding throws | `Runtime.Live = false`. **The failure floor is vanilla behaviour.** v1's `VisibilitySystem.GetVisibility` returned `1f` on a null config, so a config failure meant *every creature sees you perfectly, forever, silently*. |

`Headless.NoGraphics = SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null`. **Never `ZNet.IsDedicated()`** —
it is hardcoded `false` in the reference assembly SoM compiles against (`AV:68818`). On a dedicated server there
are no creature instances near players at all, so `Runtime.Live` is set `false` by default there and the whole
pipeline never starts.

---

## 9. Cross-mod compat surface

### 9.1 Design for the class, not for the sibling

**Correction to the record, verified in `c:\WubarrkCODING\MistsofAvalor` at the time of writing.** MoA is now
0.1.2. `Compat\AvalorStealthGuards.cs` no longer exists; there is no `HarmonyBefore`/`HarmonyAfter` anywhere in
the tree; there is no Harmony patch on `BaseAI.IsAlerted`, `BaseAI.CanSenseTarget`, `Humanoid.StartAttack` or
`MonsterAI.UpdateTarget`; and `AwarenessData` appears exactly once, in a past-tense comment describing the
removed mechanism. MoA 0.1.2 does what DvergrAllies does: stamps `SoMStealthExempt = 1`, owner-gated,
re-asserted on a timer, and probes for the `StealthExemption` **type** only to decide what to log.

**Consequence for `DESIGN-pragmatic`.** Its `Compat/ExternalPin.cs` — which that document calls *"the single
highest-leverage compat decision in v2"* — exists to read third-party writes into `AwarenessData` back as
authoritative input. Nothing writes into `AwarenessData` any more. As specified, `ExternalPin.Poll` runs at the
**start of every creature pass** and compares three shadow fields, and `ExternalPin.Publish` runs at the **end
of every creature pass** and stores eleven. At S-worst that is 2 000 × 14 field operations per second, plus a
live `AwarenessData` instance per creature that the GC must trace, in service of a consumer that does not
exist. Its §5.7 coexistence table likewise enumerates MoA prefixes that no longer ship.

### 9.2 What this design does with the pin concept: demand-driven, pull not push

I do **not** delete the capability — an old MoA build is in the wild, and "arbitrary third-party mod" is the
real requirement. I make it cost nothing when nobody is using it.

```csharp
// Api/LegacyView.cs
internal static class LegacyView
{
    /// <summary>
    /// A row becomes "observed" the first time anything calls AwarenessSystem.GetData for it.
    /// Only observed rows allocate an AwarenessData, only observed rows are polled for external
    /// writes, and the view is refreshed ON READ rather than pushed every pass.
    ///
    /// Three properties follow:
    ///   1. Zero cost for the ~100% of creatures nobody reflects into.
    ///   2. The view is never stale, because it is materialised at the moment of the read -
    ///      a push model is stale by up to one pass period (0.1 s hot, 4 s dormant).
    ///   3. No shadow-field comparison in the hot path. Ever.
    /// </summary>
    internal static void RefreshOnRead(int row, AwarenessData d)
    {
        if (d._stamp == Registry.PassStamp[row]) return;      // already fresh this pass: 1 compare
        Poll(row, d);                                          // detect an external write since our last publish
        int at = CreatureStore.AggTrack[row];
        d.CurrentState      = (VanillaAlertness)CreatureStore.AggState[row];
        d.DetectionLevel    = CreatureStore.AggDet[row];
        d.CurrentFleeReason = (FleeReason)CreatureStore.FleeReason[row];
        d.SelfHealthPercent = CreatureStore.SelfHealth[row];
        d.NearbyAlliesCount = CreatureStore.AllyCount[row];
        d.CurrentAction     = (AIBehaviorAction)CreatureStore.Action[row];
        if (at != 0xFF)
        {
            int t = row * TrackStore.K + at;
            byte f = TrackStore.Flags[t];
            d.CanSee   = (f & (byte)TrackFlags.CanSee)  != 0;
            d.CanHear  = (f & (byte)TrackFlags.CanHear) != 0;
            d.CanSense = d.CanSee || d.CanHear;
            d.LastKnownPosition = TrackStore.Lkp[t];
            d.TimeSinceSeen     = TrackStore.SinceSensed[t];
        }
        else { d.CanSee = d.CanHear = d.CanSense = false; }
        d._stamp = Registry.PassStamp[row];
        Shadow.Stamp(row, d);            // remember what WE wrote, so the next Poll can spot a foreign write
    }

    /// <summary>Runs only for observed rows, and only when one is read or passed. If a third party
    /// raised CurrentState or DetectionLevel since our last publish, latch it as a floor on EVERY
    /// track for ExternalPinTtl (default 2 s) - documented semantics: a pin is per-creature.</summary>
    private static void Poll(int row, AwarenessData d) { /* 3 compares, observed rows only */ }
}
```

`Registry.MarkObserved(row)` also logs once, at INFO, naming the calling assembly via a one-time
`new StackTrace(1, false).GetFrame(0)` walk. If a third-party mod ever starts writing into `AwarenessData`
again, the server log says so on the first creature, rather than the behaviour silently changing.

### 9.3 Frozen surface

```csharp
namespace ShadowsOfMidgard
{
    public static class StealthExemption
    {
        public const string ZDOKey = "SoMStealthExempt";
        public static bool IsExempt(Character c);   // == "the legacy key is exactly 1". NOT "the brain is off".
        public static bool IsExempt(BaseAI ai);
    }
    public class AwarenessData { /* §2.9 - class, v1 field names AND types */ }
    public static class AwarenessSystem { public static AwarenessData GetData(Character c); /* … */ }
    public enum VanillaAlertness { Unaware = 0, Suspicious = 1, Alerted = 2, Engaged = 3 }
    public enum FleeReason { None = 0, HealthCritical, StrategicRetreat, Overwhelming, Grouping }
    public enum AIBehaviorAction { Idle, Patrol, Search, Pursue, Flee, Attack, CallForHelp, Investigate, StrategicRetreat }
}
```

Invariants: type names verbatim; `IsExempt` keeps the strict `zdo.GetInt(ZDOKey, 0) == 1` reading, now via the
`int`-hash overload (`AV:62718`) with the hash precomputed in `SoMKeys` — v1 re-hashed the 16-character string
at seven patch sites per creature per tick (`AV:62713`); `AwarenessData` stays a **public class** with v1 field
names *and types*; `AwarenessSystem.GetData(Character)` stays static, single-parameter, and never returns null,
and is now null-**safe** on null input (v1 threw `ArgumentNullException` out of `Dictionary.TryGetValue(null)`
from public API). `AggressionLevel` is *added*, closing MoA 0.0.9's dead probe.

### 9.4 Additive surface and the tiered ZDO contract

```csharp
public static class SoMInterop
{
    public const int ContractVersion = 2;
    public static int      GetContractVersion();
    public static string[] SupportedKeys();
    public static string[] SupportedProfiles();
    public static string[] RetiredKeys();
    public static bool     Supports(string keyOrProfile);
    public static string[] Capabilities();          // which soft bindings resolved: "los.batched", "alert.set", …
    public static bool     IsBrainOff(Character c); // the RESOLVED question
    public static string   Describe(Character c);
    public static string   DescribeTrack(Character c, Player p);   // null-safe p
}
```

Tier-2 keys, precedence and profiles are exactly as specified in `SURVEY-interop.md` §5.2–5.5 and I adopt them
unchanged (`SoM_Contract`, `SoM_BrainOff`, `SoM_AlertFloor`, `SoM_DetectFloor`, `SoM_SightRange`,
`SoM_HearRange`, `SoM_ConeHalf`, `SoM_IgnoreLoS`, `SoM_NoFlee`, `SoM_GiveUpMul`, `SoM_Profile`, `SoM_Ack`);
there is no throughput reason to differ and the survey's precedence argument (a consumer writes both the legacy
flag and the v2 keys unconditionally, forever, with zero conditions on its side) is correct.

**The throughput-relevant part is how they are resolved.** `Directives` is a struct in a column, resolved once
and cached against `ZDO.DataRevision` (`AV:62322`, a public `uint` auto-property):

```csharp
// P2_Gather, per pass
uint rev = zdo.DataRevision;
if (rev != CreatureStore.DirRevision[row])
{
    CreatureStore.Dir[row] = DirectiveResolver.Resolve(zdo);   // 2 hashed ZDO reads on the cold path
    CreatureStore.DirRevision[row] = rev;
    CreatureStore.Flags[row] = Flagger.Recompute(row);         // BrainOff feeds RowFlags.Simulated
}
```

Steady-state cost: **one property read and one compare per pass** (4 ns). A real re-resolve reads
`SoM_Contract` and `SoMStealthExempt`; if both are default, it short-circuits to config defaults and reads
nothing else. v1 called `IsExempt` — `GetComponent<ZNetView>()` + a 16-char `GetStableHashCode()` +
a `ZDOExtraData` lookup ≈ 230 ns — at seven patch sites, per creature, per 20 Hz tick.

`SoM_Ack` is written once per creature, read-compare guarded, and only for creatures that carry at least one
interop key — so it costs nothing for the ~100 % of creatures that carry none.

**The one-resolver invariant holds:** `DirectiveResolver.Resolve` is the only implementation, the patches gate
on the struct it produces, and `Describe` renders that same struct. There is no second code path that could
describe a policy the brain does not apply.

### 9.5 DvergrAllies

Zero changes. It writes `SoMStealthExempt` = `1`/`0`, owner-gated. `DirectiveResolver` keeps `== 1` as the
exemption test, so an explicit `0` is not-exempt, exactly as intended for wild Dvergr.

---

## 10. Config and ServerSync

### 10.1 The split that matters here: gameplay is synced, throughput is not

| | Synced (server authoritative) | Local (per client) |
|---|---|---|
| What | Everything that changes an *answer*: all four sensing formulas' coefficients, thresholds, ranges, cone angle, gains, decay, `MaxEvalTimestep`, `TrackIdleTtl`, `TrackCreateMargin`, `DamageDetectionFloor`, `HardBlockAttacks`, `DriveVanillaAlert`, `AlertDebounce`, `ReplicateDetection`, `ReplicationDelta`, `ExternalPinTtl`, `IncludeAnimalAI` | Everything that changes only *when* or *how fast*: `TierPeriod[4]`, `TierBudgetNs[4]`, `LosRecheckPeriod`, `LosPerEpoch`, `LosEpochFrames`, `MinCommandsPerJob`, `BatchedLosRequested`, `ProfilePeriod`, `SweepPeriod`, `VegPeriod`, `PosStaleMax`, `MaxCreatureSpeed`, `CreatureTargetEarlyOut`, `StoreCapacity`, `CellSize`, `OpinionTtl`, `DisableOnHeadless`, all `UI/*`, all `Debug/*`, `Compat/AssertFinalSay` |
| Why | Two peers must agree on whether a creature can see you | A 4-core laptop and a 16-core desktop should be allowed to disagree about stripe sizes |

**The load-bearing rule: no local knob may change a gameplay answer.** `PerfConfig` is a separate class from
`StealthConfigModel` precisely so this is enforceable by inspection. Two consequences fall out:
`CreatureTargetEarlyOut` is only permitted to be local because §5.4 proves it is answer-identical; and any
"evaluation range" clamp may only ever *lower* a synced range, never raise it.

Section names, key names and defaults for every synced entry are **v1-verbatim**, so existing
`wubarrk.shadowsofmidgard.cfg` files load unchanged. New entries only.

### 10.2 `Config/Binder.cs` and the four v1 defects it closes

```csharp
internal static class Binder
{
    internal static ConfigEntry<float> F(ConfigFile file, ConfigSync sync, string section, string key,
                                         float def, float min, float max, string desc, Action<float> apply)
    {
        var e = file.Bind(section, key, def, new ConfigDescription(desc, new AcceptableValueRange<float>(min, max)));
        if (sync != null) sync.AddConfigEntry(e);      // sync == null is the EXPLICIT, greppable "local only"
        void Push() { apply(Sanitize(e.Value, min, max)); Tuning.Rebuild(); }
        e.SettingChanged += (_, __) => Push();
        Push();                                        // initial value: structurally impossible to forget
        return e;
    }
    private static float Sanitize(float v, float mn, float mx)
        => (float.IsNaN(v) || float.IsInfinity(v)) ? mn : Mathf.Clamp(v, mn, mx);
}
```

`Tuning.Rebuild()` on every push is what makes §2.7 safe: derived constants can never drift from the entries.

```csharp
private static readonly ConfigSync Sync = new ConfigSync(ShadowsOfMidgard.ModGUID)
{
    DisplayName            = ShadowsOfMidgard.ModName,
    CurrentVersion         = ShadowsOfMidgard.ModVersion,   // == SoMBuild.Version, generated from version.txt
    MinimumRequiredVersion = "2.0.0",                       // HAND-BUMPED ONLY - the csproj ticks the patch
                                                            // component every build and would otherwise
                                                            // hard-kick every client on every rebuild
    ModRequired            = true,                          // the owning client simulates; a vanilla client
                                                            // owning a mob runs vanilla perception on it
};

public static void Init(ConfigFile file)
{
    Active = new StealthConfigModel();                       // defaults land BEFORE anything can throw
    Perf   = new PerfConfig();
    ServerConfigLocked = file.Bind("0 - General", "Lock Configuration", true, new ConfigDescription(
        "If on, the server's values overwrite every client's and only admins may change them."));
    Sync.AddLockingConfigEntry(ServerConfigLocked);           // BEFORE any synced entry, so ReadOnly is
                                                             // correct on ConfigurationManager's first render
    BindLocal(file);                                         // sync: null
    BindSynced(file, Active);                                // ~45 Binder one-liners, each group try/caught
    CreatureProfiles = new CustomSyncedValue<string>(Sync, "creatureprofiles", "", priority: 10);
    CreatureProfiles.ValueChanged += CreatureProfileConfig.Reload;
}
```

Four v1 defects closed, all from `SURVEY-ui-config` §7: no locking entry (so `IsLocked` was permanently
`false`, the admin gate was dead, and **any non-admin client editing its own TOML broadcast that value to every
player** — a client could set `MaxVisualRange = 5` server-wide); `ModRequired = false` (so the client skipped
sending its version and a vanilla client could join); three disagreeing version numbers; and no failure floor.

### 10.3 The adaptive knob, and its limits

`Stripe.Record` maintains an EWMA of per-pass cost per tier and logs, once per 30 s, whenever a tier's measured
round-trip exceeds 2 × its configured period. It does **not** silently retune gameplay. The only automatic
adaptation in the design is `VegetationSampler` disarming after `N` consecutive all-zero samples
(`SURVEY-sensing` §12.5: Valheim's ground clutter is instanced through `ClutterSystem`, `AV:102873`, and has no
colliders, so `PlayerGrassHitCount` is expected to be ~always 0 in practice) — and that logs once when it does.

---

## 11. What is deleted, and what is deliberately not rewritten

### Deleted

| Deleted | Why |
|---|---|
| **`BaseAI_StealthBrain_IsAlerted_Patch`** (prefix + postfix) | Neutered `Alert()` (guarded by the getter it forced, `AV:5336`), froze `ZDOVars.s_alert` at `false` for every remote client, killed the alert animator bool and the aggro roar, broke boss counting and alert messages, broke the give-up/leash block (`AV:5937`), made event creatures immortal after raids (`AV:5988`), stalled taming via `UpdateConsumeItem` (`AV:6045`), gifted a permanent backstab exploit (`AV:8736`), corrupted Sneak XP (`AV:5390`), and made two clients see contradictory `EnemyHud` icons (`AV:38631`). Replaced by `AlertDriver` calling the real setter. |
| **`Character_Damage_Patch`** | Postfixes the RPC *sender* (`AV:8692`), which runs on the attacker's machine, then requires ownership — so the awareness bump was lost in every real multiplayer hit. Also demoted `Engaged → Alerted` unconditionally and ignored `StealthExemption` entirely. |
| **`Character_OnDamaged_Patch`** | `Humanoid.OnDamaged(HitData)` overrides without chaining (`AV:13249`), so it never fired for greydwarves, draugr, fulings, skeletons, dvergr or trolls. Also ignored exemption. |
| Both replaced by **`Character.m_onDamaged`** (`AV:6875`) | Public field, invoked unconditionally on the owner for every `Character` subtype (`AV:8871`), and carries the attacker. It is what `BaseAI.Awake` itself subscribes to (`AV:4028`). |
| **All six sensing postfixes** | Re-ran the identical 4-`GetComponent` guard chain to re-assert the identical value the prefix already wrote. ~50 % of the patch layer's CPU and an unwinnable sort-order race. |
| **`_harmony.PatchAll(...)`** | All-or-nothing inside a swallow-everything `try`. One unresolvable target deletes every patch after it while still logging "loaded successfully". |
| **`StealthOvermind`** | `Player.m_localPlayer`-centred; O(all loaded characters) per frame with no spatial gate; allocated a class per tracked creature per frame; tiered in *frames*; capped by a *count* not a cost; halted entirely when the local player died — which on a dedicated server is the permanent state. |
| **`StealthBrain._evalCache` + `StableAIKey` + `MigrateCacheCoroutine`** | The key was `(ZDOID.UserID, GetInstanceID())`, and `UserID` is the *spawning peer's* id (`AV:64582`, resolved through a static list at `AV:64612`), identical for everything that peer spawned — so the key degenerated to a per-process handle. The migration coroutine was a no-op rename that leaked an iterator and two closures on **every** evaluation while `NetworkUid == 0`. |
| **`Dictionary<Character, AwarenessData>`** | Keyed on a `UnityEngine.Object` (so a destroyed `Character` stays a distinct live key), pruned only by `OnDestroy`, walked in full every frame by the HUD, unbounded across a session. |
| **All four coroutines and `CoroutineManager`** | The vegetation sampler (local player only, ~240 string allocations/s forever), the ally coroutine (full dictionary key copy per lap, double `GetComponent`, a `Collider[30]` that truncated silently in exactly the dense case group logic exists for), the eval loop, the migrate loop. Folded into `Driver.Update` phases and `GroupKernel`. |
| **`BehaviorSystem.CallNearbyAllies`'s `Physics.OverlapSphere`** | Allocating overload, no layer mask, 30 m, plus an unconditional `List<string>`, an `Object.name` native marshal per ally and a `string.Join`. Replaced by a `CellGrid` query: no physics, no strings, ~250× cheaper, and it can run 6× more often so the ally count is actually fresh. |
| **Every unconditional interpolated debug string** | `StealthBrain:270`, `VisibilitySystem:40`, `NoiseSystem:21`, `HidingSystem:34`, `CamoSystem:55`, `BehaviorSystem:209,215`. Arguments are evaluated at the call site, so `string.Format` + boxing ran with debug **off**: ~46 allocations per evaluation, ≈ 700 KB/s, ~65 % of v1's total GC pressure. |
| **`AIAuthority`'s server fallback** (`nview == null → ZNet.IsServer()`) | Contradicted by the dedicated-server dossier: there are no creature instances near players on a dedicated server. `ZNetView.IsOwner()` is the only correct rule. |
| **Dead code with zero call sites** | `StealthBrain.ClearCache/ClearAllCaches`, `AwarenessSystem.ClearAll` as dead code, `CoroutineManager.StopManagedCoroutine`, `BehaviorSystem.ExecuteCombat/GetCombatCooldown/SetCombatStance`, `RaycastUtils.SkyVisible/SampleOcclusion/InTallGrass` (the middle one allocated a `Vector3[6]` per call), `EnvironmentUtils.GetAmbientLight`, `HidingSystem.VegetationMask`. |
| **`StealthUIRoot`'s `EventSystem` creation** | Ran during the chainloader, permanently won `EventSystem.current` and disabled Valheim's own. Canvas is now built lazily on first `Player.m_localPlayer`, under vanilla's `Hud`, `raycastTarget = false`. |
| **`SpriteLoader`'s disk path and disk write** | Hardcoded the plugin folder name (wrong under a Gale or Hexium profile) and silently wrote into the user's plugins tree. Embedded resource only. |
| **The duplicate `assembly_valheim` csproj reference** | `assembly_publicizer.dll` **is** `assembly_valheim`, publicized — same fusion name. `csc` rejects both with CS1704; it only ever built because RAR de-duplicated by fusion name and happened to keep the right one. |
| **`DESIGN-pragmatic`'s per-pass `ExternalPin.Poll`/`Publish`** | §9.1: nothing writes into `AwarenessData` any more. Replaced by demand-driven publish-on-read (§9.2). |

### Deliberately NOT rewritten

- **The four sensing formulas.** `Clamp01(light − shadow − grass − weather + movement + armor)` and its three
  siblings are ported term for term into `PlayerStore` producers. Their known quirks — grass double-counted,
  `Clamp01` saturating the armor tiers, weapons and shields counting as armor, `RenderSettings.sun` apparently
  never assigned by Valheim (`SURVEY-sensing` §8), `"ThunderStorm"` not matching `"rain"` — are **documented,
  logged where detectable, and fixed in 2.1**. One behavioural variable at a time; the data model is already
  the variable in this release.
- **The evaluator arithmetic.** `StateEvaluator`'s ladder, `FleeEvaluator`'s thresholds, `MovementEvaluator`'s
  deliberate `Vector3.zero` for Alerted/Engaged (vanilla owns pursuit), `CombatEvaluator`'s priority scoring
  and the ally logic keep their exact numbers. Their *shape* changes — they become kernels over columns, and
  `LadderKernel` is branch-free — but §12 pins the arithmetic with a parity test. This is the honest cost of
  the SoA choice and §13 says so.
- **Config section names, key names and defaults.** Existing TOMLs load unchanged.
- **The HUD's visual design.** Same gem, same meter, same colours; only its data source, lifecycle and write
  discipline change.
- **`ArmorProfileSystem` / `CamoSystem` tables.** Same keywords, same tiers, same biome map — including the
  rows that probably match no vanilla item token. Correcting them is a balance change, not an architecture one.
- **`MonsterAI.UpdateTarget`, `FindEnemy`, `MonsterAI.SetTarget`, `Character.RPC_Damage`, `BaseAI.Alert`,
  `SetAlerted`** stay unpatched. v2 achieves the same effects through the public methods they already call —
  more v1.0-resilient, and far less likely to collide with another mod.
- **`AnimalAI`.** Registered (so its position column feeds §5.4) but not simulated, behind
  `IncludeAnimalAI = false`. In SoA the marginal cost of including it is a row, so the argument is purely about
  behavioural blast radius, not performance.
- **Per-track replication of `LastKnownPosition` for non-dominant tracks.** The blob carries LKP for the
  dominant track only. Secondary tracks restore detection and state after handover, which is what group stealth
  needs; their search positions re-acquire within one tier period.

---

## 12. Acceptance test

One scenario, run on a **three-client session** (host H, clients A and B) with 200 spawned creatures, decides
whether the whole design works end to end. It is deliberately the same *behavioural* scenario the pragmatic
design proposes, plus the throughput and handover assertions that are specific to this architecture — because
if the throughput work broke the behaviour, the throughput work is worthless.

> **Setup.** H owns ~200 creatures around a base. A crouches two metres from three greydwarves that are
> actively fighting B. A wears full troll leather; B wears iron and is sprinting. A `Debug/DumpStore` console
> command prints the store, roster occupancy, stripe timings and allocation counters.
>
> **Behaviour — group stealth**
> 1. A's gem is **blue, small and quiet**. (v1 returns `Engaged` here, because `GetHighestAlertness` reads one
>    creature-global scalar.)
> 2. On H, the same greydwarf answers `CanSeeTarget(A) == false` and `CanSeeTarget(B) == true`, **in the same
>    frame**, and `Debug/DumpStore` shows two distinct rows in `TrackStore` for that creature with different
>    `Det` and `State`.
> 3. `FindEnemy` continues to select B, not A, even though A is closer (`AV:5205-5210`).
> 4. A's backstab lands at full multiplier; B's does not (`AV:8736`) — proving `m_alerted` is the *real* field.
> 5. A and B see the **same** `EnemyHud` alert icon on that greydwarf (`AV:38631`) — proving `ZDOVars.s_alert`
>    is replicating again.
> 6. A gains Sneak XP; B does not (`AV:5390`).
>
> **Multiplayer state survival**
> 7. A walks 60 m so ownership of the three greydwarves migrates from H to A (`ReleaseNearbyZDOS`, ≤ 2.5 s).
>    The greydwarves **keep chasing B** — they do not reset to `Unaware`. `DumpStore` on A shows three tracks
>    per creature with `TrackFlags.Seeded`, restored from `SoM_T`, including B's track *and* A's own.
> 8. A `debugmode` admin in ghost mode standing in the middle of the fight is tracked by nobody
>    (`PlayerStore.Ghost`, vanilla parity with `AV:4556`).
>
> **Throughput — the assertions specific to this design**
> 9. With `Debug/Profile` on: mean SoM main-thread time per frame is **< 100 µs** and p99 < 400 µs, measured
>    across 60 s of the fight, with 200 creatures registered and 3 players.
> 10. `Debug/DumpStore` reports **allocation delta = 0 bytes** over 60 s **except** the replication blob
>     counter, which must be < 20 KB/s.
> 11. Toggle `Perf.BatchedLos` off and on. Main-thread time rises by ≥ 20× with it off and returns with it on;
>     **observable behaviour is identical in both modes** — same detection latencies, same acceptance items 1-8.
>     This is the test that proves the fast path is an optimisation and not a semantic change.
> 12. Toggle `Perf.CreatureTargetEarlyOut` off and on with 200 creatures loaded. Frame time improves measurably
>     with it on, and **no creature changes which target it acquires** across a 60 s A/B (logged by a
>     `Debug/TargetAudit` mode that records every `m_targetCreature` transition).
> 13. Roster occupancy sums to the registered count, dormant rows report zero passes, and every tier's measured
>     round-trip is within 2× its configured period.
>
> **Parity**
> 14. A `Debug/LadderParity` command sweeps `det ∈ [0,1]` at 0.001 and `state ∈ {0..3}` across 500 randomised
>     `(Suspicious, Alerted, Engaged)` triples and asserts `LadderKernel.Step` equals a literal transcription of
>     v1 `StateEvaluator.cs:13-33` for every input. Zero mismatches. **This is the test that licenses the
>     "arithmetic unchanged" claim in §11**, and it is the one I would run first, because it is the claim the
>     SoA rewrite is most likely to have broken.

If items 1–8 read correctly, per-player tracks, honest `SetAlerted`, per-target patch answers, blob replication
and the HUD rewrite are all verified simultaneously. If 9–13 also hold, the throughput architecture is
verified. If 14 fails, the port introduced a behavioural regression and none of the rest matters.

---

## 13. Where this design loses

Against a shippability-lensed alternative like `DESIGN-pragmatic`, here is what I am actually trading. This
section is not hedging; each item is a real cost I would expect to pay.

**1. It is materially harder to review, and that is the biggest cost.** `DESIGN-pragmatic`'s central claim is
that its five evaluators are *ported almost verbatim* — the arithmetic is recognisably the same code with
different inputs, so a reviewer diffs it. Mine cannot be. `SensingEvaluator` becomes `SenseKernel.Integrate`
over columns; `StateEvaluator` becomes a branch-free sum of predicates; the decision object becomes eleven
columns. Every one of those is a place a transcription bug can hide, and a transcription bug in a stealth model
is *invisible* — it does not throw, it just makes a monster slightly wrong. Item 14 of the acceptance test
exists precisely because I do not trust this, and it only covers the ladder. I would want the same
sweep-and-compare harness for the sense kernel and the flee kernel before shipping, which is real work the
pragmatic design does not have to do.

**2. It is materially harder to debug in the field.** A user reports "the greydwarf near my base never wakes
up". Under pragmatic, you attach a debugger, find the `CreatureState`, and read fields with names. Under mine
you get row 137 and thirty arrays, and you need `Debug/DumpStore` to have been written well enough to answer
the question you did not anticipate. The mitigation is that `DumpStore` must be genuinely good — a per-row
human-readable dump, a per-row trace mode, and roster/stripe telemetry — and that is another chunk of work that
buys the user nothing directly.

**3. `RaycastCommand` is a real version risk that pragmatic does not take.** I have verified the exact overloads
in the shipped `UnityEngine.PhysicsModule.dll` and that Valheim itself calls `ScheduleBatch` at `AV:115123`, so
the risk today is zero. But Unity changed this API once already (`QueryParameters` replaced the `layerMask`/
`maxHits` ctor), and if Valheim 1.0 ships on a different Unity, my primary path may need a third binding rung.
The ladder degrades to synchronous linecasts and pragmatic's exact scheme, so the failure is graceful — but I
carry a soft-binding surface for it that pragmatic does not, and soft bindings are where "never hard-crash"
claims go to die.

**4. The creature-vs-creature early-out (§5.4) is the one thing I expect a bug report about.** Its equivalence
argument is sound against the vanilla body I read, and the staleness margin is bounded by physics. But it
depends on `m_hearRange`/`m_viewRange` being the true ceiling on sensing, and any mod that patches
`CanHearTarget`/`CanSeeTarget` to *extend* range will have its extension silently defeated for creature targets
when my prefix returns `false` first. `Priority.Low` and `__runOriginal` protect against a mod that patches
*before* me; they do not protect against one that patches *after*. Pragmatic never returns `false` for a
creature-vs-creature pair, so it cannot have this problem. If I had to cut one thing under schedule pressure,
it would be this — it is the highest-value-per-line item in the design and also the highest-risk, and default
`off` costs me 76 ms/s at N=1000 and almost nothing at N=200.

**5. Half a megabyte of pre-allocated RSS for a 200-creature session that uses 4 % of it.** Pre-sizing is what
buys the zero-allocation guarantee and the flat access pattern; it is also a straightforward waste at typical
load, and it grows with `StoreCapacity` even for users who will never see 200 creatures. Pragmatic allocates
what it uses.

**6. The direct-mapped `RowIndex` is a correctness hazard in a way a `Dictionary` is not.** Instance IDs are
recycled. I defend with dual invalidation (patch *and* sweep) and a `ReferenceEquals` revalidation on every
consumer, and I believe that closes it — but "I believe that closes it" is a weaker statement than "a
dictionary keyed on a live object cannot alias", and the failure mode if I am wrong is a creature answering
sensing queries from another creature's tracks. That is a worse bug than anything in pragmatic's design.

**7. The SoA layout makes future features more expensive, not less.** Adding a field to `CreatureState` is one
line. Adding a column is a declaration, a `Grow`, an `Alloc` reset, a `Free` clear, and a decision about which
block it belongs to. Every feature after 2.0 pays a small tax. Over a mod's lifetime that tax may exceed the
performance it bought.

**8. I am optimising a term that may not be the user's problem.** v1 costs ~15–20 ms/s at *one twentieth* of the
target load. Pragmatic's ~28 ms/s at full load is 2.8 % of a 60 fps frame — annoying but survivable. Mine is
0.15 %. The honest question is whether the delta between 2.8 % and 0.15 % is worth items 1, 2 and 6, and the
answer depends entirely on whether the user actually wants 200 creatures. **If the target is 40 creatures and
4 players, pragmatic is the correct design and mine is over-engineering.** The throughput case only becomes
compelling at the scale the brief explicitly asks for, and it becomes *decisive* only past ~500 creatures —
where, as §4.4 shows, vanilla's own `FindEnemy` is the wall anyway and only §5.4 (the riskiest item) addresses
it.

**What I would cut, in order, under schedule pressure:** (1) `Perf.CreatureTargetEarlyOut` → default off;
(2) the branch-free `LadderKernel` → a literal transcription of `StateEvaluator`, ~10 ns/pair for a large
review-confidence win; (3) the hot/cold track column split → one interleaved `Track[]` in a single global
array, keeping the contiguity but losing the cache-line packing; (4) `CellGrid` → a linear scan over the
roster for ally counts, which is fine below ~300 creatures. What I would **not** cut at any pressure: the
batched LOS (it is the whole throughput thesis and it degrades safely), the flag-byte gate (it is 98 % of the
patch-layer win and it is simple), tier rosters (they are what makes dormant creatures free), and the
`PlayerStore` hoist (without it, per-player tracks cost N×P and the entire multiplayer fix is unaffordable).
