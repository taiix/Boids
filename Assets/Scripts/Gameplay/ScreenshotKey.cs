using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace FishGame
{
    /// <summary>
    /// Press P anywhere in the game (builds too) for a 3840 x 2160 screenshot of what the camera sees - no HUD,
    /// whatever the window size. Saved to Pictures/Reef Run (falls back to the game's data folder).
    ///
    /// The frame is rendered into a 4K texture, read back from the GPU without stalling, and encoded to PNG on a
    /// worker thread, so taking one doesn't hitch the game. Ignored while typing in a text field.
    /// Spawns itself: nothing to place in a scene.
    /// </summary>
    public class ScreenshotKey : MonoBehaviour
    {
        const int Width = 3840, Height = 2160;
        static ScreenshotKey s_instance;
        static InputAction s_key;
        bool _busy;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (s_instance != null) return;
            var go = new GameObject("ScreenshotKey");
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<ScreenshotKey>();
        }

        /// <summary>Where screenshots go.</summary>
        public static string Folder
        {
            get
            {
                string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                return string.IsNullOrEmpty(pictures)
                    ? Path.Combine(Application.persistentDataPath, "Screenshots")
                    : Path.Combine(pictures, "Reef Run");
            }
        }

        void Update()
        {
            if (s_key == null)
            {
                s_key = new InputAction("screenshot", InputActionType.Button, "<Keyboard>/p");
                s_key.Enable();
            }
            if (!s_key.WasPressedThisFrame() || _busy || Typing()) return;
            Take();
        }

        /// <summary>Take one now (the P key does this).</summary>
        public static void Take()
        {
            if (s_instance == null) Boot();
            s_instance.Capture();
        }

        void Capture()
        {
            var cam = GameCamera();
            if (cam == null) return;
            var rt = RenderTexture.GetTemporary(new RenderTextureDescriptor(Width, Height, RenderTextureFormat.ARGB32, 24) { sRGB = true });
            var prev = cam.targetTexture;
            try
            {
                // HDRP draws the whole pipeline (lighting, fog, post) into our texture.
                var request = new RenderPipeline.StandardRequest { destination = rt };
                if (RenderPipeline.SupportsRenderRequest(cam, request)) RenderPipeline.SubmitRenderRequest(cam, request);
                else { cam.targetTexture = rt; cam.Render(); }
            }
            finally { cam.targetTexture = prev; }

            _busy = true;
            string path = Path.Combine(Folder, $"ReefRun_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png");
            AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32, req =>
            {
                RenderTexture.ReleaseTemporary(rt);
                if (req.hasError) { _busy = false; Debug.LogWarning("[Screenshot] Couldn't read the frame back."); return; }
                var pixels = req.GetData<byte>().ToArray();
                // PNG encoding takes a while at 4K: off the main thread.
                Task.Run(() =>
                {
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;   // HDRP leaves alpha anywhere: opaque
                        var png = ImageConversion.EncodeArrayToPNG(pixels, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB, Width, Height);
                        File.WriteAllBytes(path, png);
                        Debug.Log($"[Screenshot] Saved {path}");
                    }
                    catch (Exception e) { Debug.LogWarning($"[Screenshot] Couldn't save it: {e.Message}"); }
                    finally { _busy = false; }
                });
                MatchHud.Flash("SCREENSHOT SAVED", 1.2f);
            });
        }

        // The camera the player sees through: Camera.main if it's tagged (the game's isn't), else the game
        // camera drawn last to the screen.
        static Camera GameCamera()
        {
            if (Camera.main != null && Camera.main.isActiveAndEnabled) return Camera.main;
            Camera best = null;
            foreach (var c in Camera.allCameras)
                if (c.targetTexture == null && c.cameraType == CameraType.Game && (best == null || c.depth >= best.depth)) best = c;
            return best;
        }

        // A text field has focus (a player name, a lobby code): P is a letter there, not a shutter.
        static bool Typing()
        {
            foreach (var doc in FindObjectsByType<UIDocument>(FindObjectsInactive.Exclude))
            {
                var focused = doc.rootVisualElement?.panel?.focusController?.focusedElement as VisualElement;
                for (var e = focused; e != null; e = e.parent)
                    if (e is TextField) return true;
            }
            return false;
        }
    }
}
