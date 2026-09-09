using System;
using System.Collections.Generic;
using RavenIron.ValkyriesCargo.Config;
using RavenIron.ValkyriesCargo.Core;
using RavenIron.ValkyriesCargo.Patches;
using UnityEngine;

namespace RavenIron.ValkyriesCargo.Server
{
    /// <summary>
    /// The server authors the flight (design 3.2): two ZDOs, written whole and handed to the pilot,
    /// who instantiates and flies them. Nothing is instantiated here, and after the handoff nothing is
    /// written here either - the server only reads, because only an owner's writes replicate.
    ///
    /// Why authored and not "the server tells the pilot to spawn one": a ZDO carries its keys BEFORE any
    /// Awake runs on the receiving machine (`ZNetScene.CreateObject` parks it in `ZNetView.m_initZDO` and
    /// the first `ZNetView.Awake` consumes it), so `Patch_Valkyrie_Awake` can read `VCargo_cargo` and skip
    /// vanilla on the very first frame. A spawn-then-configure race would let vanilla `Valkyrie.Awake`
    /// run first, and vanilla `Valkyrie.Awake` teleports `Player.m_localPlayer` into the sky.
    ///
    /// Facts this file is built on, read from the decompile 2026-09-06:
    /// - `ZDOMan.CreateNewZDO(pos, hash)` uses the hash ONLY for the portal list and never stores it;
    ///   `SetPrefab` is a separate call and without it `GetPrefab()` stays 0 and nothing is ever created.
    /// - `Persistent`, `Distant` and `Type` are properties on ZDO, not setters; `ZNetView.Awake` on the
    ///   init-ZDO branch re-applies neither `m_persistent` nor (for a non-owner) `m_type`, so what is
    ///   written here is what the object gets.
    /// - the owner id `SetOwner` wants is `ZNet.GetUID()` == `ZDOMan.GetSessionID()` == the peer's
    ///   `m_uid`, which is exactly what a character ZDO's `GetOwner()` returns. One number.
    /// - a NON-persistent ZDO whose owner leaves its own active area is destroyed WORLD-WIDE within a
    ///   30 Hz tick (`ZNetScene.RemoveObjects` -> `ZDOMan.DestroyZDO`), and one whose owner disconnects
    ///   is swept by `RemoveOrphanNonPersistentZDOS`. That is the bird's whole lifetime policy, and it
    ///   is why `FlightPlan` refuses to put a waypoint outside the pilot's block.
    /// - a DEDICATED SERVER PINS its reference position to (1000000, 0, 1000000) every fixed frame -
    ///   `Game.FixedUpdate` in the server build ends with exactly that line, and the client build has no
    ///   such line at all. Confirmed word for word against both real builds, 2026-09-07
    ///   (`docs/engine-sweeps/2026-09-07-baseline-client-vs-server.md`). It is the difference that
    ///   MATTERS, not the only one: six surface members differ between the two assemblies -
    ///   `Game.FixedUpdate` (this pin), `ZNet.IsDedicated`, `Terminal.AddString`,
    ///   `GameCamera.UpdateMouseCapture`, `ZNet.Awake` and `ZNet.RPC_PeerInfo` - and the sweep report
    ///   names why the other five change nothing here. So the server's `ZNetScene` active area never
    ///   covers a real world position and the server instantiates NOTHING of ours, anywhere - not the
    ///   bird, not the merchant. Two
    ///   consequences: the "destroy a ZDO whose prefab will not resolve" branch in `CreateObjectsSorted`
    ///   can never reach us (it only walks the server's own sector list, out at a million), and every
    ///   line of `CargoFlight` and `CargoMerchant` runs on a player's machine. `HasPrefab` is still
    ///   checked below, because a prefab the CLIENTS cannot resolve is silently retried forever.
    /// </summary>
    public static class Spawner
    {
        // The bird.
        public static readonly int CargoHash = Keys.Cargo.GetStableHashCode();      // int visitId: this is ours
        public static readonly int TargetHash = Keys.Target.GetStableHashCode();    // Vector3: where to put him down
        public static readonly int DroppedHash = Keys.Dropped.GetStableHashCode();  // bool: he is on the ground
        /// <summary>
        /// `VCargo_turn`: the descent waypoint, whole, from the server's plan. It has a key of its own
        /// because the first version had the client rebuild it from the start and the drop, and the
        /// rebuild came out on the opposite side, at a different distance, with none of the plan's
        /// block clamp (PR #8's review). One author, one number, no second copy of the maths.
        /// </summary>
        public static readonly int TurnHash = Keys.Turn.GetStableHashCode();        // Vector3: the descent waypoint
        public static readonly int AwayHash = Keys.Away.GetStableHashCode();        // Vector3: where the empty bird leaves to
        // The merchant.
        public static readonly int IngvarHash = Keys.Ingvar.GetStableHashCode();    // int visitId: this is Ingvar
        public static readonly int SeedHash = Keys.Seed.GetStableHashCode();        // int: his lines and his bearing
        public static readonly int StateHash = Keys.State.GetStableHashCode();      // int: carried/approaching/trading/leaving
        /// <summary>`VCargo_carrier`: the bird he hangs from, ZDOID.None once he is down. Two int keys, the way ZDO stores an id.</summary>
        public static readonly KeyValuePair<int, int> CarrierKey = ZDO.GetHashZDOID(Keys.Carrier);

