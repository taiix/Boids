using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishGame
{
    /// <summary>
    /// Fish players can always find each other: on a fish player's own screen every other fish still in the
    /// water gets a soft glow drawn over its body that shows through fog and rocks (RoundScreen adds a name
    /// tag and distance). The shark's screen never gets any of it, and nothing is sent over the network.
    ///
    /// The glow is a second copy of each teammate's mesh, skinned to the same bones so it swims along, drawn
    /// with Resources/WaterFxLibrary's allyGlow material (additive, no depth test, no fog).
    /// </summary>
    public class FishAllies : MonoBehaviour
    {
        static FishAllies s_instance;

        /// <summary>Teammates currently marked for the local fish (empty for the shark).</summary>
        public static readonly List<FishPlayer> Marked = new List<FishPlayer>();

        /// <summary>Start marking teammates in the current (game) scene, once.</summary>
        public static void Show()
        {
            if (s_instance != null) return;
            s_instance = new GameObject("FishAllies").AddComponent<FishAllies>();
        }

        readonly Dictionary<FishPlayer, List<GameObject>> _glows = new Dictionary<FishPlayer, List<GameObject>>();
        readonly List<FishPlayer> _drop = new List<FishPlayer>();

        void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
            Marked.Clear();
        }

        void Update()
        {
            var me = Local();
            bool fishView = me != null && !me.IsSharkBody;

            Marked.Clear();
            if (fishView)
                foreach (var fp in FishPlayer.All)
                    if (IsTeammate(fp, me)) Marked.Add(fp);

            // Glows on, for new teammates; off, for any that left the water (or if we're the shark now).
            var lib = WaterFxLibrary.Get();
            if (lib != null && lib.allyGlow != null)
                foreach (var fp in Marked)
                    if (!_glows.ContainsKey(fp)) _glows[fp] = AddGlow(fp, lib.allyGlow);

            _drop.Clear();
            foreach (var kv in _glows)
                if (kv.Key == null || !Marked.Contains(kv.Key)) _drop.Add(kv.Key);
            foreach (var fp in _drop)
            {
                foreach (var go in _glows[fp]) if (go != null) Destroy(go);
                _glows.Remove(fp);
            }
        }

        static bool IsTeammate(FishPlayer fp, FishPlayer me) =>
            fp != null && fp != me && fp.isActiveAndEnabled && !fp.IsSharkBody && !fp.IsOut;

        static FishPlayer Local()
        {
            if (!NetworkClient.active) return null;
            return NetworkClient.localPlayer != null ? NetworkClient.localPlayer.GetComponent<FishPlayer>() : null;
        }

        // A copy of each of the fish's meshes in the glow material, riding the same transforms / bones.
        static List<GameObject> AddGlow(FishPlayer fish, Material glow)
        {
            var made = new List<GameObject>();
            foreach (var r in fish.GetComponentsInChildren<Renderer>())
            {
                if (r.gameObject.name == "AllyGlow") continue;
                if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null)
                {
                    var go = new GameObject("AllyGlow");
                    go.transform.SetParent(smr.transform, false);
                    var copy = go.AddComponent<SkinnedMeshRenderer>();
                    copy.sharedMesh = smr.sharedMesh;
                    copy.bones = smr.bones;
                    copy.rootBone = smr.rootBone;
                    copy.localBounds = smr.localBounds;
                    copy.updateWhenOffscreen = true; // far-off teammates are exactly the ones we want
                    copy.sharedMaterials = Repeat(glow, smr.sharedMesh.subMeshCount);
                    Quiet(copy);
                    made.Add(go);
                }
                else if (r is MeshRenderer mr && mr.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null)
                {
                    var go = new GameObject("AllyGlow");
                    go.transform.SetParent(mr.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                    var copy = go.AddComponent<MeshRenderer>();
                    copy.sharedMaterials = Repeat(glow, mf.sharedMesh.subMeshCount);
                    Quiet(copy);
                    made.Add(go);
                }
            }
            return made;
        }

        static Material[] Repeat(Material m, int n)
        {
            var a = new Material[Mathf.Max(1, n)];
            for (int i = 0; i < a.Length; i++) a[i] = m;
            return a;
        }

        static void Quiet(Renderer r)
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
    }
}
