using FishGame;
using Mirror;
using UnityEngine;

[RequireComponent(typeof(FishController))]
public class FishPlayer : NetworkBehaviour
{
    private FishController _fishController;
    private FishFlockBlend _flockBlend;
    public GameObject camera;

    private void Awake()
    {
        _fishController = GetComponent<FishController>();
        _fishController.enabled = false;

        // Local-player-only ability; disabled on remotes so they don't react to the key.
        if (TryGetComponent(out _flockBlend))
            _flockBlend.enabled = false;

        var cam = GetComponentInChildren<Camera>();

        camera = cam != null ? cam.gameObject : null;

        if (cam != null)
            camera.SetActive(false);
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();

        if (camera != null)
        {
            // Detach from the fish so the camera follows purely by script. Parenting it to the
            // interpolated Rigidbody causes jitter on fast turns; following via FishOrbitCamera
            // (in LateUpdate) is smooth. The prefab still carries the camera for easy spawning.
            camera.transform.SetParent(null, true);
            camera.SetActive(true);
        }

        _fishController.enabled = true;
        if (_flockBlend != null) _flockBlend.enabled = true;

        var orbit = camera != null ? camera.GetComponent<FishOrbitCamera>() : null;
        if (orbit == null && Camera.main != null) orbit = Camera.main.GetComponent<FishOrbitCamera>();
        if (orbit != null) orbit.SetTarget(transform);
    }

    public override void OnStopLocalPlayer()
    {
        base.OnStopLocalPlayer();

        // The camera was detached from this fish, so it won't be destroyed with us. Clean it up.
        if (camera != null)
            Destroy(camera);
    }
}
