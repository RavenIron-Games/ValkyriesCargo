using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using RavenIron.ValkyriesCargo.Core;
using UnityEngine;
using UnityEngine.Animations;

namespace RavenIron.ValkyriesCargo
{
    /// <summary>
    /// P10b: at boot, say what this DLL was built for, say what it found, and resolve every engine fact
    /// a patch relies on before a patch relies on it. Runs once, from plugin `Awake`, AFTER the config
    /// binds (a probe's answer is printed by `cargo status`, which reads config) and BEFORE
    /// `Harmony.PatchAll` (a probe must not be the thing a patch is waiting for).
    ///
    /// **Two house rules shape every line below, and both are load bearing.**
    ///
    /// 1. **Resolution goes in its own method, never the method that does the work.** Mono resolves a
    ///    member access when the CALLER is JIT-compiled, so a `try`/`catch` in the same method as the
    ///    access never runs: the exception is thrown while the method is being compiled, before its
    ///    first instruction executes. Every probe here is therefore a PAIR - a `Probe*` method that
    ///    catches, and a `[MethodImpl(NoInlining)]` `Check*` method that touches the game type. The
    ///    `NoInlining` is not decoration: an inlined `Check*` would drag its `typeof` token back into
    ///    the catching method and put us exactly where we started. `Server/AdminGate.cs` is the shape.
    /// 2. **Publicized assemblies are compile-time only, and this file is the ONE named exception to
    ///    "our files name no private member"** - by NAME, in a string, handed to reflection, inside a
    ///    probe, and never called. `ZSyncTransform.m_velocityCached` is private and the flight's whole
    ///    dead-reckoning argument rests on it existing; a probe that could not look at it would be a
    ///    probe that lies. Nothing here reads a value off a live object, writes anything, or moves
    ///    anything (house rule "never move what you do not own"), and nothing here starts a timer
    ///    (house rule 2): it runs once and is done.
    ///
    /// A mismatch is INFORMATION, not an error. The one thing this file must never do is decide that a
    /// player whose Valheim moved should get a mod that throws every frame.
    /// </summary>
    public static class EngineCheck
    {
        /// <summary>The registry the probes are recorded in. The game's one; the harness builds its own.</summary>
        public static EngineProbes Probes => EngineProbes.Current;

        public static bool Ran { get; private set; }

        /// <summary>What the live version numbers came to, or null when they could not be read.</summary>
        public static EngineComparison Comparison { get; private set; }

        /// <summary>The verdict in words, whether or not the numbers were readable.</summary>
        public static string Verdict { get; private set; } = "not checked";

        /// <summary>The one line `cargo status` carries (design: `cargo status` names its sources).</summary>
        public static string StatusLine() => "engine: " + Verdict + "; " + Probes.Encode();

        /// <summary>
        /// Read the four version numbers, then run every probe. Idempotent: a second call is a no-op, so
        /// nothing can double-record and `cargo engine` cannot disagree with the boot log.
        /// </summary>
        public static void Run()
        {
            if (Ran) return;
            Ran = true;

            CheckVersion();

            ProbeRandEvent();
            ProbeZdoAuthoring();
            ProbeZoneMaths();
            ProbeVelocityCache();
            ProbeValkyrie();
            ProbeCharacter();
            ProbeMerchantAwake();
            ProbeMerchant();
            ProbeInventory();
            ProbeComfort();
            ProbeDayLength();
            ProbeAdmin();
            ProbeRpc();
            ProbeLocalisation();
            ProbeBody();

            EngineProbes p = Probes;
            IList<EngineProbe> failed = p.Failed;
            ValkyriesCargo.Log.LogInfo(EngineBaseline.Describe() + "; running " + Verdict + "; " + p.Encode() + ".");
            foreach (EngineProbe f in failed)
                ValkyriesCargo.Log.LogWarning("engine probe [" + Wire.Int(f.Rank) + "] " + f.Name + " FAILED: " + f.Message +
                                              " -> " + f.Degrades + ". `cargo engine` for the whole list.");
            foreach (string problem in p.Problems)
                ValkyriesCargo.Log.LogWarning("engine probes: " + problem);
        }

        // ---- what we were built for, and what is running ---------------------------------------------

        private static void CheckVersion()
        {
            try
            {
                string game;
                int net, player, world;
                string why = ReadVersions(out game, out net, out player, out world);
                if (why != null)
                {
                    Comparison = null;
                    Verdict = "game version unreadable (" + why + ")";
                    return;
                }
                Comparison = EngineBaseline.Compare(game, net, player, world);
                Verdict = Comparison.Verdict;
            }
            catch (Exception ex)
            {
                Comparison = null;
                Verdict = "game version unreadable (" + ex.GetType().Name + ": " + ex.Message + ")";
            }
        }

