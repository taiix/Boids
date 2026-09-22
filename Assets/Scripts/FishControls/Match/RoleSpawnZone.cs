using System.Collections.Generic;
using UnityEngine;

namespace FishGame
{
    public enum SpawnZoneMode
    {
        /// <summary>Scatter players anywhere inside the box.</summary>
        RandomInBox = 0,
        /// <summary>Always spawn exactly at this object's position.</summary>
        Center = 1,
    }

    /// <summary>
    /// Where a given role enters the map. Fish get scattered through a volume; the shark gets a
    /// single fixed point.
    ///
    /// These register themselves at load rather than being wired in the Inspector, because the
    /// NetworkManager lives in the Bootstrap scene and survives the scene change - Unity forbids
    /// cross-scene references, so it cannot hold a list of Transforms belonging to the Island.
    /// Mirror's own NetworkStartPosition works the same way for the same reason.
    /// </summary>
    [DisallowMultipleComponent]
    public class RoleSpawnZone : MonoBehaviour
    {
        [Tooltip("Which role spawns here.")]
        [SerializeField] FishRole role = FishRole.Fish;

        [Tooltip("RandomInBox scatters players through the volume. Center puts every one of them " +
                 "on this exact spot.")]
        [SerializeField] SpawnZoneMode mode = SpawnZoneMode.RandomInBox;

        [Tooltip("Size of the spawn box in world units. Only used by RandomInBox.")]
        [SerializeField] Vector3 size = new Vector3(260f, 48f, 260f);

        [Tooltip("Try to keep spawned players at least this far apart, so nobody lands inside " +
                 "somebody else.")]
        [SerializeField] float minSeparation = 10f;

        [Tooltip("How many random points to try before accepting the best available one.")]
        [SerializeField] int placementAttempts = 24;

        [Tooltip("Face each spawned player in a random direction instead of the zone's rotation.")]
        [SerializeField] bool randomYaw = true;

        static readonly List<RoleSpawnZone> All = new List<RoleSpawnZone>();
        static readonly List<Vector3> UsedThisRound = new List<Vector3>();

        void Awake() { if (!All.Contains(this)) All.Add(this); }
        void OnDestroy() => All.Remove(this);

        /// <summary>
        /// Find a spawn pose for <paramref name="wanted"/>. Returns false when no zone covers that
        /// role, so the caller can fall back to Mirror's start positions.
        /// </summary>
        public static bool TryGetSpawn(FishRole wanted, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;

            RoleSpawnZone zone = null;
            foreach (var z in All)
                if (z != null && z.role == wanted) { zone = z; break; }

            if (zone == null) return false;

            zone.Resolve(out position, out rotation);
            UsedThisRound.Add(position);
            return true;
        }

        /// <summary>Forget who spawned where. Call at the start of a round.</summary>
        public static void ResetClaims() => UsedThisRound.Clear();

        void Resolve(out Vector3 position, out Quaternion rotation)
        {
            if (mode == SpawnZoneMode.Center)
            {
                position = transform.position;
                rotation = transform.rotation;
                return;
            }

            // Take the roomiest of several random candidates, so a handful of fish scattered into
            // the same volume do not end up stacked on top of each other.
            Vector3 best = RandomPointInBox();
            float bestClearance = Clearance(best);

            for (int i = 1; i < Mathf.Max(1, placementAttempts); i++)
            {
                if (bestClearance >= minSeparation) break;

                Vector3 candidate = RandomPointInBox();
                float clearance = Clearance(candidate);
                if (clearance > bestClearance) { best = candidate; bestClearance = clearance; }
            }

            position = best;
            rotation = randomYaw
                ? Quaternion.Euler(0f, Random.Range(0f, 360f), 0f)
                : transform.rotation;
        }

        Vector3 RandomPointInBox()
        {
            var local = new Vector3(
                Random.Range(-size.x, size.x) * 0.5f,
                Random.Range(-size.y, size.y) * 0.5f,
                Random.Range(-size.z, size.z) * 0.5f);
            return transform.TransformPoint(local);
        }

        static float Clearance(Vector3 point)
        {
            float nearest = float.MaxValue;
            foreach (var used in UsedThisRound)
            {
                float d = Vector3.Distance(point, used);
                if (d < nearest) nearest = d;
            }
            return nearest;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = role == FishRole.Shark
                ? new Color(1f, 0.3f, 0.2f, 0.25f)
                : new Color(0.3f, 0.8f, 1f, 0.25f);
            Gizmos.matrix = transform.localToWorldMatrix;

            if (mode == SpawnZoneMode.Center) Gizmos.DrawSphere(Vector3.zero, 3f);
            else Gizmos.DrawCube(Vector3.zero, size);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = role == FishRole.Shark ? new Color(1f, 0.3f, 0.2f) : new Color(0.3f, 0.8f, 1f);
            Gizmos.matrix = transform.localToWorldMatrix;

            if (mode == SpawnZoneMode.Center) Gizmos.DrawWireSphere(Vector3.zero, 3f);
            else Gizmos.DrawWireCube(Vector3.zero, size);
        }
    }
}
