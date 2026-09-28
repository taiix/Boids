using System.Collections.Generic;
using CodenameLib.ProceduralTerrain;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishGame
{
    /// <summary>
    /// Makes <see cref="ProceduralVegetation"/> scatter its coral identically for every player.
    ///
    /// It places everything with UnityEngine.Random, unseeded, so each machine got a different reef -
    /// a prey hiding behind a coral head on their screen could be in open water on the shark's, and the
    /// flock steered around coral only some players had. The package lives in PackageCache (edits there
    /// get overwritten), so instead we swap its "terrain ready" listener for one that seeds Random from
    /// the terrain's seed around the call and restores it afterwards, leaving everything else random.
    /// Placement also depends on seabed height, which the GPU erosion doesn't reproduce exactly - so
    /// <see cref="TerrainNetSync"/> makes the seabed identical first.
    /// </summary>
    static class SeededVegetation
    {
        static readonly HashSet<ProceduralVegetation> s_reefs = new HashSet<ProceduralVegetation>();
        static readonly List<ProceduralVegetation> s_dead = new List<ProceduralVegetation>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        // sceneLoaded runs after the scene's OnEnable (ProceduralVegetation has hooked the terrain
        // event) and before Start (TestTerrain.Start generates the terrain and fires it). It can fire
        // more than once during startup, so everything here is idempotent.
        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            foreach (var veg in Object.FindObjectsByType<ProceduralVegetation>(FindObjectsSortMode.None))
            {
                if (veg == null || !s_reefs.Add(veg)) continue;
                TestTerrain.OnCreatingDone.RemoveListener(veg.Init); // we run it, seeded
            }

            // A single listener of ours does seabed-then-reef in a fixed order, rather than relying on
            // the static event's listener order (which re-registering would shuffle).
            TestTerrain.OnCreatingDone.RemoveListener(OnTerrainGenerated);
            TestTerrain.OnCreatingDone.AddListener(OnTerrainGenerated);
        }

        static void OnTerrainGenerated()
        {
            // Settle the seabed first (the host's heights, or ours rounded to the shared grid), so the
            // reef is placed on the final heights.
            TerrainNetSync.OnLocalTerrainGenerated();
            Rebuild();
        }

        /// <summary>Re-place every reef on the current seabed (e.g. after the host's seabed replaced ours).</summary>
        public static void Rebuild()
        {
            Prune();
            foreach (var veg in s_reefs)
                InitSeeded(veg);
        }

        static void InitSeeded(ProceduralVegetation veg)
        {
            if (veg == null || !veg.isActiveAndEnabled) return;

            var terrain = Object.FindAnyObjectByType<TestTerrain>();
            int seed = terrain != null ? terrain.settings.seed : 0;

            var saved = Random.state;
            Random.InitState(unchecked(seed * 486187739 + 1013904223)); // derived, not the terrain's own stream
            try { veg.Init(); }
            finally { Random.state = saved; }
        }

        // Forget reefs whose scene has unloaded.
        static void Prune()
        {
            s_dead.Clear();
            foreach (var veg in s_reefs)
                if (veg == null) s_dead.Add(veg);
            foreach (var veg in s_dead)
                s_reefs.Remove(veg);
        }
    }
}
