using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// Springy seaweed: swimmers push the stalks aside through their bones, and they whip back with a
    /// little overshoot once the swimmer has passed. Put it on the root of a skinned, rigged plant
    /// every bone of its skinned meshes is used.
    ///
    /// The lower down a stalk is hit, the further over it goes: brushed at the top it only tips aside, but
    /// a swimmer hugging the seabed lays it almost flat - swim low through a forest and it parts around you.
    /// A stalk bends over from its foot up to where it's hit and rides straight on above that.
    ///
    /// Runs after the Animator, on top of the authored sway, and reads the same swimmers as the plant
    /// shaders (<see cref="VegetationPushMap.Pushers"/>), so it needs a VegetationPushMap in the scene.
    /// Purely visual and local, like the rest of the plant pushing.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class SeaweedBoneSpring : MonoBehaviour
    {
        [Tooltip("How far a stalk leans over when it's hit right at its foot, in radians (1.35 is about 77°): " +
                 "a swimmer hugging the seabed lays the stalks nearly flat.")]
        [SerializeField] float leanWhenHitLow = 1.35f;
        [Tooltip("How far a stalk leans over when it's only hit at its top, in radians (0.5 is about 30°). " +
                 "In between, the lower the hit the further it goes.")]
        [SerializeField] float leanWhenHitHigh = 0.5f;
        [Tooltip("The most a stalk can bend in all from its foot to its tip, in radians, swinging back " +
                 "included (1.5 is about 86° - just short of folding into the seabed).")]
        [SerializeField] float maxBend = 1.5f;
        [Tooltip("How quickly a stalk swings back. Higher = stiffer.")]
        [SerializeField] float stiffness = 40f;
        [Tooltip("How fast the swinging dies out. Lower = more wobble after a push.")]
        [SerializeField] float damping = 5f;
        [Tooltip("Added to every swimmer's push radius for this plant, in metres. A stalk inside the " +
                 "swimmer's body gets the full lean, fading out across this margin.")]
        [SerializeField] float extraReach = 0.3f;
        [Tooltip("How far the whole plant can tilt over at its base, in radians. Our seaweed's stalks are " +
                 "rigid meshes (only the leaves are rigged), so this is what makes the stalk give way.")]
        [SerializeField] float plantLean = 0.35f;

        Transform[] _bones;      // parents before children
        int[] _parent;           // index of each bone's parent bone, -1 at a stalk's base
        int[] _depth;            // bones between this one and its stalk's base
        Vector3[] _axis;         // each bone's direction along its stalk, in its own space
        float[] _segLen;         // world length of each bone's stretch of stalk
        Vector3[] _need;         // this frame's lean each bone's stretch needs to clear the swimmers (scratch)
        Vector3[] _total;        // lean wanted from the stalk's base up to and including each bone (scratch)
        float[] _bent;           // how far the stalk has bent so far, base up to each bone (scratch)
        Quaternion[] _base;      // the pose we bent from last frame (the animated pose)
        Quaternion[] _written;   // what we left the bone at, to tell if the Animator posed it again
        Vector3[] _lean, _speed; // spring state per bone, in world space: radians toward a sideways direction
        Bounds _area;
        float _groundY, _height; // where the plant stands and how tall it is
        bool _resting = true;
        // The whole plant tilting about its base (a spring of its own).
        Vector3 _pivot, _rootPos;
        Quaternion _rootRot;
        Vector3 _plantLean, _plantSpeed;
        bool _tiltsWhole;
        Renderer[] _renderers;
        // How far any part of the plant can ever get from its resting bounds (bounds diagonal + longest stalk,
        // straightened), so swimmers farther than that plus their reach can be skipped without testing.
        float _plantReach;
        readonly List<int> _nearby = new List<int>(); // this frame's swimmers that could touch the plant

        // Drawn by some camera this frame (not culled by distance or out of view).
        bool Seen()
        {
            if (_renderers == null) return true;
            foreach (var r in _renderers)
                if (r != null && r.isVisible) return true;
            return false;
        }

        void Awake()
        {
            var set = new HashSet<Transform>();
            foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                foreach (var b in smr.bones)
                    if (b != null) set.Add(b);

            var bones = new List<Transform>(set);
            bones.Sort((x, y) => Depth(x, set).CompareTo(Depth(y, set)));
            _bones = bones.ToArray();
            int n = _bones.Length;
            _axis = new Vector3[n];
            _segLen = new float[n];
            _base = new Quaternion[n];
            _written = new Quaternion[n];
            _lean = new Vector3[n];
            _speed = new Vector3[n];
            _need = new Vector3[n];
            _total = new Vector3[n];
            _bent = new float[n];
            _parent = new int[n];
            _depth = new int[n];
            var index = new Dictionary<Transform, int>();
            for (int i = 0; i < n; i++) index[_bones[i]] = i;
            for (int i = 0; i < n; i++)
            {
                _parent[i] = -1;
                for (var p = _bones[i].parent; p != null; p = p.parent)
                    if (index.TryGetValue(p, out int pi)) { _parent[i] = pi; break; }
                _depth[i] = Depth(_bones[i], set);
                _axis[i] = AlongStalk(_bones[i], set);
                _base[i] = _written[i] = _bones[i].localRotation;
            }
        }

        // Where the plant stands is read in Start, not Awake: something that spawns a plant (KelpForests)
        // places and scales it after creating it.
        void Start()
        {
            var renderers = GetComponentsInChildren<Renderer>(true);
            _renderers = renderers;
            _area = renderers.Length > 0 ? renderers[0].bounds : new Bounds(transform.position, Vector3.one);
            foreach (var r in renderers) _area.Encapsulate(r.bounds);
            _groundY = _area.min.y;
            _height = Mathf.Max(_area.size.y, 0.1f);

            // Each bone's stretch runs to its child bone; a tip's is as long as the one below it.
            for (int i = 0; i < _bones.Length; i++)
            {
                int p = _parent[i];
                if (p >= 0 && _segLen[p] <= 0f && _bones[i] != null && _bones[p] != null)
                    _segLen[p] = Vector3.Distance(_bones[i].position, _bones[p].position);
            }
            for (int i = 0; i < _bones.Length; i++)
                if (_segLen[i] <= 0f) _segLen[i] = _parent[i] >= 0 ? _segLen[_parent[i]] : 0.1f;

            var chain = new float[_bones.Length]; // parents come before children
            float longest = 0f;
            for (int i = 0; i < _bones.Length; i++)
            {
                chain[i] = _segLen[i] + (_parent[i] >= 0 ? chain[_parent[i]] : 0f);
                longest = Mathf.Max(longest, chain[i]);
            }
            _plantReach = _area.size.magnitude + longest + extraReach + 1f;

            _pivot = new Vector3(_area.center.x, _area.min.y, _area.center.z); // where it's rooted
            _rootPos = transform.position;
            _rootRot = transform.rotation;
            // A fully rigged plant (Kelp) bends through its bones alone; tilt only plants with rigid parts.
            _tiltsWhole = plantLean > 0f && GetComponentsInChildren<MeshRenderer>(true).Length > 0;
        }

        // Sideways (along the seabed) push at a point, away from the nearest part of each swimmer's body:
        // plants are shoved aside, never up or down. Used for tilting a whole rigid plant.
        Vector3 PushAt(Vector3 at, List<Vector4> pushers, List<Vector4> segments)
        {
            Vector3 push = Vector3.zero;
            foreach (int p in _nearby)
            {
                var s = pushers[p];
                var g = segments[p];
                Vector3 centre = new Vector3(s.x, s.y, s.z), half = new Vector3(g.x, g.y, g.z);
                Vector3 a = centre - half, ab = half * 2f;
                float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(at - a, ab) / ab.sqrMagnitude) : 0.5f;
                Vector3 away = at - (a + ab * t);
                float reach = s.w + extraReach;
                float dist = away.magnitude;
                if (dist >= reach) continue;
                away.y = 0f;
                float flat = away.magnitude;
                if (flat < 1e-4f) continue;
                float f = 1f - dist / reach;
                f = f * f * (3f - 2f * f);
                push += away / flat * (f * g.w);
            }
            return Vector3.ClampMagnitude(push, 1f);
        }

        // Scratch for the lean job, shared by every plant (they run one after another on the main thread).
        static NativeArray<Vector3> s_from, s_to, s_needOut;
        static NativeArray<byte> s_valid;
        static NativeArray<Vector4> s_bodies, s_bodySegs;
        static int s_users;

        void OnEnable() => s_users++;

        void OnDisable()
        {
            if (--s_users > 0) return;
            if (s_from.IsCreated) { s_from.Dispose(); s_to.Dispose(); s_needOut.Dispose(); s_valid.Dispose(); }
            if (s_bodies.IsCreated) { s_bodies.Dispose(); s_bodySegs.Dispose(); }
        }

        static void Ensure<T>(ref NativeArray<T> a, int length) where T : struct
        {
            if (a.IsCreated && a.Length >= length) return;
            if (a.IsCreated) a.Dispose();
            a = new NativeArray<T>(Mathf.Max(length, 64), Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        // _need[i]: how far bone i's stretch of stalk has to lean to get out of every nearby swimmer's way.
        // The bones are read here; the swimmer-by-swimmer test runs in Burst (LeanJob).
        void ComputeNeeds(List<Vector4> pushers, List<Vector4> segments)
        {
            int n = _bones.Length, m = _nearby.Count;
            Ensure(ref s_from, n); Ensure(ref s_to, n); Ensure(ref s_needOut, n); Ensure(ref s_valid, n);
            Ensure(ref s_bodies, m); Ensure(ref s_bodySegs, m);
            for (int k = 0; k < m; k++)
            {
                s_bodies[k] = pushers[_nearby[k]];
                s_bodySegs[k] = segments[_nearby[k]];
            }
            for (int i = 0; i < n; i++)
            {
                var bone = _bones[i];
                s_valid[i] = (byte)(bone != null ? 1 : 0);
                if (bone == null) continue;
                Vector3 from = bone.position;
                s_from[i] = from;
                s_to[i] = from + bone.TransformDirection(_axis[i]) * _segLen[i];
            }
            new LeanJob
            {
                from = s_from, to = s_to, valid = s_valid, bodies = s_bodies, segments = s_bodySegs, need = s_needOut,
                boneCount = n, bodyCount = m,
                extraReach = extraReach, groundY = _groundY, height = _height,
                leanLow = leanWhenHitLow, leanHigh = leanWhenHitHigh,
            }.Run();
            for (int i = 0; i < n; i++) _need[i] = s_needOut[i];
        }

        // Strict float mode: the same arithmetic, in the same order, as the managed code it replaced.
        [BurstCompile(FloatMode = FloatMode.Strict)]
        struct LeanJob : IJob
        {
            [ReadOnly] public NativeArray<Vector3> from, to;
            [ReadOnly] public NativeArray<byte> valid;
            [ReadOnly] public NativeArray<Vector4> bodies, segments;
            [WriteOnly] public NativeArray<Vector3> need;
            public int boneCount, bodyCount;
            public float extraReach, groundY, height, leanLow, leanHigh;

            public void Execute()
            {
                for (int i = 0; i < boneCount; i++)
                    need[i] = valid[i] != 0 ? Lean(from[i], to[i]) : Vector3.zero;
            }

            // Sideways, away from the nearest part of the swimmer's body, and further over the lower down
            // it's hit; the strongest swimmer wins.
            Vector3 Lean(Vector3 from, Vector3 to)
            {
                Vector3 lean = Vector3.zero;
                float strongest = 0f;
                for (int p = 0; p < bodyCount; p++)
                {
                    var s = bodies[p];
                    var g = segments[p];
                    Vector3 centre = new Vector3(s.x, s.y, s.z), half = new Vector3(g.x, g.y, g.z);
                    ClosestPoints(from, to, centre - half, centre + half, out var onStalk, out var onBody);
                    float reach = s.w + extraReach;
                    Vector3 away = onStalk - onBody;
                    float dist = away.magnitude;
                    if (dist >= reach) continue;
                    away.y = 0f;
                    if (away.sqrMagnitude < 1e-8f) { away = from - centre; away.y = 0f; } // swimmer right over it
                    if (away.sqrMagnitude < 1e-8f) continue;

                    // Anything inside the body gets the full lean, easing off across the extra reach.
                    float f = Mathf.Clamp01((reach - dist) / Mathf.Max(extraReach, 0.05f));
                    f = f * f * (3f - 2f * f);
                    float hitHeight = Mathf.Clamp01((onStalk.y - groundY) / height);
                    float angle = f * g.w * Mathf.Lerp(leanLow, leanHigh, hitHeight);
                    if (angle > strongest)
                    {
                        strongest = angle;
                        lean = away.normalized * angle;
                    }
                }
                return lean;
            }
        }

        // Closest points between segments p1-q1 and p2-q2 (Ericson, Real-Time Collision Detection 5.1.9).
        static void ClosestPoints(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2, out Vector3 c1, out Vector3 c2)
        {
            Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
            float s, t;
            if (a <= 1e-8f && e <= 1e-8f) { s = t = 0f; }
            else if (a <= 1e-8f) { s = 0f; t = Mathf.Clamp01(f / e); }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= 1e-8f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                else
                {
                    float b = Vector3.Dot(d1, d2), denom = a * e - b * b;
                    s = denom > 1e-8f ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                    else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
                }
            }
            c1 = p1 + d1 * s;
            c2 = p2 + d2 * t;
        }

        void Spring(ref Vector3 x, ref Vector3 v, Vector3 target, float dt)
        {
            v += ((target - x) * stiffness - v * damping) * dt;
            x += v * dt;
        }

        static int Depth(Transform t, HashSet<Transform> bones)
        {
            int d = 0;
            for (var p = t.parent; p != null; p = p.parent) if (bones.Contains(p)) d++;
            return d;
        }

        // Toward the bone's first child bone; a tip carries on in its parent's direction.
        static Vector3 AlongStalk(Transform bone, HashSet<Transform> bones)
        {
            foreach (Transform child in bone)
                if (bones.Contains(child) && (child.position - bone.position).sqrMagnitude > 1e-8f)
                    return bone.InverseTransformDirection(child.position - bone.position).normalized;
            if (bone.parent != null && (bone.position - bone.parent.position).sqrMagnitude > 1e-8f)
                return bone.InverseTransformDirection(bone.position - bone.parent.position).normalized;
            return Vector3.up;
        }

        void LateUpdate()
        {
            if (_bones == null || _bones.Length == 0) return;
            // A forest has hundreds of these; one nobody can see and that's standing still has nothing to do.
            if (_resting && !Seen()) return;
            var pushers = VegetationPushMap.Pushers;
            var segments = VegetationPushMap.Segments;
            // Only the swimmers that could reach this plant (the rest can't touch it, so skipping them changes
            // nothing - but with a school of a thousand fish overhead, testing them all was most of the frame).
            VegetationPushMap.Nearby(_area, _plantReach, _nearby);

            bool near = false;
            foreach (int p in _nearby)
            {
                if (near) break;
                var s = pushers[p];
                var g = segments[p];
                float reach = s.w + new Vector3(g.x, g.y, g.z).magnitude + extraReach; // whole body, nose to tail
                if (_area.SqrDistance(new Vector3(s.x, s.y, s.z)) < reach * reach) near = true;
            }
            if (!near && _resting) return; // nothing touching and nothing still swinging

            float dt = Mathf.Min(Time.deltaTime, 1f / 30f);
            bool resting = !near;

            // The whole plant leans over at its base first; the bones then bend on top of that.
            if (_tiltsWhole)
            {
                Spring(ref _plantLean, ref _plantSpeed, near ? PushAt(_area.center, pushers, segments) * plantLean : Vector3.zero, dt);
                if (_plantLean.sqrMagnitude > 1e-7f || _plantSpeed.sqrMagnitude > 1e-6f) resting = false;
                var tilt = Quaternion.FromToRotation(Vector3.up, (Vector3.up + _plantLean).normalized);
                transform.SetPositionAndRotation(_pivot + tilt * (_rootPos - _pivot), tilt * _rootRot);
            }

            int n = _bones.Length;

            // 1) Back to the animated pose: the Animator re-poses its bones every frame, but one it doesn't
            //    animate still holds our bend from last frame.
            for (int i = 0; i < n; i++)
            {
                var bone = _bones[i];
                if (bone == null) continue;
                var current = bone.localRotation;
                _base[i] = current == _written[i] ? _base[i] : current;
                // Already there (not animated, not bent last frame): writing it again would only dirty the transform.
                var b = _base[i];
                if (current.x != b.x || current.y != b.y || current.z != b.z || current.w != b.w) bone.localRotation = b;
            }

            // 2) Where the swimmers hit the stalks, and how far over each hit wants its stretch of stalk.
            //    A stalk hit at some bone curves over from its base up to there - bending most near the base -
            //    so that bone ends up leaning the full amount; above the hit it rides straight on.
            //    _total[i] is the lean wanted from the base up to and including bone i.
            for (int i = 0; i < n; i++) _total[i] = Vector3.zero;
            if (near)
            {
                ComputeNeeds(pushers, segments);
                for (int k = 0; k < n; k++)
                {
                    if (_need[k].sqrMagnitude < 1e-8f) continue;
                    int dk = _depth[k];
                    float all = (dk + 1) * (dk + 2) * 0.5f; // bones from the base up to k, weighted dk+1 .. 1
                    for (int j = k; j >= 0; j = _parent[j])
                    {
                        int dj = _depth[j];
                        float upToJ = ((dj + 1) * (dk + 1) - dj * (dj + 1) * 0.5f) / all;
                        var wanted = _need[k] * upToJ;
                        if (wanted.sqrMagnitude > _total[j].sqrMagnitude) _total[j] = wanted;
                    }
                }
                for (int i = 0; i < n; i++) // above a hit, the stalk carries the lean below it
                {
                    int p = _parent[i];
                    if (p >= 0 && _total[p].sqrMagnitude > _total[i].sqrMagnitude) _total[i] = _total[p];
                }
            }

            // 3) Bend, from the base up: each bone springs toward its share of the lean.
            for (int i = 0; i < n; i++)
            {
                var bone = _bones[i];
                if (bone == null) continue;
                int p = _parent[i];

                Vector3 target = _total[i] - (p >= 0 ? _total[p] : Vector3.zero);

                // Damped spring toward the push: it gives way, then swings back past rest and settles.
                Spring(ref _lean[i], ref _speed[i], target, dt);
                if (_lean[i].sqrMagnitude > 1e-7f || _speed[i].sqrMagnitude > 1e-6f) resting = false;

                // Swing the bone's stretch toward the lean (children follow, so the stalk curves) - but never
                // so far, with the bones below it, that the stalk folds into the seabed.
                float below = p >= 0 ? _bent[p] : 0f;
                float angle = Mathf.Min(_lean[i].magnitude, Mathf.Max(0f, maxBend - below));
                _bent[i] = below + angle;
                if (angle > 1e-4f) // (the bone's world direction is only worth looking up for a bone that bends)
                {
                    Vector3 axisWS = bone.TransformDirection(_axis[i]);
                    Vector3 hinge = Vector3.Cross(axisWS, _lean[i]);
                    if (hinge.sqrMagnitude > 1e-10f)
                        bone.rotation = Quaternion.AngleAxis(angle * Mathf.Rad2Deg, hinge.normalized) * bone.rotation;
                }

                _written[i] = bone.localRotation;
            }
            _resting = resting;
        }
    }
}
