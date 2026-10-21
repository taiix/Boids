using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace FishGame
{
    /// <summary>
    /// Photo mode for Steam screenshots, in the MarketingShowcase scene (a copy of the island with the
    /// network players and random task placement taken out). Never part of a match.
    ///
    /// Each child <see cref="ShowcaseShot"/> is one screenshot. Activating a shot (Play mode) puts the game
    /// into the state the shot needs, using the game's own code rather than mock-ups:
    ///   * bodies from the real prefabs - the great white, player-size fish, BoidsManager schools (which flee
    ///     the shark through the game's Predator flocking), tangled and free turtles;
    ///   * a catch is the real <see cref="Edible.Devour"/>: blood (BloodInWater), the shark's devour animation
    ///     and its bubbles and shockwaves (SharkEatFx), the lunge's wake (SwimTrail);
    ///   * world events run through the real MatchManager / ReefConditions, offline;
    ///   * the camera sees through <see cref="CreatureVision"/>, so the shark's or the fish's grade applies.
    /// It simulates <see cref="ShowcaseShot.settleSeconds"/> at a fixed 60 fps (same input, same picture),
    /// then freezes time. <see cref="Capture"/> renders a separate camera at the target resolution
    /// (supersampled, no UI, no gizmos), lets TAA converge on the frozen frame, and writes a PNG to
    /// <see cref="outputFolder"/> outside Assets.
    ///
    /// Drive it from Tools > Marketing, or this component's / a shot's inspector buttons.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class MarketingShowcase : MonoBehaviour
    {
        static MarketingShowcase s_instance;
        /// <summary>The director in the loaded scene (re-found if the static reference was lost).</summary>
        public static MarketingShowcase Instance
        {
            get
            {
                if (s_instance == null) s_instance = FindAnyObjectByType<MarketingShowcase>();
                return s_instance;
            }
            private set => s_instance = value;
        }

        [Header("Capture")]
        [Min(320)] public int width = 1920;
        [Min(180)] public int height = 1080;
        [Tooltip("Render this many times larger and average down: cleaner edges, foliage and particles.")]
        [Range(1, 3)] public int supersample = 2;
        [Tooltip("Frames rendered on the frozen moment before reading it back, so TAA and volumetrics converge.")]
        [Min(4)] public int convergeFrames = 40;
        [Tooltip("Relative to the project folder (next to Assets, not inside it).")]
        public string outputFolder = "Marketing/Screenshots";
        [Tooltip("Where capsule plates (ShowcaseOutput.CapsulePlate shots) are saved, relative to the project folder.")]
        public string plateFolder = "Marketing/SteamCapsules/plates";
        [Tooltip("Where RecordClip writes its frame sequences (one folder per clip), relative to the project folder.")]
        public string clipFolder = "Marketing/Clips/frames";

        [Header("Play")]
        public bool activateOnPlay = true;
        public int startShot;
        [Tooltip("Seconds after the seabed exists for the reef, kelp and sand to finish building.")]
        public float worldReadySeconds = 3f;

        [Header("Camera")]
        public Camera showcaseCamera;

        [Header("Game prefabs")]
        public GameObject sharkPrefab;
        public GameObject fishPrefab;
        public GameObject schoolPrefab;
        public GameObject tangledTurtlePrefab;
        public GameObject swimmingTurtlePrefab;

        [System.Serializable]
        public struct JawBone
        {
            public string boneName;
            [Tooltip("Local rotation added when the mouth is fully open.")]
            public Vector3 openEuler;
        }

        [Header("Bodies as in the game")]
        [Tooltip("The player shark's SharkMouth anchor: the jaw bone it hangs from and its pose there. Prey ride it.")]
        public string mouthBone = "Bone012";
        public Vector3 mouthLocalPosition = new Vector3(-0.012f, -0.072f, -0.007f);
        public Vector3 mouthLocalEuler = new Vector3(0f, 0f, 323.8f);
        [Tooltip("Where that anchor sits on the body at rest (shark space), for the eating effects.")]
        public Vector3 mouthRestOffset = new Vector3(-0.044f, -0.358f, 1.853f);
        [Tooltip("The great white rig's jaw (same bones and angles as the main menu shark).")]
        public JawBone[] jawBones =
        {
            new JawBone { boneName = "Bone011", openEuler = new Vector3(0f, 0f, -16f) },
            new JawBone { boneName = "Bone013", openEuler = new Vector3(0f, 0f, 48f) },
        };
        [Tooltip("A player fish's size: PlayerFish (x2) carrying its PinkFish model (x3).")]
        public float playerFishScale = 6f;

        [Header("Scene")]
        [Tooltip("Scene objects (by name) hidden in every shot.")]
        public string[] hideAlways = new string[0];

        // ------------------------------------------------------------------ state

        /// <summary>The shots, in hierarchy order (their file numbers).</summary>
        public List<ShowcaseShot> Shots
        {
            get
            {
                var list = new List<ShowcaseShot>(GetComponentsInChildren<ShowcaseShot>(true));
                return list;
            }
        }

        /// <summary>Index of the active shot, or -1.</summary>
        public int Current { get; private set; } = -1;
        /// <summary>World built and nothing queued: ready for a request.</summary>
        public bool Idle => _worldReady && _requests.Count == 0 && !_running;
        /// <summary>The frozen moment of the current shot is on screen.</summary>
        public bool ShotReady { get; private set; }
        /// <summary>Files written this Play session, newest last.</summary>
        public readonly List<string> Written = new List<string>();
        public string Status { get; private set; } = "waiting for the reef to build";

        class Live
        {
            public ShowcaseActor mark;
            public Transform body;
            public Animator anim;
            public Vector3[] path;
            public Quaternion[] rot;
            public bool stateStarted, eaten, dashStarted;
            public SwimTrail trail;
            public Transform[] jaw;
            public Quaternion[] jawRest, jawLastSet;
            public Edible edible;
        }

        readonly List<Live> _live = new List<Live>();
        readonly List<GameObject> _spawned = new List<GameObject>();
        // Hidden = renderers switched off, so the object's logic (e.g. ArenaWalls, which the trawler needs) keeps running.
        readonly List<Renderer> _hidden = new List<Renderer>();
        readonly HashSet<Renderer> _hiddenByShot = new HashSet<Renderer>();
        readonly Queue<System.Func<IEnumerator>> _requests = new Queue<System.Func<IEnumerator>>();
        bool _running, _worldReady;
        int _frame, _frames;
        int _postFrames;   // a clip runs on past the shot's moment; bodies keep swimming for this many frames

        Light _sun;
        float _sunIntensity;
        Quaternion _sunRotation;
        Volume _gradeVolume;
        VolumeProfile _gradeProfile;
        CreatureVision _vision;
        float _defaultNear = -1f;

        // Schools steer for a goal that roams on BoidsManager.SharedClock (time since Play by default), so the
        // same shot would come out different depending on when it's taken. While a shot runs, the clock is the
        // shot's own time instead. Safe here: this scene never networks (the clock only matters to FlockNetSync).
        System.Func<double> _gameClock;
        double _shotTime;

        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int BoostingId = Animator.StringToHash("Boosting");

        // ------------------------------------------------------------------ requests (editor / inspector)

        /// <summary>Set the game up for shot <paramref name="index"/> and freeze on it.</summary>
        public void Show(int index) => Enqueue(() => ShowRoutine(index));

        /// <summary>Set up shot <paramref name="index"/> from scratch and save it (always a fresh setup, so edits
        /// to the shot are picked up and the same shot always gives the same picture).</summary>
        public void Capture(int index) => Enqueue(() => CaptureRoutine(index, true));

        /// <summary>Set up and save every screenshot shot marked for Capture All, in order.</summary>
        public void CaptureAll() => CaptureEvery(ShowcaseOutput.Screenshot);

        /// <summary>Set up and save every capsule plate shot (artwork for the Steam capsules), in order.</summary>
        public void CaptureAllPlates() => CaptureEvery(ShowcaseOutput.CapsulePlate);

        void CaptureEvery(ShowcaseOutput output)
        {
            var shots = Shots;
            for (int i = 0; i < shots.Count; i++)
            {
                int k = i;
                if (shots[k].includeInCaptureAll && shots[k].output == output) Enqueue(() => CaptureRoutine(k, true));
            }
        }

        /// <summary>
        /// Record shot <paramref name="index"/> as a numbered JPEG sequence (for GIFs and short videos): the last
        /// <paramref name="before"/> seconds of its run-up, then <paramref name="after"/> seconds past the shot's
        /// moment with the bodies swimming on past their marks. Same fixed 60 fps simulation as a screenshot, so
        /// the clip is smooth however slowly it renders. <paramref name="dolly"/> pushes the camera this many
        /// metres along its view over the clip; <paramref name="track"/> (0..1) turns it to follow the shark.
        /// Frames go to <see cref="clipFolder"/>/&lt;name&gt;/.
        /// </summary>
        public void RecordClip(int index, string name, float before, float after, int fps = 30,
                               int clipWidth = 1280, int clipHeight = 720, float dolly = 0f, float track = 0f) =>
            Enqueue(() => ClipRoutine(index, name, before, after, fps, clipWidth, clipHeight, dolly, track));

        /// <summary>Let time run again (the shot stays set up).</summary>
        public void Resume()
        {
            Time.timeScale = 1f;
            ShotReady = false;
        }

        void Enqueue(System.Func<IEnumerator> r) => _requests.Enqueue(r);

        // ------------------------------------------------------------------ lifecycle

        void Awake()
        {
            Instance = this;
            _gameClock = BoidsManager.SharedClock;
            BoidsManager.SharedClock = () => _shotTime;
            foreach (var s in Shots) s.gameObject.SetActive(false);
        }

        IEnumerator Start()
        {
            if (showcaseCamera != null && !showcaseCamera.TryGetComponent(out _vision))
                _vision = showcaseCamera.gameObject.AddComponent<CreatureVision>();

            foreach (var n in hideAlways) Hide(n, null);

            // The seabed is generated at runtime; the reef, kelp and sand trails are placed on it afterwards.
            while (!SeabedBuilt()) yield return null;
            float until = Time.realtimeSinceStartup + worldReadySeconds;
            while (Time.realtimeSinceStartup < until) yield return null;
            _worldReady = true;
            Status = "ready";

#if UNITY_EDITOR
            // A request made from the editor before Play (Tools > Marketing) waits here.
            string pending = UnityEditor.SessionState.GetString(PendingKey, "");
            UnityEditor.SessionState.EraseString(PendingKey);
            if (pending == "all") CaptureAll();
            else if (pending == "plates") CaptureAllPlates();
            else if (pending.StartsWith("capture:") && int.TryParse(pending.Substring(8), out int c)) Capture(c);
            else if (pending.StartsWith("show:") && int.TryParse(pending.Substring(5), out int s)) Show(s);
            else
#endif
            if (activateOnPlay) Show(Mathf.Clamp(startShot, 0, Mathf.Max(0, Shots.Count - 1)));

            while (true)
            {
                if (_requests.Count > 0)
                {
                    _running = true;
                    yield return _requests.Dequeue()();
                    _running = false;
                }
                yield return null;
            }
        }

        public const string PendingKey = "FishGame.MarketingShowcase.Pending";

        static bool SeabedBuilt()
        {
            var terrain = FindAnyObjectByType<TestTerrain>();
            return terrain != null && terrain.TryGetComponent(out MeshCollider c) && c.sharedMesh != null;
        }

        void Update() => _shotTime += Time.deltaTime;   // runs before the schools (execution order -100)

        void OnDestroy()
        {
            if (_gameClock != null) BoidsManager.SharedClock = _gameClock;
            Time.captureFramerate = 0;
            Time.timeScale = 1f;
            if (s_instance == this) s_instance = null;
        }

        // ------------------------------------------------------------------ shots

        IEnumerator ShowRoutine(int index) => RunShot(index, 0, null);

        // Sets the shot up and simulates it. Without onFrame it freezes on the shot's moment (a screenshot);
        // with it, it runs postFrames further and calls onFrame at the end of every frame, after rendering.
        IEnumerator RunShot(int index, int postFrames, System.Action<int> onFrame)
        {
            var shots = Shots;
            if (index < 0 || index >= shots.Count) yield break;
            var shot = shots[index];
            Status = $"setting up {index + 1:00} {shot.slug}";
            Teardown();
            Current = index;

            Random.InitState(shot.seed);
            Time.captureFramerate = 60;   // fixed steps: the same simulation every time
            Time.timeScale = 1f;
            shot.gameObject.SetActive(true);

            ApplyCamera(shot);
            ApplySun(shot);
            foreach (var n in shot.hideObjects) Hide(n, shot);
            if (shot.worldEvent != WorldEventKind.None) StartEvent(shot);

            _frames = Mathf.Max(1, Mathf.RoundToInt(shot.settleSeconds * 60f));
            _postFrames = onFrame != null ? Mathf.Max(0, postFrames) : 0;
            _shotTime = 0.0;
            foreach (var a in shot.Actors)
                if (a.isActiveAndEnabled) Spawn(a, shot);

            if (onFrame != null)
            {
                for (_frame = 0; _frame <= _frames + _postFrames; _frame++)
                {
                    StepActors(shot, _frame);
                    yield return new WaitForEndOfFrame();
                    onFrame(_frame);
                }
                Status = $"{index + 1:00} {shot.slug}: clip done";
                yield break;
            }

            for (_frame = 0; _frame <= _frames; _frame++)
            {
                StepActors(shot, _frame);
                yield return null;
            }

            ApplyGrade(shot);   // after the event has set in: the grade adds to whatever it ended up as
            Time.timeScale = 0f;
            yield return null;
            ShotReady = true;
            Status = $"{index + 1:00} {shot.slug}: frozen";
        }

        IEnumerator CaptureRoutine(int index, bool fresh)
        {
            if (fresh || Current != index || !ShotReady)
                yield return ShowRoutine(index);
            var shot = Shots[index];
            Status = $"capturing {index + 1:00} {shot.slug}";
            bool plate = shot.output == ShowcaseOutput.CapsulePlate;
            string path = plate
                ? Path.Combine(ProjectPath(plateFolder), $"{Sanitize(shot.slug)}.png")
                : Path.Combine(OutputDirectory, $"{index + 1:00}_{Sanitize(shot.slug)}.png");
            int w = shot.resolution.x > 0 ? shot.resolution.x : width;
            int h = shot.resolution.y > 0 ? shot.resolution.y : height;
            int ss = shot.supersample > 0 ? shot.supersample : supersample;
            yield return RenderToFile(path, w, h, ss);
            Written.Add(path);
            Status = $"saved {Path.GetFileName(path)}";
            Debug.Log($"[Marketing] Saved {path} ({w}x{h}, {ss}x supersampled)");
        }

        IEnumerator ClipRoutine(int index, string name, float before, float after, int fps, int w, int h, float dolly, float track)
        {
            if (index < 0 || index >= Shots.Count || showcaseCamera == null) yield break;
            string dir = Path.Combine(ProjectPath(clipFolder), Sanitize(name));
            if (Directory.Exists(dir))
                foreach (var f in Directory.GetFiles(dir, "*.jpg")) File.Delete(f);
            Directory.CreateDirectory(dir);

            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "Showcase Clip" };
            rt.Create();
            var go = new GameObject("Showcase Clip Camera");
            go.transform.SetParent(showcaseCamera.transform, false);
            var cam = go.AddComponent<Camera>();
            var hd = go.AddComponent<HDAdditionalCameraData>();
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            go.SetActive(false);

            int step = Mathf.Max(1, Mathf.RoundToInt(60f / Mathf.Max(1, fps)));
            int post = Mathf.RoundToInt(after * 60f);
            int first = 0, last = 0, written = 0;
            Vector3 camStart = default;
            Status = $"recording {name}";

            yield return RunShot(index, post, frame =>
            {
                if (frame == 0)
                {
                    first = Mathf.Max(30, _frames - Mathf.RoundToInt(before * 60f));
                    last = _frames + _postFrames;
                }
                // Warm the capture camera up half a second early, so TAA and volumetrics have history.
                if (frame == first - 30)
                {
                    cam.CopyFrom(showcaseCamera);
                    var srcHd = showcaseCamera.GetComponent<HDAdditionalCameraData>();
                    if (srcHd != null) srcHd.CopyTo(hd);
                    cam.depth = showcaseCamera.depth - 1;
                    cam.targetTexture = rt;
                    cam.ResetAspect();
                    go.transform.localPosition = Vector3.zero;
                    go.transform.localRotation = Quaternion.identity;
                    go.SetActive(true);
                    ApplyGrade(Shots[index]);
                    camStart = showcaseCamera.transform.position;
                }
                if (frame >= first && (frame - first) % step == 0 && go.activeSelf)
                {
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                    tex.Apply(false);
                    RenderTexture.active = prev;
                    File.WriteAllBytes(Path.Combine(dir, $"{written++:0000}.jpg"), tex.EncodeToJPG(95));
                }
                if (frame >= first - 30 && string.IsNullOrEmpty(Shots[index].cameraMountBone))
                {
                    var t = showcaseCamera.transform;
                    if (dolly != 0f)
                    {
                        float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(first - 30, last, frame));
                        t.position = camStart + t.forward * (dolly * k);
                    }
                    var shark = _live.Find(l => l.mark.kind == ShowcaseActorKind.Shark && l.body != null);
                    if (track > 0f && shark != null)
                    {
                        var look = Quaternion.LookRotation(shark.body.position - t.position, Vector3.up);
                        t.rotation = Quaternion.Slerp(t.rotation, look, track * 0.08f);
                    }
                }
            });

            cam.targetTexture = null;
            Destroy(go);
            rt.Release();
            Destroy(rt);
            Destroy(tex);
            Status = $"clip {name}: {written} frames";
            Debug.Log($"[Marketing] Clip {name}: {written} frames at {60 / step} fps in {dir}");
        }

        /// <summary>The absolute screenshot folder.</summary>
        public string OutputDirectory => ProjectPath(outputFolder);

        static string ProjectPath(string relative) =>
            Path.GetFullPath(Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), relative));

        static string Sanitize(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s.Replace(' ', '_').ToLowerInvariant();
        }

        void Teardown()
        {
            ShotReady = false;
            foreach (var go in _spawned) if (go != null) Destroy(go);
            _spawned.Clear();
            _live.Clear();
            BoidsManager.Predators.RemoveAll(t => t == null);

            for (int i = _hidden.Count - 1; i >= 0; i--)
                if (_hidden[i] == null || _hiddenByShot.Remove(_hidden[i]))
                {
                    if (_hidden[i] != null) _hidden[i].enabled = true;
                    _hidden.RemoveAt(i);
                }

            if (_sun != null)
            {
                _sun.intensity = _sunIntensity;
                _sun.transform.rotation = _sunRotation;
            }
            if (_gradeVolume != null) _gradeVolume.enabled = false;

            var m = MatchManager.Instance;
            if (m != null && m.Event != WorldEventKind.None)
                m.ServerStartEvent(WorldEventKind.None, MatchManager.Now, MatchManager.Now, Vector3.zero, 0);

            foreach (var s in Shots) s.gameObject.SetActive(false);
            Current = -1;
        }

        void Hide(string objectName, ShowcaseShot shot)
        {
            if (string.IsNullOrEmpty(objectName)) return;
            var go = GameObject.Find(objectName);
            if (go == null) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled) continue;
                r.enabled = false;
                _hidden.Add(r);
                if (shot != null) _hiddenByShot.Add(r);
            }
        }

        // ------------------------------------------------------------------ camera, light, grade

        void ApplyCamera(ShowcaseShot shot)
        {
            if (showcaseCamera == null) return;
            Vector3 p = shot.transform.position;
            if (WaterFx.SeabedHeight(p + Vector3.up * 20f, out float ground))
            {
                if (shot.cameraHeightAboveSeabed > 0f) p.y = ground + shot.cameraHeightAboveSeabed;
                if (p.y < ground + shot.seabedClearance) p.y = ground + shot.seabedClearance;
            }
            showcaseCamera.transform.SetPositionAndRotation(p, shot.transform.rotation);
            showcaseCamera.fieldOfView = shot.fieldOfView;
            if (_defaultNear < 0f) _defaultNear = showcaseCamera.nearClipPlane;
            showcaseCamera.nearClipPlane = shot.nearClip > 0f ? shot.nearClip : _defaultNear;
            if (_vision != null) _vision.ApplyRole(shot.grade);
        }

        void ApplySun(ShowcaseShot shot)
        {
            if (_sun == null)
            {
                foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                    if (l.type == LightType.Directional && (_sun == null || l.intensity > _sun.intensity)) _sun = l;
                if (_sun == null) return;
                _sunIntensity = _sun.intensity;
                _sunRotation = _sun.transform.rotation;
            }
            _sun.intensity = _sunIntensity * shot.sunIntensity;
            if (shot.overrideSunDirection) _sun.transform.rotation = Quaternion.Euler(shot.sunEuler);
        }

        // DoF is always set (so the role grade's own focus never leaks in); exposure / contrast / saturation add
        // to what the game's volumes give at this spot, rather than replacing them.
        void ApplyGrade(ShowcaseShot shot)
        {
            if (_gradeVolume == null)
            {
                var go = new GameObject("Showcase Grade (runtime)");
                go.transform.SetParent(transform, false);   // Default layer: every camera's mask has it
                _gradeVolume = go.AddComponent<Volume>();
                _gradeVolume.isGlobal = true;
                _gradeVolume.priority = 60f;                // above the role grade (10) and world events (30)
                _gradeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
                _gradeProfile.Add<DepthOfField>();
                _gradeProfile.Add<ColorAdjustments>();
                _gradeProfile.Add<ChromaticAberration>();
                _gradeVolume.sharedProfile = _gradeProfile;
            }

            // What the game's volumes give this camera without us.
            _gradeVolume.enabled = false;
            var hd = showcaseCamera.GetComponent<HDAdditionalCameraData>();
            LayerMask mask = hd != null ? hd.volumeLayerMask : (LayerMask)1;
            var stack = VolumeManager.instance.CreateStack();
            VolumeManager.instance.Update(stack, showcaseCamera.transform, mask);
            var baseAdj = stack.GetComponent<ColorAdjustments>();
            float baseExposure = baseAdj.postExposure.value, baseContrast = baseAdj.contrast.value, baseSat = baseAdj.saturation.value;
            VolumeManager.instance.DestroyStack(stack);

            _gradeProfile.TryGet(out DepthOfField dof);
            bool near = shot.nearBlurEnd > shot.nearBlurStart && shot.nearBlurEnd > 0f;
            bool far = shot.farBlurEnd > shot.farBlurStart && shot.farBlurStart > 0f;
            dof.focusMode.Override(near || far ? DepthOfFieldMode.Manual : DepthOfFieldMode.Off);
            dof.nearFocusStart.Override(near ? shot.nearBlurStart : 0f);
            dof.nearFocusEnd.Override(near ? shot.nearBlurEnd : 0f);
            dof.farFocusStart.Override(far ? shot.farBlurStart : 1000f);
            dof.farFocusEnd.Override(far ? shot.farBlurEnd : 1001f);

            _gradeProfile.TryGet(out ColorAdjustments adj);
            adj.postExposure.overrideState = shot.exposure != 0f;
            adj.postExposure.value = baseExposure + shot.exposure;
            adj.contrast.overrideState = shot.contrast != 0f;
            adj.contrast.value = Mathf.Clamp(baseContrast + shot.contrast, -100f, 100f);
            adj.saturation.overrideState = shot.saturation != 0f;
            adj.saturation.value = Mathf.Clamp(baseSat + shot.saturation, -100f, 100f);

            _gradeProfile.TryGet(out ChromaticAberration ca);
            ca.intensity.overrideState = shot.chromaticAberration >= 0f;
            ca.intensity.value = Mathf.Max(0f, shot.chromaticAberration);

            _gradeVolume.enabled = true;
        }

        // ------------------------------------------------------------------ world events

        void StartEvent(ShowcaseShot shot)
        {
            var m = MatchManager.Instance;
            if (m == null)
            {
                // A scene network object: Mirror switched it off at Play start (nobody hosts here).
                foreach (var mm in Resources.FindObjectsOfTypeAll<MatchManager>())
                    if (mm.gameObject.scene == gameObject.scene) { mm.gameObject.SetActive(true); m = mm; break; }
            }
            if (m == null) { Debug.LogWarning("[Marketing] No MatchManager in the scene: world event skipped."); return; }

            m.StartRound();          // offline: Warmup -> Playing (events only run in a live round)
            WorldEvents.Show();
            ReefConditions.Show();

            double now = MatchManager.Now;
            float a = shot.eventHeading * Mathf.Deg2Rad;
            var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            if (ReefConditions.Handles(shot.worldEvent))
            {
                double start = now - shot.eventAge;
                m.ServerStartEvent(shot.worldEvent, start, start + ReefConditions.Duration(shot.worldEvent) + shot.eventAge,
                                   dir, Mathf.Max(1, shot.seed));
            }
            else if (shot.worldEvent == WorldEventKind.Current)
                m.ServerStartEvent(WorldEventKind.Current, now - shot.eventAge, now + 40f, dir, 0);
            else if (shot.worldEvent == WorldEventKind.Trawler)
                StartTrawler(m, shot, now);
        }

        // The trawler sweeps from wall `trawlerWall` to the one opposite at the game's net speed; starting it
        // eventAge seconds ago puts the net (eventAge x speed) metres in, laid out by WorldEvents' own Layout.
        void StartTrawler(MatchManager m, ShowcaseShot shot, double now)
        {
            var events = FindAnyObjectByType<WorldEvents>();
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            object layout = events != null ? typeof(WorldEvents).GetMethod("Layout", flags)?.Invoke(events, new object[] { shot.trawlerWall }) : null;
            if (layout == null || !(bool)layout.GetType().GetField("valid").GetValue(layout))
            {
                Debug.LogWarning("[Marketing] Trawler: no arena walls to lay the net out from.");
                return;
            }
            float distance = (float)layout.GetType().GetField("distance").GetValue(layout);
            var travel = (Vector3)layout.GetType().GetField("travel").GetValue(layout);
            float speed = (float)(typeof(WorldEvents).GetField("netSpeed", flags)?.GetValue(events) ?? 4f);
            double start = now - shot.eventAge;
            m.ServerStartEvent(WorldEventKind.Trawler, start, start + distance / Mathf.Max(0.5f, speed), travel, shot.trawlerWall);
        }

        // ------------------------------------------------------------------ actors

        void Spawn(ShowcaseActor a, ShowcaseShot shot)
        {
            Vector3 mark = a.transform.position;
            if (a.snapToSeabed && WaterFx.SeabedHeight(mark + Vector3.up * 20f, out float ground))
                mark.y = ground + a.heightAboveSeabed;
            Quaternion facing = a.transform.rotation;

            switch (a.kind)
            {
                case ShowcaseActorKind.Shark: SpawnShark(a, mark, facing); break;
                case ShowcaseActorKind.PlayerFish: SpawnFish(a, mark, facing); break;
                case ShowcaseActorKind.School: SpawnSchool(a, mark); break;
                case ShowcaseActorKind.TangledTurtle: SpawnTangledTurtle(a, mark, facing, shot); break;
                case ShowcaseActorKind.SwimmingTurtle: SpawnSwimmingTurtle(a, mark, facing); break;
            }
        }

        Live AddLive(ShowcaseActor a, GameObject root, Vector3 mark, Quaternion facing)
        {
            var live = new Live { mark = a, body = root.transform, anim = root.GetComponentInChildren<Animator>() };
            BuildPath(live, mark, facing);
            root.transform.SetPositionAndRotation(live.path[0], live.rot[0]);
            _live.Add(live);
            _spawned.Add(root);
            return live;
        }

        // Backwards from the mark, so the body is exactly on it (facing its way) at capture; a clip carries on
        // forwards past it.
        void BuildPath(Live live, Vector3 mark, Quaternion facing)
        {
            int n = _frames + 1 + _postFrames;
            live.path = new Vector3[n];
            live.rot = new Quaternion[n];
            Vector3 p = mark;
            Quaternion r = facing;
            float dt = 1f / 60f;
            for (int i = _frames; i >= 0; i--)
            {
                live.path[i] = p;
                live.rot[i] = r;
                p -= r * Vector3.forward * (live.mark.swimSpeed * dt);
                r = Quaternion.AngleAxis(-live.mark.turnRate * dt, Vector3.up) * r;
            }
            p = mark;
            r = facing;
            for (int i = _frames + 1; i < n; i++)
            {
                r = Quaternion.AngleAxis(live.mark.turnRate * dt, Vector3.up) * r;
                p += r * Vector3.forward * (live.mark.swimSpeed * dt);
                live.path[i] = p;
                live.rot[i] = r;
            }
        }

        void SpawnShark(ShowcaseActor a, Vector3 mark, Quaternion facing)
        {
            if (sharkPrefab == null) return;
            // Root at the body's pivot; "Jaws" at the mouth, so SharkEatFx (which uses its own transform when
            // there is no SharkAbilities) throws its bubbles and shockwaves from the mouth, as in the game.
            // Prey ride a SharkMouth anchor on the jaw bone, as on the player shark.
            var root = new GameObject("Showcase Shark");
            var jaws = new GameObject("Jaws").transform;
            jaws.SetParent(root.transform, false);
            jaws.localPosition = mouthRestOffset;
            var model = Instantiate(sharkPrefab, jaws);
            model.transform.localPosition = -mouthRestOffset;
            model.transform.localRotation = Quaternion.identity;
            var bone = FindDeep(model.transform, mouthBone);
            if (bone != null)
            {
                var anchor = new GameObject("SharkMouth").transform;
                anchor.SetParent(bone, false);
                anchor.localPosition = mouthLocalPosition;
                anchor.localEulerAngles = mouthLocalEuler;
            }
            root.transform.localScale = Vector3.one * a.scale;

            if (a.predator) root.AddComponent<Predator>();
            jaws.gameObject.AddComponent<SharkEatFx>();
            var live = AddLive(a, root, mark, facing);
            live.trail = root.AddComponent<SwimTrail>();

            live.jaw = new Transform[jawBones.Length];
            live.jawRest = new Quaternion[jawBones.Length];
            live.jawLastSet = new Quaternion[jawBones.Length];
            for (int i = 0; i < jawBones.Length; i++)
            {
                live.jaw[i] = FindDeep(model.transform, jawBones[i].boneName);
                if (live.jaw[i] != null) live.jawRest[i] = live.jawLastSet[i] = live.jaw[i].localRotation;
            }
            if (live.anim != null)
            {
                live.anim.applyRootMotion = false;
                live.anim.SetFloat(SpeedId, a.animSpeed);
                live.anim.SetBool(BoostingId, a.boosting);
            }
        }

        void SpawnFish(ShowcaseActor a, Vector3 mark, Quaternion facing)
        {
            if (fishPrefab == null) return;
            var go = Instantiate(fishPrefab);
            go.name = "Showcase Player Fish";
            // The school fish model, minus the flock's per-fish script (the player fish is the same model).
            if (go.TryGetComponent(out Boid boid)) Destroy(boid);
            go.transform.localScale = Vector3.one * playerFishScale * a.scale;
            var live = AddLive(a, go, mark, facing);
            live.edible = go.GetComponent<Edible>();
            if (live.anim != null) live.anim.applyRootMotion = false;
            if (a.allyGlow) AddAllyGlow(go);
        }

        void SpawnSchool(ShowcaseActor a, Vector3 mark)
        {
            if (schoolPrefab == null) return;
            // Configure before Awake (which spawns the fish): instantiate under an inactive holder.
            var holder = new GameObject("holder");
            holder.SetActive(false);
            var go = Instantiate(schoolPrefab, mark, Quaternion.identity, holder.transform);
            go.name = $"Showcase School ({a.name})";
            if (go.TryGetComponent(out BoidsUIController ui)) DestroyImmediate(ui);
            var bm = go.GetComponent<BoidsManager>();
            bm.area = a.schoolArea;
            bm.spawnSeed = a.schoolSeed;
            bm.goalInterval = a.schoolGoalInterval;
            typeof(BoidsManager).GetField("fishCount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(bm, a.schoolSize);
            go.transform.SetParent(null, true);
            Destroy(holder);
            go.SetActive(true);
            _spawned.Add(go);
            if (bm.allFish != null)
                foreach (var f in bm.allFish) if (f != null) _spawned.Add(f);
        }

        void SpawnTangledTurtle(ShowcaseActor a, Vector3 mark, Quaternion facing, ShowcaseShot shot)
        {
            if (tangledTurtlePrefab == null || !tangledTurtlePrefab.TryGetComponent(out TangledTurtle proto)) return;
            // As the turtle task places one, but here rather than at a random spot.
            if (WaterFx.SeabedHeight(mark + Vector3.up * 20f, out float ground)) mark.y = ground + proto.HoverHeight;
            var go = Instantiate(tangledTurtlePrefab, mark, Quaternion.Euler(0f, facing.eulerAngles.y, 0f));
            go.GetComponent<TangledTurtle>().ServerSetup(new System.Random(shot.seed));
            _spawned.Add(go);
        }

        void SpawnSwimmingTurtle(ShowcaseActor a, Vector3 mark, Quaternion facing)
        {
            if (swimmingTurtlePrefab == null) return;
            var go = Instantiate(swimmingTurtlePrefab);
            // Its own roaming would wander off the mark; the showcase steers it instead.
            if (go.TryGetComponent(out TurtleSwimmer swimmer)) Destroy(swimmer);
            go.transform.localScale *= a.scale;
            var live = AddLive(a, go, mark, facing);
            if (live.anim != null) live.anim.applyRootMotion = false;
        }

        void StepActors(ShowcaseShot shot, int frame)
        {
            float secondsLeft = (_frames - frame) / 60f;
            Live shark = _live.Find(l => l.mark.kind == ShowcaseActorKind.Shark);
            foreach (var l in _live)
            {
                if (l.body == null || l.eaten) continue;
                int i = Mathf.Clamp(frame, 0, l.path.Length - 1);
                l.body.SetPositionAndRotation(l.path[i], l.rot[i]);

                var a = l.mark;
                if (l.anim != null && !string.IsNullOrEmpty(a.animState) && !l.stateStarted && secondsLeft <= a.animStateTime)
                {
                    l.stateStarted = true;
                    l.anim.Play(a.animState, 0, 0f);
                }
                if (l.trail != null && a.dashWakeSeconds > 0f && !l.dashStarted && secondsLeft <= a.dashWakeSeconds)
                {
                    l.dashStarted = true;
                    l.trail.Dash(a.dashWakeSeconds);
                }
                if (l.edible != null && a.eatenBeforeCapture > 0f && secondsLeft <= a.eatenBeforeCapture && shark != null)
                {
                    l.eaten = true;
                    var mouth = FindDeep(shark.body, "SharkMouth");
                    if (mouth == null) mouth = shark.body.Find("Jaws");
                    l.edible.Devour(shark.body.gameObject, mouth, despawn: false, bloodSeed: BloodInWater.SeedFor(shot.seed, 7));
                }
            }
        }

        // After the Animator has posed the rig: open the jaw (as MenuSharkCruise does), then put a mounted camera
        // on its bone (as the menu's camera rides the shark's head).
        void LateUpdate()
        {
            ApplyJaws();
            var shots = Shots;
            if (Current < 0 || Current >= shots.Count || string.IsNullOrEmpty(shots[Current].cameraMountBone) || showcaseCamera == null) return;
            var shot = shots[Current];
            var shark = _live.Find(l => l.mark.kind == ShowcaseActorKind.Shark && l.body != null);
            var bone = shark != null ? FindDeep(shark.body, shot.cameraMountBone) : null;
            if (bone == null) return;
            showcaseCamera.transform.SetPositionAndRotation(bone.TransformPoint(shot.cameraMountPosition),
                                                            bone.rotation * Quaternion.Euler(shot.cameraMountEuler));
        }

        void ApplyJaws()
        {
            foreach (var l in _live)
            {
                if (l.jaw == null) continue;
                for (int i = 0; i < l.jaw.Length; i++)
                {
                    var bone = l.jaw[i];
                    if (bone == null) continue;
                    // If the Animator drives this bone its pose is fresh every frame; if not, it still holds what
                    // we set last frame, so start from the rest pose instead of stacking rotations.
                    Quaternion basePose = bone.localRotation == l.jawLastSet[i] ? l.jawRest[i] : bone.localRotation;
                    l.jawLastSet[i] = basePose * Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(jawBones[i].openEuler), l.mark.jawOpen);
                    bone.localRotation = l.jawLastSet[i];
                }
            }
        }

        static Transform FindDeep(Transform t, string n)
        {
            if (t.name == n) return t;
            foreach (Transform c in t)
            {
                var r = FindDeep(c, n);
                if (r != null) return r;
            }
            return null;
        }

        // The teammate glow FishAllies draws on a fish player's screen: a copy of the mesh, skinned to the same
        // bones, in the WaterFxLibrary allyGlow material.
        static void AddAllyGlow(GameObject fish)
        {
            var lib = WaterFxLibrary.Get();
            if (lib == null || lib.allyGlow == null) return;
            foreach (var smr in fish.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (smr.sharedMesh == null) continue;
                var go = new GameObject("AllyGlow");
                go.transform.SetParent(smr.transform, false);
                var copy = go.AddComponent<SkinnedMeshRenderer>();
                copy.sharedMesh = smr.sharedMesh;
                copy.bones = smr.bones;
                copy.rootBone = smr.rootBone;
                copy.localBounds = smr.localBounds;
                copy.updateWhenOffscreen = true;
                var mats = new Material[Mathf.Max(1, smr.sharedMesh.subMeshCount)];
                for (int i = 0; i < mats.Length; i++) mats[i] = lib.allyGlow;
                copy.sharedMaterials = mats;
                copy.shadowCastingMode = ShadowCastingMode.Off;
                copy.receiveShadows = false;
                copy.lightProbeUsage = LightProbeUsage.Off;
                copy.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
        }

        // ------------------------------------------------------------------ capture

        IEnumerator RenderToFile(string path, int outWidth, int outHeight, int supersampling)
        {
            int ss = Mathf.Clamp(supersampling, 1, 3);
            int w = outWidth * ss, h = outHeight * ss;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "Showcase Capture" };
            rt.Create();

            // A copy of the showcase camera drawing into the texture. The showcase camera keeps drawing the
            // Game view, so everything that follows "the view" (sunbeams, specks, water deformation) stays put.
            var go = new GameObject("Showcase Capture Camera");
            go.transform.SetParent(showcaseCamera.transform, false);
            var cam = go.AddComponent<Camera>();
            var hd = go.AddComponent<HDAdditionalCameraData>();
            cam.CopyFrom(showcaseCamera);
            var srcHd = showcaseCamera.GetComponent<HDAdditionalCameraData>();
            if (srcHd != null) srcHd.CopyTo(hd);
            cam.depth = showcaseCamera.depth - 1;
            cam.targetTexture = rt;
            cam.ResetAspect();   // the plate's own aspect (a capsule can be far wider or taller than the Game view)

            for (int i = 0; i < convergeFrames; i++) yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var full = new Texture2D(w, h, TextureFormat.RGB24, false);
            full.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            full.Apply(false);
            RenderTexture.active = prev;

            cam.targetTexture = null;
            Destroy(go);
            rt.Release();
            Destroy(rt);

            var output = ss == 1 ? full : BoxDownsample(full, ss);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, output.EncodeToPNG());
            if (output != full) Destroy(output);
            Destroy(full);
        }

        // Exact box filter: each output pixel is the mean of its ss x ss block.
        static Texture2D BoxDownsample(Texture2D src, int ss)
        {
            int w = src.width / ss, h = src.height / ss;
            var s = src.GetPixels32();
            var d = new Color32[w * h];
            int n = ss * ss, sw = src.width;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int r = 0, g = 0, b = 0;
                    for (int j = 0; j < ss; j++)
                    {
                        int row = (y * ss + j) * sw + x * ss;
                        for (int i = 0; i < ss; i++)
                        {
                            var c = s[row + i];
                            r += c.r; g += c.g; b += c.b;
                        }
                    }
                    d[y * w + x] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 255);
                }
            var t = new Texture2D(w, h, TextureFormat.RGB24, false);
            t.SetPixels32(d);
            t.Apply(false);
            return t;
        }
    }
}
