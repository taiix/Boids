using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishGame
{
    /// <summary>
    /// Blood in the water when a shark eats a fish: a cloud puffs out where the bite happened and
    /// drifts apart, and a thin trail streams from the shark's jaws for a moment while it chews.
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

        [Header("Water")]
        [Tooltip("How fast the water stops the flung blood.")]
        [SerializeField] float drag = 1.2f;
        [Tooltip("Blood is a touch denser than seawater and slowly sinks.")]
        [SerializeField] float sink = 0.008f;
        [Tooltip("Swirl that pulls the cloud into wisps.")]
        [SerializeField] float turbulence = 0.3f;
        [SerializeField] int maxParticles = 800;

        struct Trail
        {
            public Transform mouth;
            public float timeLeft;
            public Vector3 lastPos;
            public float owed; // puffs due but not yet emitted (fractional)
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

        /// <summary>
        /// Spill blood for a fish bitten at <paramref name="position"/>. With a <paramref name="mouth"/>
        /// the shark also trails blood from it for a moment; without one there's just the burst.
        /// The same <paramref name="seed"/> gives the same cloud, so machines that pass the same one
        /// show every player the same blood; 0 picks a random one.
        /// </summary>
        public static void Spill(Vector3 position, Transform mouth, int seed = 0)
        {
            var fx = Instance();
            if (fx == null) return;
            var rng = new System.Random(seed != 0 ? seed : Unseeded.Next(1, int.MaxValue));
            fx.Burst(position, rng);
            if (mouth != null) fx.StartTrail(mouth, rng);
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

        void Burst(Vector3 at, System.Random rng)
        {
            for (int i = 0; i < coreCount + wispCount; i++)
            {
                bool core = i < coreCount;
                Vector3 dir = RandomDirection(rng);
                float speed = Range(rng, core ? coreSpeed : wispSpeed);

                _emit.position = at + dir * Range(rng, 0f, core ? 0.15f : 0.35f);
                _emit.velocity = dir * speed;
                _emit.startSize = Range(rng, core ? coreSize : wispSize);
                _emit.startLifetime = Range(rng, burstLifetime);
                _emit.rotation = Range(rng, 0f, 360f);
                float shade = Range(rng, 0.8f, 1f);
                _emit.startColor = new Color(shade, shade, shade, core ? Range(rng, 0.6f, 0.8f) : Range(rng, 0.35f, 0.6f));
                _emit.randomSeed = (uint)rng.Next(); // spin and the shader's edge noise follow the seed too
                _ps.Emit(_emit, 1);
            }
        }

        void StartTrail(Transform mouth, System.Random rng)
        {
            // A shark that bites again mid-trail just bleeds for longer.
            for (int i = 0; i < _trails.Count; i++)
            {
                if (_trails[i].mouth != mouth) continue;
                var t = _trails[i];
                t.timeLeft = trailDuration;
                _trails[i] = t;
                return;
            }
            _trails.Add(new Trail { mouth = mouth, timeLeft = trailDuration, lastPos = mouth.position, rng = rng });
        }

        void Update()
        {
            if (_ps == null) return;
            float dt = Time.deltaTime;

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

                float thin = t.timeLeft / trailDuration;
                for (int k = 0; k < count; k++)
                {
                    // Spread along the path the mouth took this frame, so a fast shark leaves a line
                    // of blood rather than a clump every frame.
                    var r = t.rng;
                    _emit.position = Vector3.Lerp(t.lastPos, pos, (k + 1f) / count) + RandomDirection(r) * 0.05f;
                    _emit.velocity = RandomDirection(r) * Range(r, 0.05f, 0.2f);
                    _emit.startSize = Range(r, trailSize);
                    _emit.startLifetime = Range(r, trailLifetime);
                    _emit.rotation = Range(r, 0f, 360f);
                    _emit.startColor = new Color(1f, 1f, 1f, Range(r, trailDensity) * thin);
                    _emit.randomSeed = (uint)r.Next();
                    _ps.Emit(_emit, 1);
                }

                t.lastPos = pos;
                _trails[i] = t;
            }
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
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
