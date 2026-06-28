using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;


[RequireComponent(typeof(Rigidbody))]
public class FishController : NetworkBehaviour
{
    [SerializeField] private InputActionMap actionMap;
    private Rigidbody2D rb;


    private void Start()
    {
        rb = GetComponent<Rigidbody2D>();

    }

    public override void OnStartAuthority()
    {
        actionMap.FindAction("Move").performed += OnMovePerformed;

        actionMap.Enable();
    }
    public override void OnStopAuthority()
    {
        actionMap.FindAction("Move").performed -= OnMovePerformed;
        actionMap.Disable();
    }
    private void OnMovePerformed(InputAction.CallbackContext context)
    {
        Vector2 dir = context.ReadValue<Vector2>();
        Debug.Log($"Move performed: {dir}");
    }


}