        public const string BirdPrefab = "Valkyrie";

        /// <summary>
        /// P5 (`CargoMerchant`) is what pins the merchant to the talons, keeps him peaceful, walks him
        /// up and puts him away again. P4 shipped with this false because a bare vanilla Dverger falls
        /// 120 m and then stands next to the pilot with a live `MonsterAI` (PR #8's review, finding 4).
        /// P5 landed; this is the line it flips.
        /// </summary>
        /// <remarks>`static readonly`, not `const`: a const folds and the compiler reports the other
        /// branch as unreachable code, which is a warning we do not ship.</remarks>
        public static readonly bool MerchantEnabled = true;

        /// <summary>What the server remembers about the flight it authored. Not persisted: the bird cannot survive a restart (non-persistent) and the merchant is found again by his `VCargo_ingvar` key (design 3.7).</summary>
        public static ZDOID Bird { get; private set; }
        public static ZDOID Merchant { get; private set; }
        public static int VisitId { get; private set; }
        public static bool Dropped { get; private set; }
        public static string LastProblem { get; private set; } = "";

        /// <summary>
        /// The drop point the server AUTHORED, kept so that the one the pilot reports can be checked
        /// against it. The bird is owned by the pilot (`SetOwner(pilotUid)` below), so `VCargo_target` is a
        /// client-writable value, and `Tick` used to hand it to the visit unexamined -- the one place in
        /// the mod where a client's ZDO write moved server state with no bound on it at all (P11's
        /// authority audit, `docs/TRUST-BOUNDARY.md`). Not persisted: it lives exactly as long as the
        /// flight does, and a visit restored after a restart is already Dropped.
        /// </summary>
        public static Vector3 AuthoredDrop { get; private set; }

        private static float _orphanWaited;
        private static int _throws;
        private static int _liars;

        public static bool Active => VisitId != 0;

