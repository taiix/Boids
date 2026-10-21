using System.Collections;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace FishGame
{
    /// <summary>
    /// Marketing only (never part of a match): records the Game view, HUD included, as a numbered JPEG
    /// sequence while an optional autopilot flies the local shark in the offline sandbox (IslandTest).
    ///
    /// The autopilot plays through the game's own paths: it turns the shark's orbit camera (whose aim the
    /// motor follows, as the mouse does), gives the motor the same input FishController would, and fires
    /// the real lunge and sonar.
    ///
    /// It records in real time, as a screen recorder would: the game keeps its own pace, so everything timed on
    /// real time (camera shake, hit stop, HUD flashes) plays exactly as a player sees it. (A fixed capture
    /// frame rate slows game time but not real time, and played those effects about 1.6x too fast.) Frames
    /// are read back from the GPU without stalling it and encoded on worker threads; frames.txt records when
    /// each was shown, so the GIF/MP4 step can put them back on an even frame rate.
    /// </summary>
    public class GameplayClip : MonoBehaviour
    {
        public static GameplayClip Current { get; private set; }

        /// <summary>Recording finished (or never started).</summary>
        public bool Done { get; private set; }
        public int Written => Volatile.Read(ref _written);
        public string Status { get; private set; } = "idle";

        // ------------------------------------------------------------------ autopilot settings
        /// <summary>Hunt the nearest school fish (true), or swim at <see cref="goal"/>.</summary>
        public bool hunt = true;
        public Vector3 goal;
        public bool boost = true;
        [Range(0f, 1f)] public float throttle = 1f;
        [Tooltip("Turn toward the nearest orca instead (the orcas event).")]
        public bool faceOrcas;
        [Tooltip("Swim at this instead (e.g. the trawler's net).")]
        public Transform follow;
        [Tooltip("Lunge when the prey is this close and roughly ahead (0 = never).")]
        public float lungeRange = 9f;
        [Tooltip("Seconds into the clip to fire the sonar (negative = never).")]
        public float sonarAt = -1f;
        [Tooltip("Degrees per second the camera (and so the shark) turns toward the prey.")]
        public float turnRate = 70f;
        [Tooltip("Fish: seconds into the clip to blend into the nearest school (negative = never).")]
        public float blendAt = -1f;
        [Tooltip("Seconds into the clip to summon the orcas (negative = never).")]
        public float orcasAt = -1f;

        FishPlayer _body;
        FishOrbitCamera _cam;
        FishMotor _motor;
        FishController _controller;
        SharkAbilities _abilities;
        FishFlockBlend _blend;
        bool _blended;
        bool _autopilot, _sonared, _orcas;
        float _t;
        int _written, _inFlight;

        static readonly System.Reflection.BindingFlags Flags =
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

        /// <summary>Record <paramref name="seconds"/> of the Game view to Marketing/Clips/frames/&lt;name&gt;/.</summary>
        public static GameplayClip Record(string name, float seconds, float warmup = 0.5f, int fps = 30, bool autopilot = true)
        {
            if (Current != null) Destroy(Current.gameObject);
            var go = new GameObject("Gameplay Clip (marketing)");
            Current = go.AddComponent<GameplayClip>();
            Current._autopilot = autopilot;
            Current.StartCoroutine(Current.Run(name, seconds, warmup, fps));
            return Current;
        }

        IEnumerator Run(string name, float seconds, float warmup, int fps)
        {
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Marketing", "Clips", "frames", name));
            if (Directory.Exists(dir))
                foreach (var f in Directory.GetFiles(dir, "*.jpg")) File.Delete(f);
            Directory.CreateDirectory(dir);

            // The local player's body, shark or fish (a match started from the menu); else the offline sandbox shark.
            foreach (var fp in FishPlayer.All)
                if (fp != null && fp.isActiveAndEnabled && fp.isLocalPlayer) { _body = fp; break; }
            if (_body == null)
                foreach (var fp in FishPlayer.All)
                    if (fp != null && fp.isActiveAndEnabled && fp.GetComponent<SharkAbilities>() != null) { _body = fp; break; }
            if (_autopilot && _body != null)
            {
                // The player prefab carries its own orbit camera; the offline sandbox shark uses the scene's.
                _cam = _body.GetComponentInChildren<FishOrbitCamera>(true);
                if (_cam == null) _cam = FindAnyObjectByType<FishOrbitCamera>();
                _motor = _body.GetComponent<FishMotor>();
                _abilities = _body.GetComponent<SharkAbilities>();
                _controller = _body.GetComponent<FishController>();
                _blend = _body.GetComponent<FishFlockBlend>();
                if (_controller != null) _controller.enabled = false;
            }

            float start = Time.unscaledTime + warmup, end = start + seconds, last = -1f;
            float gap = 0.98f / Mathf.Max(1, fps);
            var rt = new RenderTexture(Screen.width, Screen.height, 0, RenderTextureFormat.ARGB32);
            bool flip = SystemInfo.graphicsUVStartsAtTop;
            var times = new System.Text.StringBuilder();
            int requested = 0;
            Status = "recording";
            while (Time.unscaledTime < end)
            {
                yield return new WaitForEndOfFrame();
                float now = Time.unscaledTime;
                if (now < start || now - last < gap) continue;
                last = now;
                if (rt.width != Screen.width || rt.height != Screen.height)
                {
                    rt.Release();
                    rt.width = Screen.width;
                    rt.height = Screen.height;
                }
                ScreenCapture.CaptureScreenshotIntoRenderTexture(rt);
                int w = rt.width, h = rt.height;
                string path = Path.Combine(dir, $"{requested:0000}.jpg");
                times.AppendLine($"{requested:0000}.jpg {(now - start).ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)}");
                requested++;
                Interlocked.Increment(ref _inFlight);
                AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32, req =>
                {
                    if (req.hasError) { Interlocked.Decrement(ref _inFlight); return; }
                    byte[] bytes = req.GetData<byte>().ToArray();
                    Task.Run(() =>
                    {
                        try
                        {
                            if (flip) FlipRows(bytes, w * 4, h);
                            File.WriteAllBytes(path, ImageConversion.EncodeArrayToJPG(bytes, GraphicsFormat.R8G8B8A8_SRGB, (uint)w, (uint)h, 0, 95));
                            Interlocked.Increment(ref _written);
                        }
                        finally { Interlocked.Decrement(ref _inFlight); }
                    });
                });
            }
            Status = "encoding";
            while (Volatile.Read(ref _inFlight) > 0) yield return null;
            File.WriteAllText(Path.Combine(dir, "frames.txt"), times.ToString());
            rt.Release();
            Destroy(rt);
            if (_controller != null) _controller.enabled = true;
            _autopilot = false;
            Status = $"done {Written} frames";
            Done = true;
        }

        void Update()
        {
            if (!_autopilot || _body == null || _cam == null || _motor == null || Done) return;
            float dt = Time.deltaTime;
            _t += dt;

            // Blending hands the body to the flock (FishFlockBlend turns the controller back on when it ends).
            if (_blend != null && _blend.IsBlending) return;
            if (_controller != null && _controller.enabled) _controller.enabled = false;
            if (_blend != null && blendAt >= 0f && !_blended && _t >= blendAt)
            {
                _blended = true;
                var flock = typeof(FishFlockBlend).GetMethod("FindNearestFlock", Flags)?.Invoke(_blend, null);
                if (flock != null) typeof(FishFlockBlend).GetMethod("StartBlend", Flags)?.Invoke(_blend, new[] { flock });
                if (_blend.IsBlending) return;
            }

            Vector3 me = _body.transform.position;
            Vector3 target = goal;
            if (hunt && NearestFish(me, out Vector3 fish)) target = fish;
            if (follow != null) target = follow.position;
            if (faceOrcas)
            {
                float best = float.MaxValue;
                foreach (var o in OrcaHunter.All)
                    if (o != null && (o.transform.position - me).sqrMagnitude < best)
                    {
                        best = (o.transform.position - me).sqrMagnitude;
                        target = o.transform.position;
                    }
            }
            Vector3 to = target - me;

            // Turn the camera's steering angles toward the target, as a player's mouse would.
            float yaw = (float)typeof(FishOrbitCamera).GetField("_yaw", Flags).GetValue(_cam);
            float pitch = (float)typeof(FishOrbitCamera).GetField("_pitch", Flags).GetValue(_cam);
            var want = Quaternion.LookRotation(to.sqrMagnitude > 0.01f ? to : _body.transform.forward, Vector3.up).eulerAngles;
            float wantPitch = Mathf.DeltaAngle(0f, want.x);
            yaw = Mathf.MoveTowardsAngle(yaw, want.y, turnRate * dt);
            pitch = Mathf.MoveTowards(pitch, Mathf.Clamp(wantPitch, -40f, 40f), turnRate * 0.6f * dt);
            typeof(FishOrbitCamera).GetField("_yaw", Flags).SetValue(_cam, yaw);
            typeof(FishOrbitCamera).GetField("_pitch", Flags).SetValue(_cam, pitch);

            _motor.SetInput(new FishMotor.ControlInput { AimDirection = _cam.AimDirection, Throttle = throttle, Boost = boost });

            if (_abilities != null)
            {
                if (sonarAt >= 0f && !_sonared && _t >= sonarAt && _abilities.SonarReady)
                {
                    _sonared = true;
                    typeof(SharkAbilities).GetMethod("DoSonar", Flags).Invoke(_abilities, null);
                }
                if (lungeRange > 0f && hunt && to.magnitude < lungeRange && _abilities.LungeReady &&
                    Vector3.Angle(_body.transform.forward, to) < 25f)
                    typeof(SharkAbilities).GetMethod("DoLunge", Flags).Invoke(_abilities, null);
            }
            if (orcasAt >= 0f && !_orcas && _t >= orcasAt)
            {
                _orcas = true;
                WorldEvents.ServerSummonOrcas();
            }
        }

        static void FlipRows(byte[] pixels, int rowBytes, int rows)
        {
            var row = new byte[rowBytes];
            for (int top = 0, bottom = rows - 1; top < bottom; top++, bottom--)
            {
                System.Buffer.BlockCopy(pixels, top * rowBytes, row, 0, rowBytes);
                System.Buffer.BlockCopy(pixels, bottom * rowBytes, pixels, top * rowBytes, rowBytes);
                System.Buffer.BlockCopy(row, 0, pixels, bottom * rowBytes, rowBytes);
            }
        }

        static bool NearestFish(Vector3 from, out Vector3 at)
        {
            at = default;
            float best = float.MaxValue;
            foreach (var bm in BoidsManager.All)
            {
                if (bm == null || bm.allFish == null) continue;
                foreach (var f in bm.allFish)
                {
                    if (f == null || !f.activeInHierarchy) continue;
                    float d = (f.transform.position - from).sqrMagnitude;
                    if (d < best) { best = d; at = f.transform.position; }
                }
            }
            return best < float.MaxValue;
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
            if (_controller != null) _controller.enabled = true;
        }
    }
}
