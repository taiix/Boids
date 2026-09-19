using FishGame;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(FishController))]
public class FishPlayer : NetworkBehaviour
{
    private FishController _fishController;
    private FishFlockBlend _flockBlend;
    public GameObject camera;

    // Assigned by MatchManager on the server at round start. The hook is where the shark
    // milestone will swap abilities/visuals; for now it just carries the role.
    [SyncVar(hook = nameof(OnRoleChanged))]
    public FishRole Role = FishRole.Fish;

    public bool IsShark => Role == FishRole.Shark;

    [Header("Eating")]
    [Tooltip("How close a food pellet must be to the mouth to be eaten.")]
    [SerializeField] float eatRange = 1.6f;
    [Tooltip("Forward distance from the fish pivot to its 'mouth'.")]
    [SerializeField] float mouthOffset = 1.0f;
    private InputAction _eatAction;

    private void OnRoleChanged(FishRole previous, FishRole current)
    {
        // TODO (shark milestone): enable SharkAbilities / swap model when current == Shark.
    }

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

        // Eat action (local player only): press E near a food pellet to eat it.
        _eatAction = new InputAction("Eat", InputActionType.Button);
        _eatAction.AddBinding("<Keyboard>/e");
        _eatAction.AddBinding("<Gamepad>/buttonWest");
        _eatAction.Enable();
    }

    private void Update()
    {
        if (!isLocalPlayer || _eatAction == null) return;
        if (_eatAction.WasPressedThisFrame())
            CmdEat();
    }

    // Server validates that a pellet is actually in mouth range before consuming it.
    [Command]
    private void CmdEat()
    {
        Vector3 mouth = transform.position + transform.forward * mouthOffset;
        FoodPellet best = null;
        float bestSqr = eatRange * eatRange;
        foreach (var pellet in FindObjectsByType<FoodPellet>(FindObjectsSortMode.None))
        {
            if (pellet == null || pellet.IsEaten) continue;
            float d = (pellet.transform.position - mouth).sqrMagnitude;
            if (d <= bestSqr) { bestSqr = d; best = pellet; }
        }
        if (best != null)
            best.Consume(GetComponent<FishVitals>());
    }

    public override void OnStopLocalPlayer()
    {
        base.OnStopLocalPlayer();

        if (_eatAction != null) { _eatAction.Disable(); _eatAction.Dispose(); _eatAction = null; }

        // The camera was detached from this fish, so it won't be destroyed with us. Clean it up.
        if (camera != null)
            Destroy(camera);
    }
}
