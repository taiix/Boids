using FishGame;
using UnityEngine;

/// <summary>
/// On the player fish: finds <see cref="IInteractable"/>s whose trigger it swims into and presses E on
/// them. Only the fish this machine's player drives does anything - the other players' copies here
/// ignore triggers, or one E would fire for every fish near the trash.
/// </summary>
public class FishInteract : MonoBehaviour
{
    IInteractable _interactable;
    FishPlayer _player;
    bool _leftWhileBusy;

    public IInteractable Current => _interactable;

    bool IsLocal => _player != null && (_player.isLocalPlayer || _player.IsOfflineSandboxBody);

    void Awake() => _player = GetComponentInParent<FishPlayer>();

    void Update()
    {
        if (_interactable == null) return;
        // Destroyed (sunk trash) or we stopped being the local fish: let go.
        if (_interactable is Object o && o == null) { _interactable = null; return; }
        if (!IsLocal) { Release(); return; }

        if ((_interactable.CanInteract || _interactable.IsBusy) && GameInput.Interact.WasPressedThisFrame())
            _interactable.Interact();

        // We left its trigger while it was busy: let go once it's done.
        if (_leftWhileBusy && !_interactable.IsBusy) Release();
    }

    void OnDisable() => Release();

    void OnTriggerEnter(Collider other)
    {
        if (!IsLocal) return;
        var interactable = other.GetComponentInParent<IInteractable>();
        if (interactable == null) return;
        if (interactable == _interactable) { _leftWhileBusy = false; return; }
        if (_interactable != null && _interactable.IsBusy) return;   // don't switch mid-game
        Release();
        _interactable = interactable;
        _interactable.Focus();
    }

    void OnTriggerExit(Collider other)
    {
        if (_interactable == null || other.GetComponentInParent<IInteractable>() != _interactable) return;
        if (_interactable.IsBusy) _leftWhileBusy = true;
        else Release();
    }

    void Release()
    {
        _leftWhileBusy = false;
        if (_interactable == null) return;
        var it = _interactable;
        _interactable = null;
        if (!(it is Object o) || o != null) it.LoseFocus();
    }
}
