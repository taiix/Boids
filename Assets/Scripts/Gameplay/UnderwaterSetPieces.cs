using System.Collections.Generic;
using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// Big rock formations ("Underwater environment" by ConradJustin, CC BY - see CREDITS.md) placed by hand
    /// as children of this object. The seabed is built when the match starts, so each child keeps its x/z and
    /// rotation and is dropped onto the sand once the seabed is there. The rocks get colliders on the Terrain
    /// layer (fish can't swim through them and they block the shark's line of sight); the kelp stays passable.
    /// Their centres are kept clear of task spawns (<see cref="KelpForests.KeepClearPoints"/>).
    /// </summary>
    public class UnderwaterSetPieces : MonoBehaviour
    {
        [Tooltip("How far each piece's own ground level (its pivot) sits below the lowest sand under it (metres). " +
                 "The parts that went underground in the original stay buried; nothing hangs over a dip.")]
        [SerializeField] float groundSink = 0.4f;
        [Tooltip("Materials with this in their name get no collider (the kelp).")]
        [SerializeField] string passableMaterial = "grass";
        [Tooltip("Layer the rock colliders go on (3 = Terrain: blocks the shark's sight lines).")]
        [SerializeField] int rockLayer = 3;
        [Tooltip("Our own seaweed (the kelp forests' rigged kelp), planted where the asset's kelp strands grew: " +
                 "the KelpSpot_NN_hH points in the model, H = the strand's height in model metres.")]
        [SerializeField] GameObject[] seaweeds;
        [Tooltip("Height of the seaweed prefabs as they come (metres), to size each one to its strand.")]
        [SerializeField] float seaweedHeight = 7.9f;
        [Tooltip("Smallest and largest size relative to the prefab.")]
        [SerializeField] Vector2 seaweedScale = new Vector2(0.7f, 1.6f);

        static readonly List<UnderwaterSetPieces> s_all = new List<UnderwaterSetPieces>();
        MeshCollider _seabed;
        float _seenAt = -1f;
        bool _placed;

        /// <summary>Where the pieces stand: task spawners keep clear of these.</summary>
        public static IEnumerable<Vector3> Centres()
        {
            foreach (var s in s_all)
                if (s != null)
                    foreach (Transform c in s.transform)
                        if (c.gameObject.activeSelf) yield return c.position;
        }

        // Awake / OnDestroy, not OnEnable / OnDisable: it switches itself off once placed and must stay listed.
        void Awake() => s_all.Add(this);
        void OnDestroy() => s_all.Remove(this);

        void Update()
        {
            if (_placed) { enabled = false; return; }
            if (_seabed == null)
            {
                var terrain = FindAnyObjectByType<TestTerrain>();
                if (terrain == null || !terrain.TryGetComponent(out _seabed) || _seabed.sharedMesh == null) { _seabed = null; return; }
            }
            if (_seenAt < 0f) _seenAt = Time.time;
            if (Time.time - _seenAt < 1.5f) return;   // let the seabed finish building
            int n = 0;
            foreach (Transform piece in transform) { Place(piece); Plant(piece, n++); }
            _placed = true;
        }

        void Place(Transform piece)
        {
            var rs = piece.GetComponentsInChildren<MeshRenderer>();
            if (rs.Length == 0) return;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);

            // the lowest ground under its footprint, so no edge hangs in the water
            float lowest = float.MaxValue;
            Bounds area = _seabed.bounds;
            for (int i = 0; i < 9; i++)
            {
                float fx = i % 3 - 1, fz = i / 3 - 1;
                var p = new Vector3(b.center.x + fx * b.extents.x * 0.7f, area.max.y + 5f, b.center.z + fz * b.extents.z * 0.7f);
                if (_seabed.Raycast(new Ray(p, Vector3.down), out var hit, area.size.y + 10f))
                    lowest = Mathf.Min(lowest, hit.point.y);
            }
            if (lowest == float.MaxValue) { Debug.LogWarning($"[SetPieces] No seabed under {piece.name}."); return; }
            // its own ground level (the pivot: the original scene's sand) just under the lowest sand
            piece.position += Vector3.up * (lowest - groundSink - piece.position.y);

            foreach (var r in rs)
            {
                var mat = r.sharedMaterial;
                if (mat != null && mat.name.ToLowerInvariant().Contains(passableMaterial)) continue;
                if (!r.TryGetComponent(out MeshFilter mf) || mf.sharedMesh == null || r.GetComponent<Collider>() != null) continue;
                var mc = r.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                r.gameObject.layer = rockLayer;
            }
        }

        // Our kelp where the asset's kelp grew. Spot names carry the strand's height; the same spot always gets
        // the same plant and turn, so every machine's reef looks alike.
        void Plant(Transform piece, int pieceIndex)
        {
            if (seaweeds == null || seaweeds.Length == 0) return;
            int i = 0;
            foreach (var spot in piece.GetComponentsInChildren<Transform>())
            {
                if (!spot.name.StartsWith("KelpSpot")) continue;
                int h = spot.name.LastIndexOf("_h");
                float height = h >= 0 && float.TryParse(spot.name.Substring(h + 2), out float v) ? v : seaweedHeight;
                height *= piece.lossyScale.y;
                var prefab = seaweeds[(i + pieceIndex) % seaweeds.Length];
                if (prefab == null) continue;
                // on whatever is solid under the spot (a rock ledge or the sand); none close by, no plant
                if (!Ground(piece, spot.position, out Vector3 at)) continue;
                float yaw = (spot.position.x * 37.1f + spot.position.z * 13.7f + i * 61f) % 360f;
                var go = Instantiate(prefab, at + Vector3.down * 0.25f, Quaternion.Euler(0f, yaw, 0f), piece);
                float k = Mathf.Clamp(height / Mathf.Max(0.1f, seaweedHeight), seaweedScale.x, seaweedScale.y);
                var parent = piece.lossyScale;
                go.transform.localScale = new Vector3(prefab.transform.localScale.x * k / parent.x,
                                                      prefab.transform.localScale.y * k / parent.y,
                                                      prefab.transform.localScale.z * k / parent.z);
                i++;
            }
        }

        // The surface under a kelp spot: this piece's rock colliders or the seabed, from a little above the spot
        // (it may sit just under the sand) to a few metres below.
        bool Ground(Transform piece, Vector3 spot, out Vector3 at)
        {
            at = default;
            var ray = new Ray(spot + Vector3.up * 3f, Vector3.down);
            float best = float.MaxValue;
            foreach (var c in piece.GetComponentsInChildren<MeshCollider>())
                if (c.Raycast(ray, out var hit, 6f) && hit.distance < best) { best = hit.distance; at = hit.point; }
            if (_seabed != null && _seabed.Raycast(ray, out var sand, 6f) && sand.distance < best) { best = sand.distance; at = sand.point; }
            return best < float.MaxValue;
        }
    }
}
