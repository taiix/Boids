using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace FishGame
{
    /// <summary>
    /// The game's sound: music that follows the round, one-shot effects (flat or placed in the world), and
    /// looping beds that systems ask for every frame they want them. Created once, survives scene loads.
    ///
    ///   * <b>Music</b>: the menu theme outside a match; in a match a dark round theme (one drawn per round), the
    ///     tense one once the exit opens, and silence after the result (a jingle plays instead). Tracks cross-fade.
    ///   * <b>Tension</b>: a drone under every round; for the fish, a dissonant layer that swells (and ducks the
    ///     music) as the shark closes in, and a sting when it gets close.
    ///   * <b>Effects</b>: <see cref="Play"/> (flat, for this player's own feedback) and <see cref="PlayAt"/> (3D,
    ///     muffled while the listener is underwater). Jingles duck the music while they play.
    ///   * <b>Loops</b>: <see cref="Loop"/> each frame keeps one going (rain, an engine, a heartbeat); stop asking
    ///     and it fades out by itself.
    ///   * <b>Buttons</b>: every UI Toolkit button clicks, without each screen having to wire it.
    ///
    /// Volumes come from the settings screen (Master is AudioListener.volume; Music and SFX are read from its
    /// saved values here).
    /// </summary>
    public class GameAudio : MonoBehaviour
    {
        static GameAudio s_instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (s_instance != null) return;
            var go = new GameObject("GameAudio");
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<GameAudio>();
        }

        /// <summary>The clips (null until the library is built).</summary>
        public static SoundLibrary Lib => SoundLibrary.Get();

        const string K_MUSIC = "rr_vol_music";  // shared with ReefRunSettingsPanel
        const string K_SFX = "rr_vol_sfx";
        const float MusicLevel = 0.45f;         // music sits under the effects and voices
        const float UnderwaterCutoff = 2800f;   // Hz: how muffled world sounds are underwater

        float _musicVolume = 1f, _sfxVolume = 1f, _nextPrefsRead;

        // ---- music ----
        AudioSource _musicA, _musicB, _musicOn;
        AudioClip _wantMusic, _roundTrack;
        float _duckUntil;

        // ---- tension ----
        const float ThreatFar = 40f, ThreatNear = 10f;   // m: the shark starts to be felt / is right there
        const float StingAt = 0.6f, StingRearm = 0.2f;   // threat that fires the sting, and that re-arms it
        float _threat;                                   // 0..1, eased: how close the shark is to this fish
        bool _stingArmed = true;

        // ---- one-shots ----
        readonly List<AudioSource> _pool = new List<AudioSource>();
        int _next;

        // ---- loops ----
        class LoopVoice { public AudioSource src; public float want, frame; public Transform follow; public bool world; }
        readonly Dictionary<string, LoopVoice> _loops = new Dictionary<string, LoopVoice>();
        readonly List<string> _loopDrop = new List<string>();

        // ---- buttons ----
        readonly HashSet<VisualElement> _hookedRoots = new HashSet<VisualElement>();
        float _nextUiScan;

        void Awake()
        {
            _musicA = NewSource("MusicA", false);
            _musicB = NewSource("MusicB", false);
            _musicA.loop = _musicB.loop = true;
            for (int i = 0; i < 24; i++) _pool.Add(NewSource("Sfx" + i, true));
            ReadPrefs();
        }

        AudioSource NewSource(string name, bool world)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.dopplerLevel = 0f;
            s.rolloffMode = AudioRolloffMode.Linear;
            if (world) go.AddComponent<AudioLowPassFilter>().enabled = false;
            return s;
        }

        void ReadPrefs()
        {
            _musicVolume = PlayerPrefs.GetFloat(K_MUSIC, 1f);
            _sfxVolume = PlayerPrefs.GetFloat(K_SFX, 1f);
        }

        // ================================================================== effects

        /// <summary>A random clip of <paramref name="clips"/> (null if there are none).</summary>
        public static AudioClip Pick(AudioClip[] clips) =>
            clips != null && clips.Length > 0 ? clips[Random.Range(0, clips.Length)] : null;

        /// <summary>A flat (2D) effect: this player's own feedback, alerts and jingles.</summary>
        public static void Play(AudioClip clip, float volume = 1f, float pitchJitter = 0f)
        {
            if (s_instance == null || clip == null) return;
            s_instance.Fire(clip, null, volume, pitchJitter, 0f);
        }

        /// <summary>An effect placed in the world at <paramref name="position"/>, heard up to
        /// <paramref name="range"/> metres away and muffled while the listener is underwater.</summary>
        public static void PlayAt(AudioClip clip, Vector3 position, float volume = 1f, float range = 40f, float pitchJitter = 0.06f)
        {
            if (s_instance == null || clip == null) return;
            s_instance.Fire(clip, position, volume, pitchJitter, range);
        }

        /// <summary>A flat effect played at <paramref name="pitch"/> (e.g. rising as a meter fills).</summary>
        public static void PlayPitched(AudioClip clip, float volume, float pitch)
        {
            if (s_instance == null || clip == null) return;
            s_instance.Fire(clip, null, volume, 0f, 0f, pitch);
        }

        /// <summary><see cref="PlayAt"/> at <paramref name="pitch"/> (e.g. a deep thump from a lighter sound).</summary>
        public static void PlayAtPitched(AudioClip clip, Vector3 position, float volume, float range, float pitch)
        {
            if (s_instance == null || clip == null) return;
            s_instance.Fire(clip, position, volume, 0.04f, range, pitch);
        }

        /// <summary>A short musical sting (round start, a win): played flat, with the music ducked under it.</summary>
        public static void Jingle(AudioClip clip, float volume = 0.8f)
        {
            if (s_instance == null || clip == null) return;
            s_instance._duckUntil = Time.unscaledTime + clip.length + 0.3f;
            s_instance.Fire(clip, null, volume, 0f, 0f);
        }

        void Fire(AudioClip clip, Vector3? at, float volume, float pitchJitter, float range, float pitch = 1f)
        {
            var s = _pool[_next];
            _next = (_next + 1) % _pool.Count;
            s.Stop();
            s.clip = clip;
            s.volume = Mathf.Clamp01(volume * _sfxVolume);
            s.pitch = pitch * (1f + Random.Range(-pitchJitter, pitchJitter));
            bool world = at.HasValue;
            s.spatialBlend = world ? 1f : 0f;
            if (world)
            {
                s.transform.position = at.Value;
                s.minDistance = Mathf.Max(1f, range * 0.12f);
                s.maxDistance = Mathf.Max(s.minDistance + 1f, range);
            }
            Muffle(s, world);
            s.Play();
        }

        // ================================================================== loops

        /// <summary>
        /// Keep a looping sound going this frame (call it every frame you want it). <paramref name="key"/> names
        /// the loop; <paramref name="volume"/> can change each frame (fade it with an event's strength).
        /// <paramref name="follow"/> places it in the world on that transform; null plays it flat.
        /// </summary>
        public static void Loop(string key, AudioClip clip, float volume, Transform follow = null, float range = 80f)
        {
            if (s_instance == null || clip == null) return;
            var loops = s_instance._loops;
            if (!loops.TryGetValue(key, out var v))
            {
                v = new LoopVoice { src = s_instance.NewSource("Loop_" + key, true) };
                v.src.loop = true;
                v.src.clip = clip;
                v.src.volume = 0f;
                v.src.Play();
                loops[key] = v;
            }
            if (v.src.clip != clip) { v.src.clip = clip; v.src.Play(); }
            v.want = volume;
            v.frame = Time.unscaledTime;
            v.follow = follow;
            v.world = follow != null;
            v.src.spatialBlend = v.world ? 1f : 0f;
            if (v.world)
            {
                v.src.minDistance = Mathf.Max(1f, range * 0.15f);
                v.src.maxDistance = range;
            }
        }

        void UpdateLoops()
        {
            _loopDrop.Clear();
            foreach (var kv in _loops)
            {
                var v = kv.Value;
                bool asked = Time.unscaledTime - v.frame < 0.2f;
                float target = asked ? Mathf.Clamp01(v.want * _sfxVolume) : 0f;
                v.src.volume = Mathf.MoveTowards(v.src.volume, target, Time.unscaledDeltaTime * 1.2f);
                if (v.follow != null) v.src.transform.position = v.follow.position;
                Muffle(v.src, v.world);
                if (!asked && v.src.volume <= 0.001f) _loopDrop.Add(kv.Key);
            }
            foreach (var k in _loopDrop)
            {
                Destroy(_loops[k].src.gameObject);
                _loops.Remove(k);
            }
        }

        // World sounds heard from underwater are dull and close; above the surface they're clear.
        static void Muffle(AudioSource s, bool world)
        {
            if (!s.TryGetComponent(out AudioLowPassFilter lp)) return;
            var cam = WaterFx.ViewCamera;
            bool under = world && cam != null && cam.transform.position.y < WaterFx.SurfaceY;
            lp.enabled = under;
            if (under) lp.cutoffFrequency = UnderwaterCutoff;
        }

        // ================================================================== music

        void UpdateMusic()
        {
            var lib = Lib;
            if (lib == null) return;
            var m = MatchManager.Instance;
            AudioClip want;
            if (m == null) want = lib.musicMenu;
            else if (m.IsOver) want = null;                       // the result's jingle, then quiet
            else if (m.Phase == MatchPhase.Escape) want = lib.musicEscape;
            else
            {
                if (_roundTrack == null) _roundTrack = Pick(lib.musicRounds) ?? lib.musicRound;
                want = _roundTrack;
            }
            if (m == null || m.IsOver) _roundTrack = null;        // the next round draws a fresh track

            if (want != _wantMusic)
            {
                _wantMusic = want;
                // Cross-fade: the other source takes the new track.
                var to = _musicOn == _musicA ? _musicB : _musicA;
                if (want != null)
                {
                    to.clip = want;
                    to.volume = 0f;
                    to.Play();
                    _musicOn = to;
                }
                else _musicOn = null;
            }

            float duck = Time.unscaledTime < _duckUntil ? 0.25f : 1f;
            duck *= 1f - 0.45f * _threat;                          // the shark's cluster takes over from the music
            float level = MusicLevel * _musicVolume * duck;
            float step = Time.unscaledDeltaTime * 0.35f;
            FadeMusic(_musicA, level, step);
            FadeMusic(_musicB, level, step);
        }

        void FadeMusic(AudioSource s, float level, float step)
        {
            float target = s == _musicOn ? level : 0f;
            s.volume = Mathf.MoveTowards(s.volume, target, step);
            if (s != _musicOn && s.volume <= 0.001f && s.isPlaying) s.Stop();
        }

        // ================================================================== buttons

        // Every button on every UI Toolkit screen clicks. Close / back buttons get the softer "back" sound.
        void HookButtons()
        {
            if (Time.unscaledTime < _nextUiScan) return;
            _nextUiScan = Time.unscaledTime + 1f;
            _hookedRoots.RemoveWhere(r => r == null || r.panel == null);
            foreach (var doc in FindObjectsByType<UIDocument>())
            {
                var root = doc != null ? doc.rootVisualElement : null;
                if (root == null || !_hookedRoots.Add(root)) continue;
                root.RegisterCallback<ClickEvent>(OnUiClick, TrickleDown.TrickleDown);
            }
        }

        static void OnUiClick(ClickEvent e)
        {
            var lib = Lib;
            if (lib == null || !(e.target is VisualElement ve)) return;
            var button = ve as Button ?? ve.GetFirstAncestorOfType<Button>();
            if (button == null || !button.enabledInHierarchy) return;
            string n = (button.name ?? "").ToLowerInvariant();
            bool back = n.Contains("close") || n.Contains("back") || n.Contains("cancel") || n.Contains("quit");
            Play(back ? lib.uiBack : lib.uiClick, 0.7f);
        }

        // ================================================================== frame

        void Update()
        {
            if (Time.unscaledTime >= _nextPrefsRead)
            {
                _nextPrefsRead = Time.unscaledTime + 0.5f;
                ReadPrefs();
            }
            UpdateTension();
            UpdateMusic();
            UpdateAmbience();
            UpdateLoops();
            HookButtons();
        }

        // The constant underwater fizz while in a match (only underwater).
        void UpdateAmbience()
        {
            var lib = Lib;
            if (lib == null || MatchManager.Instance == null) return;
            var cam = WaterFx.ViewCamera;
            if (cam != null && cam.transform.position.y < WaterFx.SurfaceY)
            {
                Loop("ambient", lib.ambientBubbles, 0.12f);
                Loop("deep", lib.ambientDeep, 0.3f);
            }
        }

        // ================================================================== tension

        // Under every round a low drone; for a fish, a tight dissonant cluster that swells as the shark closes in
        // (it ducks the music), and a sting the moment the shark comes close. The shark hears only the drone.
        void UpdateTension()
        {
            var lib = Lib;
            var m = MatchManager.Instance;
            if (lib == null || m == null || !m.RoundRunning) { _threat = 0f; _stingArmed = true; return; }

            float raw = SharkThreat();
            float rate = raw > _threat ? 1.2f : 0.25f;          // dread comes fast and leaves slowly
            _threat = Mathf.MoveTowards(_threat, raw, Time.unscaledDeltaTime * rate);

            Loop("dread", lib.tensionDrone, 0.22f + 0.25f * _threat);
            if (_threat > 0.02f) Loop("near", lib.tensionNear, 0.7f * _threat * _threat);

            if (raw < StingRearm) _stingArmed = true;
            else if (_stingArmed && raw >= StingAt)
            {
                _stingArmed = false;
                Play(lib.tensionSting, 0.85f);
            }
        }

        // 0 (no shark within ThreatFar) .. 1 (within ThreatNear) for the local fish, or for whatever the camera
        // follows once that fish is out (spectating). Always 0 for the shark's own player.
        static float SharkThreat()
        {
            var local = Mirror.NetworkClient.localPlayer;
            var me = local != null ? local.GetComponent<FishPlayer>() : null;
            if (me != null && me.IsSharkBody) return 0f;
            Vector3 at;
            if (me != null && !me.IsOut) at = me.transform.position;
            else if (WaterFx.ViewCamera != null) at = WaterFx.ViewCamera.transform.position;
            else return 0f;

            float best = float.MaxValue;
            foreach (var fp in FishPlayer.All)
                if (fp != null && fp != me && fp.isActiveAndEnabled && fp.IsSharkBody && !fp.IsOut)
                    best = Mathf.Min(best, Vector3.Distance(at, fp.transform.position));
            return 1f - Mathf.Clamp01(Mathf.InverseLerp(ThreatNear, ThreatFar, best));
        }

        void OnEnable() => MatchManager.TaskDone += OnTaskDone;
        void OnDisable() => MatchManager.TaskDone -= OnTaskDone;

        // The fish hear a bright chime when they finish a task; the shark hears a bell telling it where to go.
        static void OnTaskDone(Vector3 where, string what)
        {
            var lib = Lib;
            if (lib == null) return;
            var me = Mirror.NetworkClient.localPlayer != null ? Mirror.NetworkClient.localPlayer.GetComponent<FishPlayer>() : null;
            bool shark = me != null ? me.IsSharkBody : FindAnyObjectByType<SharkAbilities>() is SharkAbilities s && s.enabled;
            Play(shark ? lib.taskDoneShark : lib.taskDoneFish, 0.85f);
        }

        void OnDestroy()
        {
            if (s_instance == this) s_instance = null;
        }
    }
}
