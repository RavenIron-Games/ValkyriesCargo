using System;
using System.Collections.Generic;

namespace RavenIron.ValkyriesCargo.Core
{
    /// <summary>
    /// Ingvar's six clips, under the exact names the bundle carries them (models/README.md section 1;
    /// `IngvarBundleBuilder` tags the FBX whole, so the clip names are the FBX's take names).
    /// `None` is "no one-shot is playing" and is never a mixer input.
    /// </summary>
    public enum BodyClip { None = -1, Idle = 0, Walk = 1, Hello = 2, Talk = 3, Shrug = 4, Nod = 5 }

    /// <summary>
    /// PURE: the weights Ingvar's six clips are played at, and nothing else. No Unity types, no time
    /// source, no clips - the driver (Client/IngvarBody.cs) feeds it a delta and a measured speed and
    /// reads the six numbers back out.
    ///
    /// Two things happen at once and are kept apart:
    ///
    /// - **Locomotion.** A smoothed planar speed picks Idle or Walk through a hysteresis band
    ///   (idle below 0.05 m/s, walk above 0.06; models/README.md section 7), and `WalkBlend` crossfades
    ///   between them over `CrossfadeSeconds`. The speed is SMOOTHED here, not by the caller, so the
    ///   filter is testable off-game: a per-frame displacement is noisy and would flicker the blend.
    /// - **One-shots.** Hello, Talk, Shrug and Nod blend in over `OneShotBlendSeconds`, own the whole
    ///   body at full weight, and hand back at `HandBackFraction` of the clip's own length. They cannot
    ///   interrupt themselves; a different one replaces a running one without a gap.
    ///
    /// The two never overlap, so the six weights sum to exactly 1 by construction:
    /// locomotion is scaled by `1 - shot`, the one-shot takes `shot`.
    ///
    /// Clip lengths are PASSED IN (`SetLength`) because the shipping clips are the truth; the constants
    /// below are only what to fall back on when a bundle is missing or a clip did not come through.
    /// </summary>
    public sealed class BodyMotion
    {
        /// <summary>Mixer inputs: Idle, Walk and the four one-shots.</summary>
        public const int ClipCount = 6;

        // ---- the numbers (models/README.md section 7; design 11.4) --------------------------------

        /// <summary>Below this smoothed speed he is idling.</summary>
        public const float IdleBelow = 0.05f;
        /// <summary>Above this smoothed speed he is walking. The gap between the two is the hysteresis.</summary>
        public const float WalkAbove = 0.06f;
        /// <summary>Seconds for a full Idle-Walk crossfade.</summary>
        public const float CrossfadeSeconds = 0.15f;
        /// <summary>Time constant of the speed filter: a frame's displacement is noise, a sixth of a second is a gait.</summary>
        public const float SpeedSmoothingSeconds = 0.15f;
        /// <summary>Seconds for a one-shot to blend in, and the same to blend back out.</summary>
        public const float OneShotBlendSeconds = 0.06f;
        /// <summary>The fraction of a one-shot's length at which it starts handing back to Idle/Walk.</summary>
        public const float HandBackFraction = 0.85f;
        /// <summary>The largest step a single Tick may take. A hitch or an alt-tab must not teleport the state machine.</summary>
        public const float MaxStepSeconds = 0.25f;

        // ---- fallback lengths, in seconds (models/README.md section 1) ----------------------------
        // The driver overwrites every one of these with the clip's own `length` at load. They exist so
        // the model is usable - and testable - with no bundle on the machine.

        public const float IdleLength  = 10.00f;
        public const float WalkLength  =  4.21f;
        public const float TalkLength  =  5.17f;
        public const float HelloLength =  3.79f;
        public const float ShrugLength =  2.00f;
        public const float NodLength   =  1.25f;

        private readonly float[] _lengths =
        {
            IdleLength, WalkLength, HelloLength, TalkLength, ShrugLength, NodLength,
        };

        private readonly float[] _weights = new float[ClipCount];

        private float _speed;        // the smoothed planar speed
        private bool _walking;       // which side of the hysteresis band we are on
        private float _walkBlend;    // 0 = all Idle, 1 = all Walk
        private BodyClip _shot = BodyClip.None;
        private float _shotWeight;   // 0..1: how much of the body the one-shot owns
        private float _shotTime;     // seconds into the one-shot's clip
        private bool _releasing;     // past the hand-back point, blending out

        public BodyMotion() { Recompute(); }

        // ---- what the driver reads ---------------------------------------------------------------

        /// <summary>The filtered planar speed the blend is decided from, m/s.</summary>
        public float SmoothedSpeed => _speed;
        /// <summary>Which side of the hysteresis band the locomotion target is on.</summary>
        public bool Walking => _walking;
        /// <summary>The crossfade position: 0 all Idle, 1 all Walk.</summary>
        public float WalkBlend => _walkBlend;
        /// <summary>The one-shot playing, or None.</summary>
        public BodyClip Current => _shot;
        /// <summary>How much of the body the one-shot owns right now, 0..1.</summary>
        public float ShotWeight => _shotWeight;
        /// <summary>Seconds into the one-shot's clip; what the driver sets the clip playable's time to.</summary>
        public float ShotTime => _shotTime;
        /// <summary>The one-shot's progress through its own length, 0..1 (0 when none is playing).</summary>
        public float ShotNormalized
        {
            get
            {
                if (_shot == BodyClip.None) return 0f;
                float len = Length(_shot);
                return len > 0f ? _shotTime / len : 0f;
            }
        }
        /// <summary>The six weights, indexed by (int)BodyClip. They sum to 1.</summary>
        public IReadOnlyList<float> Weights => _weights;

