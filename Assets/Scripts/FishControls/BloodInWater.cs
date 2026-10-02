using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishGame
{
    /// <summary>
    /// Blood in the water when a shark eats a fish: a cloud puffs out where the bite happened and
    /// drifts apart, and a thin trail streams from the shark's jaws for a moment while it chews.
    /// A player fish's blood lingers much longer than a school fish's (<see cref="Spill"/>'s linger).
    ///
    /// A wounded fish that's still swimming (<see cref="FishVitals.IsBleeding"/>) leaves a long-lasting
    /// trail behind it (<see cref="KeepBleeding"/>), so the shark can follow it.
    ///
    /// The shark smells blood from far away: on the shark player's screen blood stays visible out to
    /// <see cref="sharkSenseFadeEnd"/> metres instead of the material's own fade distance, and wound
    /// trails are drawn thicker. That's this machine's rendering only - nothing is networked.
    ///
    /// MULTIPLAYER: driven from <see cref="Edible.Devour"/>, which runs exactly once on every machine
    /// for every catch (the host's RPC for player fish, FlockNetSync for school fish), so the blood
    /// needs no messages of its own. Each catch carries a seed made from ids every machine shares
    /// (<see cref="SeedFor"/>), so every player gets the same cloud: same puffs, sizes, drift and
    /// spin. Each machine places it on its own view of the fish and the shark's jaws, so it always
    /// lines up with what that player sees. Purely visual - nothing here affects gameplay.
    ///
    /// All of it is one world-space particle system, built on the first bite, so nothing has to be
    /// placed in a scene. To tune the numbers and keep them, drop this component on any object in
    /// the scene; the first bite uses that one instead of making its own. The look of each puff
    /// lives in Resources/BloodInWater.mat.
    /// </summary>
    public class BloodInWater : MonoBehaviour
    {
        const string MaterialPath = "BloodInWater";

        [Header("Burst (where the bite happened)")]
        [Tooltip("Dense puffs at the centre of the burst.")]
        [SerializeField] int coreCount = 4;
        [Tooltip("Lighter wisps flung outwards.")]
        [SerializeField] int wispCount = 16;
        [Tooltip("Final size of a core puff, in metres (they start at ~40% and swell).")]
        [SerializeField] Vector2 coreSize = new Vector2(2.2f, 3.0f);
        [SerializeField] Vector2 wispSize = new Vector2(1.2f, 2.0f);
        [SerializeField] Vector2 coreSpeed = new Vector2(0.1f, 0.3f);
        [SerializeField] Vector2 wispSpeed = new Vector2(0.6f, 1.6f);
        [SerializeField] Vector2 burstLifetime = new Vector2(3.5f, 5f);

        [Header("Trail (from the shark's mouth)")]
        [SerializeField] float trailDuration = 1.6f;
        [Tooltip("A puff every this many metres the mouth travels...")]
        [SerializeField] float trailSpacing = 0.12f;
        [Tooltip("...plus this many per second, so a shark that stops still bleeds.")]
        [SerializeField] float trailIdleRate = 12f;
        [SerializeField] Vector2 trailSize = new Vector2(0.7f, 1.2f);
        [SerializeField] Vector2 trailLifetime = new Vector2(2.2f, 3.2f);
        [Tooltip("Opacity of the first puffs; it thins out to nothing over the trail's duration.")]
        [SerializeField] Vector2 trailDensity = new Vector2(0.5f, 0.75f);

        [Header("Player fish (lingers)")]
        [Tooltip("A player fish's bite blood lasts this many times longer than a school fish's - the burst " +
                 "hangs in the water and the shark trails blood for longer.")]
        [SerializeField] float playerLinger = 3f;

        [Header("Bleeding (a wounded fish still swimming)")]
        [Tooltip("A puff every this many metres the wounded fish swims...")]
        [SerializeField] float woundSpacing = 0.7f;
        [Tooltip("...plus this many per second, so a fish hiding still keeps bleeding.")]
        [SerializeField] float woundIdleRate = 2f;
        [SerializeField] Vector2 woundSize = new Vector2(0.8f, 1.4f);
        [Tooltip("How long each puff of the trail hangs in the water, in seconds.")]
        [SerializeField] Vector2 woundLifetime = new Vector2(24f, 34f);
        [SerializeField] Vector2 woundDensity = new Vector2(0.35f, 0.55f);

        [Header("Shark's sense of smell")]
        [Tooltip("On the shark player's screen, blood fades out between these distances (metres) instead " +
                 "of the material's own, much shorter ones - it can sense blood from far away.")]
        [SerializeField] float sharkSenseFadeStart = 45f;
        [SerializeField] float sharkSenseFadeEnd = 110f;
        [Tooltip("Wound trails are drawn this much bigger and denser on the shark's screen.")]
        [SerializeField] float sharkTrailBoost = 1.35f;

        [Header("Water")]
        [Tooltip("How fast the water stops the flung blood.")]
        [SerializeField] float drag = 1.2f;
        [Tooltip("Blood is a touch denser than seawater and slowly sinks.")]
        [SerializeField] float sink = 0.008f;
        [Tooltip("Swirl that pulls the cloud into wisps.")]
        [SerializeField] float turbulence = 0.3f;
        [SerializeField] int maxParticles = 3000;

        struct Trail
        {
            public Transform mouth;
            public float timeLeft;
            public float duration;
            public float linger; // lifetime multiplier for its puffs
            public Vector3 lastPos;
            public float owed; // puffs due but not yet emitted (fractional)
            public System.Random rng;
        }

        struct Wound
        {
            public Transform fish;
            public float lastAsked; // Time.time the fish last said it's still bleeding
            public Vector3 lastPos;
            public float owed;
            public System.Random rng;
        }

        static BloodInWater _instance;
        static bool _unavailable;

        // Picks a seed for spills that weren't given one. Every spill draws from its own generator,
        // never UnityEngine.Random: some systems seed that for determinism, and a bite shouldn't
        // shift their sequence.
        static readonly System.Random Unseeded = new System.Random();

        ParticleSystem _ps;
        ParticleSystem.EmitParams _emit;
        readonly List<Trail> _trails = new List<Trail>();
        readonly List<Wound> _wounds = new List<Wound>();
        ParticleSystemRenderer _renderer;
        Material _baseMaterial, _sharkMaterial;
        bool _sharkView;
        float _nextViewCheck;

        /// <summary>
        /// Spill blood for a fish bitten at <paramref name="position"/>. With a <paramref name="mouth"/>
        /// the shark also trails blood from it for a moment; without one there's just the burst.
        /// The same <paramref name="seed"/> gives the same cloud, so machines that pass the same one
        /// show every player the same blood; 0 picks a random one. <paramref name="linger"/> (a player
        /// fish) makes the cloud and the trail last much longer.
        /// </summary>
        public static void Spill(Vector3 position, Transform mouth, int seed = 0, bool linger = false)
        {
            var fx = Instance();
            if (fx == null) return;
            var rng = new System.Random(seed != 0 ? seed : Unseeded.Next(1, int.MaxValue));
            float k = linger ? Mathf.Max(1f, fx.playerLinger) : 1f;
            fx.Burst(position, rng, k);
            if (mouth != null) fx.StartTrail(mouth, rng, k);
        }

        /// <summary>
        /// A wounded fish is still bleeding: call it every frame it bleeds and it leaves a long-lasting
        /// trail of blood behind it. The trail stops on its own once the calls stop.
        /// </summary>
        public static void KeepBleeding(Transform fish)
        {
            if (fish == null) return;
            var fx = Instance();
            if (fx == null) return;
            for (int i = 0; i < fx._wounds.Count; i++)
            {
                if (fx._wounds[i].fish != fish) continue;
                var w = fx._wounds[i];
                w.lastAsked = Time.time;
                fx._wounds[i] = w;
                return;
            }
            fx._wounds.Add(new Wound
            {
                fish = fish, lastAsked = Time.time, lastPos = fish.position,
                rng = new System.Random(Unseeded.Next(1, int.MaxValue)),
            });
        }

        /// <summary>A non-zero spill seed from ids every machine agrees on (a school and fish id, or
        /// the victim's and shark's netIds).</summary>
        public static int SeedFor(int a, int b)
        {
            int h = unchecked((a + 1) * 73856093 ^ (b + 1) * 19349663);
            return h != 0 ? h : 1;
        }

        static BloodInWater Instance()
        {
            if (_instance != null) return _instance;
            if (_unavailable) return null;

            // Nothing to draw on a headless server.
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                _unavailable = true;
                return null;
            }

            var fx = FindAnyObjectByType<BloodInWater>();
            bool made = fx == null;
            if (made) fx = new GameObject("BloodInWater").AddComponent<BloodInWater>();
            if (!fx.Build())
            {
                if (made) Destroy(fx.gameObject);
                _unavailable = true;
                return null;
            }
            return _instance = fx;
        }

        bool Build()
        {
            if (_ps != null) return true;

            var material = Resources.Load<Material>(MaterialPath);
            if (material == null)
            {
                Debug.LogWarning($"BloodInWater: no material at Resources/{MaterialPath}, bites won't bleed.");
                return false;
            }

            // Kept on a child so a placed instance's own object is never turned into an emitter.
            var go = new GameObject("Blood");
            go.transform.SetParent(transform, false);
            _ps = go.AddComponent<ParticleSystem>();

            var main = _ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = maxParticles;
            main.startSpeed = 0f;
            main.gravityModifier = sink;

            var emission = _ps.emission;
            emission.enabled = false; // everything is emitted by hand

            var shape = _ps.shape;
            shape.enabled = false;

            var size = _ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.4f, 2.5f, 2.5f),
                new Keyframe(0.3f, 0.78f),
                new Keyframe(1f, 1f, 0f, 0f)));

            var color = _ps.colorOverLifetime;
            color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.06f),
                    new GradientAlphaKey(0.75f, 0.45f),
                    new GradientAlphaKey(0f, 1f),
                });
            color.color = fade;

            var spin = _ps.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);

            var limit = _ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 100f; // never clamps; only here for the drag
            limit.drag = drag;

            var noise = _ps.noise;
            noise.enabled = true;
            noise.strength = turbulence;
            noise.frequency = 0.35f;
            noise.scrollSpeed = 0.2f;
            noise.damping = true;
            noise.quality = ParticleSystemNoiseQuality.Medium;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            _renderer = renderer;
            _baseMaterial = material;
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.maxParticleSize = 1f;
            // Layout the shader expects: UV in TEXCOORD0.xy, a per-particle random in .z, age in .w.
            renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
            {
                ParticleSystemVertexStream.Position,
                ParticleSystemVertexStream.Color,
                ParticleSystemVertexStream.UV,
                ParticleSystemVertexStream.StableRandomX,
                ParticleSystemVertexStream.AgePercent,
            });

            _ps.Play();
            return true;
        }

        void Burst(Vector3 at, System.Random rng, float linger)
        {
            for (int i = 0; i < coreCount + wispCount; i++)
            {
                bool core = i < coreCount;
                Vector3 dir = RandomDirection(rng);
                float speed = Range(rng, core ? coreSpeed : wispSpeed);

                _emit.position = at + dir * Range(rng, 0f, core ? 0.15f : 0.35f);
                _emit.velocity = dir * speed;
                _emit.startSize = Range(rng, core ? coreSize : wispSize);
                _emit.startLifetime = Range(rng, burstLifetime) * linger;
                _emit.rotation = Range(rng, 0f, 360f);
                float shade = Range(rng, 0.8f, 1f);
                _emit.startColor = new Color(shade, shade, shade, core ? Range(rng, 0.6f, 0.8f) : Range(rng, 0.35f, 0.6f));
                _emit.randomSeed = (uint)rng.Next(); // spin and the shader's edge noise follow the seed too
                _ps.Emit(_emit, 1);
            }
        }

        void StartTrail(Transform mouth, System.Random rng, float linger)
        {
            float duration = trailDuration * linger;
            // A shark that bites again mid-trail just bleeds for longer.
            for (int i = 0; i < _trails.Count; i++)
            {
                if (_trails[i].mouth != mouth) continue;
                var t = _trails[i];
                t.duration = Mathf.Max(t.duration, duration);
                t.timeLeft = Mathf.Max(t.timeLeft, duration);
                t.linger = Mathf.Max(t.linger, linger);
                _trails[i] = t;
                return;
            }
            _trails.Add(new Trail
            {
                mouth = mouth, timeLeft = duration, duration = duration, linger = linger,
                lastPos = mouth.position, rng = rng,
            });
        }

        // Is whoever plays on this machine the shark? Its blood shows from much further away.
        static bool LocalViewerIsShark()
        {
            if (NetworkClient.active)
                return NetworkClient.localPlayer != null && NetworkClient.localPlayer.TryGetComponent(out SharkAbilities _);
            foreach (var fp in FindObjectsByType<FishPlayer>(FindObjectsSortMode.None))
                if (fp.IsOfflineSandboxBody) return fp.TryGetComponent(out SharkAbilities _);
            return false;
        }

        void UpdateSharkView()
        {
            if (Time.unscaledTime < _nextViewCheck || _renderer == null) return;
            _nextViewCheck = Time.unscaledTime + 0.5f; // roles swap rarely (F8, respawns)
            bool shark = LocalViewerIsShark();
            if (shark == _sharkView) return;
            _sharkView = shark;
            if (shark && _sharkMaterial == null)
            {
                _sharkMaterial = new Material(_baseMaterial) { name = _baseMaterial.name + " (shark sense)" };
                _sharkMaterial.SetFloat("_FarFadeStart", sharkSenseFadeStart);
                _sharkMaterial.SetFloat("_FarFadeEnd", Mathf.Max(sharkSenseFadeEnd, sharkSenseFadeStart + 1f));
            }
            _renderer.sharedMaterial = shark ? _sharkMaterial : _baseMaterial;
        }

        void Update()
        {
            if (_ps == null) return;
            float dt = Time.deltaTime;
            UpdateSharkView();
            UpdateWounds(dt);

            for (int i = _trails.Count - 1; i >= 0; i--)
            {
                var t = _trails[i];
                t.timeLeft -= dt;
                if (t.mouth == null || t.timeLeft <= 0f)
                {
                    _trails.RemoveAt(i);
                    continue;
                }

                Vector3 pos = t.mouth.position;
                t.owed += Vector3.Distance(pos, t.lastPos) / Mathf.Max(trailSpacing, 0.01f) + dt * trailIdleRate;
                int count = Mathf.Min((int)t.owed, 32); // a teleport shouldn't dump hundreds at once
                t.owed -= (int)t.owed;

                float thin = t.timeLeft / Mathf.Max(t.duration, 0.01f);
                for (int k = 0; k < count; k++)
                {
                    // Spread along the path the mouth took this frame, so a fast shark leaves a line
                    // of blood rather than a clump every frame.
                    var r = t.rng;
                    _emit.position = Vector3.Lerp(t.lastPos, pos, (k + 1f) / count) + RandomDirection(r) * 0.05f;
                    _emit.velocity = RandomDirection(r) * Range(r, 0.05f, 0.2f);
                    _emit.startSize = Range(r, trailSize);
                    _emit.startLifetime = Range(r, trailLifetime) * t.linger;
                    _emit.rotation = Range(r, 0f, 360f);
                    _emit.startColor = new Color(1f, 1f, 1f, Range(r, trailDensity) * thin);
                    _emit.randomSeed = (uint)r.Next();
                    _ps.Emit(_emit, 1);
                }

                t.lastPos = pos;
                _trails[i] = t;
            }
        }

        // Each wounded fish drops long-lived puffs along its path, so the trail it leaves stays in the
        // water for half a minute and can be followed back to it.
        void UpdateWounds(float dt)
        {
            float boost = _sharkView ? Mathf.Max(1f, sharkTrailBoost) : 1f;
            for (int i = _wounds.Count - 1; i >= 0; i--)
            {
                var w = _wounds[i];
                // Stopped bleeding (or gone): the fish stops asking, and the trail it left fades by itself.
                if (w.fish == null || Time.time - w.lastAsked > 0.25f)
                {
                    _wounds.RemoveAt(i);
                    continue;
                }

                Vector3 pos = w.fish.position;
                w.owed += Vector3.Distance(pos, w.lastPos) / Mathf.Max(woundSpacing, 0.05f) + dt * woundIdleRate;
                int count = Mathf.Min((int)w.owed, 16);
                w.owed -= (int)w.owed;
                for (int k = 0; k < count; k++)
                {
                    var r = w.rng;
                    _emit.position = Vector3.Lerp(w.lastPos, pos, (k + 1f) / count) + RandomDirection(r) * 0.12f;
                    _emit.velocity = RandomDirection(r) * Range(r, 0.02f, 0.1f);
                    _emit.startSize = Range(r, woundSize) * boost;
                    _emit.startLifetime = Range(r, woundLifetime);
                    _emit.rotation = Range(r, 0f, 360f);
                    _emit.startColor = new Color(1f, 1f, 1f, Mathf.Clamp01(Range(r, woundDensity) * boost));
                    _emit.randomSeed = (uint)r.Next();
                    _ps.Emit(_emit, 1);
                }

                w.lastPos = pos;
                _wounds[i] = w;
            }
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
            if (_sharkMaterial != null) Destroy(_sharkMaterial);
        }

        static float Range(System.Random rng, Vector2 minMax) => Range(rng, minMax.x, minMax.y);
        static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);

        static Vector3 RandomDirection(System.Random rng)
        {
            // Uniform on the sphere.
            float z = Range(rng, -1f, 1f);
            float a = Range(rng, 0f, Mathf.PI * 2f);
            float r = Mathf.Sqrt(1f - z * z);
            return new Vector3(r * Mathf.Cos(a), r * Mathf.Sin(a), z);
        }
    }
}