        /// <summary>
        /// Author the bird and the merchant for a visit, both owned by the pilot. Returns the flight in
        /// words for the visit log, or null when it could not be authored (the reason is in LastProblem).
        /// </summary>
        public static string Author(int visitId, long pilotUid, float px, float py, float pz, int seed)
        {
            LastProblem = "";
            try
            {
                ZDOMan man = ZDOMan.instance;
                ZNetScene scene = ZNetScene.instance;
                if (man == null || scene == null) { LastProblem = "no ZDOMan/ZNetScene yet"; return null; }

                string bodyName = (ModConfig.BodyPrefab != null ? ModConfig.BodyPrefab.Value : "Dverger");
                int birdHash = BirdPrefab.GetStableHashCode();
                int bodyHash = bodyName.GetStableHashCode();
                if (!scene.HasPrefab(birdHash)) { LastProblem = "'" + BirdPrefab + "' is not registered with this server's ZNetScene"; return null; }
                if (!scene.HasPrefab(bodyHash)) { LastProblem = "body prefab '" + bodyName + "' is not registered with this server's ZNetScene"; return null; }

                // The runtime value, never the compiled default: ZoneSystem.m_activeArea is a public
                // inspector field (compiled 1) and the scene may say otherwise, exactly as it does for
                // EnvMan.m_dayLengthSec. `cargo status` prints what was read.
                int activeArea = ZoneSystem.instance != null ? ZoneSystem.instance.m_activeArea : 1;

                FlightPlan.Plan plan = FlightPlan.Make(px, py, pz, seed, activeArea,
                    Clamp(ModConfig.FlightStartDistance, 90f, 24f, 400f),
                    Clamp(ModConfig.FlightStartAltitude, 120f, 20f, 400f),
                    Clamp(ModConfig.FlightDescentDistance, 50f, 0f, 200f),
                    FlightPlan.DropAltitude);

                // Decision 9 (docs/DECISIONS-WUBARRK.md §9), the coordinator's PR #36: whether a bird is
                // actually authored is TWO independent gates, not one. Without `Patch_Valkyrie_Awake`,
                // vanilla `Valkyrie.Awake` runs on our bird on every machine and teleports the local
                // player into the sky (CLAUDE.md engine facts; audit F8) - that is not a degraded
                // feature, it is our object flinging the player, so the FLIGHT switches itself off, not
                // the mod. `PatchLedger.IsApplied` matches by exact short name; a rename of the class is
                // a compile error here (`nameof`), never a silently-false string.
                bool flightPatchApplied = Patching.Ledger.IsApplied(nameof(Patch_Valkyrie_Awake));
                bool authorBird = FlightPlan.AuthorBird(plan.Ok, flightPatchApplied);

                if (!plan.Ok)
                {
                    // Nowhere in the pilot's block has room for a flight. Rather than fly a bird that
                    // pops out of existence, say so and let the visit run without one: the merchant is
                    // still authored, on the ground, already dropped.
                    LastProblem = "no room in the pilot's active block for a flight (activeArea " + activeArea + ")";
                }
                else
                {
                    // Null when the patch DID apply - plan.Ok stays the only reason to fall back to.
                    string patchProblem = FlightPlan.PatchGateProblem(plan.Ok, flightPatchApplied, Patching.Ledger.Applied, Patching.Ledger.Expected);
                    if (patchProblem != null) LastProblem = patchProblem;
                }

                var start = new Vector3(plan.StartX, plan.StartY, plan.StartZ);
                var turn = new Vector3(plan.DescentX, plan.DescentY, plan.DescentZ);
                var drop = new Vector3(plan.DropX, plan.DropY, plan.DropZ);
                var away = new Vector3(plan.AwayX, plan.AwayY, plan.AwayZ);
                Quaternion look = LookAlong(plan.DropX - plan.StartX, plan.DropZ - plan.StartZ);

                ZDO bird = null;
                if (authorBird)
                {
                    bird = man.CreateNewZDO(start, birdHash);
                    bird.SetPrefab(birdHash);              // CreateNewZDO does NOT do this
                    bird.SetPosition(start);
                    bird.SetRotation(look);
                    bird.Persistent = false;               // dies with the flight, never reaches the world save
                    bird.Distant = false;
                    bird.Type = ZDO.ObjectType.Prioritized; // sent and instantiated ahead of scenery; the pilot's
                                                            // ZNetView.Awake normalises it to the prefab's value
                    bird.Set(CargoHash, visitId);
                    bird.Set(TargetHash, drop);
                    bird.Set(TurnHash, turn);
                    bird.Set(AwayHash, away);
                    bird.Set(DroppedHash, false);
                    bird.SetOwner(pilotUid);               // LAST: after this the ZDO is the pilot's to write
                }

                // He hangs under the bird until the drop; with no flight (no room, OR the patch gate
                // above) he simply starts on the ground.
                ZDO npc = null;
                if (MerchantEnabled)
                {
                    Vector3 npcStart = authorBird ? start : drop;
                    npc = man.CreateNewZDO(npcStart, bodyHash);
                    npc.SetPrefab(bodyHash);
                    npc.SetPosition(npcStart);
                    npc.SetRotation(look);
                    npc.Persistent = true;                 // survives the pilot walking off; adopted by a nearer client
                    npc.Distant = false;
                    npc.Set(IngvarHash, visitId);
                    npc.Set(SeedHash, seed);
                    npc.Set(StateHash, authorBird ? MerchantState.Carried : MerchantState.Approaching);
                    npc.Set(CarrierKey, bird != null ? bird.m_uid : ZDOID.None);
                    npc.SetOwner(pilotUid);
                }

                Bird = bird != null ? bird.m_uid : ZDOID.None;
                Merchant = npc != null ? npc.m_uid : ZDOID.None;
                AuthoredDrop = drop;                   // even with no flight: it is where he starts
                VisitId = visitId;
                Dropped = !authorBird;
                _orphanWaited = 0f;

                string flightLine;
                if (authorBird)
                {
                    flightLine = "flight authored: " + plan;
                }
                else if (!plan.Ok)
                {
                    flightLine = "NO FLIGHT (" + LastProblem + "); the merchant starts on the ground at (" +
                                 Wire.Float(plan.DropX) + ", " + Wire.Float(plan.DropZ) + ")";
                }
                else
                {
                    // The geometry was fine; the patch gate vetoed it. Worded differently from the "NO
                    // FLIGHT" line above on purpose, so a screen run can tell "no room" and "the patch
                    // that makes a bird safe did not apply" apart at a glance.
                    flightLine = "flight DISABLED - " + LastProblem + "; Ingvar placed on the ground at the drop point (" +
                                 Wire.Float(plan.DropX) + ", " + Wire.Float(plan.DropZ) + ")";
                }

                return flightLine +
                       "; bird " + Bird +
                       (MerchantEnabled ? ", " + bodyName + " " + Merchant + ", both owned by the pilot"
                                        : ", owned by the pilot; NO MERCHANT (P5 is not in yet, so nothing is authored to carry)");
            }
            catch (Exception ex)
            {
                LastProblem = ex.GetType().Name + ": " + ex.Message;
                if (_throws++ < 3) ValkyriesCargo.Log.LogError("spawner: authoring threw: " + ex);
                Clear();
                return null;
            }
        }

