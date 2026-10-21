using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace FishGame
{
    /// <summary>
    /// The game-feel layer on this machine's view - everything here is local and cosmetic, nothing is networked:
    ///   * <b>Camera shake</b> (<see cref="Shake"/>): "trauma" that decays on its own; the shake is trauma squared, so
    ///     small hits stay small and big ones really kick. Smooth Perlin noise, rotation plus a little position.
    ///     <see cref="Tremor"/> holds a floor for as long as it's called (the sand slide's quake).
    ///   * <b>Gamepad rumble</b>: follows the shake, so every shake event rumbles to its strength.
    ///   * <b>FOV</b>: widens a little with speed above cruise, and <see cref="FovKick"/> punches it out (a lunge,
    ///     tearing free of the jaws), easing back.
    ///   * <b>Hit stop</b> (<see cref="HitStop"/>): the bodies in a hit freeze for a few frames. Not Time.timeScale -
    ///     the host runs everyone's match, so that would freeze the game for all. Instead the motor of a body this
    ///     machine drives holds still (the camera follows it, so the view holds too) and the bodies' animators pause.
    ///   * <b>Speed streaks</b> around the camera when swimming fast, and <see cref="Streak"/>: water torn off the
    ///     shark's fins on a lunge.
    /// The Screen Shake setting (<see cref="ShakeScale"/>, 0-1) scales the shake and the rumble.
    /// Screen flashes and the low-health vignette are the HUD's (<see cref="MatchHud.HurtFlash"/>).
    /// </summary>
    [DefaultExecutionOrder(10001)] // after the orbit camera has placed the view (and ReefConditions, 10000)
    public class GameFeel : MonoBehaviour
    {
        public const string ShakePref = "rr_screen_shake";

        static GameFeel s_instance;
        static float s_shakeScale = -1f;

        /// <summary>Screen Shake setting, 0 (off) .. 1 (full). Scales camera shake and gamepad rumble.</summary>
        public static float ShakeScale
        {
            get
            {
                if (s_shakeScale < 0f) s_shakeScale = Mathf.Clamp01(PlayerPrefs.GetFloat(ShakePref, 1f));
                return s_shakeScale;
            }
            set
            {
                s_shakeScale = Mathf.Clamp01(value);
                PlayerPrefs.SetFloat(ShakePref, s_shakeScale);
            }
        }

        // Shake shape: the most a full-trauma shake turns (degrees) and moves (metres) the view.
        const float MaxYaw = 2.6f, MaxPitch = 2.2f, MaxRoll = 3.2f, MaxOffset = 0.22f;
        const float ShakeFrequency = 22f;   // noise samples per second
        const float TraumaDecay = 1.5f;     // trauma lost per second

        // FOV: degrees added at top sprint speed, and how fast kicks / speed changes ease.
        const float SpeedFov = 6f, FovEase = 6f, KickDecay = 4.5f;

        float _trauma, _tremor;
        int _tremorFrame = -10;
        float _seed;

        Camera _fovCam;
        float _baseFov, _speedFov, _kick;
        float _speedNow;   // 0 at cruise .. 1 at full sprint, this frame (the FOV eases toward it)

        bool _rumbling;

        readonly List<(Animator animator, float speed, float until)> _frozen = new List<(Animator, float, float)>();

        ParticleSystem _speedLines, _streaks;
        ParticleSystem.EmitParams _emit;
        float _lineDebt;
        bool _fxBuilt;

        static GameFeel Instance
        {
            get
            {
                if (s_instance == null && Application.isPlaying)
                    s_instance = new GameObject("GameFeel").AddComponent<GameFeel>(); // in the scene: goes with it
                return s_instance;
            }
        }

        // ================================================================== API

        /// <summary>Start it up for this scene (the speed FOV and streaks run every frame, not only on events).</summary>
        public static void Ensure() => _ = Instance;

        /// <summary>Add camera trauma (0-1). A light knock is ~0.15, a bite ~0.45, being caught ~0.8.</summary>
        public static void Shake(float trauma)
        {
            if (trauma <= 0f) return;
            var g = Instance;
            if (g != null) g._trauma = Mathf.Clamp01(g._trauma + trauma);
        }

        /// <summary>Hold the shake at least at <paramref name="trauma"/> this frame (call every frame, e.g. a quake).</summary>
        public static void Tremor(float trauma)
        {
            var g = Instance;
            if (g == null) return;
            g._tremor = Mathf.Clamp01(trauma);
            g._tremorFrame = Time.frameCount;
        }

        /// <summary>Punch the field of view out by <paramref name="degrees"/>; it eases back on its own.</summary>
        public static void FovKick(float degrees)
        {
            var g = Instance;
            if (g != null) g._kick = Mathf.Max(g._kick, degrees);
        }

        /// <summary>Freeze the bodies in a hit for <paramref name="seconds"/>: the motor of one this machine drives
        /// holds still, and their animators (and procedural swimming) pause.</summary>
        public static void HitStop(float seconds, GameObject a, GameObject b = null)
        {
            var g = Instance;
            if (g == null || seconds <= 0f) return;
            g.Freeze(a, seconds);
            g.Freeze(b, seconds);
        }

        /// <summary>Is <paramref name="body"/> the creature this machine's camera follows (our own, not spectating)?</summary>
        public static bool IsViewTarget(Transform body)
        {
            var orbit = ViewOrbit();
            return orbit != null && body != null && orbit.SpectateTarget == null && orbit.Target == body;
        }

        /// <summary>Is <paramref name="body"/> the creature this machine is watching - our own, or the shark eating us?</summary>
        public static bool IsWatched(Transform body)
        {
            var orbit = ViewOrbit();
            return orbit != null && body != null && orbit.Target == body;
        }

        /// <summary>A streak of torn water: a speck moving at <paramref name="velocity"/>, stretched along it.</summary>
        public static void Streak(Vector3 position, Vector3 velocity, float size, float life)
        {
            var g = Instance;
            if (g == null || position.y > WaterFx.SurfaceY - 0.1f || !g.EnsureFx() || g._streaks == null) return;
            g._emit.position = position;
            g._emit.velocity = velocity;
            g._emit.startSize = size;
            g._emit.startLifetime = life;
            g._emit.startColor = new Color(1f, 1f, 1f, 0.75f);
            g._streaks.Emit(g._emit, 1);
        }

        static FishOrbitCamera ViewOrbit()
        {
            var cam = WaterFx.ViewCamera;
            return cam != null ? cam.GetComponent<FishOrbitCamera>() : null;
        }

        // ================================================================== per frame

        void Awake() => _seed = Random.value * 100f;

        void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
            RestoreFov();
            StopRumble();
            foreach (var f in _frozen)
                if (f.animator != null) f.animator.speed = f.speed;
            _frozen.Clear();
        }

        void OnApplicationFocus(bool focus)
        {
            if (!focus) StopRumble();
        }

        void Update()
        {
            // Animators whose hit stop is over get their speed back.
            float now = Time.unscaledTime;
            for (int i = _frozen.Count - 1; i >= 0; i--)
            {
                var f = _frozen[i];
                if (f.animator == null) { _frozen.RemoveAt(i); continue; }
                if (now < f.until) continue;
                f.animator.speed = f.speed;
                _frozen.RemoveAt(i);
            }
        }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            _trauma = Mathf.Max(0f, _trauma - TraumaDecay * dt);
            float trauma = Mathf.Max(_trauma, Time.frameCount - _tremorFrame <= 1 ? _tremor : 0f);
            float shake = trauma * trauma * ShakeScale;

            var cam = WaterFx.ViewCamera;
            var orbit = cam != null ? cam.GetComponent<FishOrbitCamera>() : null;
            bool paused = PauseMenu.IsOpen;

            if (orbit != null && shake > 0.0001f)
            {
                float t = Time.time * ShakeFrequency;
                float yaw = MaxYaw * shake * Noise(_seed, t);
                float pitch = MaxPitch * shake * Noise(_seed + 11f, t);
                float roll = MaxRoll * shake * Noise(_seed + 23f, t);
                Transform ct = cam.transform;
                // Closer in (first person) the view moves less, or it'd swing through the fish's own face.
                float move = MaxOffset * shake * (FishOrbitCamera.FirstPerson ? 0.35f : 1f);
                ct.position += ct.right * (move * Noise(_seed + 37f, t)) + ct.up * (move * Noise(_seed + 51f, t));
                ct.rotation *= Quaternion.Euler(pitch, yaw, roll);
            }

            UpdateRumble(paused ? 0f : shake);
            UpdateFov(orbit != null ? cam : null, orbit, dt);
            UpdateSpeedLines(orbit != null ? cam : null, orbit, dt);
        }

        // Smooth noise in -1..1.
        static float Noise(float seed, float t) => Mathf.PerlinNoise(seed, t) * 2f - 1f;

        // ---------------------------------------------------------------- rumble

        void UpdateRumble(float shake)
        {
            var pad = Gamepad.current;
            if (pad == null) { _rumbling = false; return; }
            if (shake < 0.01f)
            {
                if (_rumbling) StopRumble();
                return;
            }
            // The big motor carries the weight of a hit; the small one its sharp edge.
            pad.SetMotorSpeeds(Mathf.Clamp01(shake * 1.2f), Mathf.Clamp01(shake * 0.7f));
            _rumbling = true;
        }

        void StopRumble()
        {
            if (!_rumbling) return;
            _rumbling = false;
            Gamepad.current?.ResetHaptics();
        }

        // ---------------------------------------------------------------- FOV

        void UpdateFov(Camera cam, FishOrbitCamera orbit, float dt)
        {
            if (cam != _fovCam)
            {
                RestoreFov();
                _fovCam = cam;
                if (cam != null) _baseFov = cam.fieldOfView;
                _speedFov = 0f;
            }
            if (cam == null) return;

            // Above cruise speed the view widens, to its most at full sprint (a lunge goes past that: kicks cover it).
            float want = 0f;
            var target = orbit.Target;
            if (target != null && orbit.SpectateTarget == null && target.TryGetComponent(out FishMotor motor) && motor.enabled)
            {
                float cruise = motor.MaxSpeed;
                float over = Mathf.InverseLerp(cruise * 1.05f, Mathf.Max(cruise * 1.1f, motor.BoostSpeed), motor.Speed);
                want = SpeedFov * over * over * (3f - 2f * over);
            }
            _speedNow = want / SpeedFov;
            _speedFov = Mathf.Lerp(_speedFov, want, 1f - Mathf.Exp(-FovEase * dt));
            _kick = Mathf.Lerp(_kick, 0f, 1f - Mathf.Exp(-KickDecay * dt));
            cam.fieldOfView = _baseFov + _speedFov + _kick;
        }

        void RestoreFov()
        {
            if (_fovCam != null) _fovCam.fieldOfView = _baseFov;
            _fovCam = null;
        }

        // ---------------------------------------------------------------- hit stop

        void Freeze(GameObject body, float seconds)
        {
            if (body == null) return;
            if (body.TryGetComponent(out FishMotor motor) && motor.enabled) motor.Freeze(seconds);
            if (body.TryGetComponent(out BodyJuice juice)) juice.Pause(seconds);
            foreach (var swim in body.GetComponentsInChildren<FishBodyAnimator>()) swim.Pause(seconds);
            float until = Time.unscaledTime + seconds;
            foreach (var animator in body.GetComponentsInChildren<Animator>())
            {
                int at = _frozen.FindIndex(f => f.animator == animator);
                if (at >= 0)
                {
                    var f = _frozen[at];
                    _frozen[at] = (animator, f.speed, Mathf.Max(f.until, until));
                    continue;
                }
                _frozen.Add((animator, animator.speed, until));
                animator.speed = 0f;
            }
        }

        // ---------------------------------------------------------------- streaks

        // Water specks streaming past the camera as it goes fast: world-still motes in a tube around the view,
        // stretched by the camera's own motion, so they read as lines only while you're quick.
        void UpdateSpeedLines(Camera cam, FishOrbitCamera orbit, float dt)
        {
            if (cam == null || orbit.SpectateTarget != null || _speedNow < 0.05f) { _lineDebt = 0f; return; }
            Transform ct = cam.transform;
            if (ct.position.y > WaterFx.SurfaceY - 0.3f || !EnsureFx() || _speedLines == null) return;

            float k = _speedNow;
            _lineDebt += 140f * k * dt;
            Vector3 fwd = ct.forward, right = ct.right, up = ct.up;
            while (_lineDebt >= 1f)
            {
                _lineDebt -= 1f;
                // Ahead and around the edges of the view, never across the middle where the fish is.
                float ang = Random.Range(0f, Mathf.PI * 2f);
                float r = Random.Range(1.6f, 4.5f);
                Vector3 p = ct.position + fwd * Random.Range(2f, 12f) + (right * Mathf.Cos(ang) + up * Mathf.Sin(ang)) * r;
                _emit.position = p;
                _emit.velocity = Vector3.zero;
                _emit.startSize = Random.Range(0.025f, 0.055f);
                _emit.startLifetime = Random.Range(0.35f, 0.6f);
                _emit.startColor = new Color(1f, 1f, 1f, 0.35f + 0.45f * k);
                _speedLines.Emit(_emit, 1);
            }
        }

        bool EnsureFx()
        {
            if (_fxBuilt) return _speedLines != null || _streaks != null;
            _fxBuilt = true;
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return false;
            var material = Resources.Load<Material>("WaterMotes");
            if (material == null) return false;
            _emit = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            _speedLines = BuildStreakSystem("Speed Lines", material, 600, velocityScale: 0f, cameraVelocityScale: 0.045f, drag: 0f);
            _streaks = BuildStreakSystem("Fin Streaks", material, 400, velocityScale: 0.09f, cameraVelocityScale: 0f, drag: 3f);
            return true;
        }

        ParticleSystem BuildStreakSystem(string name, Material material, int max, float velocityScale, float cameraVelocityScale, float drag)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = max;
            main.startSpeed = 0f;

            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;

            if (drag > 0f)
            {
                var limit = ps.limitVelocityOverLifetime;
                limit.enabled = true;
                limit.limit = 100f;
                limit.drag = drag;
            }

            // Fade in fast, thin out to nothing.
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.4f), new Keyframe(0.15f, 1f), new Keyframe(1f, 0f)));

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = velocityScale;
            r.cameraVelocityScale = cameraVelocityScale;
            r.lengthScale = 1f;
            r.sortMode = ParticleSystemSortMode.None; // additive: order doesn't matter
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.minParticleSize = 0f;
            r.maxParticleSize = 1f;
            // Same layout the mote shader expects (UnderwaterAmbience): UV, a per-particle random, age.
            r.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
            {
                ParticleSystemVertexStream.Position,
                ParticleSystemVertexStream.Color,
                ParticleSystemVertexStream.UV,
                ParticleSystemVertexStream.StableRandomX,
                ParticleSystemVertexStream.AgePercent,
            });
            ps.Play();
            return ps;
        }
    }
}
