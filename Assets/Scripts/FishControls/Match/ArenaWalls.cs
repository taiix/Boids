using System.Collections.Generic;
using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// The glass walls fencing in the seabed, and the way out. When the survival clock runs out the host's
    /// <see cref="MatchManager"/> picks one wall at random and it rises up out of the water, so the fish can
    /// swim out under it. A curtain of bubbles bursts up where it stood and keeps rising afterwards, so the
    /// open side is easy to spot from across the reef.
    ///
    /// No networking of its own: MatchManager syncs which wall and when, and every machine animates the
    /// lift from that shared time, so all players (late joiners too) see the same wall at the same height.
    ///
    /// Put it on the parent of the wall panes (FrostedGlassBarrier). Each child with a BoxCollider is a wall.
    /// </summary>
    [DisallowMultipleComponent]
    public class ArenaWalls : MonoBehaviour
    {
        public static ArenaWalls Instance { get; private set; }

        [Header("Lift")]
        [Tooltip("Seconds the wall takes to rise out of the water.")]
        [SerializeField] float liftSeconds = 10f;
        [Tooltip("How far it rises, in wall heights (1 = its bottom edge ends where its top was).")]
        [SerializeField] float liftHeights = 1f;

        [Header("Escape")]
        [Tooltip("How far past the open wall's outer face a fish has to swim to count as escaped (metres).")]
        [SerializeField] float escapeMargin = 1f;

        [Header("Bubble curtain")]
        [Tooltip("Bubble particle system to copy for the curtain (the scene's BubblesParticles). Empty = none.")]
        [SerializeField] ParticleSystem bubblesTemplate;
        [Tooltip("Bubbles per second along the whole wall while it lifts.")]
        [SerializeField] float liftBubblesPerSecond = 700f;
        [Tooltip("Bubbles per second afterwards, marking the way out for the rest of the round.")]
        [SerializeField] float markerBubblesPerSecond = 220f;
        [SerializeField] Vector2 bubbleSize = new Vector2(0.25f, 0.7f);
        [SerializeField] Vector2 bubbleRiseSpeed = new Vector2(1.5f, 3f);
        [Tooltip("Seconds a bubble lives: about as long as the fast ones take to reach the surface.")]
        [SerializeField] float bubbleLifetime = 20f;
        [Tooltip("Layers the seabed is on, so the bubbles start at the sand, not under it.")]
        [SerializeField] LayerMask seabedLayers = 1 << 3; // Terrain

        class Wall
        {
            public Transform pane;
            public string name;
            public Vector3 home;          // where it stands while closed
            public Vector3 outward;       // flat, away from the arena
            public Vector3 along;         // flat, along its length
            public float halfThickness;   // centre to outer face, along outward
            public float height;
            public float gapMin, gapMax;  // the open stretch along `along`, between the side walls
            public float shownLift = -1f;
            public GlassSdfVolume contactBlend;
            public ParticleSystem bubbles;
        }

        readonly List<Wall> _walls = new List<Wall>();
        Material _bubbleMaterial;
        static readonly int HasSdf = Shader.PropertyToID("_HasSdf");

        public int Count => _walls.Count;

        /// <summary>Compass name of a wall ("North", "East"...), as the players are told.</summary>
        public string NameOf(int i) => i >= 0 && i < _walls.Count ? _walls[i].name : "";

        void Awake()
        {
            Instance = this;
            Collect();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (_bubbleMaterial != null) Destroy(_bubbleMaterial);
        }

        void Collect()
        {
            _walls.Clear();
            var panes = new List<BoxCollider>();
            foreach (Transform child in transform)
                if (child.TryGetComponent(out BoxCollider c)) panes.Add(c);
            if (panes.Count == 0) return;

            Vector3 centre = Vector3.zero;
            foreach (var c in panes) centre += c.transform.position;
            centre /= panes.Count;

            foreach (var c in panes)
            {
                Transform t = c.transform;
                Vector3 outward = Flat(t.forward);
                if (Vector3.Dot(outward, t.position - centre) < 0f) outward = -outward;
                Bounds b = c.bounds;
                var w = new Wall
                {
                    pane = t,
                    home = t.position,
                    outward = outward,
                    along = Flat(t.right),
                    halfThickness = Mathf.Abs(Vector3.Dot(b.extents, Abs(outward))),
                    height = b.size.y,
                    contactBlend = t.GetComponent<GlassSdfVolume>(),
                };
                w.name = Mathf.Abs(outward.z) >= Mathf.Abs(outward.x)
                    ? (outward.z > 0f ? "North" : "South")
                    : (outward.x > 0f ? "East" : "West");
                float halfLength = Mathf.Abs(Vector3.Dot(b.extents, Abs(w.along)));
                w.gapMin = -halfLength;
                w.gapMax = halfLength;
                _walls.Add(w);
            }

            // A wall can run on past the corners; the way out is only the stretch between the two walls
            // at its ends.
            foreach (var w in _walls)
                foreach (var o in _walls)
                {
                    if (o == w) continue;
                    float facing = Vector3.Dot(o.outward, w.along);
                    float at = Vector3.Dot(o.home - w.home, w.along);
                    if (facing > 0.7f) w.gapMax = Mathf.Min(w.gapMax, at - o.halfThickness);
                    else if (facing < -0.7f) w.gapMin = Mathf.Max(w.gapMin, at + o.halfThickness);
                }
        }

        void Update()
        {
            var m = MatchManager.Instance;
            int open = m != null ? m.ExitWall : -1;
            for (int i = 0; i < _walls.Count; i++)
            {
                float k = 0f;
                if (i == open) k = Mathf.Clamp01((float)((MatchManager.Now - m.ExitOpenedAt) / Mathf.Max(liftSeconds, 0.01f)));
                Show(_walls[i], k);
                UpdateBubbles(_walls[i], k);
            }
        }

        void Show(Wall w, float k)
        {
            if (Mathf.Approximately(k, w.shownLift)) return;
            bool starting = w.shownLift <= 0f && k > 0f;
            w.shownLift = k;
            w.pane.position = w.home + Vector3.up * (w.height * liftHeights * Mathf.SmoothStep(0f, 1f, k));
            if (!starting) return;

            // The glass blends into the rocks it stands on using a field baked where it stood; once it moves
            // that no longer lines up, so switch the blend off.
            if (w.contactBlend != null)
            {
                w.contactBlend.enabled = false;
                if (w.pane.TryGetComponent(out Renderer r))
                {
                    var block = new MaterialPropertyBlock();
                    r.GetPropertyBlock(block);
                    block.SetFloat(HasSdf, 0f);
                    r.SetPropertyBlock(block);
                }
            }
            StartBubbles(w);
        }

        // ------------------------------------------------------------------ queries

        /// <summary>True once <paramref name="position"/> is out past where wall <paramref name="wall"/> stood.</summary>
        public bool IsPastExit(int wall, Vector3 position)
        {
            if (wall < 0 || wall >= _walls.Count) return false;
            var w = _walls[wall];
            return Vector3.Dot(position - w.home, w.outward) > w.halfThickness + escapeMargin;
        }

        /// <summary>The nearest point of the way out (wall <paramref name="wall"/>'s gap) at the height of
        /// <paramref name="from"/> - where to point a player looking for the exit.</summary>
        public Vector3 ExitPointNear(int wall, Vector3 from)
        {
            if (wall < 0 || wall >= _walls.Count) return from;
            var w = _walls[wall];
            float s = Vector3.Dot(from - w.home, w.along);
            float inset = Mathf.Min(4f, (w.gapMax - w.gapMin) * 0.25f);
            s = Mathf.Clamp(s, w.gapMin + inset, w.gapMax - inset);
            Vector3 p = w.home + w.along * s;
            p.y = from.y;
            return p;
        }

        // ------------------------------------------------------------------ bubbles

        void StartBubbles(Wall w)
        {
            if (bubblesTemplate == null || w.bubbles != null) return;

            float length = Mathf.Max(1f, w.gapMax - w.gapMin);
            Vector3 mid = w.home + w.along * ((w.gapMin + w.gapMax) * 0.5f);
            float floor = SeabedHeight(mid - w.outward * (w.halfThickness + 2f), w.home.y - w.height * 0.5f);

            // Facing up, so the box shape emits straight up; its local Y runs along the wall.
            var go = Instantiate(bubblesTemplate.gameObject, new Vector3(mid.x, floor, mid.z),
                                 Quaternion.LookRotation(Vector3.up, w.along), transform);
            go.name = $"ExitBubbles_{w.name}";
            go.SetActive(true);
            var ps = go.GetComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.loop = true;
            main.startLifetime = bubbleLifetime;
            main.startSpeed = new ParticleSystem.MinMaxCurve(bubbleRiseSpeed.x, bubbleRiseSpeed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(bubbleSize.x, bubbleSize.y);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.CeilToInt((liftBubblesPerSecond * liftSeconds + markerBubblesPerSecond * bubbleLifetime) * 1.1f);

            if (go.TryGetComponent(out ParticleSystemRenderer pr))
            {
                // Thousands of these: the template's sphere is 768 triangles, so they get an 80-triangle one.
                if (pr.renderMode == ParticleSystemRenderMode.Mesh) pr.SetMeshes(new[] { LowPolySphere() });
                // The underwater fog hides bubbles a few metres out; without it the curtain shows from
                // across the reef.
                if (pr.sharedMaterial != null)
                {
                    if (_bubbleMaterial == null)
                    {
                        _bubbleMaterial = new Material(pr.sharedMaterial) { name = "ExitBubble" };
                        _bubbleMaterial.SetFloat("_EnableFogOnTransparent", 0f);
                        _bubbleMaterial.DisableKeyword("_ENABLE_FOG_ON_TRANSPARENT");
                    }
                    pr.sharedMaterial = _bubbleMaterial;
                }
            }

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.position = Vector3.zero;
            shape.rotation = Vector3.zero;
            shape.scale = new Vector3(w.halfThickness * 2f, length, 1f);

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = liftBubblesPerSecond; // UpdateBubbles drops it to the marker rate later
            ps.Play(true);
            w.bubbles = ps;
        }

        // A burst while the wall lifts, then a steady trickle that marks the way out for the rest of the round.
        void UpdateBubbles(Wall w, float k)
        {
            if (w.bubbles == null) return;
            var emission = w.bubbles.emission;
            float rate = k < 1f ? liftBubblesPerSecond : markerBubblesPerSecond;
            if (!Mathf.Approximately(emission.rateOverTimeMultiplier, rate)) emission.rateOverTimeMultiplier = rate;
        }

        static Mesh s_sphere;

        // An icosphere, subdivided once (80 triangles, radius 0.5 like Unity's sphere).
        static Mesh LowPolySphere()
        {
            if (s_sphere != null) return s_sphere;
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var v = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            int[] f =
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            var mids = new Dictionary<long, int>();
            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (mids.TryGetValue(key, out int i)) return i;
                v.Add((v[a] + v[b]) * 0.5f);
                mids[key] = v.Count - 1;
                return v.Count - 1;
            }
            var tris = new List<int>();
            for (int i = 0; i < f.Length; i += 3)
            {
                int a = f[i], b = f[i + 1], c = f[i + 2];
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                tris.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            var normals = new Vector3[v.Count];
            var uvs = new Vector2[v.Count];
            for (int i = 0; i < v.Count; i++)
            {
                normals[i] = v[i].normalized;
                v[i] = normals[i] * 0.5f;
                uvs[i] = new Vector2(0.5f + Mathf.Atan2(normals[i].z, normals[i].x) / (2f * Mathf.PI), 0.5f + Mathf.Asin(normals[i].y) / Mathf.PI);
            }
            s_sphere = new Mesh { name = "ExitBubble" };
            s_sphere.SetVertices(v);
            s_sphere.SetNormals(normals);
            s_sphere.SetUVs(0, uvs);
            s_sphere.SetTriangles(tris, 0);
            s_sphere.RecalculateBounds();
            return s_sphere;
        }

        float SeabedHeight(Vector3 at, float fallback)
        {
            var from = new Vector3(at.x, 200f, at.z);
            return Physics.Raycast(from, Vector3.down, out RaycastHit hit, 600f, seabedLayers, QueryTriggerInteraction.Ignore)
                ? hit.point.y
                : fallback;
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying) Collect();
            foreach (var w in _walls)
            {
                Gizmos.color = new Color(0.3f, 1f, 0.6f);
                Vector3 a = w.home + w.along * w.gapMin + w.outward * (w.halfThickness + escapeMargin);
                Vector3 b = w.home + w.along * w.gapMax + w.outward * (w.halfThickness + escapeMargin);
                Gizmos.DrawLine(a, b);
            }
        }
    }
}