        /// <summary>
        /// Once a second from the director, while a visit runs. The server does not fly anything and does
        /// not write to either ZDO; it watches the pilot's `VCargo_dropped` flag so the visit's phase and drop
        /// point follow the flight, and it notices a flight that quietly died with its pilot.
        /// Returns a line to log, or null.
        /// </summary>
        public static string Tick(VisitSession session, float dt, out string publish)
        {
            publish = null;
            if (!Active || session == null || !session.Active) return null;
            try
            {
                ZDOMan man = ZDOMan.instance;
                if (man == null) return null;

                if (!Dropped)
                {
                    ZDO bird = Bird.IsNone() ? null : man.GetZDO(Bird);
                    if (bird == null || !bird.IsValid())
                    {
                        // The pilot disconnected or walked out of their own block: the non-persistent
                        // bird is gone world-wide and nobody is going to put the merchant down.
                        _orphanWaited += dt;
                        if (_orphanWaited < OrphanGraceSeconds) return null;
                        Dropped = true;
                        publish = session.SetPhase(VisitPhase.Dropped);
                        return "the bird's ZDO is gone and the merchant was never dropped; the visit continues on the ground";
                    }
                    _orphanWaited = 0f;
                    if (!bird.GetBool(DroppedHash, false)) return null;

                    Dropped = true;
                    // The pilot owns this key and may have written anything into it. Fall back to the
                    // authored point rather than Vector3.zero -- a missing key is a bird that never got
                    // its plan, not a drop at the world origin -- and refuse a value that has moved.
                    Vector3 at = bird.GetVec3(TargetHash, AuthoredDrop);
                    if (!FlightPlan.DropAccepted(at.x, at.y, at.z, AuthoredDrop.x, AuthoredDrop.y, AuthoredDrop.z))
                    {
                        if (_liars++ < 3)
                            ValkyriesCargo.Log.LogWarning("visit #" + session.VisitId + ": the pilot reported a drop at (" +
                                                          Wire.Float(at.x) + ", " + Wire.Float(at.y) + ", " + Wire.Float(at.z) +
                                                          "), too far from the authored (" + Wire.Float(AuthoredDrop.x) + ", " +
                                                          Wire.Float(AuthoredDrop.y) + ", " + Wire.Float(AuthoredDrop.z) +
                                                          "); keeping the authored point");
                        at = AuthoredDrop;
                    }
                    string s = session.SetDrop(at.x, at.y, at.z);
                    string p = session.SetPhase(VisitPhase.Dropped);
                    publish = p ?? s;
                    return "visit #" + session.VisitId + ": dropped at (" + Wire.Float(at.x) + ", " + Wire.Float(at.y) + ", " + Wire.Float(at.z) + ")";
                }
                return null;
            }
            catch (Exception ex)
            {
                if (_throws++ < 3) ValkyriesCargo.Log.LogError("spawner: tick threw: " + ex);
                return null;
            }
        }

