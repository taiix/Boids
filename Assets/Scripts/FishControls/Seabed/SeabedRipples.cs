using System.Collections.Generic;
using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// How the wave ripples in the seabed sand look (Shaders/SeabedSand.hlsl works them out from these:
    /// crests a hand-span or so apart, wandering, forking and ending, patchy across the floor, long low swells
    /// under them). Changes show straight away, in the editor too. Without this component in a scene the
    /// shader uses the same defaults as here (but without the surroundings: see below).
    ///
    /// The sand isn't the same everywhere: stretches of the seabed have small tight ripples, wide ones or
    /// none, running their own ways, with a lattice of crossing ripples here and there; the crests break up
    /// along their length. And in play, this bakes a map of the seabed's surroundings for the shader - how
    /// close the reef, rocks and coral are (<see cref="SceneryIndex"/>, and the rocks with colliders), and
    /// where the ground lies in a hollow or stands raised - so the sand by the reef is smoother, darker and
    /// gritty, hollows hold flatter, darker sand with debris, and raised ground has sharper ripples. It's
    /// baked again whenever the reef is placed again.
    /// </summary>
    [ExecuteAlways]
    public class SeabedRipples : MonoBehaviour
    {
        [Tooltip("Distance between ripple crests, in metres.")]
        [Range(0.15f, 2f)] [SerializeField] float crestSpacing = 0.6f;
        [Tooltip("How tall the ripples are: 1 = about 15% of their spacing, true to life; 0 = flat sand. Down at the " +
                 "seabed the light comes from straight above, so they need a bit more than life to show.")]
        [Range(0f, 4f)] [SerializeField] float strength = 2.4f;
        [Tooltip("1 = rounded, wave-like crests; higher = sharper crests over broader, flatter troughs.")]
        [Range(1f, 4f)] [SerializeField] float crestSharpness = 1.8f;
        [Tooltip("Which way the crests run, in degrees (they drift a little either side of it across the seabed).")]
        [Range(0f, 180f)] [SerializeField] float direction = 25f;
        [Tooltip("How much darker the sand lies in the troughs, 0..1.")]
        [Range(0f, 0.6f)] [SerializeField] float troughShade = 0.3f;
        [Tooltip("Long, low sand waves a few metres apart under the ripples: 0 = none.")]
        [Range(0f, 3f)] [SerializeField] float swellStrength = 1f;
        [Tooltip("0 = ripples everywhere alike; 1 = strong in some patches, almost gone in others.")]
        [Range(0f, 1f)] [SerializeField] float patchiness = 0.3f;

        [Header("Variety")]
        [Tooltip("How different one stretch of seabed is from the next: 0 = the same ripples everywhere; 1 = stretches " +
                 "of small tight ripples and of wide ones, each running its own way.")]
        [Range(0f, 1f)] [SerializeField] float variety = 0.7f;
        [Tooltip("Patches of smooth sand with no ripples at all: 0 = none.")]
        [Range(0f, 1f)] [SerializeField] float flatPatches = 0.4f;
        [Tooltip("Crests rising and dipping along their length, breaking into pieces and ending: 0 = unbroken ridges.")]
        [Range(0f, 1f)] [SerializeField] float crestBreakup = 0.6f;
        [Tooltip("Here and there a second set of ripples across the first, into a lattice: 0 = none.")]
        [Range(0f, 1f)] [SerializeField] float crossRipples = 0.4f;

        [Header("Surroundings")]
        [Tooltip("Next to the reef, rocks and coral the sand is smoother, darker and gritty: 0 = no different.")]
        [Range(0f, 1f)] [SerializeField] float reefInfluence = 0.65f;
        [Tooltip("How far that reaches from a big rock or reef piece, in metres (small pieces reach less).")]
        [Range(0.5f, 8f)] [SerializeField] float reefReach = 3f;
        [Tooltip("Hollows hold flatter, darker sand with bits collected in it; raised ground has sharper ripples: " +
                 "0 = no different.")]
        [Range(0f, 1f)] [SerializeField] float hollowInfluence = 0.7f;
        [Tooltip("What counts as a hollow or a rise: ground this many metres below / above the average around it " +
                 "(over about 15 m) is fully one.")]
        [Range(0.3f, 4f)] [SerializeField] float hollowDepth = 1.5f;

        static readonly int RippleId = Shader.PropertyToID("_SandRipple");
        static readonly int Ripple2Id = Shader.PropertyToID("_SandRipple2");
        static readonly int Ripple3Id = Shader.PropertyToID("_SandRipple3");
        static readonly int Ripple4Id = Shader.PropertyToID("_SandRipple4");
        static readonly int MaskId = Shader.PropertyToID("_SeabedMask");
        static readonly int MaskAreaId = Shader.PropertyToID("_SeabedMaskArea");

        const int MaskSize = 512;      // texels across the seabed (300 m: about 0.6 m each)
        const float BlurMetres = 7f;   // the "neighbourhood" a hollow is measured against (box blur radius)

        Texture2D _mask;
        int _seenVersion = -1;
        float _bakeAt = -1f, _bakeAgainAt = -1f;
        float _bakedReach, _bakedDepth;

        void OnEnable() => Apply();
        void OnValidate() => Apply();

        void Update()
        {
            Apply();   // cheap, and keeps them set through scene and domain reloads
            if (Application.isPlaying) KeepMask();
        }

        void OnDisable()
        {
            // Back to the shader's defaults, no surroundings.
            Shader.SetGlobalVector(RippleId, Vector4.zero);
            Shader.SetGlobalVector(Ripple2Id, Vector4.zero);
            Shader.SetGlobalVector(Ripple3Id, Vector4.zero);
            Shader.SetGlobalVector(Ripple4Id, Vector4.zero);
            Shader.SetGlobalVector(MaskAreaId, Vector4.zero);
        }

        void OnDestroy()
        {
            if (_mask == null) return;
            if (Application.isPlaying) Destroy(_mask);
            else DestroyImmediate(_mask);
        }

        void Apply()
        {
            Shader.SetGlobalVector(RippleId, new Vector4(crestSpacing, strength, crestSharpness, 1f));
            Shader.SetGlobalVector(Ripple2Id, new Vector4(direction, troughShade, swellStrength, patchiness));
            Shader.SetGlobalVector(Ripple3Id, new Vector4(variety, flatPatches, crestBreakup, crossRipples));
            Shader.SetGlobalVector(Ripple4Id, new Vector4(reefInfluence, hollowInfluence, 0f, 0f));
        }

        // ------------------------------------------------------------------ the surroundings map

        void KeepMask()
        {
            // The reef was placed (again): bake once things have settled - the big rock formations drop onto the
            // sand a moment after the seabed is built - and once more a little later to be sure.
            if (SceneryIndex.Version != _seenVersion)
            {
                _seenVersion = SceneryIndex.Version;
                _bakeAt = Time.unscaledTime + 3f;
                _bakeAgainAt = Time.unscaledTime + 9f;
            }
            // Its reach or depth changed in the inspector: bake again.
            if (_mask != null && (_bakedReach != reefReach || _bakedDepth != hollowDepth) && _bakeAt < 0f)
                _bakeAt = Time.unscaledTime + 0.5f;

            if (_bakeAt >= 0f && Time.unscaledTime >= _bakeAt)
            {
                _bakeAt = -1f;
                if (!Bake()) _bakeAt = Time.unscaledTime + 1f;   // the seabed isn't there yet
            }
            else if (_bakeAgainAt >= 0f && Time.unscaledTime >= _bakeAgainAt)
            {
                _bakeAgainAt = -1f;
                Bake();
            }
        }

        bool Bake()
        {
            var terrain = FindAnyObjectByType<TestTerrain>();
            if (terrain == null || !terrain.TryGetComponent(out MeshCollider seabed) || seabed.sharedMesh == null) return false;

            Bounds area = seabed.bounds;
            float size = Mathf.Max(area.size.x, area.size.z);
            float perMetre = MaskSize / size;
            var near = new float[MaskSize * MaskSize];

            // How close the scenery is: each piece's footprint, fading out over its reach. Small pieces (a lone coral,
            // a pebble) count for less and reach less than a reef rock, or the scattered bits would cover the floor.
            int pieces = 0;
            void Splat(Bounds b)
            {
                float extent = Mathf.Max(b.extents.x, b.extents.z);
                float reach = Mathf.Min(reefReach, 0.3f + 0.8f * extent);
                float peak = Mathf.Clamp01(0.15f + 0.85f * extent / 1.6f);
                int x0 = Mathf.Max(0, Mathf.FloorToInt((b.min.x - reach - area.min.x) * perMetre));
                int x1 = Mathf.Min(MaskSize - 1, Mathf.CeilToInt((b.max.x + reach - area.min.x) * perMetre));
                int z0 = Mathf.Max(0, Mathf.FloorToInt((b.min.z - reach - area.min.z) * perMetre));
                int z1 = Mathf.Min(MaskSize - 1, Mathf.CeilToInt((b.max.z + reach - area.min.z) * perMetre));
                for (int z = z0; z <= z1; z++)
                {
                    float wz = area.min.z + (z + 0.5f) / perMetre;
                    float dz = Mathf.Max(0f, Mathf.Abs(wz - b.center.z) - b.extents.z);
                    for (int x = x0; x <= x1; x++)
                    {
                        float wx = area.min.x + (x + 0.5f) / perMetre;
                        float dx = Mathf.Max(0f, Mathf.Abs(wx - b.center.x) - b.extents.x);
                        float d = Mathf.Sqrt(dx * dx + dz * dz);
                        if (d >= reach) continue;
                        float t = 1f - d / reach;
                        float v = t * t * (3f - 2f * t) * peak;
                        int i = z * MaskSize + x;
                        if (v > near[i]) near[i] = v;
                    }
                }
                pieces++;
            }
            foreach (var b in SceneryIndex.Pieces) Splat(b);
            // The rocks with colliders (the big rock formations' pieces): not the seabed itself, nor anything huge.
            foreach (var mc in FindObjectsByType<MeshCollider>(FindObjectsInactive.Exclude))
            {
                if (mc == seabed || !mc.enabled || mc.gameObject.layer != seabed.gameObject.layer) continue;
                Bounds b = mc.bounds;
                if (b.size.x > 30f || b.size.z > 30f) continue;
                Splat(b);
            }

            // Hollows and rises: the ground against the average around it.
            float[] rel = RelativeHeights(seabed, area, out int gn, out Vector2 gStep);

            var pixels = new Color32[MaskSize * MaskSize];
            for (int z = 0; z < MaskSize; z++)
                for (int x = 0; x < MaskSize; x++)
                {
                    float r = 0f;
                    if (rel != null)
                    {
                        float gx = (x + 0.5f) / perMetre / gStep.x, gz = (z + 0.5f) / perMetre / gStep.y;
                        int ix = Mathf.Clamp(Mathf.FloorToInt(gx), 0, gn - 2), iz = Mathf.Clamp(Mathf.FloorToInt(gz), 0, gn - 2);
                        float fx = Mathf.Clamp01(gx - ix), fz = Mathf.Clamp01(gz - iz);
                        float a = Mathf.Lerp(rel[iz * gn + ix], rel[iz * gn + ix + 1], fx);
                        float c = Mathf.Lerp(rel[(iz + 1) * gn + ix], rel[(iz + 1) * gn + ix + 1], fx);
                        r = Mathf.Clamp(Mathf.Lerp(a, c, fz) / hollowDepth, -1f, 1f);
                    }
                    int i = z * MaskSize + x;
                    pixels[i] = new Color32((byte)Mathf.RoundToInt(near[i] * 255f), (byte)Mathf.RoundToInt((r * 0.5f + 0.5f) * 255f), 0, 255);
                }

            if (_mask == null)
                _mask = new Texture2D(MaskSize, MaskSize, TextureFormat.RGBA32, false, true)
                {
                    name = "SeabedMask",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
            _mask.SetPixels32(pixels);
            _mask.Apply(false);
            Shader.SetGlobalTexture(MaskId, _mask);
            Shader.SetGlobalVector(MaskAreaId, new Vector4(area.min.x, area.min.z, perMetre, MaskSize));
            _bakedReach = reefReach;
            _bakedDepth = hollowDepth;
            Debug.Log($"[SeabedRipples] Sand surroundings baked: {pieces} pieces of scenery, {MaskSize}x{MaskSize} over {size:0} m.");
            return true;
        }

        // The seabed's height on its own grid (the terrain mesh is a regular grid of vertices), less the average
        // over BlurMetres around: below zero in a hollow, above on a rise. Null if the mesh isn't a plain grid.
        static float[] RelativeHeights(MeshCollider seabed, Bounds area, out int n, out Vector2 step)
        {
            var verts = seabed.sharedMesh.vertices;
            n = Mathf.RoundToInt(Mathf.Sqrt(verts.Length));
            step = Vector2.one;
            if (n < 2 || n * n != verts.Length) return null;
            step = new Vector2(area.size.x / (n - 1), area.size.z / (n - 1));
            var h = new float[n * n];
            var toWorld = seabed.transform.localToWorldMatrix;
            foreach (var v in verts)
            {
                Vector3 w = toWorld.MultiplyPoint3x4(v);
                int ix = Mathf.RoundToInt((w.x - area.min.x) / step.x), iz = Mathf.RoundToInt((w.z - area.min.z) / step.y);
                if (ix < 0 || iz < 0 || ix >= n || iz >= n) return null;
                h[iz * n + ix] = w.y;
            }
            int radius = Mathf.Max(1, Mathf.RoundToInt(BlurMetres / Mathf.Max(step.x, 0.01f)));
            var avg = BoxBlur(BoxBlur(h, n, radius), n, radius);
            for (int i = 0; i < h.Length; i++) h[i] -= avg[i];
            return h;
        }

        // A separable box blur (clamped at the edges).
        static float[] BoxBlur(float[] src, int n, int r)
        {
            var tmp = new float[src.Length];
            var dst = new float[src.Length];
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    float s = 0f;
                    for (int k = -r; k <= r; k++) s += src[z * n + Mathf.Clamp(x + k, 0, n - 1)];
                    tmp[z * n + x] = s / (2 * r + 1);
                }
            for (int z = 0; z < n; z++)
                for (int x = 0; x < n; x++)
                {
                    float s = 0f;
                    for (int k = -r; k <= r; k++) s += tmp[Mathf.Clamp(z + k, 0, n - 1) * n + x];
                    dst[z * n + x] = s / (2 * r + 1);
                }
            return dst;
        }
    }
}
