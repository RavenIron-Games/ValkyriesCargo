using System;
using RavenIron.ValkyriesCargo.Config;
using RavenIron.ValkyriesCargo.Core;
using RavenIron.ValkyriesCargo.Server;
using UnityEngine;

namespace RavenIron.ValkyriesCargo.Client
{
    /// <summary>
    /// Our Valkyrie's flight (design 3.2), added by `Patch_Valkyrie_Awake` to a bird whose ZDO carries
    /// `VCargo_cargo`, in place of the vanilla component we skipped. It lives and dies with the bird, so it
    /// owns no timer that outlives its object (house rule 2 is about long-lived timers; vanilla flies its
    /// own Valkyrie from `FixedUpdate` and so does this).
    ///
    /// The maths is vanilla `UpdateValkyrie`, read from the decompile and kept: three waypoints, a 25 m
    /// look-ahead clamped to the ground, a banked turn capped at 30 degrees of error mapped to 45 degrees
    /// of roll, linear steps at the flight speed, arrival on 0.5 m of XZ distance. What differs is only
    /// what the design changes: EVERY waypoint comes from the server's plan, read off the ZDO; the floor
    /// is `max(ground, water) + m_dropHeight` WHEN the ground raycast succeeds and otherwise no floor at
    /// all (F7, 2026-09-07 -- `TryFloorAt`/`FlightPlan.TryFloor`: an unknown floor leaves the altitude
    /// exactly where it already was, matching vanilla's own success-branch-only behaviour); and nothing
    /// anywhere touches `Player.m_localPlayer`.
    ///
    /// Two of those are corrections from PR #8's review, and both are worth naming because a clean build
    /// and 878 green checks hid them both:
    /// - the turn-in point was REBUILT here from the start and the drop. It came out on the opposite
    ///   side (`Cross(dir, up)` with `dir` pointing start-to-drop is the mirror of the plan's normal on
    ///   the outbound bearing), at a different distance, and with none of the plan's block clamp. It is
    ///   now `VCargo_turn`, authored once by the server. There is no second copy of the geometry.
    /// - the SPEED and TURN RATE are ours, from synced config, not the prefab's. Vanilla's numbers are
    ///   tuned for a 500 m approach: at 20 m/s our 76 m run is 7 s rather than design 3.2's 15-20, and
    ///   20 deg/s is a 57 m turning circle, wider than the whole flight. `m_dropHeight` still comes off
    ///   the prefab, because that one is about the bird's model and not about the approach.
    ///
    /// Only the OWNER flies. Everyone else is moved by `ZSyncTransform` and only watches `VCargo_dropped` to
    /// swing the animator - and that matters, because the flight is the one part of the visit every
    /// player sees at once.
    ///
    /// The one addition vanilla does not need: the owner writes `ZDOVars.s_velHash`. `ZSyncTransform`'s
    /// non-owner path dead-reckons `position += vel * timeSinceLastPacket` (capped at 2 s) and otherwise
    /// only lerps at 0.2 toward the last packet, so a flyer that reports no velocity visibly stutters on
    /// every screen but the pilot's. Vanilla's Valkyrie reports zero (its `GetVelocity` reads a Rigidbody
    /// the prefab does not have) and gets away with it because in vanilla the only witness IS the pilot.
    /// `ZSyncTransform.OwnerSync` caches that zero and never writes the key again, so ours survives.
    /// </summary>
    public sealed class CargoFlight : MonoBehaviour
    {
        /// <summary>Fallback only; the real value is read off the prefab's own `m_dropHeight` in Awake.</summary>
        public const float DefaultDropHeight = FlightPlan.DropAltitude;
        public const float LookAhead = 25f;
        public const float ArriveDistance = 0.5f;
        public const float MaxBankDegrees = 45f;
        public const float BankErrorCap = 30f;

        /// <summary>A flight that has not finished by now has lost its way; the bird leaves rather than circling forever.</summary>
        public const float MaxFlightSeconds = 180f;

        /// <summary>Used when the config has not bound yet (a client that has not joined). The config's own defaults.</summary>
        public const float DefaultSpeed = 8f;
        public const float DefaultTurnRate = 45f;

