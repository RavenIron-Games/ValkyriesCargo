using System;
using System.Collections.Generic;
using RavenIron.ValkyriesCargo.Config;
using RavenIron.ValkyriesCargo.Core;
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
    /// the first `ZNetView.Awake` consumes it), so `Patch_Valkyrie_Awake` can read `vc_cargo` and skip
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
    /// - a dedicated server's own `GetReferencePosition()` stays `Vector3.zero` (nothing but local-player
    ///   paths and `Tracker` ever set it), so the server instantiates nothing except near the origin, and
    ///   the "destroy a ZDO with an unresolvable prefab" branch in `CreateObjectsSorted` can only bite a
    ///   visit that happens at world centre. `HasPrefab` is checked here anyway, before authoring.
    /// </summary>
    public static class Spawner
    {
        // The bird.
        public static readonly int CargoHash = "vc_cargo".GetStableHashCode();      // int visitId: this is ours
        public static readonly int TargetHash = "vc_target".GetStableHashCode();    // Vector3: where to put him down
        public static readonly int DroppedHash = "vc_dropped".GetStableHashCode();  // bool: he is on the ground
        // The merchant.
        public static readonly int IngvarHash = "vc_ingvar".GetStableHashCode();    // int visitId: this is Ingvar
        public static readonly int SeedHash = "vc_seed".GetStableHashCode();        // int: his lines and his bearing
        public static readonly int StateHash = "vc_state".GetStableHashCode();      // int: carried/approaching/trading/leaving
        /// <summary>`vc_carrier`: the bird he hangs from, ZDOID.None once he is down. Two int keys, the way ZDO stores an id.</summary>
        public static readonly KeyValuePair<int, int> CarrierKey = ZDO.GetHashZDOID("vc_carrier");

        public const string BirdPrefab = "Valkyrie";

        /// <summary>What the server remembers about the flight it authored. Not persisted: the bird cannot survive a restart (non-persistent) and the merchant is found again by his `vc_ingvar` key (design 3.7).</summary>
        public static ZDOID Bird { get; private set; }
        public static ZDOID Merchant { get; private set; }
        public static int VisitId { get; private set; }
        public static bool Dropped { get; private set; }
        public static string LastProblem { get; private set; } = "";

        private static float _orphanWaited;
        private static int _throws;

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
                    Clamp(ModConfig.FlightDescentDistance, 50f, 0f, 200f));

                if (!plan.Ok)
                {
                    // Nowhere in the pilot's block has room for a flight. Rather than fly a bird that
                    // pops out of existence, say so and let the visit run without one: the merchant is
                    // still authored, on the ground, already dropped.
                    LastProblem = "no room in the pilot's active block for a flight (activeArea " + activeArea + ")";
                }

                var start = new Vector3(plan.StartX, plan.StartY, plan.StartZ);
                var drop = new Vector3(plan.DropX, plan.DropY, plan.DropZ);
                Quaternion look = LookAlong(plan.DropX - plan.StartX, plan.DropZ - plan.StartZ);

                ZDO bird = null;
                if (plan.Ok)
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
                    bird.Set(DroppedHash, false);
                    bird.SetOwner(pilotUid);               // LAST: after this the ZDO is the pilot's to write
                }

                // He hangs under the bird until the drop; with no flight he simply starts on the ground.
                Vector3 npcStart = plan.Ok ? start : drop;
                ZDO npc = man.CreateNewZDO(npcStart, bodyHash);
                npc.SetPrefab(bodyHash);
                npc.SetPosition(npcStart);
                npc.SetRotation(look);
                npc.Persistent = true;                     // survives the pilot walking off; adopted by a nearer client
                npc.Distant = false;
                npc.Set(IngvarHash, visitId);
                npc.Set(SeedHash, seed);
                npc.Set(StateHash, plan.Ok ? MerchantState.Carried : MerchantState.Approaching);
                npc.Set(CarrierKey, bird != null ? bird.m_uid : ZDOID.None);
                npc.SetOwner(pilotUid);

                Bird = bird != null ? bird.m_uid : ZDOID.None;
                Merchant = npc.m_uid;
                VisitId = visitId;
                Dropped = !plan.Ok;
                _orphanWaited = 0f;

                return (plan.Ok ? "flight authored: " + plan : "NO FLIGHT (" + LastProblem + "); the merchant starts on the ground at (" +
                        Wire.Float(plan.DropX) + ", " + Wire.Float(plan.DropZ) + ")") +
                       "; bird " + Bird + ", " + bodyName + " " + Merchant + ", both owned by the pilot";
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
        /// not write to either ZDO; it watches the pilot's `vc_dropped` flag so the visit's phase and drop
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
                    Vector3 at = bird.GetVec3(TargetHash, Vector3.zero);
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
        /// The bird was still flying when the visit ended. Reclaim it and destroy it: taking ownership
        /// first is the only way a server may destroy a ZDO it does not own (`ZDOMan.DestroyZDO` is a
        /// no-op for a non-owner), and is Undertow's pattern.
        /// </summary>
        public static void Clear()
        {
            try
            {
                ZDOMan man = ZDOMan.instance;
                if (man != null && !Bird.IsNone())
                {
                    ZDO bird = man.GetZDO(Bird);
                    if (bird != null && bird.IsValid())
                    {
                        bird.SetOwner(ZDOMan.GetSessionID());
                        man.DestroyZDO(bird);
                    }
                }
            }
            catch (Exception ex)
            {
                if (_throws++ < 3) ValkyriesCargo.Log.LogWarning("spawner: clearing the bird threw: " + ex.Message);
            }
            Bird = ZDOID.None;
            Merchant = ZDOID.None;
            VisitId = 0;
            Dropped = false;
            _orphanWaited = 0f;
        }

        /// <summary>How long a missing bird is given before the visit gives up on being carried.</summary>
        public const float OrphanGraceSeconds = 5f;

        /// <summary>`cargo status`: what the server authored and what it has seen since.</summary>
        public static string Describe()
        {
            if (!Active) return "flight: none authored";
            return "flight: visit #" + VisitId + ", bird " + Bird + (Dropped ? " (dropped)" : " (flying)") +
                   ", merchant " + Merchant + (LastProblem.Length > 0 ? "; problem: " + LastProblem : "");
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

    /// <summary>`vc_state` on the merchant's ZDO (design 3.3). The owner writes it; every client reacts.</summary>
    public static class MerchantState
    {
        public const int Carried = 0;
        public const int Approaching = 1;
        public const int Trading = 2;
        public const int Leaving = 3;
    }
}
