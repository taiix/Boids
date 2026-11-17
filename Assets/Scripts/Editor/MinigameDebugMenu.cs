using System;
using FishGame;
using Mirror;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Play-mode shortcuts to every minigame: each one swims your fish (swaps to a fish first if you're the
/// shark in the offline sandbox) to the nearest one that isn't done yet and starts it, so you don't have
/// to hunt for it in the fog. Tools > Reef Run > Minigames, or Ctrl+Shift+1..6.
/// </summary>
static class MinigameDebugMenu
{
    const string Root = "Tools/Reef Run/Minigames/";

    [MenuItem(Root + "Push a net ball %#1")] static void Trash() => WithFish(StartTrash);
    [MenuItem(Root + "Bite the turtle net (pretend partner) %#2")] static void Net() => WithFish(StartNet);
    [MenuItem(Root + "Seal a leak %#3")] static void Leak() => WithFish(f => StartStation<LeakStation>(f));
    [MenuItem(Root + "Scrub coral %#4")] static void Coral() => WithFish(f => StartStation<CoralScrubStation>(f));
    [MenuItem(Root + "Untangle a line %#5")] static void Line() => WithFish(f => StartStation<UntangleStation>(f));
    [MenuItem(Root + "Hide in the sand %#6")] static void Hide() => WithFish(StartHide);

    [MenuItem(Root + "Push a net ball %#1", true)]
    [MenuItem(Root + "Bite the turtle net (pretend partner) %#2", true)]
    [MenuItem(Root + "Seal a leak %#3", true)]
    [MenuItem(Root + "Scrub coral %#4", true)]
    [MenuItem(Root + "Untangle a line %#5", true)]
    [MenuItem(Root + "Hide in the sand %#6", true)]
    static bool Playing() => EditorApplication.isPlaying;

    // ------------------------------------------------------------------ the games

    static void StartStation<T>(FishPlayer fish) where T : MinigameStation
    {
        T best = null;
        float bestD = float.MaxValue;
        foreach (var s in MinigameStation.All)
            if (s is T t && !t.Done)
            {
                float d = (t.transform.position - fish.transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = t; }
            }
        if (best == null) { Debug.LogWarning($"[Minigames] No {typeof(T).Name} left to play (all done, or the island isn't loaded)."); return; }

        Vector3 dir = Vector3.ProjectOnPlane(fish.transform.position - best.transform.position, Vector3.up);
        if (dir.sqrMagnitude < 0.01f) dir = Vector3.back;
        Place(fish, best.transform.position + dir.normalized * 2.3f + Vector3.up * 0.9f, -dir);
        Next(() => best.Interact());
    }

    static void StartTrash(FishPlayer fish)
    {
        TrashMinigame best = null;
        float bestD = float.MaxValue;
        foreach (var t in UnityEngine.Object.FindObjectsByType<TrashMinigame>(FindObjectsInactive.Exclude))
            if (t.CanInteract)   // the net balls: the only trash with the push minigame (bags and scraps are pushed by swimming)
            {
                float d = (t.transform.position - fish.transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = t; }
            }
        if (best == null) { Debug.LogWarning("[Minigames] No net ball left to push."); return; }

        // behind it, the side away from its sinkhole
        var hole = Sinkhole.Nearest(best.transform.position, out _);
        Vector3 toHole = hole != null ? hole.Centre - best.transform.position : Vector3.forward;
        toHole.y = 0f;
        float r = best.TryGetComponent(out PushableTrash p) ? p.Radius : 1f;
        Place(fish, best.transform.position - toHole.normalized * (r + 1.2f) + Vector3.up * 0.3f, toHole);
        Next(() => best.Interact());
    }

    static void StartNet(FishPlayer fish)
    {
        TangledTurtle best = null;
        int rope = -1;
        Vector3 knot = default;
        float bestD = float.MaxValue;
        foreach (var t in TangledTurtle.All)
            if (t != null && t.DebugFirstKnot(out int r, out Vector3 at))
            {
                float d = (at - fish.transform.position).sqrMagnitude;
                if (d < bestD) { bestD = d; best = t; rope = r; knot = at; }
            }
        if (best == null) { Debug.LogWarning("[Minigames] No tangled turtle left."); return; }

        Vector3 look = Vector3.ProjectOnPlane(best.transform.position - knot, Vector3.up);
        Place(fish, knot - look.normalized * 0.4f + Vector3.up * 0.3f, look);
        Next(() => best.DebugOpenNet(fish, rope));
    }

    static void StartHide(FishPlayer fish)
    {
        var terrain = UnityEngine.Object.FindAnyObjectByType<TestTerrain>();
        if (terrain == null || !terrain.TryGetComponent(out MeshCollider seabed)) { Debug.LogWarning("[Minigames] No seabed here."); return; }
        Vector3 p = fish.transform.position;
        if (!seabed.Raycast(new Ray(new Vector3(p.x, seabed.bounds.max.y + 5f, p.z), Vector3.down), out var hit, seabed.bounds.size.y + 10f))
        { Debug.LogWarning("[Minigames] Couldn't find the sand below you."); return; }
        Place(fish, hit.point + Vector3.up * 0.6f, fish.transform.forward);
        Next(() => { if (!SandHide.DebugBuryNow(fish)) Debug.LogWarning("[Minigames] Couldn't dig in here (a minigame open?)."); });
    }

    // ------------------------------------------------------------------ helpers

    // This machine's fish. Offline as the shark, swap to a fish first (what F8 does) and carry on once it's in.
    static void WithFish(Action<FishPlayer> then)
    {
        var me = Local();
        if (me == null) { Debug.LogWarning("[Minigames] No player body yet - wait for the match to load."); return; }
        if (!me.IsSharkBody) { then(me); return; }
        if (!me.IsOfflineSandboxBody) { Debug.LogWarning("[Minigames] You're the shark - press F8 to be a fish first."); return; }
        typeof(FishPlayer).GetMethod("SwapBodyOffline", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.Invoke(me, null);
        int frames = 0;
        void Wait()
        {
            var fish = Local();
            if (fish != null && !fish.IsSharkBody && ++frames > 3) { EditorApplication.update -= Wait; then(fish); }
            else if (frames > 600 || !EditorApplication.isPlaying) EditorApplication.update -= Wait;
        }
        EditorApplication.update += Wait;
    }

    static FishPlayer Local()
    {
        if (NetworkClient.active && NetworkClient.localPlayer != null) return NetworkClient.localPlayer.GetComponent<FishPlayer>();
        foreach (var fp in FishPlayer.All)
            if (fp != null && fp.IsOfflineSandboxBody) return fp;
        return null;
    }

    static void Place(FishPlayer fish, Vector3 pos, Vector3 facing)
    {
        facing.y = 0f;
        if (facing.sqrMagnitude < 1e-4f) facing = fish.transform.forward;
        var rot = Quaternion.LookRotation(facing.normalized, Vector3.up);
        fish.transform.SetPositionAndRotation(pos, rot);
        if (fish.TryGetComponent(out Rigidbody rb) && !rb.isKinematic)
        {
            rb.position = pos;
            rb.rotation = rot;
            rb.linearVelocity = Vector3.zero;
        }
        foreach (var cam in UnityEngine.Object.FindObjectsByType<FishOrbitCamera>(FindObjectsInactive.Exclude))
            if (cam.Target == fish.transform) cam.TurnToward(facing, 15f, 0.1f);
    }

    // A couple of frames later, once the move has settled (and triggers have caught up).
    static void Next(Action a)
    {
        int frames = 0;
        void Tick()
        {
            if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
            if (++frames < 3) return;
            EditorApplication.update -= Tick;
            a();
        }
        EditorApplication.update += Tick;
    }
}
