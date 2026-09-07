using System;
using RavenIron.ValkyriesCargo.Core;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace RavenIron.ValkyriesCargo.Client
{
    /// <summary>
    /// Plays Ingvar's six clips. Added by `BodyLoader.Attach` to the body it hangs under the merchant,
    /// and by `cargo body preview` to a body standing on its own.
    ///
    /// **No AnimatorController anywhere.** The bundle carries the model and the clips and nothing else
    /// (`IngvarBundleBuilder` tags the FBX and the texture; it builds no controller and does not need
    /// to). The six clips are played through a `PlayableGraph`: one `AnimationPlayableOutput` on the
    /// prefab's own `Animator`, an `AnimationMixerPlayable` with one `AnimationClipPlayable` per clip
    /// found BY NAME. A clip the bundle does not carry is logged once and stays at weight 0; the rest
    /// carry on. That is why the animator contract (design 11.4) is "his own": nothing here touches
    /// vanilla's parameter set and nothing maps him onto the Dverger rig.
    ///
    /// **Speed comes from displacement, not from the network.** The blend is decided from how far this
    /// transform actually moved this frame, so every machine derives the same walk from the same
    /// replicated position: no `ZSyncAnimation` float, no extra ZDO key, nothing to desync. The maths
    /// is `Core/BodyMotion.cs`, decided off-game.
    ///
    /// `applyRootMotion = false` - the server owns position and a clip that walks the transform forward
    /// fights the netcode; every clip is in-place anyway (models/README.md section 1).
    ///
    /// House rule 2 is about long-lived timers. This component lives and dies with its own GameObject
    /// and updates only itself, the shape `CargoFlight` already uses.
    /// </summary>
    public sealed class IngvarBody : MonoBehaviour
    {
        /// <summary>How often the stand-in's renderers are switched off again. VisEquipment rebuilds them on any equipment change.</summary>
        public const float RehideSeconds = 2f;

        private readonly BodyMotion _motion = new BodyMotion();

        private Animator _animator;
        private Character _owner;
        private PlayableGraph _graph;
        private AnimationMixerPlayable _mixer;
        private readonly AnimationClipPlayable[] _players = new AnimationClipPlayable[BodyMotion.ClipCount];
        private readonly bool[] _have = new bool[BodyMotion.ClipCount];

        private Vector3 _lastPos;
        private bool _hasLast;
        private float _rehideIn;
        private int _throws;

        /// <summary>NaN = off. Any other value overrides the measured speed, so a body that cannot move can still be seen walking.</summary>
        public float SimulatedSpeed = float.NaN;

        /// <summary>The smoothed planar speed the blend is decided from, m/s.</summary>
        public float Speed => _motion.SmoothedSpeed;
        /// <summary>The one-shot playing, or None.</summary>
        public BodyClip CurrentClip => _motion.Current;
        public bool Walking => _motion.Walking;
        public float WalkBlend => _motion.WalkBlend;
        public bool GraphLive { get; private set; }
        /// <summary>How many of the six clips the bundle actually carried.</summary>
        public int ClipsBound { get; private set; }
        /// <summary>The character this body was hung on, or null for a preview.</summary>
        public Character Owner => _owner;

        // ---- the public one-shots: P5 calls these on EVERY machine from the ZDO state and VCargo_say ----

        public bool Greet() => Fire(BodyClip.Hello);
        public bool Talk()  => Fire(BodyClip.Talk);
        public bool Shrug() => Fire(BodyClip.Shrug);
        public bool Nod()   => Fire(BodyClip.Nod);

        /// <summary>Start a one-shot. False when it is already running (a gesture cannot interrupt itself) or the clip is missing.</summary>
        public bool Fire(BodyClip clip)
        {
            try
            {
                int i = (int)clip;
                if (i < 0 || i >= BodyMotion.ClipCount || !_have[i]) return false;
                if (!_motion.Fire(clip)) return false;
                if (GraphLive) _players[i].SetTime(0.0);
                return true;
            }
            catch (Exception ex) { Complain("firing " + clip, ex); return false; }
        }

        /// <summary>Called by BodyLoader right after AddComponent. A preview never gets one.</summary>
        public void Bind(Character owner)
        {
            _owner = owner;
            _rehideIn = RehideSeconds;
        }

        // ---- Unity ----------------------------------------------------------------------------------

        private void Awake()
        {
            try
            {
                // The FBX imports with an Animator on its root. Add one only if the bake somehow lost it:
                // AnimationPlayableOutput needs a target and there is nothing else to aim at.
                _animator = GetComponentInChildren<Animator>(true);
                if (_animator == null) _animator = gameObject.AddComponent<Animator>();
                _animator.applyRootMotion = false;
                _animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                _animator.logWarnings = false;
                BuildGraph();
            }
            catch (Exception ex) { Complain("building the animation graph", ex); }
        }

        private void BuildGraph()
        {
            _graph = PlayableGraph.Create("ValkyriesCargo.IngvarBody");
            _mixer = AnimationMixerPlayable.Create(_graph, BodyMotion.ClipCount);
            AnimationPlayableOutput output = AnimationPlayableOutput.Create(_graph, "Ingvar", _animator);
            output.SetSourcePlayable(_mixer);

            for (int i = 0; i < BodyMotion.ClipCount; i++)
            {
                var which = (BodyClip)i;
                AnimationClip clip = BodyLoader.Clip(BodyMotion.ClipName(which));
                if (clip == null) continue;                       // logged once by the loader; weight stays 0

                _players[i] = AnimationClipPlayable.Create(_graph, clip);
                _players[i].SetApplyFootIK(false);                // there is no IK anywhere in this, on either side
                // Idle and Walk run themselves so their loops stay seamless; a one-shot's time is set from
                // the model every frame, so its own clock is stopped or the two would advance it twice.
                if (BodyMotion.IsOneShot(which)) _players[i].SetSpeed(0.0);
                _graph.Connect(_players[i], 0, _mixer, i);
                _have[i] = true;
                ClipsBound++;
                _motion.SetLength(which, clip.length);            // the clip's OWN length, not the constant
            }

            ApplyWeights();
            _graph.Play();
            GraphLive = true;
        }

        private void Update()
        {
            try
            {
                float dt = Time.deltaTime;
                _motion.Tick(dt, MeasureSpeed(dt));
                ApplyWeights();

                if (_owner != null)
                {
                    _rehideIn -= dt;
                    if (_rehideIn <= 0f)
                    {
                        _rehideIn = RehideSeconds;
                        BodyLoader.HideStandIn(_owner.transform, transform);
                    }
                }
            }
            catch (Exception ex) { Complain("ticking", ex); }
        }

        /// <summary>
        /// How far this transform moved on the plane, per second. Not the agent's velocity and not a
        /// network value: the replicated position is the one thing every machine agrees on, so deriving
        /// the blend from it is what makes the walk identical on every screen.
        /// </summary>
        private float MeasureSpeed(float dt)
        {
            Vector3 now = transform.position;
            if (!_hasLast) { _lastPos = now; _hasLast = true; return 0f; }
            Vector3 moved = now - _lastPos;
            _lastPos = now;
            if (!float.IsNaN(SimulatedSpeed)) return SimulatedSpeed;
            moved.y = 0f;
            return dt > 0.0001f ? moved.magnitude / dt : 0f;
        }

        private void ApplyWeights()
        {
            if (!_mixer.IsValid()) return;
            for (int i = 0; i < BodyMotion.ClipCount; i++)
                if (_have[i]) _mixer.SetInputWeight(i, _motion.Weight((BodyClip)i));

            BodyClip shot = _motion.Current;
            int s = (int)shot;
            if (s >= 0 && s < BodyMotion.ClipCount && _have[s]) _players[s].SetTime(_motion.ShotTime);
        }

        private void OnDestroy()
        {
            GraphLive = false;
            try { if (_graph.IsValid()) _graph.Destroy(); }
            catch (Exception ex) { Complain("destroying the animation graph", ex); }
            // The bundle is never unloaded: an Unload while any instance is alive takes the mesh and the
            // material out from under it (models/README.md section 6).
        }

        /// <summary>House rule 3: cosmetics off the gameplay path, at most three complaints.</summary>
        private void Complain(string what, Exception ex)
        {
            if (_throws++ < 3) ValkyriesCargo.Log.LogError("body: " + what + " threw: " + ex);
        }
    }
}
