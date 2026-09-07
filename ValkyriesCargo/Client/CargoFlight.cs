using System;
using RavenIron.ValkyriesCargo.Core;
using RavenIron.ValkyriesCargo.Server;
using UnityEngine;

namespace RavenIron.ValkyriesCargo.Client
{
    /// <summary>
    /// Our Valkyrie's flight (design 3.2), added by `Patch_Valkyrie_Awake` to a bird whose ZDO carries
    /// `vc_cargo`, in place of the vanilla component we skipped. It lives and dies with the bird, so it
    /// owns no timer that outlives its object (house rule 2 is about long-lived timers; vanilla flies its
    /// own Valkyrie from `FixedUpdate` and so does this).
    ///
    /// The maths is vanilla `UpdateValkyrie`, read from the decompile and kept: three waypoints, a 25 m
    /// look-ahead clamped to the ground, a banked turn capped at 30 degrees of error mapped to 45 degrees
    /// of roll, linear steps at `m_speed`, arrival on 0.5 m of XZ distance. What differs is only what the
    /// design changes: the waypoints come from the server's authored `vc_target` instead of a re-rolled
    /// 500 m approach, the floor is `max(ground, water) + m_dropHeight`, and nothing anywhere touches
    /// `Player.m_localPlayer`.
    ///
    /// Only the OWNER flies. Everyone else is moved by `ZSyncTransform` and only watches `vc_dropped` to
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
        /// <summary>Vanilla's `m_dropHeight`: ground clearance the whole way, not just at the drop.</summary>
        public const float DropHeight = 10f;
        public const float LookAhead = 25f;
        public const float ArriveDistance = 0.5f;
        public const float MaxBankDegrees = 45f;
        public const float BankErrorCap = 30f;

        /// <summary>A flight that has not finished by now has lost its way; the bird leaves rather than circling forever.</summary>
        public const float MaxFlightSeconds = 180f;

        private ZNetView _nview;
        private Valkyrie _valkyrie;
        private Animator _animator;
        private Vector3 _drop, _descentStart, _away;
        private bool _descent, _dropped, _animated;
        private float _speed = 10f, _turnRate = 5f;
        private float _flying;
        private int _visitId;
        private int _throws;

        /// <summary>Where the merchant hangs from (design 3.3). The prefab's attach point if it has one, the bird itself otherwise.</summary>
        public Transform AttachPoint => _valkyrie != null && _valkyrie.m_attachPoint != null ? _valkyrie.m_attachPoint : transform;

        /// <summary>The offset the merchant hangs at, in the attach point's space. Vanilla's own value for a passenger.</summary>
        public Vector3 AttachOffset => _valkyrie != null ? _valkyrie.m_attachOffset : new Vector3(0f, 0f, 1f);

        public bool HasDropped => _dropped;
        public int VisitId => _visitId;

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _valkyrie = GetComponent<Valkyrie>();
            _animator = GetComponentInChildren<Animator>();
            if (_valkyrie != null)
            {
                // The prefab's own tuning, not constants of ours: if a game update retunes the bird, ours
                // flies the same way it does.
                _speed = _valkyrie.m_speed;
                _turnRate = _valkyrie.m_turnRate;
            }

            ZDO zdo = _nview != null ? _nview.GetZDO() : null;
            if (zdo == null) { enabled = false; return; }
            _visitId = zdo.GetInt(Spawner.CargoHash, 0);
            _drop = zdo.GetVec3(Spawner.TargetHash, transform.position);
            _dropped = zdo.GetBool(Spawner.DroppedHash, false);

            // The turn-in point, rebuilt from where we start and where we are going, so it needs no key
            // of its own: the same geometry FlightPlan used, minus the block clamp the server applied.
            Vector3 flat = _drop - transform.position; flat.y = 0f;
            float run = flat.magnitude;
            Vector3 dir = run > 0.01f ? flat / run : transform.forward;
            Vector3 lateral = Vector3.Cross(dir, Vector3.up);
            float descent = Mathf.Clamp(run * 0.5f, 0f, 50f);
            _descentStart = _drop - dir * descent + lateral * descent;
            _descentStart.y = transform.position.y;
            _away = _drop - dir * (run * 2f + 40f);
            _away.y = transform.position.y;
            _descent = _dropped;

            ValkyriesCargo.Log.LogInfo("cargo flight #" + _visitId + ": " + (_nview.IsOwner() ? "flying" : "watching") +
                                       " from " + Vec(transform.position) + " to " + Vec(_drop) + ", " + Wire.Float(run) + " m out");
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
            Vector3 ahead = transform.position + (target - transform.position).normalized * LookAhead;
            ahead.y = Mathf.Max(ahead.y, Floor(ahead) + DropHeight);

            Vector3 heading = (ahead - transform.position).normalized;
            Quaternion want = Quaternion.LookRotation(heading);
            Vector3 to = heading; to.y = 0f; to.Normalize();
            Vector3 from = transform.forward; from.y = 0f; from.Normalize();
            float bank = Mathf.Clamp(Vector3.SignedAngle(from, to, Vector3.up), -BankErrorCap, BankErrorCap) / BankErrorCap;
            want = Quaternion.Euler(0f, 0f, bank * MaxBankDegrees) * want;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, want, (_dropped ? _turnRate * 4f : _turnRate) * dt);

            Vector3 velocity = transform.forward * _speed;
            Vector3 next = transform.position + velocity * dt;
            next.y = Mathf.Max(next.y, Floor(next) + DropHeight);
            transform.position = next;

            // So every other screen sees a bird gliding rather than stepping. See the class comment.
            ZDO zdo = _nview.GetZDO();
            if (zdo != null) zdo.Set(ZDOVars.s_velHash, velocity);
        }

        /// <summary>
        /// The owner puts him down: mark the bird dropped, write the drop point the merchant will stand
        /// on, and cut the carry link so `CargoMerchant` stops pinning him to the talons and falls the
        /// last few metres. The server reads `vc_dropped` on its next tick and moves the visit's phase.
        /// </summary>
        private void Drop()
        {
            _dropped = true;
            _descent = true;

            Vector3 at = _drop;
            at.y = Floor(at);
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

        /// <summary>Ground or water, whichever is higher: he is never carried below the sea.</summary>
        private static float Floor(Vector3 p)
        {
            float ground = p.y;
            ZoneSystem zs = ZoneSystem.instance;
            if (zs != null)
            {
                if (!zs.GetGroundHeight(p, out ground)) ground = p.y;
                ground = Mathf.Max(ground, zs.m_waterLevel);
            }
            return ground;
        }

        private static float DistanceXZ(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static string Vec(Vector3 v) => "(" + Wire.Float(v.x) + ", " + Wire.Float(v.y) + ", " + Wire.Float(v.z) + ")";
    }
}