        public float Weight(BodyClip clip)
        {
            int i = (int)clip;
            return i >= 0 && i < ClipCount ? _weights[i] : 0f;
        }

        // ---- clip lengths ------------------------------------------------------------------------

        public float Length(BodyClip clip)
        {
            int i = (int)clip;
            return i >= 0 && i < ClipCount ? _lengths[i] : 0f;
        }

        /// <summary>Take a clip's real length from the bundle. A nonsense length is ignored and the fallback kept.</summary>
        public void SetLength(BodyClip clip, float seconds)
        {
            int i = (int)clip;
            if (i < 0 || i >= ClipCount) return;
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0f) return;
            _lengths[i] = seconds;
        }

        // ---- the public one-shots (P5 calls these on every machine) --------------------------------

        /// <summary>
        /// Start a one-shot. False when it is not a one-shot, or when that same one is already running:
        /// a wave cannot interrupt itself. A DIFFERENT one-shot replaces a running one at its current
        /// blend weight, so the swap has no gap in it.
        /// </summary>
        public bool Fire(BodyClip clip)
        {
            if (!IsOneShot(clip)) return false;
            if (_shot == clip) return false;
            _shot = clip;
            _shotTime = 0f;
            _releasing = false;
            Recompute();
            return true;
        }

        /// <summary>Drop a running one-shot at once (a visit ending, a body being taken down).</summary>
        public void CancelOneShot()
        {
            _shot = BodyClip.None;
            _shotWeight = 0f;
            _shotTime = 0f;
            _releasing = false;
            Recompute();
        }

        /// <summary>Back to a standing start: idle, no one-shot, no remembered speed.</summary>
        public void Reset()
        {
            _speed = 0f;
            _walking = false;
            _walkBlend = 0f;
            CancelOneShot();
        }

        // ---- the tick ----------------------------------------------------------------------------

        /// <summary>
        /// Advance by `dt` seconds with a RAW measured planar speed. A NaN, infinite or absurd delta is
        /// clamped away; a NaN speed sample is dropped (the filter holds its last value) rather than
        /// poisoning the blend, and a negative one is treated as standing still.
        /// </summary>
        public void Tick(float dt, float speed)
        {
            if (float.IsNaN(dt) || float.IsInfinity(dt) || dt < 0f) dt = 0f;
            if (dt > MaxStepSeconds) dt = MaxStepSeconds;

            // The filter: hold on a bad sample, clamp a negative one. exp() keeps the response the same
            // whatever the frame rate, which a naive lerp does not.
            if (!float.IsNaN(speed) && !float.IsInfinity(speed))
            {
                if (speed < 0f) speed = 0f;
                float k = dt > 0f ? 1f - (float)Math.Exp(-dt / SpeedSmoothingSeconds) : 0f;
                _speed += (speed - _speed) * k;
            }

            // Hysteresis: inside the band, keep whatever we were doing.
            if (_speed > WalkAbove) _walking = true;
            else if (_speed < IdleBelow) _walking = false;

            float step = CrossfadeSeconds > 0f ? dt / CrossfadeSeconds : 1f;
            float target = _walking ? 1f : 0f;
            if (_walkBlend < target) _walkBlend = Math.Min(target, _walkBlend + step);
            else if (_walkBlend > target) _walkBlend = Math.Max(target, _walkBlend - step);

            if (_shot != BodyClip.None)
            {
                _shotTime += dt;
                if (!_releasing && _shotTime >= HandBackFraction * Length(_shot)) _releasing = true;

                float blend = OneShotBlendSeconds > 0f ? dt / OneShotBlendSeconds : 1f;
                if (_releasing)
                {
                    _shotWeight -= blend;
                    if (_shotWeight <= 0f) { _shotWeight = 0f; _shot = BodyClip.None; _shotTime = 0f; _releasing = false; }
                }
                else
                {
                    _shotWeight = Math.Min(1f, _shotWeight + blend);
                }
            }

            Recompute();
        }

        private void Recompute()
        {
            float loco = 1f - _shotWeight;
            for (int i = 0; i < ClipCount; i++) _weights[i] = 0f;
            _weights[(int)BodyClip.Idle] = loco * (1f - _walkBlend);
            _weights[(int)BodyClip.Walk] = loco * _walkBlend;
            if (_shot != BodyClip.None) _weights[(int)_shot] = _shotWeight;
        }

        // ---- names (the bundle's, and the console's) ----------------------------------------------

        public static bool IsOneShot(BodyClip clip) =>
            clip == BodyClip.Hello || clip == BodyClip.Talk || clip == BodyClip.Shrug || clip == BodyClip.Nod;

        /// <summary>The name the clip carries INSIDE the bundle. Getting one wrong loses that clip silently.</summary>
        public static string ClipName(BodyClip clip)
        {
            switch (clip)
            {
                case BodyClip.Idle:  return "Idle";
                case BodyClip.Walk:  return "Walk";
                case BodyClip.Hello: return "Hello";
                case BodyClip.Talk:  return "Talk";
                case BodyClip.Shrug: return "Shrug";
                case BodyClip.Nod:   return "Nod";
                default:             return "";
            }
        }

        /// <summary>The console's side of ClipName; case-insensitive, None for anything else.</summary>
        public static BodyClip ByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return BodyClip.None;
            for (int i = 0; i < ClipCount; i++)
            {
                var c = (BodyClip)i;
                if (string.Equals(ClipName(c), name, StringComparison.OrdinalIgnoreCase)) return c;
            }
            return BodyClip.None;
        }
    }
}