        /// <summary>
        /// F3 (docs/AUDIT-P4P5-2026-09-07.md), the server half: ask the merchant to vanish before
        /// `Clear` destroys his ZDO, so `CargoMerchant.RPC_Vanish` -- which already exists and already
        /// does the right thing on receive: the farewell line, `Leaving`, the Odin despawn effect
        /// created by the OWNER -- gets a chance to run first. Nobody had ever sent it (F3's finding);
        /// this is the send.
        ///
        /// The server has no `ZNetView` INSTANCE of the merchant to call `InvokeRPC` on -- this file's
        /// own class comment: "the server instantiates NOTHING of ours, anywhere" -- so this goes
        /// through the static, ZDOID-targeted overload instead. Confirmed in the decompile, 2026-09-07
        /// (assembly_valheim.decompiled.cs, 0.221.12, ~144k lines):
        /// - `ZRoutedRpc.InvokeRoutedRPC(long targetPeerID, ZDOID targetZDO, string methodName, params
        ///   object[] parameters)` is public (class `ZRoutedRpc`, the four-argument overload).
        /// - `ZRoutedRpc.Everybody` is `public static long` and is never assigned anywhere but its
        ///   declaration, so it is `0L` (grepped every call site in the assembly). With
        ///   `targetPeerID == 0L`, `InvokeRoutedRPC` calls `HandleRoutedRPC` LOCALLY as well as routing
        ///   to every connected peer -- the same "Everybody also invokes locally" rule CLAUDE.md
        ///   already documents for the config channel, confirmed again here for the routed RPC itself.
        /// - `ZRoutedRpc.HandleRoutedRPC(RoutedRPCData)` resolves a non-None target ZDOID through
        ///   `ZDOMan.instance.GetZDO` then `ZNetScene.instance.FindInstance`, and calls
        ///   `ZNetView.HandleRoutedRPC` on whichever machine actually has that ZDO instanced -- which
        ///   dispatches through `ZNetView`'s OWN per-object `m_functions` dictionary, exactly the one
        ///   `CargoMerchant.Awake` adds to with `_nview.Register(Keys.Vanish, RPC_Vanish)`. On a
        ///   dedicated server `FindInstance` finds nothing local (there is no instance to find), so the
        ///   local call is a silent no-op there and a real one on every client that has him.
        /// - `RPC_Vanish` is registered with `Register(string, Action&lt;long&gt;)` (no extra parameter),
        ///   so this is sent with none; the non-generic `RoutedMethod.Invoke` ignores the package
        ///   regardless.
        ///
        /// Does nothing when nothing is bound (`Merchant.IsNone()`); the caller (`VisitDirector.End`)
        /// is expected to skip straight to reclaiming in that case rather than wait on a grace period
        /// for an RPC with nowhere to go.
        /// </summary>
        public static void SendVanish()
        {
            if (Merchant.IsNone()) return;
            try
            {
                ZRoutedRpc rpc = ZRoutedRpc.instance;
                if (rpc == null) return;      // between worlds; Clear() is still the backstop
                rpc.InvokeRoutedRPC(ZRoutedRpc.Everybody, Merchant, Keys.Vanish);
            }
            catch (Exception ex)
            {
                if (_throws++ < 3) ValkyriesCargo.Log.LogWarning("spawner: sending " + Keys.Vanish + " threw: " + ex.Message);
            }
        }