        private ZNetView _nview;
        private Valkyrie _valkyrie;
        private Animator _animator;
        private Vector3 _drop, _descentStart, _away;
        private bool _descent, _dropped, _animated;
        /// <summary>F7: logged once per bird, the first time the ground raycast has nothing to say.</summary>
        private bool _loggedFloorUnknown;
        // Speed and turn rate are the synced config's; only the drop height is the prefab's. The
        // prefab says speed 20, turn rate 20, drop height 10, and the first two are vanilla's tuning
        // for a 500 m approach we are not flying. See the class comment.
        private float _speed = DefaultSpeed, _turnRate = DefaultTurnRate, _dropHeight = DefaultDropHeight;
        private float _flying;
        private int _visitId;
        private int _throws;

        /// <summary>Where the merchant hangs from (design 3.3). The prefab's attach point if it has one, the bird itself otherwise.</summary>
        public Transform AttachPoint => _valkyrie != null && _valkyrie.m_attachPoint != null ? _valkyrie.m_attachPoint : transform;

        /// <summary>The offset the merchant hangs at, in the attach point's space. The fallback is the SHIPPED
        /// prefab's value, read 2026-09-07 -- the field initialiser says (0,0,1) and the prefab overrides it.</summary>
        public Vector3 AttachOffset => _valkyrie != null ? _valkyrie.m_attachOffset : new Vector3(0f, 0.3f, 0.4f);

        public bool HasDropped => _dropped;
        public int VisitId => _visitId;

        /// <summary>
        /// True on the one machine flying this bird - the pilot's client, because `Spawner` authors the
        /// bird owned by the pilot. The bird is NON-persistent, and `ZDOMan.ReleaseNearbyZDOS` skips
        /// `!Persistent` outright (`asm:65189`), so unlike the merchant's this ownership is never
        /// reassigned by proximity and stays true for the whole flight. That makes it the honest test
        /// for "am I the machine that should own the merchant too" - see `CargoMerchant.FixedUpdate`.
        /// </summary>
        public bool Flying => _nview != null && _nview.IsValid() && _nview.IsOwner();

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _valkyrie = GetComponent<Valkyrie>();
            _animator = GetComponentInChildren<Animator>();
            // Ours, synced and locked from the server, so every witness flies the same bird.
            if (ModConfig.FlightSpeed != null) _speed = Mathf.Clamp(ModConfig.FlightSpeed.Value, 2f, 40f);
            if (ModConfig.FlightTurnRate != null) _turnRate = Mathf.Clamp(ModConfig.FlightTurnRate.Value, 5f, 360f);
            // The prefab's, because it is about how high the model's talons hang, not about the approach.
            if (_valkyrie != null) _dropHeight = _valkyrie.m_dropHeight;

            ZDO zdo = _nview != null ? _nview.GetZDO() : null;
            if (zdo == null) { enabled = false; return; }
            _visitId = zdo.GetInt(Spawner.CargoHash, 0);
            _drop = zdo.GetVec3(Spawner.TargetHash, transform.position);
            _dropped = zdo.GetBool(Spawner.DroppedHash, false);

            // The descent waypoint is the SERVER'S, whole. The fallback is the straight line rather than
            // a rebuild of the old swing: a bird that flies dead at the drop is worse-looking than one
            // that glides, never a broken one.
            Vector3 flat = _drop - transform.position; flat.y = 0f;
            float run = flat.magnitude;
            Vector3 dir = run > 0.01f ? flat / run : transform.forward;
            float descent = Mathf.Min(50f, run);
            float frac = run > 0.001f ? descent / run : 0f;
            Vector3 fallback = _drop + dir * -descent;
            fallback.y = _drop.y + _dropHeight + (transform.position.y - _drop.y - _dropHeight) * frac;
            _descentStart = zdo.GetVec3(Spawner.TurnHash, fallback);
            // The departure is the SERVER'S, whole, for the same reason the descent waypoint is: a
            // second copy of the geometry here drifted from the plan's and nobody noticed. This one
            // had drifted in DIRECTION - `_drop - dir * ...` sends the bird back the way it came,
            // while the plan authors it continuing past the pilot - and in LENGTH, ~194 m against the
            // plan's `DepartDistance`. That is the empty-bird complaint of 2026-09-08: at 8 m/s the
            // old leg was ~24 s of visible, low, empty bird against a ~17 s carry flown high and
            // steep. The fallback below is the plan's own shape, so the two can no longer disagree.
            Vector3 awayFallback = _drop + dir * FlightPlan.DepartDistance;   // `dir` is start -> drop: PAST the drop, not back
            awayFallback.y = _drop.y + FlightPlan.DepartClimb;
            _away = zdo.GetVec3(Spawner.AwayHash, awayFallback);
            _descent = _dropped;

