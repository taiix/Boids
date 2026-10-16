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
    ///   * <b>Music</b>: the menu theme outside a match; in a match the round theme, the tense one once the exit
    ///     opens, and silence after the result (a jingle plays instead). Tracks cross-fade.
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
        AudioClip _wantMusic;
        float _duckUntil;

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

        /// <summary>A short musical sting (round start, a win): played flat, with the music ducked under it.</summary>
        public static void Jingle(AudioClip clip, float volume = 0.8f)
        {
            if (s_instance == null || clip == null) return;
            s_instance._duckUntil = Time.unscaledTime + clip.length + 0.3f;
            s_instance.Fire(clip, null, volume, 0f, 0f);
        }

        void Fire(AudioClip clip, Vector3? at, float volume, float pitchJitter, float range)
        {
            var s = _pool[_next];
            _next = (_next + 1) % _pool.Count;
            s.Stop();
            s.clip = clip;
            s.volume = Mathf.Clamp01(volume * _sfxVolume);
            s.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
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
            else want = lib.musicRound;

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
                Loop("ambient", lib.ambientBubbles, 0.12f);
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