        /// <summary>
        /// F3's grace period: how long `VisitDirector.End` waits after `SendVanish` before `Clear`
        /// destroys the merchant's ZDO -- long enough for the farewell line and the Odin despawn effect
        /// to actually play on every screen that has him instanced. Named, not a coroutine:
        /// `VisitDirector.Tick` already runs once a second and drives this from there (house rule 2).
        /// </summary>
        public const float VanishGraceSeconds = 2f;

        /// <summary>
        /// The bird was still flying when the visit ended. Reclaim it and destroy it: taking ownership
        /// first is the only way a server may destroy a ZDO it does not own (`ZDOMan.DestroyZDO` is a
        /// no-op for a non-owner), and is Undertow's pattern.
        /// </summary>
        public static void Clear()
        {
            Reclaim(Bird, "bird");
            // The merchant is PERSISTENT, so unlike the bird he does not sweep himself up: a visit that
            // ends with him still standing would leave him in the world save forever. P5's departure is
            // the Odin vanish (`SendVanish`, above) and destroys him itself; this is the backstop under
            // it, and the only thing standing between a crashed visit and a permanent Dverger (PR #8's
            // review).
            Reclaim(Merchant, "merchant");
            Bird = ZDOID.None;
            Merchant = ZDOID.None;
            VisitId = 0;
            Dropped = false;
            AuthoredDrop = Vector3.zero;
            _orphanWaited = 0f;
        }

        /// <summary>
        /// F3's guard: reclaim what `Author` (or `Rebind`) currently holds, but ONLY if nothing has
        /// re-authored since `expectedVisitId` was the one ending. A visit that begins during the
        /// grace window (an admin's `cargo visit` landing in the same second `cargo dismiss` did, say)
        /// calls `Author` again, which overwrites `VisitId`/`Bird`/`Merchant` with the NEW visit's -- a
        /// deferred `Clear()` that did not check would destroy the new visit's bird and merchant
        /// instead of the one that actually ended. Returns true when it actually cleared.
        /// </summary>
        public static bool ClearIfStillOurs(int expectedVisitId)
        {
            if (expectedVisitId == 0 || VisitId != expectedVisitId) return false;
            Clear();
            return true;
        }

        /// <summary>
        /// Take a ZDO back and destroy it. Taking ownership FIRST is the only way a server may destroy
        /// a ZDO it does not own -- `ZDOMan.DestroyZDO` is a no-op for a non-owner -- and is Undertow's
        /// pattern.
        /// </summary>
        private static void Reclaim(ZDOID id, string what)
        {
            try
            {
                ZDOMan man = ZDOMan.instance;
                if (man == null || id.IsNone()) return;
                ZDO zdo = man.GetZDO(id);
                if (zdo == null || !zdo.IsValid()) return;
                zdo.SetOwner(ZDOMan.GetSessionID());
                man.DestroyZDO(zdo);
            }
            catch (Exception ex)
            {
                if (_throws++ < 3) ValkyriesCargo.Log.LogWarning("spawner: clearing the " + what + " threw: " + ex.Message);
            }
        }

        /// <summary>How long a missing bird is given before the visit gives up on being carried.</summary>
        public const float OrphanGraceSeconds = 5f;

        /// <summary>
        /// Every ZDO carrying the configured body prefab, walked once. Shared by `Sweep` (the restart
        /// backstop) and `Rebind` (F4: finding the one merchant ZDO an adopted visit belongs to) so
        /// there is exactly one place that knows how to find "every merchant-shaped ZDO" -- reusing it
        /// rather than inventing a second walk, per the audit.
        /// </summary>
        private static List<ZDO> FindBodyZDOs(ZDOMan man, string bodyName)
        {
            var found = new List<ZDO>();
            int index = 0;
            // The iterative form is the one that does not allocate the whole table: it fills the list
            // and returns whether it finished, so it is called until it says it has.
            while (!man.GetAllZDOsWithPrefabIterative(bodyName, found, ref index)) { }
            return found;
        }

