using UnityEngine;

namespace FishGame
{
    /// <summary>What a shot is captured for.</summary>
    public enum ShowcaseOutput
    {
        Screenshot = 0,   // a store screenshot: Capture All, NN_slug.png at the director's resolution
        CapsulePlate = 1, // artwork for a Steam capsule (title added afterwards): Capture Capsule Plates, slug.png
    }

    /// <summary>
    /// One marketing screenshot: a camera setup plus everything that has to be true in the game for it.
    ///
    /// This object's transform IS the camera (move it in the Scene view, or use the inspector's
    /// "Set From Scene View"). Children carry the rest:
    ///   * <see cref="ShowcaseActor"/> marks - the shark, player fish, a school, a tangled turtle... placed
    ///     where they must be at the moment of capture.
    ///   * Lights - on only while this shot is active (key / fill / rim for this frame).
    ///
    /// <see cref="MarketingShowcase"/> activates the shot in Play mode: it spawns the actors from the real game
    /// prefabs, runs the game's own systems (flocking, blood, bubbles, world events) for
    /// <see cref="settleSeconds"/> at a fixed 60 fps so the result is the same every time, then freezes time
    /// for the capture.
    /// </summary>
    public class ShowcaseShot : MonoBehaviour
    {
        [Tooltip("File name part: the screenshot is saved as NN_<slug>.png (NN = its order under the director).")]
        public string slug = "shot";
        [Tooltip("What this screenshot has to say about the game (for whoever tunes it next).")]
        [TextArea(2, 4)] public string purpose;
        [Tooltip("Skipped by Capture All / Capture Capsule Plates when off.")]
        public bool includeInCaptureAll = true;
        [Tooltip("Screenshot (Capture All) or the art plate for a Steam capsule (Capture Capsule Plates).")]
        public ShowcaseOutput output = ShowcaseOutput.Screenshot;
        [Tooltip("Pixel size of the saved image. 0 = the director's width/height. Capsule plates use their capsule's size or a multiple of it.")]
        public Vector2Int resolution;
        [Tooltip("Supersampling for this shot. 0 = the director's.")]
        [Range(0, 3)] public int supersample;

        [Header("Camera")]
        [Range(10f, 100f)] public float fieldOfView = 50f;
        [Tooltip("Whose eyes: the shark's or the fish's grade (CreatureVision), as in the game. Unassigned = the plain scene grade.")]
        public FishRole grade = FishRole.Shark;
        [Tooltip("The seabed is regenerated every run (GPU erosion drifts it by up to a few metres): keep the camera at least this far above it.")]
        public float seabedClearance = 0.8f;
        [Tooltip("> 0: put the camera this high above the seabed under it (for shots framed on the sand), instead of its own height.")]
        public float cameraHeightAboveSeabed;
        [Tooltip("Ride a bone of this shot's shark instead (the main menu rides Bone022, the head, looking out of the mouth). Empty = off.")]
        public string cameraMountBone = "";
        public Vector3 cameraMountPosition;
        public Vector3 cameraMountEuler;
        [Tooltip("Near clip plane for this shot (a camera inside the mouth needs ~0.02 to see the teeth). 0 = the camera's own.")]
        public float nearClip;

        [Header("Depth of field (manual ranges, metres; 0 = off)")]
        public float nearBlurStart;
        public float nearBlurEnd;
        public float farBlurStart;
        public float farBlurEnd;

        [Header("Grade (on top of the game's own)")]
        [Tooltip("EV added before tonemapping.")]
        [Range(-2f, 2f)] public float exposure;
        [Range(-30f, 30f)] public float contrast;
        [Range(-30f, 30f)] public float saturation;
        [Tooltip("Chromatic aberration for this shot (the role grades use 0.15+, which smears small fish at screenshot size). Negative = keep the game's.")]
        [Range(-1f, 1f)] public float chromaticAberration = 0.03f;

        [Header("Sun")]
        [Tooltip("Multiplier on the scene's sun for this shot (the key light underwater).")]
        [Range(0f, 3f)] public float sunIntensity = 1f;
        public bool overrideSunDirection;
        public Vector3 sunEuler = new Vector3(52f, 285f, 0f);

        [Header("Simulation")]
        [Tooltip("Seconds of game time simulated before the capture (schools settle, blood spreads, events ramp in).")]
        [Min(0.5f)] public float settleSeconds = 5f;
        [Tooltip("Seeds UnityEngine.Random and every actor, so the same shot comes out the same.")]
        public int seed = 1;
        [Tooltip("A world event running during the shot (the real MatchManager event, played offline).")]
        public WorldEventKind worldEvent = WorldEventKind.None;
        [Tooltip("How long the event has been running when the shot starts (conditions take ~4 s to set in; the trawler's net has come this many seconds x its speed).")]
        public float eventAge = 8f;
        [Tooltip("Direction a reef condition drifts / the current flows (degrees around Y).")]
        public float eventHeading;
        [Tooltip("Trawler: which arena wall it comes from (ArenaWalls index); eventAge then sets how far the net has come.")]
        public int trawlerWall;
        [Tooltip("Scene objects (by name) hidden for this shot only, e.g. clutter that crosses the frame.")]
        public string[] hideObjects = new string[0];

        /// <summary>The actor marks under this shot.</summary>
        public ShowcaseActor[] Actors => GetComponentsInChildren<ShowcaseActor>(true);

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.9f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawFrustum(Vector3.zero, fieldOfView, 6f, 0.2f, 16f / 9f);
        }
    }
}
