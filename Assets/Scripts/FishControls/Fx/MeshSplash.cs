using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishGame
{
    /// <summary>
    /// One splash of real 3D water (WaterFx.Splash keeps a few): plays the simulated water as a mesh per frame
    /// (exported from the Blender liquid simulation), riding the waves where it broke the surface, then hides
    /// itself until it's needed again. Between frames its vertices glide to where the next frame has them
    /// (stored with each mesh) and its shading is worked out from the moved shape, so it moves smoothly at any
    /// frame rate instead of stepping from frame to frame. Only cameras above the water draw it:
    /// from under the surface its base, tucked just below the waves, would show as a pale ring.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class MeshSplash : MonoBehaviour
    {
        // One simulated frame as the game plays it: its shape, and how far each vertex moves by the next frame.
        class Frame
        {
            public Vector3[] at, move;
            public int[] triangles;
        }

        // How quickly it follows the waves under it (the water's height lookup is a search, a little noisy).
        const float FollowRate = 10f;

        static readonly List<MeshSplash> s_live = new List<MeshSplash>();
        static readonly Dictionary<Mesh, Frame> s_frameData = new Dictionary<Mesh, Frame>();
        static readonly List<Vector2> s_uvA = new List<Vector2>(), s_uvB = new List<Vector2>();
        static bool s_hooked;

        Mesh[] _frames;
        MeshFilter _filter;
        MeshRenderer _renderer;
        Mesh _mesh;             // this splash's own copy, its vertices moved between frames
        Vector3[] _work = new Vector3[0];
        int _shown = -1;
        Vector2 _at;            // where it broke the surface (x, z)
        Quaternion _yaw;
        float _duration, _time, _sink, _scale, _waterY;
        Vector3 _up;

        public bool Playing => gameObject.activeSelf;

        /// <summary>Play <paramref name="frames"/> over <paramref name="duration"/> seconds at
        /// <paramref name="at"/>, scaled by <paramref name="scale"/>, its base tucked <paramref name="sink"/> metres
        /// under the water so the cut edge never shows.</summary>
        public void Play(Mesh[] frames, Vector3 at, float scale, float duration, float sink)
        {
            if (_filter == null) _filter = GetComponent<MeshFilter>();
            if (_renderer == null) _renderer = GetComponent<MeshRenderer>();
            _frames = frames;
            _at = new Vector2(at.x, at.z);
            _duration = Mathf.Max(0.05f, duration);
            _sink = sink;
            _scale = scale;
            _time = 0f;
            _shown = -1;
            _yaw = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            transform.localScale = Vector3.one * scale;
            _waterY = WaterFx.WaterHeightAt(new Vector3(_at.x, 0f, _at.y), out _up, withDisplacement: false);
            Show(0f);
            Ride(0f, 0f);
            gameObject.SetActive(true);
        }

        void OnEnable()
        {
            s_live.Add(this);
            if (!s_hooked)
            {
                RenderPipelineManager.beginCameraRendering += BeforeCamera;
                s_hooked = true;
            }
        }

        void OnDisable() => s_live.Remove(this);

        void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }

        static void BeforeCamera(ScriptableRenderContext context, Camera camera)
        {
            if (s_live.Count == 0) return;
            Vector3 p = camera.transform.position;
            float flat = WaterFx.SurfaceY;
            bool above = p.y > flat + 3f || (p.y > flat - 3f && p.y > WaterFx.WaterHeightAt(p));
            foreach (var splash in s_live)
                if (splash._renderer != null) splash._renderer.enabled = above;
        }

        void Update()
        {
            _time += Time.deltaTime;
            float u = _time / _duration;
            if (u >= 1f || _frames == null || _frames.Length == 0)
            {
                gameObject.SetActive(false);
                return;
            }
            Show(u);
            Ride(u, Time.deltaTime);
        }

        // The frame for u (0..1 through the splash), its vertices part of the way to the next frame's.
        void Show(float u)
        {
            float k = u * _frames.Length;
            int i = Mathf.Min(_frames.Length - 1, (int)k);
            var source = _frames[i];
            var frame = source != null && source.isReadable ? FrameOf(source) : null;
            if (frame == null)
            {
                // Meshes that can't be read (imported unreadable): plain frame swaps.
                if (_filter.sharedMesh != source) _filter.sharedMesh = source;
                return;
            }

            if (_mesh == null)
            {
                _mesh = new Mesh { name = "Splash" };
                _mesh.MarkDynamic();
            }
            if (_filter.sharedMesh != _mesh) _filter.sharedMesh = _mesh;
            int n = frame.at.Length;
            if (_work.Length < n) _work = new Vector3[n];
            float t = Mathf.Clamp01(k - i);
            for (int v = 0; v < n; v++) _work[v] = frame.at[v] + frame.move[v] * t;

            if (i != _shown)
            {
                _mesh.Clear();
                _mesh.SetVertices(_work, 0, n);
                _mesh.SetTriangles(frame.triangles, 0);
                _shown = i;
            }
            else
            {
                _mesh.SetVertices(_work, 0, n);
                _mesh.RecalculateBounds();
            }
            // Shading from the shape as it is now: the glints slide over the water instead of jumping each frame.
            _mesh.RecalculateNormals();
        }

        static Frame FrameOf(Mesh mesh)
        {
            if (s_frameData.TryGetValue(mesh, out var frame)) return frame;
            frame = new Frame { at = mesh.vertices, triangles = mesh.triangles };
            frame.move = new Vector3[frame.at.Length];
            mesh.GetUVs(0, s_uvA);
            mesh.GetUVs(1, s_uvB);
            if (s_uvA.Count == frame.at.Length && s_uvB.Count == frame.at.Length)
                for (int v = 0; v < frame.at.Length; v++)
                    frame.move[v] = new Vector3(s_uvA[v].x, s_uvA[v].y, s_uvB[v].x);
            s_frameData[mesh] = frame;
            return frame;
        }

        // Stay on the water as the waves lift, drop and tilt it (its base follows the slope of the swell, so
        // none of it pokes out on the low side). Over the second half what's left drains down into the sea: the
        // ring its collapse spreads is left to the sea's own ring wave (SurfaceDisplacement), never lying on top.
        void Ride(float u, float dt)
        {
            float y = WaterFx.WaterHeightAt(new Vector3(_at.x, 0f, _at.y), out Vector3 up, withDisplacement: false);
            float follow = 1f - Mathf.Exp(-FollowRate * dt);
            _waterY = Mathf.Lerp(_waterY, y, follow);
            _up = Vector3.Slerp(_up, up, follow).normalized;
            float sink = _sink + Mathf.SmoothStep(0f, 1f, (u - 0.45f) / 0.55f) * 0.3f * _scale;
            transform.SetPositionAndRotation(new Vector3(_at.x, _waterY, _at.y) - _up * sink,
                                             Quaternion.FromToRotation(Vector3.up, _up) * _yaw);
        }
    }
}