        /// <summary>
        /// The four live numbers, by reflection, and it HAS to be reflection twice over.
        ///
        /// - `Version` is `internal` in the real assembly. Naming it would be exactly the thing house
        ///   rule 5 forbids: it compiles against the publicized copy and is a runtime coin toss.
        /// - `m_networkVersion`, `m_playerVersion` and `m_worldVersion` are `const`. A direct reference
        ///   is inlined AT OUR COMPILE TIME, so the comparison would read our own baseline back to
        ///   itself and answer "same build" on every Valheim ever released. Reflection reads the LOADED
        ///   assembly's metadata, which is the only place the live numbers exist.
        ///
        /// Returns null when all four came back, or the reason in words.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string ReadVersions(out string game, out int net, out int player, out int world)
        {
            game = "";
            net = 0;
            player = 0;
            world = 0;

            Type t = typeof(ZNet).Assembly.GetType("Version", false);
            if (t == null) return "assembly_valheim carries no type named 'Version'";

            PropertyInfo current = t.GetProperty("CurrentVersion", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (current == null) return "Version.CurrentVersion is gone";
            object gv = current.GetValue(null, null);
            if (gv == null) return "Version.CurrentVersion is null";
            game = gv.ToString();

            if (!ConstInt(t, "m_networkVersion", out net)) return "Version.m_networkVersion is gone or is not a number";
            if (!ConstInt(t, "m_playerVersion", out player)) return "Version.m_playerVersion is gone or is not a number";
            if (!ConstInt(t, "m_worldVersion", out world)) return "Version.m_worldVersion is gone or is not a number";
            return null;
        }

        /// <summary>A static field's value whether it is a `const` (a literal, with no storage) or a `static readonly`.</summary>
        private static bool ConstInt(Type t, string name, out int value)
        {
            value = 0;
            FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (f == null) return false;
            object v = f.IsLiteral ? f.GetRawConstantValue() : f.GetValue(null);
            if (v == null) return false;
            try { value = Convert.ToInt32(v, CultureInfo.InvariantCulture); return true; }
            catch { return false; }
        }

        // ---- the probes ------------------------------------------------------------------------------
        //
        // One pair each. The Probe* half catches; the Check* half touches the game type and is never
        // inlined into it. A Check* returns the number of members it looked at and fills `bad` with
        // every one that is not what the design says it is.

        private static void ProbeRandEvent()
        {
            var bad = new List<string>();
            int looked;
            // The JIT compiles CheckRandEvent's body when THIS call reaches it - inside this try, never before it.
            try { looked = CheckRandEvent(bad); }
            catch (Exception ex) { Threw(EngineProbes.RandEvent, ex); return; }
            Record(EngineProbes.RandEvent, looked, bad);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int CheckRandEvent(List<string> bad)
        {
            Type res = typeof(RandEventSystem);
            Type ev = typeof(RandomEvent);
            NeedProperty(res, "instance", res, bad);
            // PRIVATE, and the target `Patch_RandEventSystem_Awake` names in a string: a rename here is a
            // Harmony failure at PatchAll, not a compile error, so it belongs in the probe like RPC_Damage.
            NeedMethod(res, "Awake", Type.EmptyTypes, bad);
            NeedField(res, "m_events", typeof(List<RandomEvent>), true, bad);
            NeedMethod(res, "SetRandomEventByName", new[] { typeof(string), typeof(Vector3) }, bad);
            NeedMethod(res, "ResetRandomEvent", Type.EmptyTypes, bad);
            NeedMethod(res, "GetCurrentRandomEvent", Type.EmptyTypes, bad);
            NeedMethod(res, "HaveEvent", new[] { typeof(string) }, bad);
            // The fields CargoEvent's definition writes, and the three the engine's own clock reads.
            NeedField(ev, "m_name", typeof(string), true, bad);
            NeedField(ev, "m_enabled", typeof(bool), true, bad);
            NeedField(ev, "m_random", typeof(bool), true, bad);
            NeedField(ev, "m_duration", typeof(float), true, bad);
            NeedField(ev, "m_time", typeof(float), true, bad);
            NeedField(ev, "m_eventRange", typeof(float), true, bad);
            NeedField(ev, "m_pauseIfNoPlayerInArea", typeof(bool), true, bad);
            NeedField(ev, "m_cameraShakeCurve", typeof(AnimationCurve), true, bad);
            NeedField(ev, "m_standaloneInterval", typeof(float), true, bad);
            NeedField(ev, "m_startMessage", typeof(string), true, bad);
            NeedField(ev, "m_endMessage", typeof(string), true, bad);
            NeedField(ev, "m_forceMusic", typeof(string), true, bad);
            NeedField(ev, "m_forceEnvironment", typeof(string), true, bad);
            NeedField(ev, "m_biome", typeof(Heightmap.Biome), true, bad);
            return 21;
        }

        private static void ProbeZdoAuthoring()
        {
            var bad = new List<string>();
            int looked;
            // The JIT compiles CheckZdoAuthoring's body when THIS call reaches it - inside this try, never before it.
            try { looked = CheckZdoAuthoring(bad); }
            catch (Exception ex) { Threw(EngineProbes.ZdoAuthoring, ex); return; }
            Record(EngineProbes.ZdoAuthoring, looked, bad);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int CheckZdoAuthoring(List<string> bad)
        {
            Type zdo = typeof(ZDO);
            NeedSettableProperty(zdo, "Persistent", typeof(bool), bad);
            NeedSettableProperty(zdo, "Distant", typeof(bool), bad);
            NeedSettableProperty(zdo, "Type", typeof(ZDO.ObjectType), bad);
            NeedMethod(zdo, "SetPrefab", new[] { typeof(int) }, bad);
            NeedMethod(zdo, "SetOwner", new[] { typeof(long) }, bad);
            NeedMethod(zdo, "SetPosition", new[] { typeof(Vector3) }, bad);
            NeedMethod(zdo, "SetRotation", new[] { typeof(Quaternion) }, bad);
            NeedMethod(zdo, "GetHashZDOID", new[] { typeof(string) }, bad);
            NeedMethod(typeof(ZDOMan), "CreateNewZDO", new[] { typeof(Vector3), typeof(int) }, bad);
            NeedMethod(typeof(ZDOMan), "DestroyZDO", new[] { typeof(ZDO) }, bad);
            NeedMethod(typeof(ZDOMan), "GetSessionID", Type.EmptyTypes, bad);
            NeedMethod(typeof(ZNetScene), "HasPrefab", new[] { typeof(int) }, bad);
            NeedMethod(typeof(ZNetView), "GetZDO", Type.EmptyTypes, bad);
            return 13;
        }

        private static void ProbeZoneMaths()
        {
            var bad = new List<string>();
            int looked;
            // The JIT compiles CheckZoneMaths's body when THIS call reaches it - inside this try, never before it.
            try { looked = CheckZoneMaths(bad); }
            catch (Exception ex) { Threw(EngineProbes.ZoneMaths, ex); return; }
            Record(EngineProbes.ZoneMaths, looked, bad);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int CheckZoneMaths(List<string> bad)
        {
            // The three overloads FlightPlan's clamp is reasoned against. The design's arithmetic
            // (|zone - centre| <= m_activeArea - 1 on 64 m zones) lives in the two-zone one.
            NeedMethod(typeof(ZNetScene), "InActiveArea", new[] { typeof(Vector2i), typeof(Vector3) }, bad);
            NeedMethod(typeof(ZNetScene), "InActiveArea", new[] { typeof(Vector2i), typeof(Vector2i) }, bad);
            NeedMethod(typeof(ZNetScene), "InActiveArea", new[] { typeof(Vector2i), typeof(Vector2i), typeof(int) }, bad);
            Type zs = typeof(ZoneSystem);
            NeedMethod(zs, "GetZone", new[] { typeof(Vector3) }, bad);
            NeedField(zs, "m_zoneSize", typeof(float), true, bad);
            NeedField(zs, "m_activeArea", typeof(int), true, bad);
            NeedField(zs, "m_activeDistantArea", typeof(int), true, bad);
            NeedField(zs, "m_waterLevel", typeof(float), true, bad);
            return 8;
        }

        private static void ProbeVelocityCache()
        {
            var bad = new List<string>();
            int looked;
            // The JIT compiles CheckVelocityCache's body when THIS call reaches it - inside this try, never before it.
            try { looked = CheckVelocityCache(bad); }
            catch (Exception ex) { Threw(EngineProbes.VelocityCache, ex); return; }
            Record(EngineProbes.VelocityCache, looked, bad);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int CheckVelocityCache(List<string> bad)
        {
            // PRIVATE, and named here on purpose (see the class comment). Never read, never written:
            // the flight's argument is that OwnerSync writes s_velHash only when the velocity differs
            // from this cache, so the owner's own write survives. No cache, no argument.
            NeedField(typeof(ZSyncTransform), "m_velocityCached", typeof(Vector3), false, bad);
            NeedField(typeof(ZDOVars), "s_velHash", typeof(int), true, bad);
            // And that the key really is "vel": a rename keeps the field and moves the hash, and every
            // watcher would then dead-reckon off a key nothing writes.
            NeedHash(typeof(ZDOVars), "s_velHash", "vel", bad);
            return 3;
        }

        private static void ProbeValkyrie()
        {
            var bad = new List<string>();
            int looked;
            // The JIT compiles CheckValkyrie's body when THIS call reaches it - inside this try, never before it.
            try { looked = CheckValkyrie(bad); }
            catch (Exception ex) { Threw(EngineProbes.ValkyrieFields, ex); return; }
            Record(EngineProbes.ValkyrieFields, looked, bad);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int CheckValkyrie(List<string> bad)
        {
            Type v = typeof(Valkyrie);
            NeedField(v, "m_attachPoint", typeof(Transform), true, bad);
            NeedField(v, "m_attachOffset", typeof(Vector3), true, bad);
            NeedField(v, "m_dropHeight", typeof(float), true, bad);
            NeedField(v, "m_speed", typeof(float), true, bad);
            NeedField(v, "m_turnRate", typeof(float), true, bad);
            // Patch_Valkyrie_Awake exists because vanilla Awake takes this before its owner guard.
            NeedField(v, "m_instance", typeof(Valkyrie), true, bad);
            NeedMethod(v, "Awake", Type.EmptyTypes, bad);
            return 7;
        }

        private static void ProbeCharacter()
        {
            var bad = new List<string>();
            int looked;
            // The JIT compiles CheckCharacter's body when THIS call reaches it - inside this try, never before it.
            try { looked = CheckCharacter(bad); }
            catch (Exception ex) { Threw(EngineProbes.CharacterAi, ex); return; }
            Record(EngineProbes.CharacterAi, looked, bad);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int CheckCharacter(List<string> bad)
        {
            Type c = typeof(Character);
            NeedMethod(c, "GetAllCharacters", Type.EmptyTypes, bad);       // used today, by CargoFlight
            NeedMethod(c, "GetSEMan", Type.EmptyTypes, bad);               // used today, by ComfortReporter
            NeedMethod(c, "InIntro", Type.EmptyTypes, bad);                // Patch_Character_InIntro's target
            // THE PROBE GAP (docs/P10B-PROBE-GAP.md, fixed here). This asked for the PUBLIC
            // `Character.Damage(HitData)` and nothing in this mod depends on it: `Damage` is a thin
            // sender that runs on the ATTACKER's machine, computes a weak-spot index and forwards to
            // `InvokeRPC("RPC_Damage", hit)`. `Patch_Character_RPC_Damage` patches the PRIVATE
            // `RPC_Damage(long, HitData)` - the victim-side choke point every hit passes through - so
            // that is the member the immortality depends on and the only one worth probing. Probing the
            // sender meant the probe could be green while the merchant had quietly become mortal.
            // Verified against the real assembly, not the publicized one: `private void RPC_Damage(long
            // sender, HitData hit)`, assembly_valheim 0.221.12 decompiled line 8700. `Anywhere` already
            // includes NonPublic, so the name and the signature are the whole change.
            NeedMethod(c, "RPC_Damage", new[] { typeof(long), typeof(HitData) }, bad);
            NeedMethod(typeof(MonsterAI), "MakeTame", Type.EmptyTypes, bad);
            NeedMethod(typeof(BaseAI), "IsEnemy", new[] { typeof(Character) }, bad);
            // The STATIC overload is what `Patch_BaseAI_IsEnemy` patches (ghost mode, F11): every targeting
            // path, hit filter and the enemy HUD go through it. Decompiled line 4994.
            NeedMethod(typeof(BaseAI), "IsEnemy", new[] { typeof(Character), typeof(Character) }, bad);
            return 7;
        }

        private static void ProbeMerchantAwake()
        {
            var bad = new List<string>();
            int looked;
            // The JIT compiles CheckMerchantAwake's body when THIS call reaches it - inside this try, never before it.
            try { looked = CheckMerchantAwake(bad); }
            catch (Exception ex) { Threw(EngineProbes.MerchantAwake, ex); return; }
            Record(EngineProbes.MerchantAwake, looked, bad);
        }

        /// <summary>
        /// What P5's merchant is BUILT by, as opposed to what he does once he is standing.
        ///
        /// `Patch_Humanoid_Awake` names `Humanoid.Awake` in a string, so a rename is a Harmony failure
        /// at PatchAll rather than a compile error - the same class of silence as `RPC_Damage`. The rest
        /// of this probe is the cast of the ordering bug PR #22 fixed: `BaseAI.Awake` is what assigns
        /// `m_character`, `MonsterAI.MakeTame` dereferences it on its first line, and a `Humanoid.Awake`
        /// POSTFIX runs before either of MonsterAI's own Awake and Start have. `CargoMerchant` therefore
        /// skips `MakeTame` on the call from Awake and lets the 0.5 s stagger do it. The ORDER itself is
        /// a method body and no reflection can see it - that fact is registered separately as
        /// `awake_order`, not probeable, for P10a's sweep.
        ///
        /// `BaseAI.m_character` is `protected` in the real assembly, so it is named here (a string, to
        /// reflection, never called) under the same exception as `ZSyncTransform.m_velocityCached`, and
        /// asked for WITHOUT `mustBePublic`.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int CheckMerchantAwake(List<string> bad)
        {
            Type h = typeof(Humanoid);
            NeedMethod(h, "Awake", Type.EmptyTypes, bad);                   // Patch_Humanoid_Awake's target (protected)
            NeedMethod(h, "Start", Type.EmptyTypes, bad);                   // where GiveDefaultItems is actually called from
            NeedMethod(h, "GiveDefaultItems", Type.EmptyTypes, bad);        // the crossbow UnequipAllItems takes back off
            Type ai = typeof(BaseAI);
            NeedMethod(ai, "Awake", Type.EmptyTypes, bad);
            NeedField(ai, "m_character", typeof(Character), false, bad);
            NeedMethod(typeof(MonsterAI), "Awake", Type.EmptyTypes, bad);
            NeedMethod(typeof(Character), "SetTamed", new[] { typeof(bool) }, bad);
            NeedMethod(typeof(ZNetView), "IsValid", Type.EmptyTypes, bad);  // the guard CargoMerchant puts in front of SetTamed
            return 8;
        }

        private static void ProbeMerchant()
        {
            var bad = new List<string>();
            int looked;
            // The JIT compiles CheckMerchant's body when THIS call reaches it - inside this try, never before it.
            try { looked = CheckMerchant(bad); }
            catch (Exception ex) { Threw(EngineProbes.Merchant, ex); return; }
            Record(EngineProbes.Merchant, looked, bad);
        }

        /// <summary>
        /// Everything else `Client/CargoMerchant.cs` reaches for: the setup it re-asserts every five
        /// seconds, the carry pin, the words, and the Odin vanish. All public in the real assembly.
        /// `Character.Faction.Players` is asked for by NAME rather than by value - the enum is ordered
        /// and a member inserted ahead of it would silently renumber every following one, so the name is
        /// the thing worth checking and the number never is.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int CheckMerchant(List<string> bad)
        {
            Type c = typeof(Character);
            NeedField(c, "m_name", typeof(string), true, bad);
            NeedField(c, "m_faction", typeof(Character.Faction), true, bad);
            NeedEnumValue(typeof(Character.Faction), "Players", bad);
            NeedMethod(c, "IsOnGround", Type.EmptyTypes, bad);
            NeedMethod(typeof(Humanoid), "UnequipAllItems", Type.EmptyTypes, bad);

            Type ai = typeof(BaseAI);
            NeedMethod(typeof(MonsterAI), "SetFollowTarget", new[] { typeof(GameObject) }, bad);
            NeedMethod(ai, "SetPatrolPoint", Type.EmptyTypes, bad);
            NeedField(ai, "m_aggravatable", typeof(bool), true, bad);
            NeedField(ai, "m_passiveAggresive", typeof(bool), true, bad);   // vanilla's spelling, kept
            NeedField(typeof(MonsterAI), "m_alertRange", typeof(float), true, bad);
            NeedField(ai, "m_randomMoveRange", typeof(float), true, bad);

            NeedMethod(typeof(Player), "GetClosestPlayer", new[] { typeof(Vector3), typeof(float) }, bad);
            NeedMethod(typeof(ZNetScene), "FindInstance", new[] { typeof(ZDOID) }, bad);
            NeedMethod(typeof(ZNetScene), "GetPrefab", new[] { typeof(string) }, bad);
            NeedMethod(typeof(ZDO), "GetZDOID", new[] { typeof(KeyValuePair<int, int>) }, bad);

            // The words and the leaving. `Chat.SetNpcText` is unguarded client UI; `EffectList.Create`'s
            // three optional parameters are part of the signature OUR call site compiled against.
            NeedMethod(typeof(Chat), "SetNpcText",
                       new[] { typeof(GameObject), typeof(Vector3), typeof(float), typeof(float), typeof(string), typeof(string), typeof(bool) }, bad);
            NeedField(typeof(Odin), "m_despawn", typeof(EffectList), true, bad);
            NeedMethod(typeof(EffectList), "Create",
                       new[] { typeof(Vector3), typeof(Quaternion), typeof(Transform), typeof(float), typeof(int) }, bad);
            return 18;
        }

        private static void ProbeInventory()
        {
            var bad = new List<string>();
            int looked;
            // The JIT compiles CheckInventory's body when THIS call reaches it - inside this try, never before it.
            try { looked = CheckInventory(bad); }
            catch (Exception ex) { Threw(EngineProbes.InventoryOps, ex); return; }
            Record(EngineProbes.InventoryOps, looked, bad);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int CheckInventory(List<string> bad)
        {
            Type inv = typeof(Inventory);
            // The optional parameters are part of the signature we compiled: a call site fills them in
            // at OUR compile time, so a vanilla that drops them is a MissingMethodException at the call,
            // not a recompile. All four, by arity.
            NeedMethod(inv, "RemoveItem", new[] { typeof(string), typeof(int), typeof(int), typeof(bool) }, bad);
            NeedMethod(inv, "AddItem", new[] { typeof(GameObject), typeof(int) }, bad);
            NeedMethod(inv, "CanAddItem", new[] { typeof(GameObject), typeof(int) }, bad);
            NeedMethod(inv, "CountItems", new[] { typeof(string), typeof(int), typeof(bool) }, bad);
            NeedMethod(typeof(ObjectDB), "GetItemPrefab", new[] { typeof(string) }, bad);
            NeedMethod(typeof(Player), "GetInventory", Type.EmptyTypes, bad);
            return 6;
        }

        private static void ProbeComfort()
        {
            var bad = new List<string>();
            int looked;
            // The JIT compiles CheckComfort's body when THIS call reaches it - inside this try, never before it.
            try { looked = CheckComfort(bad); }
            catch (Exception ex) { Threw(EngineProbes.Comfort, ex); return; }
            Record(EngineProbes.Comfort, looked, bad);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int CheckComfort(List<string> bad)
        {
            NeedMethod(typeof(Player), "GetComfortLevel", Type.EmptyTypes, bad);
            NeedMethod(typeof(Player), "GetPlayerName", Type.EmptyTypes, bad);
            NeedField(typeof(SEMan), "s_statusEffectRested", typeof(int), true, bad);
            NeedHash(typeof(SEMan), "s_statusEffectRested", "Rested", bad);
            NeedMethod(typeof(SEMan), "HaveStatusEffect", new[] { typeof(int) }, bad);
            NeedField(typeof(ZDOVars), "s_baseValue", typeof(int), true, bad);
            NeedHash(typeof(ZDOVars), "s_baseValue", "baseValue", bad);
            NeedField(typeof(ZDOVars), "s_dead", typeof(int), true, bad);
            NeedHash(typeof(ZDOVars), "s_dead", "dead", bad);
            NeedField(typeof(ZDOVars), "s_playerName", typeof(int), true, bad);
            NeedHash(typeof(ZDOVars), "s_playerName", "playerName", bad);
            NeedMethod(typeof(ZNet), "GetAllCharacterZDOS", Type.EmptyTypes, bad);
            return 12;
        }

        private static void ProbeDayLength()
        {
            var bad = new List<string>();
            int looked;
            // The JIT compiles CheckDayLength's body when THIS call reaches it - inside this try, never before it.
            try { looked = CheckDayLength(bad); }
            catch (Exception ex) { Threw(EngineProbes.DayLength, ex); return; }
            Record(EngineProbes.DayLength, looked, bad);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int CheckDayLength(List<string> bad)
        {
            Type env = typeof(EnvMan);
            NeedProperty(env, "instance", env, bad);
            // A LONG, not a float and not an int: the market widens it to a double and never assumes.
            NeedField(env, "m_dayLengthSec", typeof(long), true, bad);
            NeedMethod(env, "IsDay", Type.EmptyTypes, bad);
            return 3;
        }

        private static void ProbeAdmin()
        {
            var bad = new List<string>();
            int looked;
            // The JIT compiles CheckAdmin's body when THIS call reaches it - inside this try, never before it.
            try { looked = CheckAdmin(bad); }
            catch (Exception ex) { Threw(EngineProbes.AdminList, ex); return; }
            Record(EngineProbes.AdminList, looked, bad);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int CheckAdmin(List<string> bad)
        {
            Type znet = typeof(ZNet);
            NeedMethod(znet, "IsAdmin", new[] { typeof(string) }, bad);
            NeedMethod(znet, "GetUID", Type.EmptyTypes, bad);
            NeedMethod(znet, "GetWorldUID", Type.EmptyTypes, bad);
            NeedMethod(znet, "IsDedicated", Type.EmptyTypes, bad);
            NeedMethod(znet, "GetTimeSeconds", Type.EmptyTypes, bad);
            return 5;
        }

        private static void ProbeRpc()
        {
            var bad = new List<string>();
            int looked;
            // The JIT compiles CheckRpc's body when THIS call reaches it - inside this try, never before it.
            try { looked = CheckRpc(bad); }
            catch (Exception ex) { Threw(EngineProbes.Rpc, ex); return; }
            Record(EngineProbes.Rpc, looked, bad);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int CheckRpc(List<string> bad)
        {
            Type rpc = typeof(ZRpc);
            NeedGenericMethod(rpc, "Register", 1, 2, bad);      // ZRpc.Register<T>(name, Action<ZRpc,T>)
            NeedGenericMethod(rpc, "Register", 2, 2, bad);      // and the two-argument form the router uses
            NeedMethod(rpc, "Invoke", new[] { typeof(string), typeof(object[]) }, bad);
            NeedMethod(typeof(ZNet), "GetServerRPC", Type.EmptyTypes, bad);
            NeedMethod(typeof(ZNet), "GetPeers", Type.EmptyTypes, bad);
            NeedField(typeof(ZNetPeer), "m_rpc", rpc, true, bad);
            NeedGenericMethod(typeof(ZRoutedRpc), "Register", 1, 2, bad);
            NeedGenericMethod(typeof(ZRoutedRpc), "Register", 2, 2, bad);
            NeedMethod(typeof(ZRoutedRpc), "InvokeRoutedRPC", new[] { typeof(long), typeof(string), typeof(object[]) }, bad);
            return 9;
        }

        private static void ProbeLocalisation()
        {
            var bad = new List<string>();
            int looked;
            // The JIT compiles CheckLocalisation's body when THIS call reaches it - inside this try, never before it.
            try { looked = CheckLocalisation(bad); }
            catch (Exception ex) { Threw(EngineProbes.Localisation, ex); return; }
            Record(EngineProbes.Localisation, looked, bad);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int CheckLocalisation(List<string> bad)
        {
            Type loc = typeof(Localization);
            NeedProperty(loc, "instance", loc, bad);
            NeedMethod(loc, "Localize", new[] { typeof(string) }, bad);
            return 2;
        }

        private static void ProbeBody()
        {
            var bad = new List<string>();
            int looked;
            // The JIT compiles CheckBody's body when THIS call reaches it - inside this try, never before it.
            try { looked = CheckBody(bad); }
            catch (Exception ex) { Threw(EngineProbes.Body, ex); return; }
            Record(EngineProbes.Body, looked, bad);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int CheckBody(List<string> bad)
        {
            Type ab = typeof(AssetBundle);
            NeedMethod(ab, "LoadFromStream", new[] { typeof(Stream) }, bad);
            NeedMethod(ab, "LoadFromFile", new[] { typeof(string) }, bad);
            NeedGenericMethod(ab, "LoadAsset", 1, 1, bad);
            NeedGenericMethod(ab, "LoadAllAssets", 1, 0, bad);
            // The PlayableGraph half: IngvarBody drives the six clips with no AnimatorController at all,
            // so these three Create calls ARE the animation system as far as this mod is concerned.
            NeedMethod(typeof(UnityEngine.Playables.PlayableGraph), "Create", new[] { typeof(string) }, bad);
            NeedMethod(typeof(AnimationMixerPlayable), "Create", new[] { typeof(UnityEngine.Playables.PlayableGraph), typeof(int), typeof(bool) }, bad);
            NeedMethod(typeof(AnimationClipPlayable), "Create", new[] { typeof(UnityEngine.Playables.PlayableGraph), typeof(AnimationClip) }, bad);
            NeedMethod(typeof(AnimationPlayableOutput), "Create", new[] { typeof(UnityEngine.Playables.PlayableGraph), typeof(string), typeof(Animator) }, bad);

            // The donor material (BodyLoader.IngvarMaterial). A bundle baked in the Editor carries
            // Unity's `Standard`, which Valheim lights only from direct light, so Ingvar is dressed in a
            // COPY of the stand-in's own material with our albedo in it. These are the calls that copy
            // makes. The other half of that path - that a prefab named by `Server.BodyPrefab` is in the
            // scene AND carries a `Custom/Creature` material to copy - is a RUNTIME fact about the
            // loaded world, not an assembly fact: no reflection at plugin Awake can see it, ZNetScene
            // does not exist yet at that moment, and there is no runtime tier in this registry. It is
            // not probed, deliberately; `IngvarMaterial` answers null on every leg of it (no scene, no
            // prefab, no renderer, no material, or a throw) and `Dress` then binds the albedo onto the
            // bundle's own material instead. See docs/ENGINE-PROBES.md, "the donor material".
            NeedConstructor(typeof(Material), new[] { typeof(Material) }, bad);
            NeedMethod(typeof(Material), "HasProperty", new[] { typeof(string) }, bad);
            NeedMethod(typeof(Material), "SetTexture", new[] { typeof(string), typeof(Texture) }, bad);
            NeedMethod(typeof(Material), "SetColor", new[] { typeof(string), typeof(Color) }, bad);
            NeedMethod(typeof(Material), "DisableKeyword", new[] { typeof(string) }, bad);
            NeedSettableProperty(typeof(Material), "globalIlluminationFlags", typeof(MaterialGlobalIlluminationFlags), bad);
            NeedSettableProperty(typeof(Renderer), "sharedMaterials", typeof(Material[]), bad);
            NeedProperty(typeof(Renderer), "sharedMaterial", typeof(Material), bad);
            return 16;
        }

        // ---- recording -------------------------------------------------------------------------------

        /// <summary>
        /// Turn one Check*'s findings into a pass or a failure and record it once. Deliberately takes
        /// the ANSWER rather than a delegate to the Check*: a method group would be cached in a
        /// compiler-generated holder and put a second thing between the try and the resolution, and
        /// there is nothing here worth that. The direct call at each `Probe*` site is the whole
        /// mechanism.
        /// </summary>
        private static void Record(string name, int looked, List<string> bad)
        {
            if (bad.Count == 0)
            {
                Probes.Record(name, true, Wire.Int(looked) + " member(s) as expected");
                return;
            }
            Probes.Record(name, false, string.Join("; ", bad.ToArray()));
        }

        private static void Threw(string name, Exception ex)
        {
            Probes.Record(name, false, "the probe threw " + ex.GetType().Name + ": " + ex.Message +
                                       " (the type itself is probably gone)");
        }

        // ---- the reflection assertions ----------------------------------------------------------------
        //
        // Every one of these takes a Type the caller already resolved, so none of them binds a game type
        // of its own and none of them can be the method a JIT failure lands in.

        private const BindingFlags Anywhere =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;

        /// <summary>
        /// The lookups themselves, guarded. `GetField`, `GetProperty` and `GetMethod` all throw
        /// `AmbiguousMatchException` when a derived type hides a base member of the same name, and an
        /// ambiguous match is still a match: the member is THERE, which is the only question a probe is
        /// asking. A probe that failed on an ambiguity would be a probe that turns a feature off because
        /// Valheim added a `new` keyword.
        /// </summary>
        private static FieldInfo GetField(Type t, string name)
        {
            try { return t.GetField(name, Anywhere); }
            catch (AmbiguousMatchException) { return null; }
        }

        /// <summary>The property, null when it is not there, and `ambiguous` when reflection could not pick one.</summary>
        private static PropertyInfo GetProperty(Type t, string name, out bool ambiguous)
        {
            ambiguous = false;
            try { return t.GetProperty(name, Anywhere); }
            catch (AmbiguousMatchException) { ambiguous = true; return null; }
        }

        private static void NeedField(Type t, string name, Type expected, bool mustBePublic, List<string> bad)
        {
            FieldInfo f = GetField(t, name);
            if (f == null) { bad.Add(t.Name + "." + name + " is gone"); return; }
            if (expected != null && f.FieldType != expected)
                bad.Add(t.Name + "." + name + " is a " + f.FieldType.Name + ", not a " + expected.Name);
            if (mustBePublic && !f.IsPublic)
                bad.Add(t.Name + "." + name + " is no longer public");
        }

        /// <summary>A `static readonly int` key hash really is the hash of the string we think names it.</summary>
        private static void NeedHash(Type t, string name, string key, List<string> bad)
        {
            FieldInfo f = GetField(t, name);
            if (f == null) return;                                  // NeedField already said so
            object v = f.IsLiteral ? f.GetRawConstantValue() : f.GetValue(null);
            if (!(v is int)) { bad.Add(t.Name + "." + name + " holds no int"); return; }
            int want = key.GetStableHashCode();
            if ((int)v != want)
                bad.Add(t.Name + "." + name + " is " + Wire.Int((int)v) + ", not the hash of \"" + key + "\" (" + Wire.Int(want) + ")");
        }

        private static void NeedMethod(Type t, string name, Type[] args, List<string> bad)
        {
            MethodInfo m = null;
            try { m = t.GetMethod(name, Anywhere, null, args ?? Type.EmptyTypes, null); }
            catch (AmbiguousMatchException) { return; }             // more than one match is still a match
            if (m == null) bad.Add(t.Name + "." + name + "(" + Names(args) + ") is gone");
        }

        /// <summary>A generic method by name, generic arity and parameter count - `GetMethod` cannot ask for one by argument types.</summary>
        private static void NeedGenericMethod(Type t, string name, int genericArgs, int paramCount, List<string> bad)
        {
            foreach (MethodInfo m in t.GetMethods(Anywhere))
            {
                if (m.Name != name || !m.IsGenericMethodDefinition) continue;
                if (m.GetGenericArguments().Length != genericArgs) continue;
                if (m.GetParameters().Length != paramCount) continue;
                return;
            }
            bad.Add(t.Name + "." + name + "<" + Wire.Int(genericArgs) + ">(" + Wire.Int(paramCount) + " args) is gone");
        }

        /// <summary>A constructor by argument types. `new Material(donor)` is a call like any other and can go the same way.</summary>
        private static void NeedConstructor(Type t, Type[] args, List<string> bad)
        {
            ConstructorInfo c = null;
            try { c = t.GetConstructor(Anywhere, null, args ?? Type.EmptyTypes, null); }
            catch (AmbiguousMatchException) { return; }             // more than one match is still a match
            if (c == null) bad.Add("new " + t.Name + "(" + Names(args) + ") is gone");
        }

        /// <summary>
        /// An enum MEMBER by name. The value is deliberately not checked: vanilla's enums are ordered
        /// and inserting a member renumbers everything after it, so the name is the durable half and the
        /// number never was.
        /// </summary>
        private static void NeedEnumValue(Type t, string name, List<string> bad)
        {
            bool there;
            try { there = t.IsEnum && Enum.IsDefined(t, name); }
            catch { there = false; }
            if (!there) bad.Add(t.Name + "." + name + " is gone");
        }

        private static void NeedProperty(Type t, string name, Type expected, List<string> bad)
        {
            bool ambiguous;
            PropertyInfo p = GetProperty(t, name, out ambiguous);
            if (ambiguous) return;
            if (p == null) { bad.Add(t.Name + "." + name + " is gone"); return; }
            if (expected != null && p.PropertyType != expected)
                bad.Add(t.Name + "." + name + " is a " + p.PropertyType.Name + ", not a " + expected.Name);
        }

        private static void NeedSettableProperty(Type t, string name, Type expected, List<string> bad)
        {
            bool ambiguous;
            PropertyInfo p = GetProperty(t, name, out ambiguous);
            if (ambiguous) return;
            if (p == null) { bad.Add(t.Name + "." + name + " is gone"); return; }
            if (expected != null && p.PropertyType != expected)
                bad.Add(t.Name + "." + name + " is a " + p.PropertyType.Name + ", not a " + expected.Name);
            MethodInfo set = p.GetSetMethod(true);
            if (set == null) bad.Add(t.Name + "." + name + " has no setter");
            else if (!set.IsPublic) bad.Add(t.Name + "." + name + "'s setter is no longer public");
        }

        private static string Names(Type[] args)
        {
            if (args == null || args.Length == 0) return "";
            var parts = new string[args.Length];
            for (int i = 0; i < args.Length; i++) parts[i] = args[i].Name;
            return string.Join(", ", parts);
        }
    }
}
