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
    ///
    /// Once the exit opens (MatchManager.FishRevealed) every player fish still in the water is revealed:
    /// tinted and marked on screen at any distance, through rocks and fog, with arrows at the screen edge
    /// for those off to the side or behind - and any of them can be locked, however far away.
    ///
    /// The shark also hears fish talk (<see cref="ProximityVoice.SharkHeard"/>): a ripple spreads where
    /// the voice came from - a tight one on a fish close by, a wide one over the rough area of a far one,
    /// held at the screen edge when it's off to the side. A fish heard close by is tinted and can be
    /// locked for <see cref="heardSeconds"/>, through rocks and fog.
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

        [Header("Exit open: every player fish revealed")]
        [Tooltip("Tint and marker colour of the player fish once the exit opens.")]
        [SerializeField] Color revealColor = new Color(1f, 0.2f, 0.25f);

        [Header("Hearing fish talk")]
        [Tooltip("Colour of the ripples where a voice came from, and the tint on a fish heard close by.")]
        [SerializeField] Color heardColor = new Color(0.65f, 0.92f, 1f);
        [Tooltip("How long a fish heard close by stays tinted and lockable (seconds).")]
        [SerializeField] float heardSeconds = 2f;
        [Tooltip("How long one ripple lasts on screen (seconds).")]
        [SerializeField] float rippleSeconds = 1.6f;

        [Header("Fish finished a task")]
        [Tooltip("Colour of the marker where the fish just finished a task.")]
        [SerializeField] Color taskColor = new Color(1f, 0.72f, 0.3f);
        [Tooltip("How long that marker stays up (seconds).")]
        [SerializeField] float taskMarkerSeconds = 12f;

        /// <summary>The fish the shark is focused on (or null). SharkAbilities reads this.</summary>
        public GameObject LockedTarget { get; private set; }

        InputAction _lockAction, _cycleAction;
        Collider[] _body;   // the shark's own colliders, so fish hidden behind its body aren't picked
        readonly Highlight _fx = new Highlight();
        readonly List<GameObject> _candidates = new List<GameObject>();
        readonly HashSet<GameObject> _seen = new HashSet<GameObject>();
        readonly List<Pick> _visible = new List<Pick>();
        // Player fish revealed by the open exit (tinted with revealColor while they are).
        readonly HashSet<GameObject> _revealed = new HashSet<GameObject>();
        readonly List<GameObject> _revealScratch = new List<GameObject>();
        GUIStyle _revealStyle;
        string _flash;
        float _flashUntil;
        GUIStyle _flashStyle;
        // Fish heard talking close by: tinted and lockable until the time stored (Time.time).
        readonly Dictionary<GameObject, float> _heard = new Dictionary<GameObject, float>();
        readonly List<GameObject> _dropScratch = new List<GameObject>();   // fish losing a reveal or a hearing
        // Ripples where voices came from, newest last.
        readonly List<Ripple> _ripples = new List<Ripple>();
        static Texture2D s_ring;

        struct Ripple { public Vector3 pos; public float error; public float born; }

        // How fast each player fish is moving, measured here from frame to frame (remote bodies have no
        // velocity of their own), for the sonar.
        readonly Dictionary<FishPlayer, Motion> _motion = new Dictionary<FishPlayer, Motion>();
        readonly List<FishPlayer> _motionDrop = new List<FishPlayer>();
        struct Motion { public Vector3 last; public float speed; }

        // Where the fish finished tasks lately, until when (Time.time).
        readonly List<TaskMark> _tasks = new List<TaskMark>();
        struct TaskMark { public Vector3 pos; public string what; public float until; }

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

        void OnEnable()
        {
            _lockAction.Enable();
            _cycleAction.Enable();
            ProximityVoice.SharkHeard += OnHeard;
            MatchManager.TaskDone += OnTaskDone;
        }

        void OnDisable()
        {
            _lockAction.Disable();
            _cycleAction.Disable();
            ProximityVoice.SharkHeard -= OnHeard;
            MatchManager.TaskDone -= OnTaskDone;
            ClearLock();
            foreach (var f in _revealed) _fx.Remove(f);
            _revealed.Clear();
            foreach (var f in _heard.Keys) _fx.Remove(f);
            _heard.Clear();
            _ripples.Clear();
            _tasks.Clear();
        }

        void Update()
        {
            RefreshRevealed();
            RefreshHeard();
            TrackMotion();

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
            else Flash("Fish is too far");
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
                bool revealed = _revealed.Contains(f) || _heard.ContainsKey(f); // any distance, seen through rocks and fog
                if (!revealed && (p - shark).sqrMagnitude > range2) continue;
                // The camera sits behind the shark: fish between the two are behind you, not targets.
                if (Vector3.Dot(p - shark, look) <= 0f) continue;

                Vector3 vp = cam.WorldToViewportPoint(p);
                if (vp.z <= 0f || vp.x < 0f || vp.x > 1f || vp.y < 0f || vp.y > 1f) continue; // behind / off screen

                // Distance from the centre in screen-height units, so the pick area is round.
                float dx = (vp.x - 0.5f) * aspect, dy = vp.y - 0.5f;
                float sd = Mathf.Sqrt(dx * dx + dy * dy);
                if (requireNearCentre && sd > pickRadius) continue;

                if (!revealed && occluders != 0 && Physics.Linecast(eye, p, occluders, QueryTriggerInteraction.Ignore)) continue;
                // Deep in a sand cloud or a red tide, a fish can't be picked out from afar.
                if (!revealed && ReefConditions.Hides(p, Vector3.Distance(eye, p))) continue;
                if (!revealed && HiddenBehindBody(eye, p)) continue;
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

            foreach (var e in FindObjectsByType<Edible>())
                if (e != null && !e.IsEaten && e.gameObject != gameObject && _seen.Add(e.gameObject))
                    _candidates.Add(e.gameObject);
        }

        // A player fish that escaped or died is gone from the reef (its body stays, hidden).
        static bool OutOfRound(GameObject f) => f.TryGetComponent(out FishPlayer fp) && (fp.IsOut || fp.IsBeingEaten);

        bool StillValid(GameObject f)
        {
            if (f == null || !f.activeInHierarchy) return false;
            if (f.TryGetComponent(out Edible e) && e.IsEaten) return false;
            if (OutOfRound(f)) return false;
            if (_revealed.Contains(f)) return true; // no running out of range once revealed
            float grace = maxRange * 1.5f; // some slack so a fleeing fish isn't dropped instantly
            return (f.transform.position - transform.position).sqrMagnitude <= grace * grace;
        }

        // ---- lock + highlight ----
        void SetLock(GameObject fish)
        {
            if (fish == LockedTarget) return;
            ClearLock();
            LockedTarget = fish;
            _fx.Remove(fish); // a revealed fish swaps its reveal tint for the lock's
            _fx.Apply(fish, glowColor, tintStrength, solidTint);
        }

        void ClearLock()
        {
            var was = LockedTarget;
            LockedTarget = null;
            if (was == null) return;
            Retint(was);
        }

        /// <summary>Give a fish the tint it should have now: the lock's, the open exit's, a heard fish's, or none.</summary>
        void Retint(GameObject f)
        {
            if (f == null) return;
            _fx.Remove(f);
            if (f == LockedTarget) _fx.Apply(f, glowColor, tintStrength, solidTint);
            else if (_revealed.Contains(f)) _fx.Apply(f, revealColor, tintStrength, solidTint);
            else if (_heard.ContainsKey(f)) _fx.Apply(f, heardColor, tintStrength, solidTint);
        }

        // ---- hearing ----
        void OnHeard(Vector3 heardAt, float error, GameObject fish) => Reveal(heardAt, error, fish, heardSeconds);

        // A ripple at heardAt; a named fish is also tinted and lockable for `seconds`.
        void Reveal(Vector3 at, float error, GameObject fish, float seconds)
        {
            _ripples.Add(new Ripple { pos = at, error = error, born = Time.time });
            if (_ripples.Count > 16) _ripples.RemoveAt(0);
            if (fish == null || fish == gameObject) return;
            bool fresh = !_heard.ContainsKey(fish);
            float until = Time.time + seconds;
            if (fresh || _heard[fish] < until) _heard[fish] = until;
            if (fresh) Retint(fish);
        }

        // ---- sonar ----
        /// <summary>The shark's pulse: every player fish within <paramref name="radius"/> of <paramref name="from"/>
        /// moving faster than <paramref name="movingSpeed"/> is revealed (a ripple on it, tinted and lockable) for
        /// <paramref name="seconds"/>. A fish keeping still stays hidden. Returns how many were caught.</summary>
        public int SonarSweep(Vector3 from, float radius, float movingSpeed, float seconds)
        {
            // The pulse itself: one wide ring spreading from the shark.
            _ripples.Add(new Ripple { pos = from, error = radius * 0.5f, born = Time.time });
            int found = 0;
            foreach (var kv in _motion)
            {
                var fp = kv.Key;
                if (fp == null || !IsHuntable(fp)) continue;
                if ((fp.transform.position - from).sqrMagnitude > radius * radius) continue;
                if (kv.Value.speed < movingSpeed) continue;
                Reveal(fp.transform.position, 0f, fp.gameObject, seconds);
                found++;
            }
            Flash(found == 0 ? "Sonar: nothing moving" : found == 1 ? "Sonar: 1 fish moving!" : $"Sonar: {found} fish moving!");
            return found;
        }

        bool IsHuntable(FishPlayer fp) =>
            fp.gameObject != gameObject && fp.isActiveAndEnabled && !fp.IsSharkBody && !fp.IsOut && !fp.IsBeingEaten;

        void TrackMotion()
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            foreach (var fp in FishPlayer.All)
            {
                if (fp == null || !IsHuntable(fp)) continue;
                Vector3 p = fp.transform.position;
                if (!_motion.TryGetValue(fp, out var m)) { _motion[fp] = new Motion { last = p }; continue; }
                // Smoothed over ~0.3 s, so the jitter of network updates doesn't read as swimming.
                float now = Vector3.Distance(p, m.last) / dt;
                m.speed = Mathf.Lerp(m.speed, now, 1f - Mathf.Exp(-dt / 0.3f));
                m.last = p;
                _motion[fp] = m;
            }
            _motionDrop.Clear();
            foreach (var fp in _motion.Keys) if (fp == null || !IsHuntable(fp)) _motionDrop.Add(fp);
            foreach (var fp in _motionDrop) _motion.Remove(fp);
        }

        // ---- the fish finished a task: show the shark where ----
        void OnTaskDone(Vector3 where, string what)
        {
            _tasks.Add(new TaskMark { pos = where, what = what, until = Time.time + taskMarkerSeconds });
            Flash($"The fish finished a task: {what}!");
        }

        // Heard fish stop being lockable once the time runs out (or they leave the water).
        void RefreshHeard()
        {
            _ripples.RemoveAll(r => Time.time - r.born > rippleSeconds);
            _tasks.RemoveAll(t => Time.time > t.until);
            if (_heard.Count == 0) return;
            _dropScratch.Clear();
            foreach (var kv in _heard)
                if (kv.Key == null || Time.time > kv.Value || !kv.Key.activeInHierarchy || OutOfRound(kv.Key))
                    _dropScratch.Add(kv.Key);
            foreach (var f in _dropScratch)
            {
                _heard.Remove(f);
                Retint(f);
            }
        }

        // Once the exit opens, every player fish still in the water is revealed to the shark.
        void RefreshRevealed()
        {
            _revealScratch.Clear();
            var match = MatchManager.Instance;
            if (match != null && match.FishRevealed)
                foreach (var fp in FishPlayer.All)
                    if (fp != null && fp.gameObject != gameObject && fp.gameObject.activeInHierarchy && !fp.IsSharkBody
                        && !fp.IsOut && !fp.IsBeingEaten)
                        _revealScratch.Add(fp.gameObject);

            _dropScratch.Clear();
            _revealed.RemoveWhere(f =>
            {
                if (f != null && _revealScratch.Contains(f)) return false;
                _dropScratch.Add(f);
                return true;
            });
            foreach (var f in _dropScratch) Retint(f);
            foreach (var f in _revealScratch)
                if (_revealed.Add(f)) Retint(f);
        }

        // ---- on-screen feedback ----
        void Flash(string msg) => MatchHud.Flash(msg);   // shown by the HUD

        void OnGUI()
        {
            var cam = Cam;
            if (cam != null && _ripples.Count > 0) DrawRipples(cam);
            if (cam != null && _tasks.Count > 0) DrawTasks(cam);
            if (cam != null && _revealed.Count > 0) DrawRevealed(cam);
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
        }

        // A small bracket and distance on every revealed fish; those off screen or behind get a marker held
        // at the edge of the screen, in their direction.
        void DrawRevealed(Camera cam)
        {
            if (_revealStyle == null)
                _revealStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontSize = 13, fontStyle = FontStyle.Bold };
            _revealStyle.normal.textColor = revealColor;
            float w = Screen.width, h = Screen.height, inset = Mathf.Min(w, h) * 0.06f;

            foreach (var f in _revealed)
            {
                if (f == null || f == LockedTarget) continue;
                Vector3 pos = f.transform.position;
                Vector3 sp = cam.WorldToScreenPoint(pos);
                string metres = $"{Vector3.Distance(transform.position, pos):0} m";
                bool onScreen = sp.z > 0f && sp.x > inset && sp.x < w - inset && sp.y > inset && sp.y < h - inset;
                if (onScreen)
                {
                    var gui = new Vector2(sp.x, h - sp.y);
                    float size = Mathf.Clamp(500f / sp.z, 20f, 70f);
                    DrawBrackets(gui, size, revealColor);
                    GUI.Label(new Rect(gui.x - 50f, gui.y + size * 0.5f + 2f, 100f, 20f), metres, _revealStyle);
                    continue;
                }

                var at = EdgePoint(sp, inset);
                DrawDiamond(at, 14f, revealColor);
                GUI.Label(new Rect(at.x - 50f, at.y + 10f, 100f, 20f), metres, _revealStyle);
            }
        }

        // A pulsing diamond where each task was finished, with what it was and how far; held at the screen edge
        // when it's off to the side or behind.
        void DrawTasks(Camera cam)
        {
            if (_revealStyle == null)
                _revealStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter, fontSize = 13, fontStyle = FontStyle.Bold };
            var prevText = _revealStyle.normal.textColor;
            _revealStyle.normal.textColor = taskColor;
            float w = Screen.width, h = Screen.height, inset = Mathf.Min(w, h) * 0.06f;
            float pulse = 1f + 0.25f * Mathf.Sin(Time.time * 6f);
            foreach (var t in _tasks)
            {
                Vector3 sp = cam.WorldToScreenPoint(t.pos);
                bool onScreen = sp.z > 0f && sp.x > inset && sp.x < w - inset && sp.y > inset && sp.y < h - inset;
                Vector2 at = onScreen ? new Vector2(sp.x, h - sp.y) : EdgePoint(sp, inset);
                DrawDiamond(at, 16f * pulse, taskColor);
                string label = $"{t.what.ToUpperInvariant()}  {Vector3.Distance(transform.position, t.pos):0} m";
                GUI.Label(new Rect(at.x - 90f, at.y + 12f, 180f, 20f), label, _revealStyle);
            }
            _revealStyle.normal.textColor = prevText;
        }

        /// <summary>For a point off screen or behind: slide out from the centre toward it until the edge
        /// (GUI coordinates, y down).</summary>
        internal static Vector2 EdgePoint(Vector3 sp, float inset)
        {
            float w = Screen.width, h = Screen.height;
            var centre = new Vector2(w * 0.5f, h * 0.5f);
            Vector2 d = new Vector2(sp.x, sp.y) - centre;
            if (sp.z < 0f) d = -d;
            if (d.sqrMagnitude < 1f) d = Vector2.down;
            float k = Mathf.Min((centre.x - inset) / Mathf.Max(Mathf.Abs(d.x), 1e-3f),
                                (centre.y - inset) / Mathf.Max(Mathf.Abs(d.y), 1e-3f));
            Vector2 edge = centre + d * k;
            return new Vector2(edge.x, h - edge.y);
        }

        // A voice: rings spreading from where it came from, as wide as the guess is rough - a tight pulse on
        // a fish close by, a broad one over the area a far one is somewhere in. Off screen, a smaller pulse
        // held at the edge, in its direction.
        void DrawRipples(Camera cam)
        {
            var ring = RingTexture();
            float h = Screen.height, inset = Mathf.Min(Screen.width, h) * 0.06f;
            float pxPerMetreAt1 = h / (2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
            var prev = GUI.color;
            foreach (var r in _ripples)
            {
                float age = Mathf.Clamp01((Time.time - r.born) / Mathf.Max(0.05f, rippleSeconds));
                Vector3 sp = cam.WorldToScreenPoint(r.pos);
                bool onScreen = sp.z > 0f && sp.x > inset && sp.x < Screen.width - inset && sp.y > inset && sp.y < h - inset;
                Vector2 c;
                float radius;
                if (onScreen)
                {
                    c = new Vector2(sp.x, h - sp.y);
                    radius = Mathf.Clamp(Mathf.Max(r.error, 1.2f) * pxPerMetreAt1 / sp.z, 22f, h * 0.45f);
                }
                else
                {
                    c = EdgePoint(sp, inset);
                    radius = 26f;
                }

                // The rough area, faint and steady, so a far voice reads as "somewhere in here".
                if (onScreen && r.error > 1.5f)
                {
                    GUI.color = new Color(heardColor.r, heardColor.g, heardColor.b, 0.22f * (1f - age));
                    GUI.DrawTexture(new Rect(c.x - radius, c.y - radius, radius * 2f, radius * 2f), ring);
                }
                // Two rings spreading out, the second a beat behind the first.
                for (int i = 0; i < 2; i++)
                {
                    float t = Mathf.Clamp01((age - i * 0.25f) / 0.75f);
                    if (t <= 0f) continue;
                    float s = radius * Mathf.Lerp(0.15f, 1f, 1f - (1f - t) * (1f - t));
                    GUI.color = new Color(heardColor.r, heardColor.g, heardColor.b, 0.9f * (1f - t));
                    GUI.DrawTexture(new Rect(c.x - s, c.y - s, s * 2f, s * 2f), ring);
                }
            }
            GUI.color = prev;
        }

        // A thin white ring with soft edges, made once.
        static Texture2D RingTexture()
        {
            if (s_ring != null) return s_ring;
            const int size = 128;
            const float outer = size * 0.5f - 1f, width = 2.5f;
            s_ring = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size * 0.5f, size * 0.5f));
                    float a = Mathf.Clamp01(1f - Mathf.Abs(d - (outer - width)) / width);
                    px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            s_ring.SetPixels32(px);
            s_ring.Apply();
            return s_ring;
        }

        internal static void DrawDiamond(Vector2 c, float size, Color col)
        {
            var prevColor = GUI.color;
            var prevMatrix = GUI.matrix;
            GUI.color = col;
            GUIUtility.RotateAroundPivot(45f, c);
            GUI.DrawTexture(new Rect(c.x - size * 0.5f, c.y - size * 0.5f, size, size), Texture2D.whiteTexture);
            GUI.matrix = prevMatrix;
            GUI.color = prevColor;
        }

        internal static void DrawBrackets(Vector2 c, float size, Color col)
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
