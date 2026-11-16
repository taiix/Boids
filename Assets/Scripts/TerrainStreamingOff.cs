using UnityEngine;

/// <summary>
/// Switches CodenameLib's <see cref="TestTerrain"/> off once it has generated the seabed.
///
/// TestTerrain's only per-frame work is streaming InfiniteTerrain chunks around a viewer. The game never
/// uses that (ProceduralTerrainSurround builds the surround instead) and never assigns a viewer, so all
/// its LateUpdate did was log "[InfiniteTerrain] Viewer not assigned." every frame: a stack trace each
/// time, and a Player.log that grows for as long as the game runs. The package lives in PackageCache, so
/// it's turned off from here instead of edited.
///
/// Disabling the component is safe: <c>FindAnyObjectByType&lt;TestTerrain&gt;</c> still finds it, and
/// <c>Generate()</c> still works on it.
/// </summary>
static class TerrainStreamingOff
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Hook()
    {
        TestTerrain.OnCreatingDone.RemoveListener(DisableStreaming);
        TestTerrain.OnCreatingDone.AddListener(DisableStreaming);
    }

    static void DisableStreaming()
    {
        foreach (var t in Object.FindObjectsByType<TestTerrain>(FindObjectsSortMode.None))
            t.enabled = false;
    }
}
