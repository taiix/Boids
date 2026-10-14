using System.Collections.Generic;
using FishGame;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace ReefRun
{
    /// <summary>
    /// Main-menu backdrop: a shark swims a slow closed loop over the reef while the menu camera rides
    /// inside its mouth, looking out between the teeth (the key-art shot, but alive).
    ///
    /// Put it on an object in the menu scene and point it at a shark model that carries no gameplay
    /// scripts (just the mesh + Animator). The path runs through <see cref="waypoints"/> (or this object's
    /// children) as a closed Catmull-Rom spline; with fewer than 4 points it swims an automatic loop around
    /// this object. A downward ray keeps it clear of the seabed. The jaw is opened on top of the swim
    /// animation, breathing between half-open and wide, with an occasional snap.
    /// </summary>
    public class MenuSharkCruise : MonoBehaviour
    {
        [Header("Shark")]
        [Tooltip("Root of the shark model in the menu scene (moved along the path).")]
        [SerializeField] Transform shark;
        [Tooltip("Forward axis correction if the model doesn't face +Z.")]
        [SerializeField] Vector3 modelEulerOffset;
        [SerializeField] Animator animator;
        [SerializeField] string speedParam = "Speed";
        [Range(0f, 1f)][SerializeField] float animSpeed = 0.4f;

        [Header("Path")]
        [Tooltip("Closed loop, 4+ points. Leave empty to use this object's children.")]
        [SerializeField] Transform[] waypoints;
        [Tooltip("Automatic loop size (x/z radius, m) when there are no waypoints.")]
        [SerializeField] Vector2 autoLoopRadius = new(28f, 20f);
        [SerializeField] float autoLoopBob = 2.5f;
        [SerializeField] float swimSpeed = 3.2f;
        [Tooltip("How quickly the body turns to follow the path. Lower = lazier, smoother turns.")]
        [SerializeField] float turnSmoothing = 2.2f;
        [SerializeField] float maxBank = 16f;
        [SerializeField] float minClearance = 2.2f;
        [SerializeField] LayerMask groundMask = ~0;

        [Header("Mouth camera")]
        [SerializeField] Camera menuCamera;
        [Tooltip("Bone or transform the camera rides on (head / SharkMouth).")]
        [SerializeField] Transform cameraMount;
        [SerializeField] Vector3 cameraLocalPosition;
        [SerializeField] Vector3 cameraLocalEuler;
        [Tooltip("Rotation smoothing so head sway reads as swimming, not shaking. Position is never smoothed, so the camera stays inside the mouth.")]
        [SerializeField] float cameraRotationSmoothing = 5f;
        [Tooltip("Optional: the camera's CreatureVision, set to the shark's own grade (we're seeing through its eyes).")]
        [SerializeField] CreatureVision vision;
        [Tooltip("The shark grade focuses 10 m out, which blurs the teeth right in front of this camera. On = depth of field off for the menu.")]
        [SerializeField] bool keepTeethSharp = true;

        [System.Serializable]
        public struct JawBone
        {
            public Transform bone;
            [Tooltip("Local rotation added to this bone when the mouth is fully open.")]
            public Vector3 openEuler;
        }

        [Header("Jaw")]
        [Tooltip("Bones that open the mouth (the great white rig: Bone011 lifts the snout, Bone013 drops the lower jaw).")]
        [SerializeField] JawBone[] jawBones;
        [Range(0f, 1f)][SerializeField] float minGape = 0.55f;
        [SerializeField] float gapePeriod = 7f;
        [Tooltip("Seconds between occasional snaps (random within the range).")]
        [SerializeField] Vector2 snapInterval = new(9f, 16f);

        readonly List<Vector3> _pts = new();
        readonly List<float> _lut = new();   // cumulative arc length per sample
        const int SamplesPerSegment = 24;
        float _length, _distance;
        Quaternion _bodyRot;
        Quaternion _camRot;
        Quaternion[] _jawRest, _jawLastSet;
        float _snapAt, _snapT = -1f;
        int _speedId;

        void Start()
        {
            BuildPath();
            _speedId = Animator.StringToHash(speedParam);
            if (shark != null)
            {
                shark.position = PointAt(0f);
                _bodyRot = Quaternion.LookRotation(TangentAt(0f), Vector3.up);
                shark.rotation = _bodyRot * Quaternion.Euler(modelEulerOffset);
            }
            if (menuCamera != null && cameraMount != null)
            {
                _camRot = cameraMount.rotation * Quaternion.Euler(cameraLocalEuler);
                menuCamera.transform.SetPositionAndRotation(cameraMount.TransformPoint(cameraLocalPosition), _camRot);
            }
            _snapAt = Time.time + Random.Range(snapInterval.x, snapInterval.y);
            if (vision != null) vision.ApplyRole(FishRole.Shark);
            if (keepTeethSharp) AddNoDepthOfFieldVolume();
        }

        void AddNoDepthOfFieldVolume()
        {
            var go = new GameObject("Menu: no depth of field");
            go.transform.SetParent(transform, false);   // Default layer, which every camera's volume mask includes
            var volume = go.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 50f;                       // above the role grade (10)
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.Add<DepthOfField>(true).focusMode.value = DepthOfFieldMode.Off;
            volume.sharedProfile = profile;
        }

        void Update()
        {
            if (shark == null || _length <= 0f) return;
            float dt = Time.deltaTime;

            _distance = Mathf.Repeat(_distance + swimSpeed * dt, _length);
            Vector3 pos = PointAt(_distance);
            Vector3 fwd = TangentAt(_distance);

            // Keep clear of the seabed (the path may dip into a rise the waypoints didn't account for).
            if (Physics.Raycast(pos + Vector3.up * 50f, Vector3.down, out var hit, 100f, groundMask, QueryTriggerInteraction.Ignore)
                && pos.y < hit.point.y + minClearance)
                pos.y = hit.point.y + minClearance;

            // Lazy turn towards the path heading, banking into the turn like a real cruise.
            Quaternion target = Quaternion.LookRotation(fwd, Vector3.up);
            float yawRate = Vector3.SignedAngle(_bodyRot * Vector3.forward, fwd, Vector3.up);
            Quaternion bank = Quaternion.AngleAxis(Mathf.Clamp(-yawRate * 1.5f, -maxBank, maxBank), Vector3.forward);
            _bodyRot = Quaternion.Slerp(_bodyRot, target, 1f - Mathf.Exp(-turnSmoothing * dt));

            shark.SetPositionAndRotation(pos, _bodyRot * bank * Quaternion.Euler(modelEulerOffset));

            if (animator != null) animator.SetFloat(_speedId, animSpeed);
        }

        // After the Animator has posed the rig: open the jaw, then place the camera on the posed head.
        void LateUpdate()
        {
            ApplyJaw();

            if (menuCamera == null || cameraMount == null) return;
            Quaternion targetRot = cameraMount.rotation * Quaternion.Euler(cameraLocalEuler);
            _camRot = Quaternion.Slerp(_camRot, targetRot, 1f - Mathf.Exp(-cameraRotationSmoothing * Time.deltaTime));
            menuCamera.transform.SetPositionAndRotation(cameraMount.TransformPoint(cameraLocalPosition), _camRot);
        }

        void ApplyJaw()
        {
            if (jawBones == null || jawBones.Length == 0) return;
            if (_jawRest == null || _jawRest.Length != jawBones.Length)
            {
                _jawRest = new Quaternion[jawBones.Length];
                _jawLastSet = new Quaternion[jawBones.Length];
                for (int i = 0; i < jawBones.Length; i++)
                    if (jawBones[i].bone != null) _jawRest[i] = _jawLastSet[i] = jawBones[i].bone.localRotation;
            }

            float breathe = Mathf.Lerp(minGape, 1f, 0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.PI * 2f / gapePeriod));

            // Occasional snap: shut fast, then ease back open.
            if (_snapT < 0f && Time.time >= _snapAt) _snapT = 0f;
            if (_snapT >= 0f)
            {
                _snapT += Time.deltaTime;
                float shut = _snapT < 0.12f ? _snapT / 0.12f : Mathf.Clamp01(1f - (_snapT - 0.12f) / 0.9f);
                breathe *= 1f - 0.85f * shut;
                if (_snapT > 1.02f) { _snapT = -1f; _snapAt = Time.time + Random.Range(snapInterval.x, snapInterval.y); }
            }

            for (int i = 0; i < jawBones.Length; i++)
            {
                var bone = jawBones[i].bone;
                if (bone == null) continue;
                // If the Animator drives this bone its pose is fresh every frame; if not, it still holds what
                // we set last frame, so start from the rest pose instead of stacking rotations.
                Quaternion basePose = bone.localRotation == _jawLastSet[i] ? _jawRest[i] : bone.localRotation;
                _jawLastSet[i] = basePose * Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(jawBones[i].openEuler), breathe);
                bone.localRotation = _jawLastSet[i];
            }
        }

        // ===================================================================
        //  PATH (closed Catmull-Rom, arc-length parametrised)
        // ===================================================================
        void BuildPath()
        {
            _pts.Clear();
            var src = waypoints != null && waypoints.Length > 0 ? waypoints : ChildPoints();
            foreach (var t in src) if (t != null) _pts.Add(t.position);

            if (_pts.Count < 4)
            {
                _pts.Clear();
                const int n = 10;
                for (int i = 0; i < n; i++)
                {
                    float a = i / (float)n * Mathf.PI * 2f;
                    _pts.Add(transform.position + new Vector3(Mathf.Cos(a) * autoLoopRadius.x,
                        Mathf.Sin(a * 2f) * autoLoopBob, Mathf.Sin(a) * autoLoopRadius.y));
                }
            }

            _lut.Clear();
            _length = 0f;
            Vector3 prev = Spline(0f);
            _lut.Add(0f);
            int total = _pts.Count * SamplesPerSegment;
            for (int i = 1; i <= total; i++)
            {
                Vector3 p = Spline(i / (float)SamplesPerSegment);
                _length += Vector3.Distance(prev, p);
                _lut.Add(_length);
                prev = p;
            }
        }

        Transform[] ChildPoints()
        {
            var list = new Transform[transform.childCount];
            for (int i = 0; i < list.Length; i++) list[i] = transform.GetChild(i);
            return list;
        }

        // Spline parameter u in [0, count): integer part = segment, fraction = position in it.
        Vector3 Spline(float u)
        {
            int n = _pts.Count;
            int i = Mathf.FloorToInt(u);
            float t = u - i;
            Vector3 p0 = _pts[(i - 1 + n * 2) % n], p1 = _pts[(i + n) % n], p2 = _pts[(i + 1) % n], p3 = _pts[(i + 2) % n];
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        float ParamAt(float distance)
        {
            int lo = 0, hi = _lut.Count - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (_lut[mid] < distance) lo = mid; else hi = mid;
            }
            float seg = _lut[hi] - _lut[lo];
            float f = seg > 1e-5f ? (distance - _lut[lo]) / seg : 0f;
            return (lo + f) / SamplesPerSegment;
        }

        Vector3 PointAt(float distance) => Spline(ParamAt(Mathf.Repeat(distance, _length)));

        Vector3 TangentAt(float distance)
        {
            Vector3 d = PointAt(distance + 0.5f) - PointAt(distance - 0.5f);
            return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.forward;
        }

        void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying) BuildPath();
            if (_length <= 0f) return;
            Gizmos.color = new Color(1f, 0.72f, 0.3f);
            Vector3 prev = PointAt(0f);
            for (float d = 1f; d <= _length; d += 1f)
            {
                Vector3 p = PointAt(d);
                Gizmos.DrawLine(prev, p);
                prev = p;
            }
        }
    }
}
