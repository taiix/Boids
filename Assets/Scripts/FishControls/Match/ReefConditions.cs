using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace FishGame
{
    /// <summary>
    /// The reef's weather - world events (<see cref="WorldEvents"/> picks when) that change the conditions for
    /// fish and shark alike:
    ///   * <b>Nightfall</b>: the sun goes down and nobody sees far, but anything that moves - fish or shark -
    ///     stirs up glowing plankton that gives it away.
    ///   * <b>Lightning storm</b>: dark and choppy; near the surface the water shoves everyone about, and every
    ///     few seconds lightning lights the whole reef up for a moment.
    ///   * <b>Cold layer</b>: a band of cold water at mid depth; anyone in it swims slower.
    ///   * <b>Jellyfish swarm</b>: glowing jellyfish drift through; touching one stings (hurts and slows).
    ///   * <b>Sand slide</b>: a quake shakes the reef and raises thick sand along the seabed - nobody sees far
    ///     down there, and the shark can't pick out a fish hidden in it from afar.
    ///   * <b>Red tide</b>: a drifting patch of toxic algae; inside it you lose health and can't see far.
    ///
    /// Every machine plays the condition from MatchManager's synced event (kind, times, direction and a seed),
    /// so the same jellyfish are in the same places on every screen without sending them. The host (or the
    /// offline sandbox) alone decides who is stung and who is poisoned.
    /// </summary>
    [DefaultExecutionOrder(10000)] // LateUpdate after the orbit camera, so the quake can shake it
    public class ReefConditions : MonoBehaviour
    {
        static ReefConditions s_instance;

        /// <summary>Run the reef's conditions in the current (game) scene, once.</summary>
        public static void Show()
        {
            if (s_instance != null) return;
            s_instance = new GameObject("ReefConditions").AddComponent<ReefConditions>();
        }

        [Header("Durations (seconds)")]
        [SerializeField] float nightSeconds = 60f;
        [SerializeField] float stormSeconds = 50f;
        [SerializeField] float coldSeconds = 60f;
        [SerializeField] float jellySeconds = 70f;
        [SerializeField] float sandSeconds = 40f;
        [SerializeField] float tideSeconds = 70f;
        [Tooltip("Seconds each condition takes to set in and to clear.")]
        [SerializeField] float ramp = 4f;

        [Header("Nightfall")]
        [Tooltip("What's left of the sun at the darkest.")]
        [SerializeField] float nightSun = 0.06f;
        [Tooltip("How far everyone sees at the darkest, as a fraction of normal.")]
        [SerializeField] float nightVisibility = 0.5f;
        [Tooltip("A body moving faster than this (m/s) stirs up plankton.")]
        [SerializeField] float planktonSpeed = 1.2f;

        [Header("Lightning storm")]
        [SerializeField] float stormSun = 0.4f;
        [Tooltip("Seconds between lightning strikes (random in this range).")]
        [SerializeField] Vector2 strikeGap = new Vector2(4f, 9f);
        [Tooltip("How hard the water churns right under the surface (m/s), fading to nothing at Churn Depth.")]
        [SerializeField] float churnStrength = 4f;
        [SerializeField] float churnDepth = 12f;

        [Header("Cold layer")]
        [SerializeField] float coldThickness = 12f;
        [Tooltip("Swimming speed inside the cold layer, as a fraction of normal.")]
        [SerializeField] float coldSpeed = 0.6f;

        [Header("Jellyfish")]
        [SerializeField] int jellyCount = 40;
        [SerializeField] float jellyDrift = 0.5f;
        [SerializeField] float stingRadius = 1.4f;
        [SerializeField] float stingDamage = 8f;
        [SerializeField] float stingSlowSeconds = 2.5f;
        [SerializeField] float stingSlow = 0.5f;

        [Header("Sand slide")]
        [Tooltip("How high above the seabed the sand rises (metres).")]
        [SerializeField] float sandHeight = 9f;
        [Tooltip("How far you see inside the sand (the cloud's mean free path, metres).")]
        [SerializeField] float sandThickness = 7f;
        [SerializeField] float quakeSeconds = 4f;

        [Header("Red tide")]
        [SerializeField] float tideRadius = 24f;
        [SerializeField] float tideDepth = 28f;
        [SerializeField] float tideDrift = 0.7f;
        [Tooltip("Health lost per second inside the red tide.")]
        [SerializeField] float tideDrain = 3f;

        [Header("Hiding")]
        [Tooltip("Inside the sand or the red tide, a fish farther than this from the shark can't be locked onto.")]
        [SerializeField] float hideBeyond = 8f;

        // ---- shared per-event state (every machine) ----
        WorldEventKind _kind;
        double _start, _end;
        Vector3 _dir;
        int _seed;
        float _k;                       // 0..1, how strongly the condition is on (ramps in and out)
        float _t;                       // seconds since it started

        Vector3 _arenaCentre;
        Vector2 _arenaHalf;             // half size along x and z
        float _surface, _seabed;

        float _coldY;                   // cold layer: middle height
        Vector3 _tideStart;             // red tide: centre at the start
        readonly List<double> _strikes = new List<double>();
        readonly List<Jelly> _jellies = new List<Jelly>();

        struct Jelly { public Vector3 start; public float phase; public Transform body; }

        // ---- looks ----
        Light _sun;
        float _sunBase = -1f;
        WaterSurface _water;
        float _windBase = -1f, _ripplesBase = -1f;
        Volume _volume;
        ColorAdjustments _grade;
        Fog _fog;
        LocalVolumetricFog _cloud;      // the cold layer, the sand, or the red tide
        ParticleSystem _plankton;
        Mesh _jellyMesh;
        Material _jellyMaterial;
        Transform _jellyRoot;
        float _nextKick, _nextToxicFlash, _nextBurn;
        int _nextStrike;                // the storm's next lightning strike to sound
        readonly Dictionary<FishPlayer, double> _stingImmune = new Dictionary<FishPlayer, double>();

        static bool Authority => MatchManager.Offline || NetworkServer.active;

        /// <summary>Is <paramref name="kind"/> one of the reef's conditions (rather than the current or the trawler)?</summary>
        public static bool Handles(WorldEventKind kind) => kind >= WorldEventKind.Nightfall && kind <= WorldEventKind.RedTide;

        /// <summary>How long <paramref name="kind"/> lasts.</summary>
        public static float Duration(WorldEventKind kind)
        {
            var c = s_instance;
            if (c == null) return 60f;
            switch (kind)
            {
                case WorldEventKind.Nightfall: return c.nightSeconds;
                case WorldEventKind.Storm: return c.stormSeconds;
                case WorldEventKind.ColdLayer: return c.coldSeconds;
                case WorldEventKind.Jellyfish: return c.jellySeconds;
                case WorldEventKind.SandSlide: return c.sandSeconds;
                case WorldEventKind.RedTide: return c.tideSeconds;
                default: return 60f;
            }
        }

        /// <summary>
        /// A fish at <paramref name="p"/>, <paramref name="distance"/> metres from whoever is looking, can't be
        /// picked out: it's deep in the sand along the seabed or inside the red tide.
        /// </summary>
        public static bool Hides(Vector3 p, float distance)
        {
            var c = s_instance;
            if (c == null || c._k < 0.5f || distance < c.hideBeyond) return false;
            if (c._kind == WorldEventKind.SandSlide) return c.InSand(p);
            if (c._kind == WorldEventKind.RedTide) return c.InTide(p);
            return false;
        }

        void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
            // The sun, the ocean and the view range are shared scene state: hand them back as they were.
            FishMotor.FlowAt = null;
            FishMotor.SpeedScaleAt = null;
            CreatureVision.VisibilityScale = 1f;
            if (_sun != null && _sunBase >= 0f) _sun.intensity = _sunBase;
            if (_water != null && _windBase >= 0f) { _water.largeWindSpeed = _windBase; _water.ripplesWindSpeed = _ripplesBase; }
        }

        void Update()
        {
            var m = MatchManager.Instance;
            bool on = m != null && Handles(m.Event) && !m.IsOver && m.Phase != MatchPhase.Warmup && MatchManager.Now < m.EventEnd;
            if (!on)
            {
                if (_kind != WorldEventKind.None) Clear();
                return;
            }

            // A new condition: lay it out the same way every machine does.
            if (m.Event != _kind || m.EventStart != _start)
            {
                Clear();
                Begin(m);
            }

            double now = MatchManager.Now;
            _t = (float)(now - _start);
            float left = (float)(_end - now);
            _k = Mathf.SmoothStep(0f, 1f, Mathf.Min(_t, left) / Mathf.Max(0.1f, ramp));

            // Reset what the conditions drive this frame, then let the one that's on set it.
            float sun = 1f, visibility = 1f, exposure = 0f, saturation = 0f;
            Color filter = Color.white;
            FishMotor.FlowAt = null;
            FishMotor.SpeedScaleAt = null;

            switch (_kind)
            {
                case WorldEventKind.Nightfall: Night(ref sun, ref visibility, ref exposure, ref saturation, ref filter); break;
                case WorldEventKind.Storm: Storm(now, ref sun, ref visibility, ref exposure, ref saturation, ref filter); break;
                case WorldEventKind.ColdLayer: Cold(ref visibility, ref saturation, ref filter); break;
                case WorldEventKind.Jellyfish: Jellies(now); break;
                case WorldEventKind.SandSlide: Sand(ref visibility, ref filter); break;
                case WorldEventKind.RedTide: Tide(ref visibility, ref saturation, ref filter); break;
            }

            ApplyLook(sun, visibility, exposure, saturation, filter);
        }

        void LateUpdate()
        {
            // The quake at the start of a sand slide shakes this player's view.
            if (_kind != WorldEventKind.SandSlide || _t > quakeSeconds) return;
            GameFeel.Tremor(0.8f * (1f - _t / quakeSeconds));
        }

        // ------------------------------------------------------------------ setup

        void Begin(MatchManager m)
        {
            _kind = m.Event;
            _start = m.EventStart;
            _end = m.EventEnd;
            _dir = m.EventDirection.sqrMagnitude > 1e-4f ? m.EventDirection.normalized : Vector3.forward;
            _seed = m.EventParam;
            Arena();
            var rng = new System.Random(_seed);
            _nextStrike = 0;
            var lib = GameAudio.Lib;
            if (lib != null)
            {
                GameAudio.Play(lib.uiAnnounce, 0.8f);
                switch (_kind)
                {
                    case WorldEventKind.Nightfall: GameAudio.Play(lib.nightfallOnset, 0.8f); break;
                    case WorldEventKind.ColdLayer: GameAudio.Play(lib.coldOnset, 0.8f); break;
                    case WorldEventKind.SandSlide: GameAudio.Play(lib.quake, 1f); break;
                    case WorldEventKind.RedTide: GameAudio.Play(lib.tideOnset, 0.7f); break;
                }
            }

            switch (_kind)
            {
                case WorldEventKind.Nightfall:
                    MatchHud.Flash("NIGHTFALL - the reef goes dark. Anything that moves will glow", 4.5f);
                    break;
                case WorldEventKind.Storm:
                    MatchHud.Flash("LIGHTNING STORM - rough water near the surface", 4.5f);
                    double at = _start + 2.0;
                    while (at < _end - 1.0)
                    {
                        _strikes.Add(at);
                        at += strikeGap.x + rng.NextDouble() * (strikeGap.y - strikeGap.x);
                    }
                    break;
                case WorldEventKind.ColdLayer:
                    _coldY = Mathf.Lerp(_seabed, _surface, 0.35f + 0.3f * (float)rng.NextDouble());
                    MatchHud.Flash($"A COLD LAYER has formed {Depth(_coldY + coldThickness * 0.5f):0}-{Depth(_coldY - coldThickness * 0.5f):0} m down - everyone slows inside it", 4.5f);
                    break;
                case WorldEventKind.Jellyfish:
                    SpawnJellies(rng);
                    MatchHud.Flash("JELLYFISH SWARM - don't touch them, they sting", 4.5f);
                    break;
                case WorldEventKind.SandSlide:
                    MatchHud.Flash("SAND SLIDE - a quake is clouding the seabed", 4.5f);
                    break;
                case WorldEventKind.RedTide:
                    _tideStart = _arenaCentre + new Vector3(((float)rng.NextDouble() * 2f - 1f) * _arenaHalf.x * 0.6f, 0f,
                                                            ((float)rng.NextDouble() * 2f - 1f) * _arenaHalf.y * 0.6f);
                    MatchHud.Flash("RED TIDE - toxic water is drifting in. Stay out of it", 4.5f);
                    break;
            }
        }

        // Where the arena is (from its walls; around this player if there are none), the surface and the seabed.
        void Arena()
        {
            _surface = WaterFx.SurfaceY;
            var walls = ArenaWalls.Instance;
            Vector3 min = new Vector3(float.MaxValue, 0f, float.MaxValue), max = new Vector3(float.MinValue, 0f, float.MinValue);
            int found = 0;
            if (walls != null)
                for (int i = 0; i < walls.Count; i++)
                    if (walls.Describe(i, out var home, out _, out _, out _, out _, out _))
                    {
                        min = Vector3.Min(min, home);
                        max = Vector3.Max(max, home);
                        found++;
                    }
            if (found >= 2)
            {
                _arenaCentre = (min + max) * 0.5f;
                _arenaHalf = new Vector2(Mathf.Max(20f, (max.x - min.x) * 0.5f), Mathf.Max(20f, (max.z - min.z) * 0.5f));
            }
            else
            {
                var cam = WaterFx.ViewCamera;
                _arenaCentre = cam != null ? cam.transform.position : Vector3.zero;
                _arenaHalf = new Vector2(100f, 100f);
            }
            _arenaCentre.y = _surface;
            _seabed = WaterFx.SeabedHeight(_arenaCentre, out float y) ? y : _surface - 45f;
        }

        float Depth(float y) => Mathf.Max(0f, _surface - y);

        void Clear()
        {
            _kind = WorldEventKind.None;
            _k = 0f;
            _strikes.Clear();
            foreach (var j in _jellies) if (j.body != null) Destroy(j.body.gameObject);
            _jellies.Clear();
            _stingImmune.Clear();
            if (_cloud != null) Destroy(_cloud.gameObject);
            _cloud = null;
            if (_plankton != null) _plankton.Clear();
            FishMotor.FlowAt = null;
            FishMotor.SpeedScaleAt = null;
            ApplyLook(1f, 1f, 0f, 0f, Color.white);
        }

        // ------------------------------------------------------------------ nightfall

        void Night(ref float sun, ref float visibility, ref float exposure, ref float saturation, ref Color filter)
        {
            sun = Mathf.Lerp(1f, nightSun, _k);
            visibility = Mathf.Lerp(1f, nightVisibility, _k);
            exposure = -0.6f * _k;
            saturation = -30f * _k;
            filter = Color.Lerp(Color.white, new Color(0.62f, 0.75f, 1f), _k);
            MatchHud.ShowEvent($"NIGHTFALL  -  MOVING MAKES YOU GLOW  {Mathf.CeilToInt((float)(_end - MatchManager.Now))}s");

            // Every body that swims stirs up plankton - the fish and the shark alike.
            if (_k < 0.2f || !EnsurePlankton()) return;
            foreach (var fp in FishPlayer.All)
            {
                if (fp == null || !fp.isActiveAndEnabled || fp.IsOut || !fp.TryGetComponent(out FishMotor motor)) continue;
                float speed = motor.Speed;
                if (speed < planktonSpeed) continue;
                float rate = (speed - planktonSpeed + 1f) * 22f * _k * (fp.IsSharkBody ? 1.6f : 1f);
                int n = Mathf.FloorToInt(rate * Time.deltaTime + Random.value);
                for (int i = 0; i < n; i++)
                {
                    var p = new ParticleSystem.EmitParams
                    {
                        position = fp.transform.position + Random.insideUnitSphere * (fp.IsSharkBody ? 0.9f : 0.4f),
                        velocity = Random.insideUnitSphere * 0.25f,
                        startSize = Random.Range(0.1f, 0.24f),
                        startLifetime = Random.Range(2.5f, 4f),
                        startColor = Color.white,
                        applyShapeToPosition = false,
                    };
                    _plankton.Emit(p, 1);
                }
            }
        }

        bool EnsurePlankton()
        {
            if (_plankton != null) return true;
            var material = Resources.Load<Material>("WorldEvents/Plankton");
            if (material == null) return false;
            var go = new GameObject("Plankton");
            go.transform.SetParent(transform, false);
            _plankton = go.AddComponent<ParticleSystem>();
            _plankton.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _plankton.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 4000;
            var emission = _plankton.emission;
            emission.enabled = false;
            var shape = _plankton.shape;
            shape.enabled = false;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = material;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sortMode = ParticleSystemSortMode.None;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            // The same streams as the water motes: UV, a stable random, and the particle's age.
            r.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
            {
                ParticleSystemVertexStream.Position,
                ParticleSystemVertexStream.Color,
                ParticleSystemVertexStream.UV,
                ParticleSystemVertexStream.StableRandomX,
                ParticleSystemVertexStream.AgePercent,
            });
            _plankton.Play();
            return true;
        }

        // ------------------------------------------------------------------ lightning storm

        void Storm(double now, ref float sun, ref float visibility, ref float exposure, ref float saturation, ref Color filter)
        {
            sun = Mathf.Lerp(1f, stormSun, _k);
            visibility = Mathf.Lerp(1f, 0.75f, _k);
            saturation = -25f * _k;
            filter = Color.Lerp(Color.white, new Color(0.75f, 0.8f, 0.9f), _k);

            // Lightning: a bright double flicker that dies away over half a second, lighting the whole reef.
            float flash = 0f;
            foreach (double s in _strikes)
            {
                float dt = (float)(now - s);
                if (dt < 0f || dt > 0.8f) continue;
                float f = dt < 0.06f ? 1f : dt < 0.12f ? 0.35f : dt < 0.18f ? 0.9f : Mathf.Exp(-(dt - 0.18f) * 7f) * 0.9f;
                flash = Mathf.Max(flash, f);
            }
            // Thunder right on each strike (every machine shares the strike times, so everyone hears it together).
            while (_nextStrike < _strikes.Count && now >= _strikes[_nextStrike])
            {
                if (now - _strikes[_nextStrike] < 0.5 && GameAudio.Lib != null) GameAudio.Play(GameAudio.Lib.thunder, 1f, 0.1f);
                _nextStrike++;
            }
            if (GameAudio.Lib != null) GameAudio.Loop("rain", GameAudio.Lib.rainLoop, 0.5f * _k);
            sun += flash * 4f;
            exposure += flash * 0.9f;
            visibility *= 1f + flash * 2.5f;
            filter = Color.Lerp(filter, Color.white, flash);

            // Near the surface the water heaves everyone about.
            float k = _k, surface = _surface, depth = churnDepth, strength = churnStrength, time = (float)now;
            FishMotor.FlowAt = p =>
            {
                float d = surface - p.y;
                if (d < 0f || d > depth) return Vector3.zero;
                float a = Mathf.PerlinNoise(p.x * 0.04f + time * 0.35f, p.z * 0.04f) * Mathf.PI * 4f;
                float up = Mathf.PerlinNoise(p.z * 0.05f, time * 0.5f) * 2f - 1f;
                return new Vector3(Mathf.Cos(a), up * 0.6f, Mathf.Sin(a)) * (strength * (1f - d / depth) * k);
            };
            MatchHud.ShowEvent($"LIGHTNING STORM  -  ROUGH WATER NEAR THE SURFACE  {Mathf.CeilToInt((float)(_end - now))}s");
        }

        // ------------------------------------------------------------------ cold layer

        void Cold(ref float visibility, ref float saturation, ref Color filter)
        {
            float half = coldThickness * 0.5f, mid = _coldY, slowest = coldSpeed, k = _k;
            FishMotor.SpeedScaleAt = p =>
            {
                float inside = 1f - Mathf.Clamp01((Mathf.Abs(p.y - mid) - half) / 1.5f); // soft 1.5 m edges
                return Mathf.Lerp(1f, slowest, inside * k);
            };
            Cloud(new Vector3(_arenaCentre.x, _coldY, _arenaCentre.z), new Vector3(_arenaHalf.x * 2f + 20f, coldThickness, _arenaHalf.y * 2f + 20f),
                  new Color(0.78f, 0.92f, 1f), Mathf.Lerp(400f, 45f, _k), new Vector3(0f, 0.25f, 0f));

            var cam = WaterFx.ViewCamera;
            if (cam != null && Mathf.Abs(cam.transform.position.y - _coldY) < half)
            {
                filter = Color.Lerp(Color.white, new Color(0.8f, 0.93f, 1f), _k);
                saturation = -20f * _k;
                visibility = Mathf.Lerp(1f, 0.85f, _k);
            }
            MatchHud.ShowEvent($"COLD LAYER {Depth(_coldY + half):0}-{Depth(_coldY - half):0} m DOWN  -  SLOWS EVERYONE  {Mathf.CeilToInt((float)(_end - MatchManager.Now))}s");
        }

        // ------------------------------------------------------------------ jellyfish

        void SpawnJellies(System.Random rng)
        {
            EnsureJellyLook();
            _jellyRoot = new GameObject("Jellyfish").transform;
            _jellyRoot.SetParent(transform, false);
            for (int i = 0; i < jellyCount; i++)
            {
                var start = new Vector3(_arenaCentre.x + ((float)rng.NextDouble() * 2f - 1f) * _arenaHalf.x * 0.85f,
                                        Mathf.Lerp(_seabed + 4f, _surface - 3f, (float)rng.NextDouble()),
                                        _arenaCentre.z + ((float)rng.NextDouble() * 2f - 1f) * _arenaHalf.y * 0.85f);
                Transform body = null;
                if (_jellyMesh != null && _jellyMaterial != null)
                {
                    var go = new GameObject("Jelly");
                    go.transform.SetParent(_jellyRoot, false);
                    go.AddComponent<MeshFilter>().sharedMesh = _jellyMesh;
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterial = _jellyMaterial;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    body = go.transform;
                }
                _jellies.Add(new Jelly { start = start, phase = (float)rng.NextDouble() * 6.28f, body = body });
            }
        }

        Vector3 JellyAt(Jelly j, float t) =>
            j.start + _dir * (jellyDrift * t) + Vector3.up * (0.6f * Mathf.Sin(t * 0.9f + j.phase));

        void Jellies(double now)
        {
            float t = _t;
            foreach (var j in _jellies)
            {
                if (j.body == null) continue;
                j.body.position = JellyAt(j, t);
                float pulse = 1f + 0.12f * Mathf.Sin(t * 3f + j.phase);
                j.body.localScale = new Vector3(pulse, 1f / pulse, pulse) * Mathf.Max(0.01f, _k);
            }
            MatchHud.ShowEvent($"JELLYFISH SWARM  -  DON'T TOUCH THEM  {Mathf.CeilToInt((float)(_end - now))}s");

            // Authority: who swam into one. The tentacles hang below the bell, so test a little under it.
            if (!Authority || _k < 0.5f) return;
            foreach (var fp in FishPlayer.All)
            {
                if (fp == null || !fp.isActiveAndEnabled || fp.IsOut || fp.IsBeingEaten) continue;
                if (!MatchManager.Offline && fp.netId == 0) continue;
                if (_stingImmune.TryGetValue(fp, out double until) && now < until) continue;
                Vector3 p = fp.transform.position;
                foreach (var j in _jellies)
                {
                    Vector3 c = JellyAt(j, t) + Vector3.down * 0.6f;
                    if ((p - c).sqrMagnitude > stingRadius * stingRadius) continue;
                    _stingImmune[fp] = now + stingSlowSeconds + 1f;
                    fp.ServerStung(stingDamage, stingSlowSeconds, stingSlow);
                    break;
                }
            }
        }

        // A bell (a dome) with tentacles hanging under it, made once.
        void EnsureJellyLook()
        {
            if (_jellyMaterial == null) _jellyMaterial = Resources.Load<Material>("WorldEvents/Jellyfish");
            if (_jellyMesh != null) return;
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var tri = new List<int>();
            const int rings = 6, segs = 14;
            const float radius = 0.55f;
            for (int r = 0; r <= rings; r++)
            {
                float a = r / (float)rings * Mathf.PI * 0.5f; // top to rim
                for (int s = 0; s <= segs; s++)
                {
                    float b = s / (float)segs * Mathf.PI * 2f;
                    var dir = new Vector3(Mathf.Sin(a) * Mathf.Cos(b), Mathf.Cos(a), Mathf.Sin(a) * Mathf.Sin(b));
                    v.Add(new Vector3(dir.x * radius, dir.y * radius * 0.75f, dir.z * radius));
                    n.Add(dir);
                }
            }
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < segs; s++)
                {
                    int i0 = r * (segs + 1) + s, i1 = i0 + segs + 1;
                    tri.AddRange(new[] { i0, i1, i0 + 1, i0 + 1, i1, i1 + 1 });
                }
            // Tentacles: thin wavy ribbons hanging from the rim.
            for (int k = 0; k < 7; k++)
            {
                float b = k / 7f * Mathf.PI * 2f;
                var root = new Vector3(Mathf.Cos(b) * radius * 0.6f, 0f, Mathf.Sin(b) * radius * 0.6f);
                var side = new Vector3(-Mathf.Sin(b), 0f, Mathf.Cos(b)) * 0.025f;
                int at = v.Count;
                const int steps = 8;
                for (int st = 0; st <= steps; st++)
                {
                    float f = st / (float)steps;
                    var p = root + new Vector3(Mathf.Sin(f * 9f + k) * 0.08f, -f * 1.6f, Mathf.Cos(f * 7f + k) * 0.08f);
                    v.Add(p - side); n.Add(-side.normalized);
                    v.Add(p + side); n.Add(side.normalized);
                }
                for (int st = 0; st < steps; st++)
                {
                    int a0 = at + st * 2;
                    tri.AddRange(new[] { a0, a0 + 2, a0 + 1, a0 + 1, a0 + 2, a0 + 3 });
                }
            }
            _jellyMesh = new Mesh { name = "Jellyfish" };
            _jellyMesh.SetVertices(v);
            _jellyMesh.SetNormals(n);
            _jellyMesh.SetTriangles(tri, 0);
            _jellyMesh.RecalculateBounds();
        }

        // ------------------------------------------------------------------ sand slide

        bool InSand(Vector3 p) => p.y < _seabed + sandHeight * _k;

        void Sand(ref float visibility, ref Color filter)
        {
            float top = _seabed + sandHeight;
            Cloud(new Vector3(_arenaCentre.x, (top + _seabed - 15f) * 0.5f, _arenaCentre.z),
                  new Vector3(_arenaHalf.x * 2f + 20f, top - (_seabed - 15f), _arenaHalf.y * 2f + 20f),
                  new Color(0.95f, 0.84f, 0.62f), Mathf.Lerp(300f, sandThickness, _k), new Vector3(0f, 0.12f, 0f));

            var cam = WaterFx.ViewCamera;
            if (cam != null)
            {
                Vector3 c = cam.transform.position;
                if (InSand(c))
                {
                    filter = Color.Lerp(Color.white, new Color(1f, 0.9f, 0.72f), _k);
                    visibility = Mathf.Lerp(1f, 0.45f, _k);
                }
                // Sand keeps pouring off the slopes around us.
                if (Time.time >= _nextKick && _k > 0.3f)
                {
                    _nextKick = Time.time + 0.25f;
                    var at = c + new Vector3(Random.Range(-25f, 25f), 0f, Random.Range(-25f, 25f));
                    if (WaterFx.SeabedHeight(at, out float y)) SandTrails.Kick(new Vector3(at.x, y, at.z), Random.Range(1.5f, 3.5f));
                }
            }
            if (GameAudio.Lib != null) GameAudio.Loop("rumble", GameAudio.Lib.rumbleLoop, 0.7f * _k);
            MatchHud.ShowEvent($"SAND SLIDE  -  THE SEABED IS CLOUDED  {Mathf.CeilToInt((float)(_end - MatchManager.Now))}s");
        }

        // ------------------------------------------------------------------ red tide

        Vector3 TideCentre() => _tideStart + _dir * (tideDrift * _t);
        float TideRadius() => tideRadius * Mathf.Max(0.05f, _k);

        bool InTide(Vector3 p)
        {
            Vector3 c = TideCentre();
            float r = TideRadius() * 0.85f;
            Vector2 d = new Vector2(p.x - c.x, p.z - c.z);
            return d.sqrMagnitude < r * r && p.y > _surface - tideDepth && p.y < _surface + 1f;
        }

        void Tide(ref float visibility, ref float saturation, ref Color filter)
        {
            Vector3 c = TideCentre();
            float r = TideRadius();
            Cloud(new Vector3(c.x, _surface - tideDepth * 0.5f, c.z), new Vector3(r * 2f, tideDepth, r * 2f),
                  new Color(0.6f, 0.2f, 0.08f), Mathf.Lerp(200f, 5f, _k), new Vector3(0.35f, 0.15f, 0.35f));

            // Authority: the toxin works on everyone inside, fish and shark.
            if (Authority)
                foreach (var fp in FishPlayer.All)
                {
                    if (fp == null || !fp.isActiveAndEnabled || fp.IsOut || fp.IsBeingEaten) continue;
                    if (InTide(fp.transform.position) && fp.TryGetComponent(out FishVitals vitals)) vitals.Drain(tideDrain * Time.deltaTime);
                }

            // This player: inside, the water turns red and murky, and they're told it's hurting them.
            var me = Local();
            var cam = WaterFx.ViewCamera;
            if (cam != null && InTide(cam.transform.position))
            {
                filter = Color.Lerp(Color.white, new Color(1f, 0.6f, 0.5f), _k);
                saturation = -10f * _k;
                visibility = Mathf.Lerp(1f, 0.4f, _k);
            }
            if (me != null && !me.IsOut && InTide(me.transform.position) && Time.time >= _nextBurn && GameAudio.Lib != null)
            {
                _nextBurn = Time.time + 1.5f;
                GameAudio.Play(GameAudio.Lib.tideBurn, 0.7f, 0.1f);
            }
            if (me != null && !me.IsOut && InTide(me.transform.position) && Time.time >= _nextToxicFlash)
            {
                _nextToxicFlash = Time.time + 3f;
                MatchHud.Flash("TOXIC WATER - you're losing health!", 1.8f);
            }
            MatchHud.ShowEvent($"RED TIDE  -  TOXIC WATER, STAY OUT  {Mathf.CeilToInt((float)(_end - MatchManager.Now))}s");
        }

        static FishPlayer Local()
        {
            if (NetworkClient.active)
                return NetworkClient.localPlayer != null ? NetworkClient.localPlayer.GetComponent<FishPlayer>() : null;
            foreach (var fp in FishPlayer.All) if (fp != null && fp.isActiveAndEnabled) return fp;
            return null;
        }

        // ------------------------------------------------------------------ the looks

        // One block of coloured haze (HDRP local volumetric fog): the cold layer, the sand, or the red tide.
        void Cloud(Vector3 centre, Vector3 size, Color albedo, float meanFreePath, Vector3 fade)
        {
            if (_cloud == null)
            {
                var go = new GameObject("ConditionHaze");
                go.transform.SetParent(transform, false);
                _cloud = go.AddComponent<LocalVolumetricFog>();
            }
            _cloud.transform.position = centre;
            var p = _cloud.parameters;
            p.albedo = albedo;
            p.meanFreePath = Mathf.Max(1f, meanFreePath);
            p.size = size;
            p.positiveFade = fade;
            p.negativeFade = fade;
            p.distanceFadeStart = 10000f;
            p.distanceFadeEnd = 10000f;
            _cloud.parameters = p;
        }

        // Sun, grade and view range for this frame (all back to normal when nothing is on).
        void ApplyLook(float sun, float visibility, float exposure, float saturation, Color filter)
        {
            if (_sun == null)
            {
                _sun = RenderSettings.sun;
                if (_sun == null)
                    foreach (var l in FindObjectsByType<Light>()) if (l.type == LightType.Directional) { _sun = l; break; }
                if (_sun != null) _sunBase = _sun.intensity;
            }
            if (_sun != null && _sunBase >= 0f) _sun.intensity = _sunBase * sun;

            // The storm whips the ocean up.
            if (_water == null)
            {
                _water = FindAnyObjectByType<WaterSurface>();
                if (_water != null) { _windBase = _water.largeWindSpeed; _ripplesBase = _water.ripplesWindSpeed; }
            }
            if (_water != null && _windBase >= 0f)
            {
                float storm = _kind == WorldEventKind.Storm ? _k : 0f;
                _water.largeWindSpeed = _windBase * (1f + 1.5f * storm);
                _water.ripplesWindSpeed = _ripplesBase * (1f + 2f * storm);
            }

            CreatureVision.VisibilityScale = visibility;
            bool any = !Mathf.Approximately(visibility, 1f) || exposure != 0f || saturation != 0f || filter != Color.white;
            EnsureVolume();
            _volume.weight = any ? 1f : 0f;
            _grade.postExposure.value = exposure;
            _grade.saturation.value = saturation;
            _grade.colorFilter.value = filter;
            var vision = CreatureVision.Local;
            float haze = vision != null && vision.RoleFogDistance > 0f ? vision.RoleFogDistance : 60f;
            _fog.meanFreePath.overrideState = !Mathf.Approximately(visibility, 1f);
            _fog.meanFreePath.value = haze * visibility;
        }

        // A global volume on the shared layer, above the role grades, so every player camera picks it up.
        void EnsureVolume()
        {
            if (_volume != null) return;
            var go = new GameObject("ConditionGrade");
            go.transform.SetParent(transform, false);
            _volume = go.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 30f;
            _volume.weight = 0f;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.hideFlags = HideFlags.DontSave;
            _grade = profile.Add<ColorAdjustments>(false);
            _grade.postExposure.overrideState = true;
            _grade.saturation.overrideState = true;
            _grade.colorFilter.overrideState = true;
            _fog = profile.Add<Fog>(false);
            _volume.sharedProfile = profile;
        }
    }
}