            ValkyriesCargo.Log.LogInfo("cargo flight #" + _visitId + ": " + (_nview.IsOwner() ? "flying" : "watching") +
                                       " from " + Vec(transform.position) + " via " + Vec(_descentStart) + " to " + Vec(_drop) +
                                       ", " + Wire.Float(run) + " m out at " + Wire.Float(_speed) + " m/s, turning " +
                                       Wire.Float(_turnRate) + " deg/s (radius " +
                                       Wire.Float(FlightPlan.TurningRadius(_speed, _turnRate)) + " m)");
        }

        private void FixedUpdate()
        {
            try
            {
                if (_nview == null || !_nview.IsValid()) return;
                WatchDropped();
                if (!_nview.IsOwner()) return;
                Fly(Time.fixedDeltaTime);
            }
            catch (Exception ex)
            {
                if (_throws++ < 3) ValkyriesCargo.Log.LogError("cargo flight #" + _visitId + " threw: " + ex);
            }
        }

        /// <summary>Every machine: the animator swings to the dropped pose when the owner says so.</summary>
        private void WatchDropped()
        {
            if (_animated) return;
            ZDO zdo = _nview.GetZDO();
            if (zdo == null || !zdo.GetBool(Spawner.DroppedHash, false)) return;
            _animated = true;
            _dropped = true;
            _descent = true;
            if (_animator != null) _animator.SetBool("dropped", true);
        }

        private void Fly(float dt)
        {
            _flying += dt;
            if (_flying > MaxFlightSeconds)
            {
                ValkyriesCargo.Log.LogWarning("cargo flight #" + _visitId + ": " + Wire.Float(MaxFlightSeconds) +
                                              " s and still flying; dropping where it stands and leaving");
                if (!_dropped) Drop();
                _nview.Destroy();
                return;
            }

            Vector3 target = _dropped ? _away : (_descent ? _drop : _descentStart);
            if (DistanceXZ(target, transform.position) < ArriveDistance)
            {
                if (!_descent) _descent = true;
                else if (!_dropped) Drop();
                else { _nview.Destroy(); return; }
            }

            // Look 25 m ahead along the bearing and lift that point clear of ground and water; steering
            // at the lifted point is what keeps the bird from flying into a hillside on the way in.
            // F7: an unknown floor leaves this alone -- `ahead.y` already carries the current climb/dive
            // trend forward from `transform.position`, which is exactly "leave the altitude alone" here.
            Vector3 ahead = transform.position + (target - transform.position).normalized * LookAhead;
            if (TryFloorAt(ahead, out float aheadFloor)) ahead.y = Mathf.Max(ahead.y, aheadFloor + _dropHeight);

            Vector3 heading = (ahead - transform.position).normalized;
            Quaternion want = Quaternion.LookRotation(heading);
            Vector3 to = heading; to.y = 0f; to.Normalize();
            Vector3 from = transform.forward; from.y = 0f; from.Normalize();
            float bank = Mathf.Clamp(Vector3.SignedAngle(from, to, Vector3.up), -BankErrorCap, BankErrorCap) / BankErrorCap;
            want = Quaternion.Euler(0f, 0f, bank * MaxBankDegrees) * want;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, want, (_dropped ? _turnRate * 4f : _turnRate) * dt);

            Vector3 velocity = transform.forward * _speed;
            Vector3 next = transform.position + velocity * dt;
            // F7: same rule. `next.y` already carries forward from `transform.position.y` via the pitch
            // the steering above just chose, so an unknown floor means "trust that", not "assume p.y is
            // also the ground" -- the self-reference that used to add dropHeight back in every step.
            if (TryFloorAt(next, out float nextFloor)) next.y = Mathf.Max(next.y, nextFloor + _dropHeight);
            transform.position = next;

