using FishGame;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem.XR;

[RequireComponent(typeof(FishController))]
public class FishPlayer : NetworkBehaviour
{
    private FishController _fishController;
    public GameObject camera;

    private void Awake()
    {
        _fishController = GetComponent<FishController>();
        _fishController.enabled = false;

        var cam = GetComponentInChildren<Camera>();

        camera = cam != null ? cam.gameObject : null;

        _fishController.enabled = false;

        if (cam != null)
            camera.SetActive(false);
    }

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();

        if (camera != null) camera.SetActive(true);

        _fishController.enabled = true;

        var cam = Camera.main ? Camera.main.GetComponent<FishOrbitCamera>() : null;
        if (cam != null) cam.SetTarget(transform);
    }
}