        /// <summary>
        /// Design 3.7, the restart sweep. The merchant is the PERSISTENT half of the pair, so a server
        /// that stopped mid-visit brings him back with the world - standing in a field, with no visit
        /// around him and no bird to be carried by. This walks the ZDO table for `VCargo_ingvar` and puts
        /// away anyone who is not the visit now running.
        ///
        /// It also clears `VCargo_carrier` on the ones it keeps, and that is not tidiness. **A `ZDOID` is
        /// a session handle, not an identity**: `ZDO.Load` renumbers every id in the save on every
        /// world read, so a restored `VCargo_carrier` holds a number that now belongs to some unrelated
        /// object, and a merchant left believing it would pin himself to whatever that is. The carry
        /// never survives a restart by design - the bird is non-persistent - so the honest value
        /// afterwards is None. (`libs-Tools\IMPLEMENTATIONS\MASTER_IMPLEMENTATIONS.md`; it cost
        /// TortalPortal its favourites feature. See CLAUDE.md's knowledge-base section.)
        ///
        /// Called at boot (sparing the visit about to be adopted) AND again by `VisitDirector` once a
        /// visit's departure finishes (F4's belt-and-braces, alongside `ClearIfStillOurs`): after an
        /// end, nothing is "live" but whatever NEW visit may already have begun in the meantime, so the
        /// caller passes that visit's id, or 0 when none is running.
        ///
        /// `when` names the occasion for the log line ("boot", "boot, after giving up on the carry",
        /// "after visit #N") -- the caller knows why it is sweeping and D3
        /// (docs/AUDIT-STORMTEST-2026-09-07.md §2) found that a hardcoded "restart sweep" printed on
        /// every visit end, not just a restart, because the same walk backs both occasions.
        ///
        /// Returns a line to log, or null when there was nothing to do.
        /// </summary>
        public static string Sweep(int liveVisitId, string when)
        {
            try
            {
                ZDOMan man = ZDOMan.instance;
                ZNetScene scene = ZNetScene.instance;
                if (man == null || scene == null) return null;

                string bodyName = ModConfig.BodyPrefab != null ? ModConfig.BodyPrefab.Value : "Dverger";
                int bodyHash = bodyName.GetStableHashCode();

                int cleared = 0, stranded = 0;
                foreach (ZDO zdo in FindBodyZDOs(man, bodyName))
                {
                    if (zdo == null || !zdo.IsValid()) continue;
                    int visit = zdo.GetInt(IngvarHash, 0);
                    if (visit == 0) continue;                       // not ours: an ordinary Dverger

                    if (visit != liveVisitId)
                    {
                        // Left over from a visit that is not running any more.
                        zdo.SetOwner(ZDOMan.GetSessionID());
                        man.DestroyZDO(zdo);
                        stranded++;
                        continue;
                    }
                    if (!zdo.GetZDOID(CarrierKey).IsNone())
                    {
                        zdo.SetOwner(ZDOMan.GetSessionID());
                        zdo.Set(CarrierKey, ZDOID.None);
                        zdo.Set(StateHash, MerchantState.Approaching);
                        cleared++;
                    }
                }
                if (stranded == 0 && cleared == 0) return null;
                return when + " sweep: " + stranded + " stranded merchant(s) destroyed" +
                       (cleared > 0 ? ", " + cleared + " carry link(s) cleared (a restored ZDOID means nothing)" : "") +
                       "; prefab '" + bodyName + "' hash " + bodyHash;
            }
            catch (Exception ex)
            {
                if (_throws++ < 3) ValkyriesCargo.Log.LogError("spawner: the restart sweep threw: " + ex);
                return null;
            }
        }

