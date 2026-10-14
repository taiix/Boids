using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace FishGame
{
    /// <summary>
    /// The sunlit-reef feel around whoever is looking: slanting sunbeams and drifting specks in the
    /// water. Purely local rendering, nothing networked.
    ///
    /// Sunbeams: HDRP's volumetric fog already lights the water with the caustics, but at our depth
    /// that only reads as a broad glow. These are art-directed beams on top: soft quads laid along the
    /// refracted sun direction, rising from the seabed, turning around their own axis to face the
    /// camera. They are anchored to the world on a grid (so they don't swim with you), sway and breathe
    /// slowly, and take their brightness from the water behind them (LightShaft.shader), so the same
    /// settings work for both roles' water and in the menu.
    ///
    /// Specks: a box of tiny particles around the camera, emitted in world space so they hang in the
    /// water as you swim through them (WaterMote.shader).
    ///
    /// Put one in any underwater scene. Materials load from Resources (LightShafts, WaterMotes).
    /// </summary>
    [DisallowMultipleComponent]
    public class UnderwaterAmbience : MonoBehaviour
    {
        const string ShaftMaterialPath = "LightShafts";
        const string MoteMaterialPath = "WaterMotes";

        [Header("Sunbeams")]
        [SerializeField] bool sunbeams = true;
        [Tooltip("Grid the beams are anchored to (m). Each cell may hold one beam.")]
        [SerializeField] float cellSize = 8f;
        [Tooltip("Share of cells that hold a beam.")]
        [Range(0f, 1f)] [SerializeField] float density = 0.42f;
        [Tooltip("Beams are kept within this horizontal distance of the camera (m). Keep it beyond the " +
                 "material's far fade so beams never pop at the edge.")]
        [SerializeField] float radius = 52f;
        [SerializeField] Vector2 width = new Vector2(2.5f, 7f);
        [Tooltip("How far each beam reaches up from the seabed (m), before the surface cuts it short.")]
        [SerializeField] Vector2 length = new Vector2(22f, 42f);
        [Tooltip("Sideways drift of a beam, like the surface rocking above it (m).")]
        [SerializeField] float sway = 0.7f;
        [Tooltip("How much a beam's strength breathes over time (0 = steady).")]
        [Range(0f, 1f)] [SerializeField] float flicker = 0.55f;

        [Header("Specks")]
        [SerializeField] bool specks = true;
        [SerializeField] int maxSpecks = 1600;
        [SerializeField] float specksPerSecond = 170f;
        [Tooltip("Box around the camera that specks appear in (m).")]
        [SerializeField] Vector3 speckBox = new Vector3(34f, 18f, 34f);
        [SerializeField] Vector2 speckSize = new Vector2(0.02f, 0.075f);
        [Tooltip("Slow sinking speed (m/s), like marine snow.")]
        [SerializeField] float sinkSpeed = 0.04f;

        [Header("Scene")]
        [Tooltip("The sun. Left empty, the brightest directional light is used.")]
        [SerializeField] Light sun;

        struct Beam
        {
            public Vector3 bottom;
            public float height;      // along the beam
            public float halfWidth;
            public float strength;
            public float seed;
            public float phase;
        }

        MeshFilter _shaftFilter;
        MeshRenderer _shaftRenderer;
        Mesh _mesh;
        ParticleSystem _specks;
        readonly List<Beam> _beams = new List<Beam>();
        readonly Dictionary<long, float> _seabedCache = new Dictionary<long, float>();
        readonly List<Vector3> _verts = new List<Vector3>();
        readonly List<Vector3> _normals = new List<Vector3>();
        readonly List<Vector4> _uvs = new List<Vector4>();
        readonly List<int> _tris = new List<int>();
        Vector2Int _builtAroundCell = new Vector2Int(int.MinValue, int.MinValue);
        Vector3 _builtForDirection;

        void OnEnable()
        {
            if (sunbeams) BuildShafts();
            if (specks) BuildSpecks();
        }

        void OnDisable()
        {
            if (_shaftRenderer != null) _shaftRenderer.enabled = false;
            if (_specks != null) _specks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }

        // ------------------------------------------------------------------ setup

        void BuildShafts()
        {
            if (_shaftRenderer != null) return;
            var material = Resources.Load<Material>(ShaftMaterialPath);
            if (material == null)
            {
                Debug.LogWarning($"UnderwaterAmbience: no material at Resources/{ShaftMaterialPath}, no sunbeams.");
                sunbeams = false;
                return;
            }

            // World-space mesh on an unmoved child: vertices are written in world coordinates.
            var go = new GameObject("Sunbeams");
            go.transform.SetParent(transform, false);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.localScale = Vector3.one;
            _shaftFilter = go.AddComponent<MeshFilter>();
            _shaftRenderer = go.AddComponent<MeshRenderer>();
            _shaftRenderer.sharedMaterial = material;
            _shaftRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _shaftRenderer.receiveShadows = false;
            _shaftRenderer.lightProbeUsage = LightProbeUsage.Off;
            _shaftRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _shaftRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;

            _mesh = new Mesh { name = "Sunbeams" };
            _mesh.MarkDynamic();
            _shaftFilter.sharedMesh = _mesh;
        }

        void BuildSpecks()
        {
            if (_specks != null) return;
            var material = Resources.Load<Material>(MoteMaterialPath);
            if (material == null)
            {
                Debug.LogWarning($"UnderwaterAmbience: no material at Resources/{MoteMaterialPath}, no specks.");
                specks = false;
                return;
            }

            var go = new GameObject("Specks");
            go.transform.SetParent(transform, false);
            _specks = go.AddComponent<ParticleSystem>();
            _specks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = _specks.main;
            main.playOnAwake = false;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = maxSpecks;
            main.startSpeed = 0f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(6f, 11f);
            main.startSize = new ParticleSystem.MinMaxCurve(speckSize.x, speckSize.y);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 1f), new Color(0.85f, 0.95f, 1f, 0.7f));

            var emission = _specks.emission;
            emission.enabled = true;
            emission.rateOverTime = specksPerSecond;

            var shape = _specks.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = speckBox;

            // Sinking slowly. x/y/z must share one curve mode (Unity errors otherwise).
            var vel = _specks.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.y = new ParticleSystem.MinMaxCurve(-sinkSpeed * 1.5f, -sinkSpeed * 0.5f);
            vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

            var noise = _specks.noise;
            noise.enabled = true;
            noise.strength = 0.12f;
            noise.frequency = 0.25f;
            noise.scrollSpeed = 0.08f;
            noise.damping = true;
            noise.quality = ParticleSystemNoiseQuality.Low;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sortMode = ParticleSystemSortMode.None; // additive: order doesn't matter
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.minParticleSize = 0f;
            renderer.maxParticleSize = 0.05f;
            // Layout the shader expects: UV in TEXCOORD0.xy, a per-particle random in .z, age in .w.
            renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
            {
                ParticleSystemVertexStream.Position,
                ParticleSystemVertexStream.Color,
                ParticleSystemVertexStream.UV,
                ParticleSystemVertexStream.StableRandomX,
                ParticleSystemVertexStream.AgePercent,
            });
        }

        // ------------------------------------------------------------------ per frame

        void LateUpdate()
        {
            var cam = WaterFx.ViewCamera;
            if (cam == null) return;
            Vector3 eye = cam.transform.position;
            bool underwater = eye.y < WaterFx.SurfaceY - 0.2f;

            if (_specks != null)
            {
                _specks.transform.position = eye;
                if (underwater && !_specks.isPlaying) _specks.Play();
                else if (!underwater && _specks.isPlaying) _specks.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }

            if (_shaftRenderer != null)
            {
                _shaftRenderer.enabled = underwater;
                if (underwater) UpdateShafts(eye);
            }
        }

        void UpdateShafts(Vector3 eye)
        {
            Vector3 down = BeamDirection();
            Vector3 up = -down;

            // Re-pick the beams only when the camera moves to another cell (or the sun moves).
            var cell = new Vector2Int(Mathf.FloorToInt(eye.x / cellSize), Mathf.FloorToInt(eye.z / cellSize));
            if (cell != _builtAroundCell || Vector3.Angle(down, _builtForDirection) > 0.5f)
            {
                _builtAroundCell = cell;
                _builtForDirection = down;
                PickBeams(cell, up);
            }

            _verts.Clear(); _normals.Clear(); _uvs.Clear();
            float t = Time.time;
            Vector3 swayDir = Vector3.Cross(up, Vector3.up).sqrMagnitude > 1e-4f
                ? Vector3.Cross(up, Vector3.up).normalized
                : Vector3.right;

            foreach (var b in _beams)
            {
                // Rocks gently with the surface overhead; strength breathes, each beam on its own beat.
                Vector3 bottom = b.bottom + swayDir * (Mathf.Sin(t * 0.21f + b.phase) * sway);
                Vector3 top = bottom + up * b.height;
                float breathe = 1f - flicker * Mathf.PerlinNoise(b.seed * 13.1f, t * 0.12f);
                float strength = b.strength * breathe;

                Vector3 mid = (bottom + top) * 0.5f;
                Vector3 side = Vector3.Cross(up, mid - eye);
                if (side.sqrMagnitude < 1e-6f) side = swayDir;
                side = side.normalized * b.halfWidth;

                _verts.Add(bottom - side); _verts.Add(bottom + side);
                _verts.Add(top - side);    _verts.Add(top + side);
                for (int i = 0; i < 4; i++) _normals.Add(up);
                _uvs.Add(new Vector4(-1f, 0f, b.seed, strength)); _uvs.Add(new Vector4(1f, 0f, b.seed, strength));
                _uvs.Add(new Vector4(-1f, 1f, b.seed, strength)); _uvs.Add(new Vector4(1f, 1f, b.seed, strength));
            }

            int quads = _beams.Count;
            if (_tris.Count != quads * 6)
            {
                _mesh.Clear();
                _tris.Clear();
                for (int q = 0; q < quads; q++)
                {
                    int v = q * 4;
                    _tris.Add(v); _tris.Add(v + 2); _tris.Add(v + 1);
                    _tris.Add(v + 1); _tris.Add(v + 2); _tris.Add(v + 3);
                }
            }
            _mesh.SetVertices(_verts);
            _mesh.SetNormals(_normals);
            _mesh.SetUVs(0, _uvs);
            if (_mesh.GetIndexCount(0) != (uint)_tris.Count) _mesh.SetTriangles(_tris, 0, false);
            _mesh.bounds = new Bounds(eye, Vector3.one * (radius + length.y) * 2.5f);
        }

        void PickBeams(Vector2Int around, Vector3 up)
        {
            _beams.Clear();
            float surface = WaterFx.SurfaceY;
            int reach = Mathf.CeilToInt(radius / cellSize);
            float r2 = radius * radius;
            Vector2 centre = new Vector2((around.x + 0.5f) * cellSize, (around.y + 0.5f) * cellSize);

            for (int x = around.x - reach; x <= around.x + reach; x++)
            for (int z = around.y - reach; z <= around.y + reach; z++)
            {
                uint h = Hash((uint)x, (uint)z);
                if (Rand01(ref h) > density) continue;

                var anchor = new Vector2((x + Rand01(ref h)) * cellSize, (z + Rand01(ref h)) * cellSize);
                if ((anchor - centre).sqrMagnitude > r2) continue;
                if (!Seabed(anchor, out float floorY)) continue;

                float want = Mathf.Lerp(length.x, length.y, Rand01(ref h));
                // Never poke out of the water: stop a little under the surface.
                float room = (surface - 1.5f - floorY) / Mathf.Max(up.y, 0.1f);
                float height = Mathf.Min(want, room);
                if (height < 4f) continue;

                _beams.Add(new Beam
                {
                    // Start a little into the seabed; the shader's soft contact hides the join.
                    bottom = new Vector3(anchor.x, floorY - 0.5f, anchor.y),
                    height = height,
                    halfWidth = Mathf.Lerp(width.x, width.y, Rand01(ref h) * Rand01(ref h)) * 0.5f,
                    strength = Mathf.Lerp(0.45f, 1f, Rand01(ref h)),
                    seed = Rand01(ref h),
                    phase = Rand01(ref h) * 6.283f,
                });
            }
        }

        bool Seabed(Vector2 xz, out float y)
        {
            long key = ((long)Mathf.RoundToInt(xz.x * 4f) << 32) ^ (uint)Mathf.RoundToInt(xz.y * 4f);
            if (_seabedCache.TryGetValue(key, out y)) return !float.IsNaN(y);
            bool hit = WaterFx.SeabedHeight(new Vector3(xz.x, WaterFx.SurfaceY - 1f, xz.y), out y);
            if (!hit) y = float.NaN;
            _seabedCache[key] = y;
            return hit;
        }

        // The sun's direction once it has bent into the water (Snell, air -> water).
        Vector3 BeamDirection()
        {
            if (sun == null || !sun.isActiveAndEnabled) sun = FindSun();
            Vector3 i = sun != null ? sun.transform.forward : new Vector3(-0.4f, -0.85f, 0.2f);
            if (i.y > -0.05f) i = new Vector3(i.x, -0.05f, i.z);
            i.Normalize();
            const float eta = 1f / 1.333f;
            Vector3 n = Vector3.up;
            float cosI = -Vector3.Dot(n, i);
            float k = 1f - eta * eta * (1f - cosI * cosI);
            return (eta * i + (eta * cosI - Mathf.Sqrt(Mathf.Max(k, 0f))) * n).normalized;
        }

        static Light FindSun()
        {
            Light best = null;
            foreach (var l in FindObjectsByType<Light>())
                if (l.type == LightType.Directional && l.isActiveAndEnabled && (best == null || l.intensity > best.intensity))
                    best = l;
            return best;
        }

        static uint Hash(uint x, uint z)
        {
            uint h = x * 374761393u + z * 668265263u;
            h = (h ^ (h >> 13)) * 1274126177u;
            return h ^ (h >> 16);
        }

        static float Rand01(ref uint state)
        {
            state ^= state << 13; state ^= state >> 17; state ^= state << 5;
            return (state & 0xFFFFFF) / 16777216f;
        }
    }
}
