using System.Collections.Generic;
using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// The reef's fish species: the models a player fish can be, and the mix the schools are drawn from.
    /// Lives at Resources/FishSpecies.asset and is (re)built by Tools > Fish > Build Species
    /// (FishSpeciesBuilder.cs), which bakes each species' shader-swim mesh, material and school prefab.
    ///
    /// Everything that picks from it is deterministic or networked: a school's species comes from its rank
    /// among the scene's schools, and a player's from a SyncVar on <see cref="FishPlayer"/>, so every
    /// machine sees the same fish.
    /// </summary>
    [CreateAssetMenu(menuName = "Reef Run/Fish Species", fileName = "FishSpecies")]
    public class FishSpecies : ScriptableObject
    {
        [System.Serializable]
        public class Species
        {
            public string name;
            [Tooltip("The school fish (Boid + Edible + FishSwimRig, shader swim).")]
            public GameObject schoolPrefab;
            [Tooltip("Shader-swim mesh: nose along +Z, one unit long (see FishSwim.hlsl).")]
            public Mesh swimMesh;
            public Material swimMaterial;
            [Tooltip("Players can be this fish.")]
            public bool playable;
            [Tooltip("A player of this species, relative to the original player fish's length.")]
            public float playerSize = 1f;
            [Tooltip("School speed relative to the scene's tuning (a mackerel cruises faster than a butterflyfish).")]
            public float speedScale = 1f;
            [Tooltip("School spacing relative to the scene's tuning (bigger fish keep further apart).")]
            public float spacingScale = 1f;
        }

        public List<Species> species = new List<Species>();

        [Tooltip("Schools take species from this list in turn, biggest school first (indices into Species). " +
                 "Starts with the playable fish, so players always have their own kind to hide among.")]
        public List<int> schoolCycle = new List<int>();

        static FishSpecies s_instance;
        static bool s_loaded;

        public static FishSpecies Instance
        {
            get
            {
                if (!s_loaded) { s_instance = Resources.Load<FishSpecies>("FishSpecies"); s_loaded = true; }
                return s_instance;
            }
        }

        public Species Get(int index) => index >= 0 && index < species.Count ? species[index] : null;

        /// <summary>The species the school at <paramref name="rank"/> (0 = biggest) gets, or -1 for its own prefab.</summary>
        public int SchoolSpecies(int rank)
        {
            if (schoolCycle == null || schoolCycle.Count == 0) return -1;
            int i = schoolCycle[((rank % schoolCycle.Count) + schoolCycle.Count) % schoolCycle.Count];
            var sp = Get(i);
            return sp != null && sp.schoolPrefab != null ? i : -1;
        }

        /// <summary>Server: a species for a new player fish - the playable one fewest players are already
        /// wearing, ties broken at random, so a group of three gets three different fish.</summary>
        public int PickPlayerSpecies(IList<int> taken)
        {
            int best = int.MaxValue;
            var candidates = new List<int>();
            for (int i = 0; i < species.Count; i++)
            {
                var sp = species[i];
                if (sp == null || !sp.playable || sp.swimMesh == null || sp.swimMaterial == null) continue;
                int used = 0;
                if (taken != null) for (int t = 0; t < taken.Count; t++) if (taken[t] == i) used++;
                if (used < best) { best = used; candidates.Clear(); }
                if (used == best) candidates.Add(i);
            }
            return candidates.Count == 0 ? -1 : candidates[UnityEngine.Random.Range(0, candidates.Count)];
        }

        // ---------------------------------------------------------------- player model

        const string ModelName = "SpeciesModel";

        /// <summary>
        /// Dress a player fish body as <paramref name="index"/> (-1 = the original model). The original
        /// skinned mesh is switched off (its GameObject, so nothing that measures the body counts it) and the
        /// species' shader-swim mesh is put in its place, under the same visual root: it banks, leans and
        /// squashes with the body exactly like the original did, and looks just like that species' schools.
        /// Safe to call again; it only rebuilds when the species changes.
        /// </summary>
        public static void ApplyToPlayer(Transform body, int index)
        {
            if (body == null) return;
            var lib = Instance;
            var sp = lib != null ? lib.Get(index) : null;
            if (sp != null && (sp.swimMesh == null || sp.swimMaterial == null)) sp = null;

            // The original model: its skinned mesh, and the body's child it hangs under (the visual root
            // that FishBodyAnimator banks and BodyJuice squashes).
            var original = FindOriginal(body);
            if (original == null) return;
            Transform root = original.transform;
            while (root.parent != null && root.parent != body) root = root.parent;
            if (root == body) return;

            var existing = root.Find(ModelName);
            if (existing != null)
            {
                if (sp != null && existing.TryGetComponent(out MeshFilter mf0) && mf0.sharedMesh == sp.swimMesh) return;
                existing.gameObject.SetActive(false);
                Object.Destroy(existing.gameObject);
            }

            if (sp == null)
            {
                original.gameObject.SetActive(true);
                return;
            }

            // Where the original sits and how long it is, in the visual root's space (nose along the body's +Z).
            original.gameObject.SetActive(true);
            Bounds ob = LocalBounds(original, root);
            original.gameObject.SetActive(false);

            var go = new GameObject(ModelName);
            go.layer = original.gameObject.layer;
            var t = go.transform;
            t.SetParent(root, false);
            // Face the body's forward, whatever way the visual root is turned.
            t.localRotation = Quaternion.Inverse(root.localRotation);
            float length = Mathf.Max(ob.size.z, 0.01f) * Mathf.Max(0.1f, sp.playerSize);
            t.localScale = Vector3.one * length;
            t.localPosition = ob.center;

            go.AddComponent<MeshFilter>().sharedMesh = sp.swimMesh;
            var mr = go.AddComponent<MeshRenderer>();
            var mats = new Material[Mathf.Max(1, sp.swimMesh.subMeshCount)];
            for (int i = 0; i < mats.Length; i++) mats[i] = sp.swimMaterial;
            mr.sharedMaterials = mats;
            mr.shadowCastingMode = original.shadowCastingMode;
            mr.receiveShadows = original.receiveShadows;
            mr.lightProbeUsage = original.lightProbeUsage;
            mr.reflectionProbeUsage = original.reflectionProbeUsage;
            mr.renderingLayerMask = original.renderingLayerMask;
            mr.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            // Its tail beat follows the body: idling, cruising, sprinting (school fish beat on the clock).
            go.AddComponent<SpeciesSwimDriver>().Init(mr, body, sp.swimMesh);
        }

        // The body's original skinned mesh, found even while it's switched off.
        static SkinnedMeshRenderer FindOriginal(Transform body)
        {
            foreach (var smr in body.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.name != "AllyGlow") return smr;
            return null;
        }

        // A skinned mesh's bounds in another transform's space.
        static Bounds LocalBounds(SkinnedMeshRenderer r, Transform space)
        {
            Transform from = r.rootBone != null ? r.rootBone : r.transform;
            Bounds lb = r.localBounds;
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            for (int i = 0; i < 8; i++)
            {
                var corner = lb.center + Vector3.Scale(lb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = space.InverseTransformPoint(from.TransformPoint(corner));
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            // In the visual root's space, but measured along the body's axes.
            var b = new Bounds((min + max) * 0.5f, max - min);
            Vector3 fwd = space.InverseTransformDirection(space.parent != null ? space.parent.forward : Vector3.forward);
            // The original fish lies along whichever local axis the body's forward maps to.
            float along = Mathf.Abs(fwd.x) > Mathf.Abs(fwd.y) && Mathf.Abs(fwd.x) > Mathf.Abs(fwd.z) ? b.size.x
                        : Mathf.Abs(fwd.y) > Mathf.Abs(fwd.z) ? b.size.y : b.size.z;
            b.size = new Vector3(b.size.x, b.size.y, along);
            return b;
        }
    }
}
