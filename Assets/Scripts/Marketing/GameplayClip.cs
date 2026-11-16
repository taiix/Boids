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
        [Tooltip("Seconds into the clip to lunge whatever is ahead (negative = never). Used for breaches.")]
        public float lungeAt = -1f;
        [Tooltip("Steering pitch limit for the autopilot (the camera allows up to 85).")]
        public float pitchLimit = 40f;
        [Tooltip("Free-look orbit: the camera swings from freeYawFrom to freeYawTo degrees over the clip, " +
                 "as a player holding free-look would (NaN = leave the camera behind the body).")]
        public float freeYawFrom = float.NaN, freeYawTo = float.NaN;
        public float freePitch;

        FishPlayer _body;
        FishOrbitCamera _cam;
        FishMotor _motor;
        FishController _controller;
        SharkAbilities _abilities;
        FishFlockBlend _blend;
        bool _blended;
        bool _autopilot, _sonared, _orcas, _lunged;
        float _t, _length = 1f;
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
            Current._length = Mathf.Max(0.1f, seconds + warmup);
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
            int w = Screen.width, h = Screen.height;
            var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
            bool flip = SystemInfo.graphicsUVStartsAtTop;

            // Raw frames go to ffmpeg, which encodes them on the GPU (AMF/NVENC). The old path encoded a JPEG per
            // frame on the CPU and allocated 8 MB per frame, which starved the fish jobs and caused GC hitches.
            var writer = new FrameWriter(dir, w, h, flip);
            if (!writer.Start(out string error))
            {
                Status = "error: " + error;
                Debug.LogError("[GameplayClip] " + error);
                Done = true;
                yield break;
            }
            Status = "recording";
            while (Time.unscaledTime < end)
            {
                yield return new WaitForEndOfFrame();
                float now = Time.unscaledTime;
                if (now < start || now - last < gap) continue;
                last = now;
                if (Screen.width != w || Screen.height != h) continue; // the pipe has a fixed size
                ScreenCapture.CaptureScreenshotIntoRenderTexture(rt);
                float t = now - start;
                Interlocked.Increment(ref _inFlight);
                AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32, req =>
                {
                    try { if (!req.hasError) writer.Push(req.GetData<byte>(), t); }
                    finally { Interlocked.Decrement(ref _inFlight); }
                });
            }
            Status = "encoding";
            while (Volatile.Read(ref _inFlight) > 0) yield return null;
            var finish = Task.Run(writer.Finish);
            while (!finish.IsCompleted) yield return null;
            _written = writer.Written;
            if (writer.Dropped > 0) Debug.LogWarning($"[GameplayClip] {name}: dropped {writer.Dropped} frames (encoder behind)");
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

            // The free-look swing runs even while the flock steers the body.
            if (!float.IsNaN(freeYawFrom))
            {
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_t / _length));
                typeof(FishOrbitCamera).GetField("_freeYaw", Flags).SetValue(_cam, Mathf.Lerp(freeYawFrom, float.IsNaN(freeYawTo) ? freeYawFrom : freeYawTo, k));
                typeof(FishOrbitCamera).GetField("_freePitch", Flags).SetValue(_cam, freePitch);
            }

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
            pitch = Mathf.MoveTowards(pitch, Mathf.Clamp(wantPitch, -pitchLimit, pitchLimit), turnRate * 0.6f * dt);
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
                if (lungeAt >= 0f && !_lunged && _t >= lungeAt && _abilities.LungeReady)
                {
                    _lunged = true;
                    typeof(SharkAbilities).GetMethod("DoLunge", Flags).Invoke(_abilities, null);
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

    /// <summary>Streams raw RGBA frames to an ffmpeg process (hardware HEVC) from a background thread, with pooled
    /// buffers so recording allocates nothing per frame. Writes capture.mkv and frames.txt (frame index -> time).</summary>
    sealed class FrameWriter
    {
        readonly string _dir;
        readonly int _w, _h;
        readonly bool _flip;
        readonly System.Collections.Concurrent.BlockingCollection<(byte[] buf, float t)> _queue = new(); // bounded by the pool
        readonly System.Collections.Concurrent.ConcurrentBag<byte[]> _pool = new();
        readonly System.Text.StringBuilder _times = new();
        System.Diagnostics.Process _proc;
        Thread _thread;
        int _written, _dropped;
        public int Written => Volatile.Read(ref _written);
        public int Dropped => Volatile.Read(ref _dropped);

        public FrameWriter(string dir, int w, int h, bool flip) { _dir = dir; _w = w; _h = h; _flip = flip; }

        // The Editor may have been started before ffmpeg was put on PATH, so also look where winget installs it.
        static string FfmpegPath()
        {
            foreach (var d in (System.Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'))
                if (d.Length > 0 && File.Exists(Path.Combine(d, "ffmpeg.exe"))) return Path.Combine(d, "ffmpeg.exe");
            string links = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Links", "ffmpeg.exe");
            return File.Exists(links) ? links : "ffmpeg";
        }

        public bool Start(out string error)
        {
            error = null;
            foreach (var f in Directory.GetFiles(_dir)) File.Delete(f);
            for (int i = 0; i < 10; i++) _pool.Add(new byte[_w * _h * 4]);
            string encoder = SystemInfo.graphicsDeviceVendor.ToLowerInvariant().Contains("nvidia")
                ? "-c:v hevc_nvenc -preset p7 -rc constqp -qp 14"
                : "-c:v hevc_amf -quality quality -rc cqp -qp_i 14 -qp_p 14";
            // The readback is already top-down where UVs start at the top (D3D, Vulkan, Metal); flip only on GL.
            string vf = _flip ? "format=nv12" : "vflip,format=nv12";
            var psi = new System.Diagnostics.ProcessStartInfo(FfmpegPath(),
                $"-hide_banner -loglevel error -y -f rawvideo -pix_fmt rgba -s {_w}x{_h} -r 60 -i - -vf {vf} {encoder} \"{Path.Combine(_dir, "capture.mkv")}\"")
            {
                UseShellExecute = false, RedirectStandardInput = true, RedirectStandardError = true, CreateNoWindow = true,
            };
            try { _proc = System.Diagnostics.Process.Start(psi); }
            catch (System.Exception e) { error = "could not start ffmpeg: " + e.Message; return false; }
            _proc.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) Debug.LogWarning("[ffmpeg] " + e.Data); };
            _proc.BeginErrorReadLine();
            _thread = new Thread(Pump) { IsBackground = true, Priority = System.Threading.ThreadPriority.BelowNormal, Name = "GameplayClip writer" };
            _thread.Start();
            return true;
        }

        /// <summary>Main thread (readback callback): copy into a pooled buffer, or drop the frame if the encoder is behind.</summary>
        public void Push(Unity.Collections.NativeArray<byte> data, float t)
        {
            if (!_pool.TryTake(out var buf)) { Interlocked.Increment(ref _dropped); return; }
            data.CopyTo(buf);
            _queue.Add((buf, t));
        }

        void Pump()
        {
            var stdin = _proc.StandardInput.BaseStream;
            int index = 0;
            foreach (var (buf, t) in _queue.GetConsumingEnumerable())
            {
                try { stdin.Write(buf, 0, buf.Length); }
                catch (System.Exception e) { Debug.LogWarning("[GameplayClip] pipe: " + e.Message); _pool.Add(buf); continue; }
                _times.AppendLine($"{index:0000}.png {t.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture)}");
                index++;
                Interlocked.Increment(ref _written);
                _pool.Add(buf);
            }
        }

        public void Finish()
        {
            _queue.CompleteAdding();
            _thread.Join();
            try { _proc.StandardInput.Close(); } catch { }
            _proc.WaitForExit(120000);
            File.WriteAllText(Path.Combine(_dir, "frames.txt"), _times.ToString());
        }
    }
}
