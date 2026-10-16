using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishGame
{
    /// <summary>
    /// Furrows in the sand wherever something drags along the bottom - players, the shark, trash being
    /// pushed, and school fish near the camera - fading back into plain sand over <see cref="fadeSeconds"/>,
    /// plus a puff of sand kicked up as it goes. A fish hugging the seabed leaves a path that can be followed.
    ///
    /// One map covers the whole seabed (texels store when each spot was last disturbed and how deep); the
    /// sand shader (SandTrail.hlsl, via SeabedSand.hlsl) draws the furrows from it. A compute shader stamps
    /// it, only where something touches, so the cost doesn't grow with the size of the seabed.
    ///
    /// Purely visual and local: every machine sees the same players, trash and school fish, so everyone
    /// gets the same trails with no networking.
    /// </summary>
    [DefaultExecutionOrder(60)] // after everything has moved this frame
    public class SandTrails : MonoBehaviour
    {
        [SerializeField] ComputeShader compute;

        [Header("Trail map")]
        [Tooltip("Texels across the whole seabed. 2048 over 300 m = about 15 cm each.")]
        [SerializeField] int resolution = 2048;

        [Header("Furrows")]
        [Tooltip("Seconds a furrow takes to fade back into plain sand.")]
        [SerializeField] float fadeSeconds = 45f;
        [Tooltip("A body leaves a furrow once its underside comes this close to the sand (metres) - deeper " +
                 "the closer it gets.")]
        [SerializeField] float touchHeight = 0.3f;
        [Tooltip("Furrow half-width, as a share of the body's radius (the body ploughs a little more than " +
                 "its own width).")]
        [SerializeField] float furrowWidth = 1.6f;
        [Tooltip("Narrowest a furrow gets (half-width, metres), so little fish still leave a visible line.")]
        [SerializeField] float minFurrowWidth = 0.35f;
        [Tooltip("How strongly a furrow's sides catch the light - how deep it looks.")]
        [SerializeField] float sideTilt = 0.35f;
        [Tooltip("How much darker the churned sand down the middle is, 0..1. The water's caustics play over " +
                 "everything, so it takes a fair bit to show.")]
        [Range(0f, 1f)][SerializeField] float darken = 0.7f;
        [Tooltip("How much lighter the sand pushed up along a furrow's edges is (0.5 = 50% lighter).")]
        [SerializeField] float rimLight = 0.5f;

        [Header("School fish")]
        [SerializeField] bool schoolFish = true;
        [Tooltip("Only school fish this close to the camera are checked (metres) - they're mostly up in the " +
                 "water anyway, and far ones wouldn't be seen.")]
        [SerializeField] float schoolFishRange = 60f;
        [Tooltip("How deep a school fish's furrow is next to a player's, 0..1.")]
        [Range(0f, 1f)][SerializeField] float schoolFishDepth = 0.6f;

        [Header("Sand puffs")]
        [SerializeField] bool puffs = true;
        [Tooltip("A puff every this many metres something drags along the bottom...")]
        [SerializeField] float puffSpacing = 0.7f;
        [Tooltip("...as long as it's moving at least this fast (m/s).")]
        [SerializeField] float puffMinSpeed = 0.6f;
        [Tooltip("Puff size for a fish-sized body (metres); bigger bodies kick up bigger ones.")]
        [SerializeField] Vector2 puffSize = new Vector2(0.8f, 1.6f);
        [SerializeField] Vector2 puffLifetime = new Vector2(2.5f, 4.5f);
        [SerializeField] int maxPuffs = 600;

        static readonly int MapId = Shader.PropertyToID("_SandTrailMap");
        static readonly int AreaId = Shader.PropertyToID("_SandTrailArea");
        static readonly int ParamsId = Shader.PropertyToID("_SandTrailParams");
        static readonly int InfoId = Shader.PropertyToID("_SandTrailInfo");
        static readonly int CsTrailId = Shader.PropertyToID("_Trail");
        static readonly int CsSizeId = Shader.PropertyToID("_Size");
        static readonly int CsOriginId = Shader.PropertyToID("_Origin");
        static readonly int CsAreaId = Shader.PropertyToID("_Area");
        static readonly int CsSegId = Shader.PropertyToID("_Seg");
        static readonly int CsStampId = Shader.PropertyToID("_Stamp");
        const string PuffMaterialPath = "SandPuff";

        struct Contact
        {
            public Vector3 point;   // where it last touched the sand
            public int frame;       // when
            public float puffOwed;  // puffs due but not yet kicked up (fractional)
        }

        RenderTexture _map;
        int _stampKernel = -1, _clearKernel = -1;
        readonly int[] _origin = new int[2];
        Vector4 _area;          // xy = seabed's min corner (x,z), z = texels per metre
        bool _ready;
        bool _unavailable;

        MeshCollider _seabed;
        float[] _heights;       // seabed height on a regular grid
        int _hnx, _hnz;
        Vector2 _hMin, _hStep;
        bool _heightsDirty = true;

        readonly Dictionary<Object, Contact> _contacts = new Dictionary<Object, Contact>();
        readonly List<Object> _stale = new List<Object>();
        readonly Dictionary<BoidsManager, float> _schoolFishRadius = new Dictionary<BoidsManager, float>();

        ParticleSystem _puffs;
        ParticleSystem.EmitParams _emit;
        readonly System.Random _rng = new System.Random(7);

        static SandTrails s_active;

        /// <summary>Kick up a cloud of sand on the seabed at <paramref name="ground"/> (a violent bite on the
        /// bottom). Nothing happens without sand trails in the scene.</summary>
        public static void Kick(Vector3 ground, float radius)
        {
            if (s_active == null) return;
            for (int i = 0; i < 3; i++)
            {
                Vector2 d = Random.insideUnitCircle.normalized;
                s_active.Puff(ground, new Vector3(d.x, 0f, d.y), radius, 1f);
            }
        }

        void OnEnable()
        {
            s_active = this;
            TerrainNetSync.SeabedChanged += MarkSeabedChanged;
            _heightsDirty = true;
        }

        void OnDisable()
        {
            if (s_active == this) s_active = null;
            TerrainNetSync.SeabedChanged -= MarkSeabedChanged;
            Shader.SetGlobalVector(AreaId, Vector4.zero); // the sand draws no furrows
            if (_map != null) { _map.Release(); _map = null; }
            _ready = false;
        }

        void MarkSeabedChanged() => _heightsDirty = true;

        void LateUpdate()
        {
            if (!EnsureReady()) return;
            if (_heightsDirty) BuildHeights();
            if (_heights == null) return;

            float now = Time.timeSinceLevelLoad;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Shader.SetGlobalVector(ParamsId, new Vector4(now, fadeSeconds, sideTilt, darken));
            Shader.SetGlobalVector(InfoId, new Vector4(resolution, rimLight, 0f, 0f));

            // Players, the shark, trash - anything that pushes plants has a body shape we can use.
            foreach (var p in VegetationPusher.All)
                if (p != null) Touch(p, p.Centre, p.HalfSegment, p.Radius, 1f, puffSpacing, now, dt);

            // School fish near the camera.
            if (schoolFish)
            {
                Transform view = VegetationPushMap.ViewPoint();
                if (view != null)
                {
                    Vector3 v = view.position;
                    float range2 = schoolFishRange * schoolFishRange;
                    foreach (var flock in BoidsManager.All)
                    {
                        if (flock == null || flock.allFish == null) continue;
                        float r = SchoolFishRadius(flock);
                        // The sim's own copy of the positions (fish-id order, like allFish) - far cheaper
                        // than reading a thousand transforms.
                        if (flock.TryGetSimPositions(out var simPos, out var ids, out int simCount))
                        {
                            var fish = flock.allFish;
                            for (int k = 0; k < simCount; k++)
                            {
                                Vector3 pos = simPos[k];
                                float dx = pos.x - v.x, dz = pos.z - v.z;
                                if (dx * dx + dz * dz > range2) continue;
                                var f = fish[ids[k]];
                                if (f == null || !f.activeInHierarchy) continue;
                                Touch(f, pos, Vector3.zero, r, schoolFishDepth, puffSpacing * 3f, now, dt);
                            }
                            continue;
                        }
                        foreach (var f in flock.allFish)
                        {
                            if (f == null || !f.activeInHierarchy) continue;
                            Vector3 pos = f.transform.position;
                            float dx = pos.x - v.x, dz = pos.z - v.z;
                            if (dx * dx + dz * dz > range2) continue;
                            Touch(f, pos, Vector3.zero, r, schoolFishDepth, puffSpacing * 3f, now, dt);
                        }
                    }
                }
            }

            // Forget bodies that haven't touched for a while (so the next touch starts a fresh furrow).
            if (Time.frameCount % 120 == 0)
            {
                _stale.Clear();
                foreach (var kv in _contacts)
                    if (kv.Key == null || Time.frameCount - kv.Value.frame > 60) _stale.Add(kv.Key);
                foreach (var k in _stale) _contacts.Remove(k);
            }
        }

        // If this body is down on the sand, furrow it from where it touched last frame to here.
        void Touch(Object key, Vector3 centre, Vector3 halfSegment, float radius, float depthScale, float spacing,
                   float now, float dt)
        {
            // Its lowest point: the middle or either end of its body, whichever is nearest the sand.
            Vector3 low = centre;
            float gap = Gap(centre, radius);
            if (halfSegment.sqrMagnitude > 1e-6f)
            {
                Vector3 a = centre - halfSegment, b = centre + halfSegment;
                float ga = Gap(a, radius), gb = Gap(b, radius);
                if (ga < gap) { gap = ga; low = a; }
                if (gb < gap) { gap = gb; low = b; }
            }
            if (gap >= touchHeight) return;

            float depth = Mathf.Clamp01(1f - Mathf.Max(gap, 0f) / touchHeight) * depthScale;
            Vector3 here = new Vector3(low.x, Height(low.x, low.z), low.z);
            Vector3 from = here;
            float owed = 0f;
            if (_contacts.TryGetValue(key, out var last) && last.frame == Time.frameCount - 1 &&
                (last.point - here).sqrMagnitude < 25f) // a teleport doesn't draw a line across the map
            {
                from = last.point;
                owed = last.puffOwed;
            }

            float width = Mathf.Max(minFurrowWidth, radius * furrowWidth);
            Stamp(from, here, width, depth, now);

            // Sand kicked up as it drags along.
            float moved = new Vector2(here.x - from.x, here.z - from.z).magnitude;
            if (puffs && moved / dt >= puffMinSpeed)
            {
                owed += moved / Mathf.Max(spacing, 0.05f);
                int count = Mathf.Min((int)owed, 4);
                owed -= (int)owed;
                Vector3 dir = (here - from) / Mathf.Max(moved, 1e-4f);
                for (int i = 0; i < count; i++)
                    Puff(Vector3.Lerp(from, here, (i + 1f) / count), dir, radius, depth);
            }

            _contacts[key] = new Contact { point = here, frame = Time.frameCount, puffOwed = owed };
        }

        float Gap(Vector3 p, float radius) => p.y - radius - Height(p.x, p.z);

        void Stamp(Vector3 a, Vector3 b, float halfWidth, float depth, float now)
        {
            float tpm = _area.z;
            int x0 = Mathf.FloorToInt((Mathf.Min(a.x, b.x) - halfWidth - _area.x) * tpm);
            int x1 = Mathf.CeilToInt((Mathf.Max(a.x, b.x) + halfWidth - _area.x) * tpm);
            int z0 = Mathf.FloorToInt((Mathf.Min(a.z, b.z) - halfWidth - _area.y) * tpm);
            int z1 = Mathf.CeilToInt((Mathf.Max(a.z, b.z) + halfWidth - _area.y) * tpm);
            x0 = Mathf.Max(x0, 0); z0 = Mathf.Max(z0, 0);
            x1 = Mathf.Min(x1, resolution - 1); z1 = Mathf.Min(z1, resolution - 1);
            if (x1 < x0 || z1 < z0) return;

            // Bound every time, not once: the compute shader's state is lost whenever the asset is
            // reimported (editing shaders while playing), and every stamp would then miss the map.
            compute.SetTexture(_stampKernel, CsTrailId, _map);
            compute.SetInt(CsSizeId, resolution);
            compute.SetVector(CsAreaId, _area);
            _origin[0] = x0; _origin[1] = z0; // reused: SetInts' params array would allocate every stamp
            compute.SetInts(CsOriginId, _origin);
            compute.SetVector(CsSegId, new Vector4(a.x, a.z, b.x, b.z));
            compute.SetVector(CsStampId, new Vector4(halfWidth, depth, now, fadeSeconds));
            compute.Dispatch(_stampKernel, Mathf.CeilToInt((x1 - x0 + 1) / 8f), Mathf.CeilToInt((z1 - z0 + 1) / 8f), 1);
        }

        // ---------------------------------------------------------------- setup

        bool EnsureReady()
        {
            if (_ready) return true;
            if (_unavailable) return false;
            if (compute == null || !SystemInfo.supportsComputeShaders)
            {
                Debug.LogWarning("[SandTrails] No compute shader (or no compute support) - no trails in the sand.", this);
                _unavailable = true;
                return false;
            }
            var terrain = FindAnyObjectByType<TestTerrain>();
            if (terrain == null || !terrain.TryGetComponent(out _seabed) || _seabed.sharedMesh == null) return false;

            Bounds b = _seabed.bounds;
            float size = Mathf.Max(b.size.x, b.size.z);
            _area = new Vector4(b.min.x, b.min.z, resolution / size, 0f);

            _map = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.RGHalf)
            {
                name = "SandTrailMap",
                enableRandomWrite = true,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            _map.Create();

            _clearKernel = compute.FindKernel("Clear");
            _stampKernel = compute.FindKernel("Stamp");
            compute.SetInt(CsSizeId, resolution);
            compute.SetVector(CsAreaId, _area);
            compute.SetTexture(_clearKernel, CsTrailId, _map);
            compute.SetTexture(_stampKernel, CsTrailId, _map);
            int groups = Mathf.CeilToInt(resolution / 8f);
            compute.Dispatch(_clearKernel, groups, groups, 1);

            Shader.SetGlobalTexture(MapId, _map);
            Shader.SetGlobalVector(AreaId, new Vector4(_area.x, _area.y, _area.z, 1f));
            Shader.SetGlobalVector(InfoId, new Vector4(resolution, rimLight, 0f, 0f));
            _ready = true;
            Debug.Log($"[SandTrails] {resolution}x{resolution} trail map over {size:0} m of seabed ({size / resolution * 100f:0.#} cm per texel).");
            return true;
        }

        // The seabed's height on a regular grid, so checking who's touching it costs no raycasts. The
        // terrain mesh is itself a regular grid; anything else falls back to sampling it with raycasts.
        void BuildHeights()
        {
            _heightsDirty = false;
            if (_seabed == null || _seabed.sharedMesh == null) return;
            Bounds b = _seabed.bounds;
            var verts = _seabed.sharedMesh.vertices;
            int n = Mathf.RoundToInt(Mathf.Sqrt(verts.Length));
            if (n >= 2 && n * n == verts.Length && TryGridFromVertices(verts, n, b)) return;

            // Fallback: sample it every metre.
            _hnx = Mathf.CeilToInt(b.size.x) + 1;
            _hnz = Mathf.CeilToInt(b.size.z) + 1;
            _hMin = new Vector2(b.min.x, b.min.z);
            _hStep = new Vector2(b.size.x / (_hnx - 1), b.size.z / (_hnz - 1));
            _heights = new float[_hnx * _hnz];
            for (int z = 0; z < _hnz; z++)
                for (int x = 0; x < _hnx; x++)
                {
                    var ray = new Ray(new Vector3(_hMin.x + x * _hStep.x, b.max.y + 5f, _hMin.y + z * _hStep.y), Vector3.down);
                    _heights[z * _hnx + x] = _seabed.Raycast(ray, out var hit, b.size.y + 10f) ? hit.point.y : b.min.y;
                }
        }

        bool TryGridFromVertices(Vector3[] verts, int n, Bounds b)
        {
            var grid = new float[n * n];
            var filled = new bool[n * n];
            var toWorld = _seabed.transform.localToWorldMatrix;
            var step = new Vector2(b.size.x / (n - 1), b.size.z / (n - 1));
            int count = 0;
            foreach (var v in verts)
            {
                Vector3 w = toWorld.MultiplyPoint3x4(v);
                int ix = Mathf.RoundToInt((w.x - b.min.x) / step.x);
                int iz = Mathf.RoundToInt((w.z - b.min.z) / step.y);
                if (ix < 0 || iz < 0 || ix >= n || iz >= n) return false;
                int i = iz * n + ix;
                if (!filled[i]) { filled[i] = true; count++; }
                grid[i] = w.y;
            }
            if (count != n * n) return false; // not a plain grid
            _heights = grid;
            _hnx = _hnz = n;
            _hMin = new Vector2(b.min.x, b.min.z);
            _hStep = step;
            return true;
        }

        float Height(float x, float z)
        {
            float fx = Mathf.Clamp((x - _hMin.x) / _hStep.x, 0f, _hnx - 1.001f);
            float fz = Mathf.Clamp((z - _hMin.y) / _hStep.y, 0f, _hnz - 1.001f);
            int ix = (int)fx, iz = (int)fz;
            float tx = fx - ix, tz = fz - iz;
            int i = iz * _hnx + ix;
            float h0 = Mathf.Lerp(_heights[i], _heights[i + 1], tx);
            float h1 = Mathf.Lerp(_heights[i + _hnx], _heights[i + _hnx + 1], tx);
            return Mathf.Lerp(h0, h1, tz);
        }

        // Every fish in a school is the same prefab, so measure one and remember it (as the plants do).
        float SchoolFishRadius(BoidsManager flock)
        {
            if (_schoolFishRadius.TryGetValue(flock, out float r)) return r;
            foreach (var f in flock.allFish)
                if (f != null)
                {
                    r = VegetationPusher.RadiusFromBody(f) * 0.6f;
                    _schoolFishRadius[flock] = r;
                    return r;
                }
            return 0.25f;
        }

        // ---------------------------------------------------------------- sand puffs

        void Puff(Vector3 ground, Vector3 dir, float radius, float depth)
        {
            if (!EnsurePuffs()) return;
            float scale = Mathf.Clamp(radius / 0.35f, 0.6f, 2.2f); // the shark kicks up more than a fish
            for (int k = 0; k < 2; k++)
            {
                Vector3 spread = new Vector3(R(-1f, 1f), 0f, R(-1f, 1f)) * (radius * 0.5f);
                _emit.position = ground + spread + Vector3.up * (0.1f + 0.15f * scale);
                // Sand thrown up and back from the body's path, then the water stops it.
                _emit.velocity = (Vector3.up * R(0.15f, 0.4f) - dir * R(0.1f, 0.35f) + new Vector3(R(-0.2f, 0.2f), 0f, R(-0.2f, 0.2f))) * scale;
                _emit.startSize = R(puffSize.x, puffSize.y) * scale;
                _emit.startLifetime = R(puffLifetime.x, puffLifetime.y);
                _emit.rotation = R(0f, 360f);
                _emit.startColor = new Color(1f, 1f, 1f, R(0.35f, 0.6f) * Mathf.Lerp(0.6f, 1f, depth));
                _emit.randomSeed = (uint)_rng.Next();
                _puffs.Emit(_emit, 1);
            }
        }

        float R(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        // One world-space particle system for every puff, built the first time it's needed. The look of a
        // puff (a soft, silty cloud) lives in Resources/SandPuff.mat.
        bool EnsurePuffs()
        {
            if (_puffs != null) return true;
            if (!puffs || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return false;
            var material = Resources.Load<Material>(PuffMaterialPath);
            if (material == null)
            {
                Debug.LogWarning($"[SandTrails] No material at Resources/{PuffMaterialPath} - no sand puffs.", this);
                puffs = false;
                return false;
            }

            var go = new GameObject("Sand Puffs");
            go.transform.SetParent(transform, false);
            _puffs = go.AddComponent<ParticleSystem>();
            var main = _puffs.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = maxPuffs;
            main.startSpeed = 0f;
            main.gravityModifier = 0.004f; // silt settles back, slowly

            var emission = _puffs.emission;
            emission.enabled = false;
            var shape = _puffs.shape;
            shape.enabled = false;

            var size = _puffs.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.35f, 2.5f, 2.5f), new Keyframe(0.4f, 0.8f), new Keyframe(1f, 1f, 0f, 0f)));

            var color = _puffs.colorOverLifetime;
            color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
            color.color = fade;

            var spin = _puffs.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);

            var limit = _puffs.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 100f; // never clamps; only here for the drag
            limit.drag = 1.8f;

            var noise = _puffs.noise;
            noise.enabled = true;
            noise.strength = 0.15f;
            noise.frequency = 0.4f;
            noise.scrollSpeed = 0.15f;
            noise.damping = true;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.maxParticleSize = 1f;
            // The shader (the blood one, tinted for silt) wants: UV in TEXCOORD0.xy, a random in .z, age in .w.
            renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
            {
                ParticleSystemVertexStream.Position,
                ParticleSystemVertexStream.Color,
                ParticleSystemVertexStream.UV,
                ParticleSystemVertexStream.StableRandomX,
                ParticleSystemVertexStream.AgePercent,
            });
            _puffs.Play();
            return true;
        }
    }
}