        /// <summary>
        /// F4 (docs/AUDIT-P4P5-2026-09-07.md): a visit ADOPTED after a restart never goes through
        /// `Author` in THIS process, so `Bird`/`Merchant`/`VisitId`/`Dropped`/`Active` are never bound
        /// to it. Without this, `End` -&gt; `Clear` reclaims nothing (`Reclaim` returns at `id.IsNone()`),
        /// and the merchant -- his ZDO is PERSISTENT -- stays in the world forever: immortal
        /// (`CargoMerchant.LiveCount` &gt;= 1 wherever he is instanced), still `Interactable`, and the
        /// next visit authors a SECOND one beside him. Called once, from `VisitDirector.Adopt`, right
        /// after `VisitSession.Resume` succeeds.
        ///
        /// The boot sweep (`VisitDirector.Create` -&gt; `Sweep(keep)`) already ran by the time this does,
        /// with `keep` peeked from the same saved row via `VisitSession.VisitIdOf` -- so it already
        /// SPARED this exact ZDO (and cleared its carry link) rather than destroying it. This just
        /// finds that same ZDO again and binds `Spawner` to it, reusing `Sweep`'s own walk.
        ///
        /// The BIRD is deliberately NOT rebound: `Author` sets `Persistent = false` on it, so it cannot
        /// survive a restart -- by the time there is anything left to adopt, the bird that carried this
        /// merchant is already gone, world-wide, and there is no ZDO left to find. `Bird` stays
        /// `ZDOID.None`, which reads exactly like "no flight" already does (F3's `AuthorBird` /
        /// `authorBird` = false path) -- an honest answer, not a special case.
        ///
        /// Returns a line to log, or null when no merchant ZDO carries this visit id (nothing to
        /// rebind: it died before the restart, or `MerchantEnabled` was off when it was authored).
        /// </summary>
        public static string Rebind(int visitId)
        {
            try
            {
                ZDOMan man = ZDOMan.instance;
                if (man == null || visitId == 0) return null;

                string bodyName = ModConfig.BodyPrefab != null ? ModConfig.BodyPrefab.Value : "Dverger";
                foreach (ZDO zdo in FindBodyZDOs(man, bodyName))
                {
                    if (zdo == null || !zdo.IsValid()) continue;
                    if (zdo.GetInt(IngvarHash, 0) != visitId) continue;

                    Bird = ZDOID.None;            // non-persistent: gone before this process existed
                    Merchant = zdo.m_uid;
                    VisitId = visitId;
                    Dropped = true;               // can only be found already down; nothing carried him here
                    AuthoredDrop = zdo.GetPosition();
                    _orphanWaited = 0f;
                    return "visit #" + visitId + " resumed: merchant ZDO " + Merchant +
                           " rebound (the bird did not survive the restart - non-persistent by design)";
                }
                return null;
            }
            catch (Exception ex)
            {
                if (_throws++ < 3) ValkyriesCargo.Log.LogError("spawner: rebind threw: " + ex);
                return null;
            }
        }

        /// <summary>`cargo status`: what the server authored and what it has seen since.</summary>
        public static string Describe()
        {
            if (!Active) return "flight: none authored";
            return "flight: visit #" + VisitId + ", bird " + Bird + (Dropped ? " (dropped)" : " (flying)") +
                   ", merchant " + (MerchantEnabled ? Merchant.ToString() : "not authored (P5 not in yet)") +
                   (LastProblem.Length > 0 ? "; problem: " + LastProblem : "");
        }

        private static float Clamp(BepInEx.Configuration.ConfigEntry<float> entry, float fallback, float lo, float hi)
            => Mathf.Clamp(entry != null ? entry.Value : fallback, lo, hi);

        /// <summary>A flat rotation along an XZ direction; identity when the direction is degenerate.</summary>
        private static Quaternion LookAlong(float dx, float dz)
        {
            var v = new Vector3(dx, 0f, dz);
            return v.sqrMagnitude < 0.0001f ? Quaternion.identity : Quaternion.LookRotation(v.normalized, Vector3.up);
        }
    }

    /// <summary>`VCargo_state` on the merchant's ZDO (design 3.3). The owner writes it; every client reacts.</summary>
    public static class MerchantState
    {
        public const int Carried = 0;
        public const int Approaching = 1;
        public const int Trading = 2;
        public const int Leaving = 3;
    }
}
