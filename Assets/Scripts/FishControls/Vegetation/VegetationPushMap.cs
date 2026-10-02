using System.Collections.Generic;
using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// Lets swimmers shove the seabed plants out of their way.
    ///
    /// Every frame each swimmer near the camera - anything with a <see cref="VegetationPusher"/> (players,
    /// the shark, NPC creatures) plus every school fish - is painted into a small top-down "push map" that
    /// follows the camera, as the capsule its body fills. A plant standing in a body's path is moved out to
    /// the body's surface (the VegetationPush node in M_Seabed), so you part kelp rather than pass through it,
    /// and the map fades over a moment so the path closes back in behind you. Skinned seaweed reads the same
    /// swimmer list to bend its bones (<see cref="SeaweedBoneSpring"/>).
    ///
    /// Purely visual and local: every machine already sees the same players and school fish, so the plants
    /// move the same for everyone with no networking.
    /// </summary>
    [DefaultExecutionOrder(-50)] // swimmers are collected before the seaweed springs read them
    public class VegetationPushMap : MonoBehaviour
    {
        [SerializeField] ComputeShader compute;

        [Header("Map")]
        [Tooltip("Width of the area around the camera where plants react, in metres.")]
        [SerializeField] float areaSize = 64f;
        [Tooltip("Texels across the map. 256 over 64 m = 25 cm per texel.")]
        [SerializeField] int resolution = 256;

        [Header("Feel")]
        [Tooltip("Seconds for the path behind a swimmer to close halfway (plants springing back).")]
        [SerializeField] float wakeHalfLife = 0.5f;
        [Tooltip("Extra room plants leave around a body, in metres.")]
        [SerializeField] float margin = 0.2f;
        [Tooltip("Furthest any part of a plant is pushed, in metres.")]
        [SerializeField] float maxPush = 3f;
        [Tooltip("How far above a body a stalk stays leaning over before easing back, in metres.")]
        [SerializeField] float leanAbove = 2.5f;
        [Tooltip("Height over which a plant stiffens toward its base, in metres - the base stays rooted.")]
        [SerializeField] float rootHold = 0.4f;

        [Header("School fish")]
        [SerializeField] bool schoolFishPush = true;
        [Tooltip("How much room school fish make (1 = fully clear their bodies), so a school brushes " +
                 "through the plants rather than carving lanes.")]
        [Range(0f, 1f)][SerializeField] float schoolFishStrength = 0.5f;

        /// <summary>This frame's swimmers near the camera: xyz = body centre, w = body radius.</summary>
        public static readonly List<Vector4> Pushers = new List<Vector4>();
        /// <summary>Per swimmer: xyz = half its straight length along its axis, w = strength (0..1).</summary>
        public static readonly List<Vector4> Segments = new List<Vector4>();

        const int MaxPushers = 1024;

        static readonly int AreaId = Shader.PropertyToID("_VegPushArea");
        static readonly int ParamsId = Shader.PropertyToID("_VegPushParams");
        static readonly int MapId = Shader.PropertyToID("_VegPushMap");
        static readonly int PrevId = Shader.PropertyToID("_Prev");
        static readonly int ResultId = Shader.PropertyToID("_Result");
        static readonly int CsAreaId = Shader.PropertyToID("_Area");
        static readonly int CsPrevAreaId = Shader.PropertyToID("_PrevArea");
        static readonly int FadeId = Shader.PropertyToID("_Fade");
        static readonly int MarginId = Shader.PropertyToID("_Margin");
        static readonly int CountId = Shader.PropertyToID("_PusherCount");
        static readonly int PushersId = Shader.PropertyToID("_Pushers");
        static readonly int SegmentsId = Shader.PropertyToID("_Segments");

        RenderTexture _a, _b;   // ping-pong: last frame's map is read while this frame's is written
        ComputeBuffer _pusherBuffer, _segmentBuffer;
        readonly Vector4[] _pusherArray = new Vector4[MaxPushers];
        readonly Vector4[] _segmentArray = new Vector4[MaxPushers];
        Vector4 _prevArea;
        int _kernel = -1;
        readonly Dictionary<BoidsManager, float> _schoolFishRadius = new Dictionary<BoidsManager, float>();

        void OnEnable()
        {
            if (compute == null || !SystemInfo.supportsComputeShaders)
            {
                Debug.LogWarning("[Vegetation] No compute shader (or no compute support) - plants won't react to swimmers.", this);
                enabled = false;
                return;
            }
            _kernel = compute.FindKernel("Advance");
            _a = CreateMap();
            _b = CreateMap();
            _pusherBuffer = new ComputeBuffer(MaxPushers, 16);
            _segmentBuffer = new ComputeBuffer(MaxPushers, 16);
            _prevArea = new Vector4(0f, 0f, areaSize, 0f);
        }

        void OnDisable()
        {
            Shader.SetGlobalVector(AreaId, Vector4.zero); // plants stop bending
            Pushers.Clear();
            Segments.Clear();
            if (_a != null) { _a.Release(); _a = null; }
            if (_b != null) { _b.Release(); _b = null; }
            _pusherBuffer?.Release(); _pusherBuffer = null;
            _segmentBuffer?.Release(); _segmentBuffer = null;
        }

        RenderTexture CreateMap()
        {
            var rt = new RenderTexture(resolution, resolution, 0, RenderTextureFormat.ARGBHalf)
            {
                name = "VegetationPushMap",
                enableRandomWrite = true,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            rt.Create();
            return rt;
        }

        void LateUpdate()
        {
            Transform view = ViewPoint();
            if (view == null) return;

            // Follow the camera in whole texels, so the map doesn't shimmer as it slides.
            float texel = areaSize / resolution;
            Vector3 c = view.position;
            var area = new Vector4(Mathf.Round(c.x / texel) * texel, Mathf.Round(c.z / texel) * texel, areaSize, resolution);

            CollectPushers(area);

            int n = Mathf.Min(Pushers.Count, MaxPushers);
            for (int i = 0; i < n; i++) { _pusherArray[i] = Pushers[i]; _segmentArray[i] = Segments[i]; }
            _pusherBuffer.SetData(_pusherArray, 0, 0, n);
            _segmentBuffer.SetData(_segmentArray, 0, 0, n);

            (_a, _b) = (_b, _a); // _a = this frame's, _b = last frame's
            compute.SetTexture(_kernel, PrevId, _b);
            compute.SetTexture(_kernel, ResultId, _a);
            compute.SetBuffer(_kernel, PushersId, _pusherBuffer);
            compute.SetBuffer(_kernel, SegmentsId, _segmentBuffer);
            compute.SetVector(CsAreaId, area);
            compute.SetVector(CsPrevAreaId, _prevArea);
            compute.SetFloat(FadeId, wakeHalfLife > 0f ? Mathf.Pow(0.5f, Time.deltaTime / wakeHalfLife) : 0f);
            compute.SetFloat(MarginId, margin);
            compute.SetInt(CountId, n);
            int groups = Mathf.CeilToInt(resolution / 8f);
            compute.Dispatch(_kernel, groups, groups, 1);
            _prevArea = area;

            Shader.SetGlobalTexture(MapId, _a);
            Shader.SetGlobalVector(AreaId, new Vector4(area.x, area.y, areaSize, 1f));
            Shader.SetGlobalVector(ParamsId, new Vector4(maxPush, leanAbove, rootHold, 0f));
        }

        // The local player's view: their own camera (only the shark's is tagged MainCamera), else the main
        // camera, else their body, else whatever camera is rendering (offline sandbox).
        static Transform ViewPoint()
        {
            var lp = Mirror.NetworkClient.localPlayer;
            if (lp != null && lp.TryGetComponent(out FishPlayer fp) && fp.camera != null && fp.camera.activeInHierarchy)
                return fp.camera.transform;
            var cam = Camera.main;
            if (cam != null) return cam.transform;
            if (lp != null) return lp.transform;
            return Camera.allCamerasCount > 0 ? Camera.allCameras[0].transform : null;
        }

        void CollectPushers(Vector4 area)
        {
            Pushers.Clear();
            Segments.Clear();
            float half = areaSize * 0.5f;

            foreach (var p in VegetationPusher.All)
            {
                if (p == null) continue;
                Vector3 centre = p.Centre;
                Vector3 seg = p.HalfSegment;
                float reach = p.Radius + seg.magnitude;
                if (Mathf.Abs(centre.x - area.x) > half + reach || Mathf.Abs(centre.z - area.y) > half + reach) continue;
                Add(centre, p.Radius, seg, p.Strength);
            }

            if (!schoolFishPush) return;
            foreach (var flock in BoidsManager.All)
            {
                if (flock == null || flock.allFish == null) continue;
                float r = SchoolFishRadius(flock);
                var fish = flock.allFish;
                for (int i = 0; i < fish.Length && Pushers.Count < MaxPushers; i++)
                {
                    var f = fish[i];
                    if (f == null || !f.activeInHierarchy) continue;
                    Vector3 pos = f.transform.position;
                    if (Mathf.Abs(pos.x - area.x) > half + r || Mathf.Abs(pos.z - area.y) > half + r) continue;
                    Add(pos, r, Vector3.zero, schoolFishStrength);
                }
            }
        }

        static void Add(Vector3 centre, float radius, Vector3 halfSegment, float strength)
        {
            if (Pushers.Count >= MaxPushers) return;
            Pushers.Add(new Vector4(centre.x, centre.y, centre.z, radius));
            Segments.Add(new Vector4(halfSegment.x, halfSegment.y, halfSegment.z, strength));
        }

        // Every fish in a school is the same prefab, so measure one and remember it. A school fish is
        // small, so a ball around it is close enough.
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
            return 0.25f; // nothing spawned yet - measure once there is
        }
    }
}
