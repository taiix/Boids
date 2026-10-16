using UnityEngine;

namespace FishGame
{
    /// <summary>
    /// The looks the water effects use (bubbles, the shockwave of a bite, splashes, the glow on
    /// fish teammates), in one asset so they can be swapped in the Inspector. Lives at
    /// Resources/WaterFxLibrary; built by FishGame > Build Water FX Assets.
    /// </summary>
    [CreateAssetMenu(menuName = "FishGame/Water FX Library", fileName = "WaterFxLibrary")]
    public class WaterFxLibrary : ScriptableObject
    {
        [Tooltip("Small realistic bubble (a sprite: bright thin rim, a glint, clear middle).")]
        public Material bubble;
        [Tooltip("Mesh-mode bubble without underwater fog, for the exit's curtain seen from far off.")]
        public Material bubbleFar;
        [Tooltip("Ring of buckling water bursting out from a bite (4x4 flipbook, fading as it grows).")]
        public Material shockwave;
        [Tooltip("Soft glow drawn over fish teammates, through fog and rocks (fish screens only).")]
        public Material allyGlow;
        [Tooltip("Water droplets thrown up where a body breaks the surface (16 drops rendered in Blender; " +
                 "stretched along their flight).")]
        public Material droplet;
        [Tooltip("Real 3D water for the splashes (refractive, lit by the scene).")]
        public Material splashWater;
        [Tooltip("A body diving in: the crown of water it throws up, one mesh per simulated frame (30 fps), from " +
                 "the Blender liquid simulation (Blender/WaterSplash).")]
        public Mesh[] entrySplash;
        [Tooltip("A body leaping out: the column of water it drags up, rising and falling back (same kind).")]
        public Mesh[] leapSplash;
        [Tooltip("Simulated frames a second of the splash meshes (a body's size slows them further).")]
        public float splashFps = 30f;
        [Tooltip("HDRP water decal pushing the ocean down (or up) where a body went through: a smooth hollow.")]
        public Material waterCrater;
        [Tooltip("HDRP water decal raising a ring wave on the ocean, spreading out from a splash.")]
        public Material waterRing;
        [Tooltip("Fine spray hanging over a splash (lit, soft).")]
        public Material mist;
        [Tooltip("Ring spreading across the surface from a splash (4x4 flipbook, fading as it grows).")]
        public Material ripple;
        [Tooltip("Rope of the trawler's net (WorldEvents).")]
        public Material trawlNet;
        [Tooltip("The trawler on the surface, dragging its net across the arena (WorldEvents).")]
        public GameObject trawlerBoat;

        static WaterFxLibrary s_library;
        static bool s_looked;

        public static WaterFxLibrary Get()
        {
            if (s_library == null && !s_looked)
            {
                s_looked = true;
                s_library = Resources.Load<WaterFxLibrary>("WaterFxLibrary");
                if (s_library == null) Debug.LogWarning("[WaterFx] No Resources/WaterFxLibrary - water effects are off.");
            }
            return s_library;
        }
    }
}
