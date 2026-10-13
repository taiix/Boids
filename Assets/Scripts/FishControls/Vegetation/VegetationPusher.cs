using System.Collections.Generic;
using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// Makes this body shove seabed plants out of its way as it swims through them
    /// (see <see cref="VegetationPushMap"/>). Put it on players, the shark and any NPC creature; the school
    /// fish push on their own.
    ///
    /// The body is treated as a capsule - taken from its CapsuleCollider, or its size if it has none - so
    /// the shark clears a shark-shaped lane, nose to tail, rather than a ball around its middle.
    /// </summary>
    [DisallowMultipleComponent]
    public class VegetationPusher : MonoBehaviour
    {
        [Tooltip("Body radius in metres. 0 = from the body's CapsuleCollider (or its size).")]
        [SerializeField] float radius;
        // (Not called "strength": an earlier version used that name with 0 meaning "from body size", and
        // prefabs still hold that 0 - which here would mean "pushes nothing".)
        [Tooltip("How much room it makes, 0..1. 1 = plants clear the body completely.")]
        [Range(0f, 1f)][SerializeField] float clearance = 1f;

        public static readonly List<VegetationPusher> All = new List<VegetationPusher>();

        public float Radius { get; private set; }
        public float Strength => clearance;

        Vector3 _centreLocal;   // capsule centre, in the body's space
        Vector3 _axisLocal;     // capsule axis, in the body's space (unit)
        float _halfLength;      // half the straight part of the capsule, in metres

        void Awake()
        {
            if (TryGetComponent(out CapsuleCollider capsule))
            {
                Vector3 s = transform.lossyScale;
                int dir = capsule.direction; // 0 = x, 1 = y, 2 = z
                _axisLocal = dir == 0 ? Vector3.right : dir == 1 ? Vector3.up : Vector3.forward;
                float along = Mathf.Abs(dir == 0 ? s.x : dir == 1 ? s.y : s.z);
                float across = dir == 0 ? Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z))
                             : dir == 1 ? Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z))
                             : Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y));
                Radius = capsule.radius * across;
                _halfLength = Mathf.Max(0f, capsule.height * 0.5f * along - Radius);
                _centreLocal = capsule.center;

                // Colliders are trimmed to the core body; widen toward what you see (fins, nose to tail),
                // or plants slide through the parts the collider leaves out.
                if (VisibleSize(out Vector3 size))
                {
                    float length = dir == 0 ? size.x : dir == 1 ? size.y : size.z;
                    float girth = dir == 0 ? Mathf.Max(size.y, size.z) : dir == 1 ? Mathf.Max(size.x, size.z) : Mathf.Max(size.x, size.y);
                    Radius = Mathf.Max(Radius, girth * 0.3f);
                    _halfLength = Mathf.Max(_halfLength, length * 0.45f - Radius);
                }
            }
            else
            {
                Radius = RadiusFromBody(gameObject);
                _axisLocal = Vector3.forward;
                _halfLength = 0f;
                _centreLocal = Vector3.zero;
            }
            if (radius > 0f) Radius = radius;
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        /// <summary>The capsule's centre, in the world.</summary>
        public Vector3 Centre => transform.TransformPoint(_centreLocal);
        /// <summary>Half the capsule's straight part, along its axis, in the world.</summary>
        public Vector3 HalfSegment => transform.rotation * _axisLocal * _halfLength;

        // The visible body's size along this body's own axes (whatever way it's facing right now).
        bool VisibleSize(out Vector3 size)
        {
            size = Vector3.zero;
            bool any = false;
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer) continue;
                Transform space = r is SkinnedMeshRenderer smr && smr.rootBone != null ? smr.rootBone : r.transform;
                Bounds lb = r.localBounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = lb.center + Vector3.Scale(lb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var local = transform.InverseTransformPoint(space.TransformPoint(corner));
                    min = Vector3.Min(min, local);
                    max = Vector3.Max(max, local);
                    any = true;
                }
            }
            if (!any) return false;
            size = Vector3.Scale(max - min, transform.lossyScale);
            return true;
        }

        /// <summary>A body's radius from its size (half its longest side), for bodies without a collider.</summary>
        public static float RadiusFromBody(GameObject body)
        {
            var renderers = body.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return 0.5f;
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            Vector3 e = b.extents;
            return Mathf.Max(0.3f, Mathf.Max(e.x, Mathf.Max(e.y, e.z)));
        }
    }
}
