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

        /// <summary>Wall <paramref name="i"/>'s layout where it stands closed: its middle, the flat direction out of
        /// the arena and along the wall, half its thickness, and the open stretch between the side walls (along
        /// <paramref name="along"/>, from <paramref name="home"/>).</summary>
        public bool Describe(int i, out Vector3 home, out Vector3 outward, out Vector3 along, out float halfThickness,
                             out float gapMin, out float gapMax)
        {
            home = outward = along = Vector3.zero;
            halfThickness = gapMin = gapMax = 0f;
            if (i < 0 || i >= _walls.Count) return false;
            var w = _walls[i];
            home = w.home; outward = w.outward; along = w.along;
            halfThickness = w.halfThickness; gapMin = w.gapMin; gapMax = w.gapMax;
            return true;
        }

        /// <summary>The wall facing wall <paramref name="i"/> across the arena, or -1.</summary>
        public int Opposite(int i)
        {
            if (i < 0 || i >= _walls.Count) return -1;
            for (int j = 0; j < _walls.Count; j++)
                if (j != i && Vector3.Dot(_walls[j].outward, _walls[i].outward) < -0.7f) return j;
            return -1;
        }

        /// <summary>True when <paramref name="position"/> is inside the arena, at least <paramref name="margin"/>
        /// metres in from every wall (where the walls stand closed).</summary>
        public bool Inside(Vector3 position, float margin)
        {
            foreach (var w in _walls)
                if (Vector3.Dot(position - w.home, w.outward) > -(w.halfThickness + margin)) return false;
            return true;
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
                // Thousands of these: the template's sphere is 768 triangles, the shared bubble mesh 80.
                if (pr.renderMode == ParticleSystemRenderMode.Mesh) pr.SetMeshes(new[] { WaterFx.BubbleMesh });
                // The underwater fog hides bubbles a few metres out; the fog-free bubble shows from across
                // the reef.
                var lib = WaterFxLibrary.Get();
                if (lib != null && lib.bubbleFar != null) pr.sharedMaterial = lib.bubbleFar;
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
