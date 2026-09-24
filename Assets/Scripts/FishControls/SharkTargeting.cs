using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FishGame
{
    /// <summary>
    /// Client-side shark "focus" system. Press <b>Lock</b> (F) to focus the fish nearest the middle of
    /// your screen; press <b>Cycle</b> (Tab) to step through the other fish in view; press Lock again to
    /// drop it. The focused fish is tinted and bracketed on screen, and <see cref="SharkAbilities"/>
    /// bites THAT fish specifically - even if others are closer to the mouth. Purely local (the flock is
    /// client-side), so it works offline and in multiplayer without any networking.
    ///
    /// Picking is done in screen space rather than with a physics raycast: flock fish have no colliders,
    /// and a fish-sized ray target would be near impossible to hit anyway.
    /// </summary>
    public class SharkTargeting : MonoBehaviour
    {
        [Header("Aim / range")]
        [Tooltip("Camera whose view defines 'what you're looking at'. Auto-found if empty.")]
        [SerializeField] Camera aimCam;
        [Tooltip("Fish farther than this from the SHARK can't be focused.")]
        [SerializeField] float maxRange = 30f;
        [Tooltip("How far from the centre of the screen a fish may be and still get picked by Lock, " +
                 "as a fraction of the screen height.")]
        [Range(0.02f, 0.5f)][SerializeField] float pickRadius = 0.25f;
        [Tooltip("Layers that block line of sight (the seabed). Fish hidden behind them are skipped.")]
        [SerializeField] LayerMask occluders = 1 << 3; // Terrain

        [Header("Highlight")]
        [SerializeField] Color glowColor = new Color(1f, 0.35f, 0.15f);
        [Tooltip("Brightness of the tint on the locked fish. Above 1 glows through bloom.")]
        [SerializeField] float tintStrength = 3f;
        [Tooltip("Paint the locked fish a solid glow colour. Off = only tint its texture, which on the " +
                 "dark-blue flock fish comes out too dark to read.")]
        [SerializeField] bool solidTint = true;
        [Tooltip("Draw corner brackets around the locked fish on screen.")]
        [SerializeField] bool showMarker = true;

        /// <summary>The fish the shark is focused on (or null). SharkAbilities reads this.</summary>
        public GameObject LockedTarget { get; private set; }

        InputAction _lockAction, _cycleAction;
        Collider[] _body;   // the shark's own colliders, so fish hidden behind its body aren't picked
        readonly Highlight _fx = new Highlight();
        readonly List<GameObject> _candidates = new List<GameObject>();
        readonly HashSet<GameObject> _seen = new HashSet<GameObject>();
        readonly List<Pick> _visible = new List<Pick>();
        string _flash;
        float _flashUntil;
        GUIStyle _flashStyle;

        struct Pick { public GameObject go; public float screenDist; }

        // The camera is detached from the body on spawn and can be destroyed with the player, so fall
        // back to whatever is rendering.
        Camera Cam => aimCam != null ? aimCam : Camera.main;

        void Awake()
        {
            if (aimCam == null) aimCam = GetComponentInChildren<Camera>(true);
            _body = GetComponents<Collider>();

            _lockAction = new InputAction("LockTarget", InputActionType.Button);
            _lockAction.AddBinding("<Keyboard>/f");
            _lockAction.AddBinding("<Gamepad>/buttonEast");

            _cycleAction = new InputAction("CycleTarget", InputActionType.Button);
            _cycleAction.AddBinding("<Keyboard>/tab");
            _cycleAction.AddBinding("<Gamepad>/rightShoulder");
        }

        void OnEnable() { _lockAction.Enable(); _cycleAction.Enable(); }
        void OnDisable() { _lockAction.Disable(); _cycleAction.Disable(); ClearLock(); }

        void Update()
        {
            // Drop the lock if the fish was eaten, destroyed, or got away.
            if (LockedTarget != null && !StillValid(LockedTarget)) ClearLock();

            if (_lockAction.WasPressedThisFrame())
            {
                if (LockedTarget != null) ClearLock();
                else LockNearestToCentre();
            }

            if (_cycleAction.WasPressedThisFrame())
            {
                if (LockedTarget != null) CycleTarget();
                else LockNearestToCentre();
            }
        }

        // ---- selection ----
        void LockNearestToCentre()
        {
            GatherVisible(requireNearCentre: true);
            if (_visible.Count > 0) SetLock(_visible[0].go);
            else Flash("No fish in view");
        }

        void CycleTarget()
        {
            GatherVisible(requireNearCentre: false);
            if (_visible.Count == 0) return;
            int i = _visible.FindIndex(p => p.go == LockedTarget);
            SetLock(_visible[(i + 1) % _visible.Count].go); // i == -1 (lock left view) -> nearest to centre
        }

        /// <summary>Fish in range, on screen and in line of sight, nearest the screen centre first.
        /// With <paramref name="requireNearCentre"/>, only those within <see cref="pickRadius"/>.</summary>
        void GatherVisible(bool requireNearCentre)
        {
            _visible.Clear();
            var cam = Cam;
            if (cam == null) return;
            GatherCandidates();

            Vector3 shark = transform.position, eye = cam.transform.position, look = cam.transform.forward;
            float range2 = maxRange * maxRange, aspect = cam.aspect;
            foreach (var f in _candidates)
            {
                Vector3 p = f.transform.position;
                if ((p - shark).sqrMagnitude > range2) continue;
                // The camera sits behind the shark: fish between the two are behind you, not targets.
                if (Vector3.Dot(p - shark, look) <= 0f) continue;

                Vector3 vp = cam.WorldToViewportPoint(p);
                if (vp.z <= 0f || vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f) continue; // behind / off screen

                // Distance from the centre in screen-height units, so the pick area is round.
                float dx = (vp.x - 0.5f) * aspect, dy = vp.y - 0.5f;
                float sd = Mathf.Sqrt(dx * dx + dy * dy);
                if (requireNearCentre && sd > pickRadius) continue;

                if (occluders != 0 && Physics.Linecast(eye, p, occluders, QueryTriggerInteraction.Ignore)) continue;
                if (HiddenBehindBody(eye, p)) continue;
                _visible.Add(new Pick { go = f, screenDist = sd });
            }
            _visible.Sort((a, b) => a.screenDist.CompareTo(b.screenDist));
        }

        // The shark fills the middle of the screen, so a fish "nearest the centre" is often right
        // behind its body. Only checks our own colliders (Collider.Raycast), not the whole scene.
        bool HiddenBehindBody(Vector3 eye, Vector3 p)
        {
            if (_body == null) return false;
            var ray = new Ray(eye, p - eye);
            float dist = Vector3.Distance(eye, p);
            foreach (var c in _body)
                if (c != null && c.enabled && !c.isTrigger && c.Raycast(ray, out _, dist)) return true;
            return false;
        }

        void GatherCandidates()
        {
            _candidates.Clear();
            _seen.Clear();
            // Ambient flock fish (no colliders) — enumerated straight from the schools.
            for (int m = 0; m < BoidsManager.All.Count; m++)
            {
                var mgr = BoidsManager.All[m];
                if (mgr == null || mgr.allFish == null) continue;
                foreach (var f in mgr.allFish) if (f != null && _seen.Add(f)) _candidates.Add(f);
            }
            // Player prey fish (they carry an Edible). Flock fish may carry one too, so _seen keeps
            // each fish single — otherwise Cycle would "advance" onto a duplicate of the same fish.
            foreach (var e in FindObjectsByType<Edible>(FindObjectsSortMode.None))
                if (e != null && !e.IsEaten && e.gameObject != gameObject && _seen.Add(e.gameObject))
                    _candidates.Add(e.gameObject);
        }

        bool StillValid(GameObject f)
        {
            if (f == null || !f.activeInHierarchy) return false;
            if (f.TryGetComponent(out Edible e) && e.IsEaten) return false;
            float grace = maxRange * 1.5f; // some slack so a fleeing fish isn't dropped instantly
            return (f.transform.position - transform.position).sqrMagnitude <= grace * grace;
        }

        // ---- lock + highlight ----
        void SetLock(GameObject fish)
        {
            if (fish == LockedTarget) return;
            if (LockedTarget != null) _fx.Remove(LockedTarget);
            LockedTarget = fish;
            _fx.Apply(fish, glowColor, tintStrength, solidTint);
        }

        void ClearLock()
        {
            if (LockedTarget != null) _fx.Remove(LockedTarget);
            LockedTarget = null;
        }

        // ---- on-screen feedback ----
        void Flash(string msg) { _flash = msg; _flashUntil = Time.unscaledTime + 1.2f; }

        void OnGUI()
        {
            var cam = Cam;
            if (showMarker && LockedTarget != null && cam != null)
            {
                Vector3 sp = cam.WorldToScreenPoint(LockedTarget.transform.position);
                if (sp.z > 0f)
                {
                    // Brackets shrink with distance, but never below a readable size.
                    float size = Mathf.Clamp(600f / sp.z, 28f, 90f);
                    DrawBrackets(new Vector2(sp.x, Screen.height - sp.y), size, glowColor);
                }
            }

            if (_flash != null && Time.unscaledTime < _flashUntil)
            {
                if (_flashStyle == null)
                    _flashStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 18 };
                GUI.Label(new Rect(0f, Screen.height * 0.62f, Screen.width, 30f), _flash, _flashStyle);
            }
        }

        static void DrawBrackets(Vector2 c, float size, Color col)
        {
            float h = size * 0.5f, len = size * 0.3f, t = 2f;
            var tex = Texture2D.whiteTexture;
            var prev = GUI.color;
            GUI.color = col;
            GUI.DrawTexture(new Rect(c.x - h, c.y - h, len, t), tex);            // top-left
            GUI.DrawTexture(new Rect(c.x - h, c.y - h, t, len), tex);
            GUI.DrawTexture(new Rect(c.x + h - len, c.y - h, len, t), tex);      // top-right
            GUI.DrawTexture(new Rect(c.x + h - t, c.y - h, t, len), tex);
            GUI.DrawTexture(new Rect(c.x - h, c.y + h - t, len, t), tex);        // bottom-left
            GUI.DrawTexture(new Rect(c.x - h, c.y + h - len, t, len), tex);
            GUI.DrawTexture(new Rect(c.x + h - len, c.y + h - t, len, t), tex);  // bottom-right
            GUI.DrawTexture(new Rect(c.x + h - t, c.y + h - len, t, len), tex);
            GUI.color = prev;
        }

        // ================================================================= highlight helper
        /// <summary>Paints a fish's renderers in the glow colour via a MaterialPropertyBlock, so no
        /// materials are created or permanently changed. Sets the colour (and, for a solid tint, swaps the
        /// texture for white) on every shader we use - the flock fish are HDRP/Unlit (_UnlitColor), player
        /// fish HDRP/Lit (_BaseColor); properties a shader doesn't have are simply ignored. Emission isn't
        /// used: HDRP scales it by camera exposure, so it's invisible at sensible values underwater.</summary>
        class Highlight
        {
            static readonly int UnlitColor = Shader.PropertyToID("_UnlitColor");
            static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
            static readonly int LegacyColor = Shader.PropertyToID("_Color");
            static readonly int UnlitColorMap = Shader.PropertyToID("_UnlitColorMap");
            static readonly int BaseColorMap = Shader.PropertyToID("_BaseColorMap");
            static readonly int MainTex = Shader.PropertyToID("_MainTex");
            readonly Dictionary<GameObject, Renderer[]> _on = new Dictionary<GameObject, Renderer[]>();

            public void Apply(GameObject fish, Color color, float tint, bool solid)
            {
                if (fish == null || _on.ContainsKey(fish)) return;
                var tinted = new Color(color.r * tint, color.g * tint, color.b * tint, 1f);
                var rends = fish.GetComponentsInChildren<Renderer>();
                var mpb = new MaterialPropertyBlock();
                foreach (var r in rends)
                {
                    if (r == null) continue;
                    r.GetPropertyBlock(mpb);
                    mpb.SetColor(UnlitColor, tinted);
                    mpb.SetColor(BaseColor, tinted);
                    mpb.SetColor(LegacyColor, tinted);
                    if (solid)
                    {
                        // colour = texture x tint, so a white texture lets the tint show at full strength.
                        mpb.SetTexture(UnlitColorMap, Texture2D.whiteTexture);
                        mpb.SetTexture(BaseColorMap, Texture2D.whiteTexture);
                        mpb.SetTexture(MainTex, Texture2D.whiteTexture);
                    }
                    r.SetPropertyBlock(mpb);
                }
                _on[fish] = rends;
            }

            public void Remove(GameObject fish)
            {
                if (fish == null || !_on.TryGetValue(fish, out var rends)) { _on.Remove(fish); return; }
                var empty = new MaterialPropertyBlock();
                foreach (var r in rends) if (r != null) r.SetPropertyBlock(empty);
                _on.Remove(fish);
            }
        }
    }
}
