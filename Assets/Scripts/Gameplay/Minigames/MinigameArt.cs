using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// Materials the code-built minigame stations borrow from the trash set (HDRP needs real materials).
    /// One asset at Resources/Minigames/MinigameArt. Swap any of them in its Inspector.
    /// </summary>
    [CreateAssetMenu(menuName = "Reef Run/Minigame Art", fileName = "MinigameArt")]
    public class MinigameArt : ScriptableObject
    {
        [Tooltip("Oil drum body.")] public Material rust;
        [Tooltip("Drum bands / buoy.")] public Material buoy;
        [Tooltip("Second buoy colour, and the coral (white, so its pink tint shows true).")] public Material buoyWhite;
        [Tooltip("Fishing line and net.")] public Material rope;
        [Tooltip("The oil (tinted black).")] public Material plastic;
        [Tooltip("Algae on bleached coral (tinted green-brown in code).")] public Material junk;

        [Header("Props (made in Blender: Art/MinigameProps.blend)")]
        [Tooltip("A rusty 55-gallon oil drum, pivot at its base.")] public GameObject drum;
        [Tooltip("Fishing line wound over a rock ~1.4 x 0.9 m, pivot on the sand.")] public GameObject tangle;
        [Tooltip("The red and white fishing float caught in it.")] public GameObject floatProp;
        [Tooltip("The rock the coral grows on and the line snags on.")] public GameObject rock;
        [Tooltip("A low, flat rock the coral garden grows on.")] public GameObject flatRock;
        [Tooltip("Corals for the scrub station (Coral Reef Decor Pack).")] public GameObject[] corals;

        /// <summary>A copy of a prop (no colliders), or null if it isn't assigned.</summary>
        public static Transform Spawn(GameObject prefab, Transform parent, Vector3 localPos, Quaternion localRot, float scale)
        {
            if (prefab == null) return null;
            var go = Instantiate(prefab, parent, false);
            foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = prefab.transform.localScale * scale;   // on top of the model's own scale
            return go.transform;
        }

        /// <summary>Move a spawned prop so it's centred on its parent's origin, with its top at
        /// <paramref name="topY"/> (local metres) - for rocks whose pivots sit anywhere.</summary>
        public static void FitTop(Transform t, float topY)
        {
            if (t == null || t.parent == null) return;
            var rs = t.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            Vector3 top = t.parent.InverseTransformPoint(new Vector3(b.center.x, b.max.y, b.center.z));
            t.localPosition += new Vector3(-top.x, topY - top.y, -top.z);
        }

        static MinigameArt s_art;
        public static MinigameArt Get()
        {
            if (s_art == null) s_art = Resources.Load<MinigameArt>("Minigames/MinigameArt");
            if (s_art == null) s_art = CreateInstance<MinigameArt>();   // missing asset: grey defaults, still playable
            return s_art;
        }

        static MaterialPropertyBlock s_block;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>Tint one renderer without touching the shared material.</summary>
        public static void Tint(Renderer r, Color c)
        {
            if (r == null) return;
            s_block ??= new MaterialPropertyBlock();
            r.GetPropertyBlock(s_block);
            s_block.SetColor(BaseColorId, c);
            s_block.SetColor(ColorId, c);
            r.SetPropertyBlock(s_block);
        }
    }
}