            // So every other screen sees a bird gliding rather than stepping. See the class comment.
            ZDO zdo = _nview.GetZDO();
            if (zdo != null) zdo.Set(ZDOVars.s_velHash, velocity);
        }

        /// <summary>
        /// The owner puts him down: mark the bird dropped, write the drop point the merchant will stand
        /// on, and cut the carry link so `CargoMerchant` stops pinning him to the talons and falls the
        /// last few metres. The server reads `VCargo_dropped` on its next tick and moves the visit's phase.
        /// </summary>
        private void Drop()
        {
            _dropped = true;
            _descent = true;

            // F7: if the ground under the drop point is not known yet, keep the AUTHORED y (the pilot's
            // own altitude plus DropAltitude, from FlightPlan.Make) rather than overwrite it with
            // anything -- a guess here is what P5 stands the merchant on.
            Vector3 at = _drop;
            if (TryFloorAt(at, out float dropFloor)) at.y = dropFloor;
            ZDO zdo = _nview.GetZDO();
            if (zdo != null)
            {
                zdo.Set(Spawner.TargetHash, at);
                zdo.Set(Spawner.DroppedHash, true);
            }

            // The merchant is ours to write too - the server authored him owned by this same pilot.
            try
            {
                ZDO npc = ZDOMan.instance != null && !Spawner.Merchant.IsNone() ? ZDOMan.instance.GetZDO(Spawner.Merchant) : null;
                if (npc == null) npc = FindMerchantByCarrier();
                if (npc != null && npc.IsValid())
                {
                    npc.Set(Spawner.CarrierKey, ZDOID.None);
                    npc.Set(Spawner.StateHash, MerchantState.Approaching);
                }
                else
                {
                    // Spawner.Merchant is server-side state, so on a client the scan above is the
                    // only lookup, and a miss used to be silent. He stays pinned until this bird is
                    // gone and then lands on his own (MerchantPlan: carried -> landed once the
                    // carrier no longer resolves), so the visit survives; but the walk-up starts
                    // from wherever the bird left him, and the log must say so.
                    ValkyriesCargo.Log.LogWarning("cargo flight #" + _visitId + ": the drop found no merchant naming this bird as his carrier " +
                        "among " + Character.GetAllCharacters().Count + " instanced characters; he stays carried until the bird is gone");
                }
            }
            catch (Exception ex)
            {
                if (_throws++ < 3) ValkyriesCargo.Log.LogWarning("cargo flight #" + _visitId + ": cutting the carry link threw: " + ex.Message);
            }

            if (_animator != null) _animator.SetBool("dropped", true);
            _animated = true;
            ValkyriesCargo.Log.LogInfo("cargo flight #" + _visitId + ": dropped at " + Vec(at) + " after " + Wire.Float(_flying) + " s");
        }

        /// <summary>
        /// `Spawner`'s ids live on the server, so on a pure client this is the ONLY path -- the lookup
        /// above only ever hits on a listen host. Find the merchant the way any machine can: the
        /// instantiated character whose ZDO names this bird as its carrier.
        /// </summary>
        private ZDO FindMerchantByCarrier()
        {
            ZDOID me = _nview.GetZDO().m_uid;
            foreach (Character c in Character.GetAllCharacters())
            {
                if (c == null) continue;
                ZNetView nv = c.GetComponent<ZNetView>();
                ZDO z = nv != null ? nv.GetZDO() : null;
                if (z != null && z.GetZDOID(Spawner.CarrierKey) == me) return z;
            }
            return null;
        }

        /// <summary>
        /// The engine half of F7 (2026-09-07): run the ground raycast and hand its answer to the pure
        /// `FlightPlan.TryFloor` for the actual decision. Ground or water, whichever is higher, when the
        /// raycast succeeds; "leave the altitude alone" -- signalled by returning false, never by a
        /// sentinel float -- when it does not, e.g. a freshly generated zone at the flight's own 90 m
        /// start whose terrain collider has not finished building
        /// (`ZoneSystem.GetGroundHeight(Vector3, out float)`, decompiled 2026-09-06/07: returns false
        /// with `height = 0` on a raycast miss). Logged once per bird so a screen run explains a bird
        /// that sat still on altitude rather than looking like a silent no-op (debugging discipline).
        /// </summary>
        private bool TryFloorAt(Vector3 p, out float floor)
        {
            ZoneSystem zs = ZoneSystem.instance;
            if (zs == null) { floor = 0f; return false; }
            bool groundKnown = zs.GetGroundHeight(p, out float ground);
            bool known = FlightPlan.TryFloor(groundKnown, ground, zs.m_waterLevel, out floor);
            if (!known && !_loggedFloorUnknown)
            {
                _loggedFloorUnknown = true;
                ValkyriesCargo.Log.LogInfo("cargo flight #" + _visitId + ": ground unknown at " + Vec(p) +
                                            " (the zone has likely not finished generating); the altitude is left alone until a later step's raycast succeeds");
            }
            return known;
        }

        private static float DistanceXZ(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static string Vec(Vector3 v) => "(" + Wire.Float(v.x) + ", " + Wire.Float(v.y) + ", " + Wire.Float(v.z) + ")";
    }
}
