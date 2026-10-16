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
    ///
    /// When a ghost reveals the shark (<see cref="FishPlayer.GhostRevealed"/>), the fish still in the water see
    /// it the same way for a few seconds, with a red marker and its distance (held at the screen edge when
    /// it's off to the side). The shark is told it's been seen; the ghosts that it worked.
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
        // Sharks a ghost revealed, until when (Time.time).
        readonly Dictionary<FishPlayer, float> _revealedSharks = new Dictionary<FishPlayer, float>();
        readonly List<FishPlayer> _glowing = new List<FishPlayer>();   // teammates + revealed sharks
        static readonly Color SharkMark = new Color(1f, 0.36f, 0.42f);
        GUIStyle _label;

        void OnEnable() => FishPlayer.GhostRevealed += OnGhostRevealed;
        void OnDisable() => FishPlayer.GhostRevealed -= OnGhostRevealed;

        void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
            Marked.Clear();
        }

        void OnGhostRevealed(FishPlayer shark, float seconds, string by)
        {
            var me = Local();
            if (me == null || shark == null) return;
            if (GameAudio.Lib != null) GameAudio.Play(GameAudio.Lib.ghostHint, 0.8f);
            if (me == shark) { MatchHud.Flash("A ghost revealed you to the fish!", 2.5f); return; }
            if (me.IsOut) { MatchHud.Flash(me.CanGhostHint ? $"{by} showed the shark to the fish" : "The fish can see the shark", 2.5f); return; }
            if (me.IsSharkBody) return;
            _revealedSharks[shark] = Time.time + seconds;
            MatchHud.Flash($"A ghost shows you the shark! ({by})", 2.5f);
        }

        void Update()
        {
            var me = Local();
            bool fishView = me != null && !me.IsSharkBody;

            Marked.Clear();
            if (fishView)
                foreach (var fp in FishPlayer.All)
                    if (IsTeammate(fp, me)) Marked.Add(fp);

            // A shark a ghost revealed glows like a teammate while it lasts (only to fish still in the water).
            _drop.Clear();
            foreach (var kv in _revealedSharks)
                if (kv.Key == null || Time.time > kv.Value || !fishView || me.IsOut) _drop.Add(kv.Key);
            foreach (var shark in _drop) _revealedSharks.Remove(shark);
            _glowing.Clear();
            _glowing.AddRange(Marked);
            _glowing.AddRange(_revealedSharks.Keys);

            // Glows on, for new teammates; off, for any that left the water (or if we're the shark now).
            var lib = WaterFxLibrary.Get();
            if (lib != null && lib.allyGlow != null)
                foreach (var fp in _glowing)
                    if (!_glows.ContainsKey(fp)) _glows[fp] = AddGlow(fp, lib.allyGlow);

            _drop.Clear();
            foreach (var kv in _glows)
                if (kv.Key == null || !_glowing.Contains(kv.Key)) _drop.Add(kv.Key);
            foreach (var fp in _drop)
            {
                foreach (var go in _glows[fp]) if (go != null) Destroy(go);
                _glows.Remove(fp);
            }
        }

        void OnGUI()
        {
            if (_revealedSharks.Count == 0) return;
            var cam = WaterFx.ViewCamera; // Camera.main is null for a networked player (its camera isn't tagged)
            var me = Local();
            if (cam == null || me == null) return;
            if (_label == null)
                _label = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontSize = 14, fontStyle = FontStyle.Bold };
            _label.normal.textColor = SharkMark;
            float w = Screen.width, h = Screen.height, inset = Mathf.Min(w, h) * 0.06f;
            foreach (var shark in _revealedSharks.Keys)
            {
                if (shark == null) continue;
                Vector3 sp = cam.WorldToScreenPoint(shark.transform.position);
                string text = $"SHARK  {Vector3.Distance(me.transform.position, shark.transform.position):0} m";
                bool onScreen = sp.z > 0f && sp.x > inset && sp.x < w - inset && sp.y > inset && sp.y < h - inset;
                if (onScreen)
                {
                    var at = new Vector2(sp.x, h - sp.y);
                    float size = Mathf.Clamp(900f / sp.z, 40f, 140f);
                    SharkTargeting.DrawBrackets(at, size, SharkMark);
                    GUI.Label(new Rect(at.x - 70f, at.y + size * 0.5f + 2f, 140f, 22f), text, _label);
                }
                else
                {
                    var at = SharkTargeting.EdgePoint(sp, inset);
                    SharkTargeting.DrawDiamond(at, 18f, SharkMark);
                    GUI.Label(new Rect(at.x - 70f, at.y + 12f, 140f, 22f), text, _label);
                }
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
