using FishGame;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;



public class PlayerTaskInteraction : MonoBehaviour
{
    public MoveObjectsTask task;
    private List<Direction> input = new List<Direction>();

    public bool isFake;
    public PlayerTaskInteraction otherFake;
    FishMotor fishMotor;

    private InputAction _holdAction;
    public bool IsHolding { get; private set; }
    private InputAction _puzzleMove;
    public Vector2 PuzzleInput { get; private set; }
    private InputAction _submitAction;   // Enter — check the full sequence
    private InputAction _deleteAction;   // Backspace — delete the last arrow

    public GameObject uiElement;
    //public bool isAtInteractionSpot;

    Dictionary<Direction, int> map = new Dictionary<Direction, int>
    {
        {Direction.Down,0 },
        {Direction.Right,90 },
        {Direction.Up,180 },
        {Direction.Left,270 }
    };

    void Awake()
    {
        _holdAction = new InputAction("Hold", InputActionType.Button);
        _holdAction.AddBinding("<Mouse>/leftButton");
        _holdAction.AddBinding("<Gamepad>/rightTrigger");

        _puzzleMove = new InputAction("PuzzleMove", InputActionType.Value, "Vector2");

        _puzzleMove.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");

        _puzzleMove.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow")
            .With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow")
            .With("Right", "<Keyboard>/rightArrow");

        _puzzleMove.AddBinding("<Gamepad>/dpad");

        _puzzleMove.performed += OnPuzzleMove;

        _holdAction.started += ctx => IsHolding = true;
        _holdAction.canceled += ctx => IsHolding = false;

        _submitAction = new InputAction("SubmitSequence", InputActionType.Button);
        _submitAction.AddBinding("<Keyboard>/enter");
        _submitAction.AddBinding("<Keyboard>/numpadEnter");
        _submitAction.AddBinding("<Gamepad>/start");
        _submitAction.performed += ctx => { if (!isFake && task != null && task.isWorkedOn) SubmitCurrent(); };

        _deleteAction = new InputAction("DeleteArrow", InputActionType.Button);
        _deleteAction.AddBinding("<Keyboard>/backspace");
        _deleteAction.AddBinding("<Gamepad>/buttonEast");
        _deleteAction.performed += ctx => { if (!isFake && task != null && task.isWorkedOn) DeleteLast(); };
    }
    private void OnPuzzleMove(InputAction.CallbackContext ctx)
    {
        // During the input phase (isWorkedOn) just press the directions — no holding required.
        if (task == null || !task.isWorkedOn || isFake)
            return;

        Vector2 input = ctx.ReadValue<Vector2>();

        if (input == Vector2.up)
            SubmitDirection(Direction.Up);
        else if (input == Vector2.down)
            SubmitDirection(Direction.Down);
        else if (input == Vector2.left)
            SubmitDirection(Direction.Left);
        else if (input == Vector2.right)
            SubmitDirection(Direction.Right);
    }
    void OnEnable()
    {
        _holdAction.Enable();
        _puzzleMove.Enable();
        _submitAction.Enable();
        _deleteAction.Enable();

        _puzzleMove.performed += ctx =>
        {
            PuzzleInput = ctx.ReadValue<Vector2>();
        };

        _puzzleMove.canceled += ctx =>
        {
            PuzzleInput = Vector2.zero;
        };
    }

    void OnDisable()
    {
        _holdAction.Disable();
        _puzzleMove.Disable();
        _submitAction.Disable();
        _deleteAction.Disable();
    }
    public void ShowSequence(List<Direction> sequence)
    {
        if (!isFake)
        {
            Debug.Log("Showing Sequence");
            foreach (var i in sequence)
            {
                Debug.Log(i);
            }
            for (int i = 0; i < uiElement.transform.childCount; i++)
            {
                Transform currentChild = uiElement.transform.GetChild(i).GetChild(0);
                currentChild.gameObject.SetActive(true);

                RectTransform rect = currentChild.GetComponent<RectTransform>();
                rect.localRotation = Quaternion.Euler(0f, 0f, map[sequence[i]]);
            }

        }
        else
        {
            Debug.Log("Your teammate is giving you the sequence");
            foreach (var i in sequence)
            {
                Debug.Log("Press " + i);
            }
        }


    }

    public void PrepareInput(List<Direction> sequence)
    {
        if (!isFake)
        {
            input.Clear();
            for (int i = 0; i < uiElement.transform.childCount; i++)
            {
                uiElement.transform.GetChild(i).GetChild(0).gameObject.SetActive(false);
            }
        }
        else
        {
            StartCoroutine(Complete(sequence));
        }

    }
    IEnumerator Complete(List<Direction> sequence)
    {
        yield return new WaitForSeconds(0.7f);


        List<Direction> fakeInput = new List<Direction>();


        foreach (Direction d in sequence)
        {
            yield return new WaitForSeconds(0.7f);

            fakeInput.Add(d);

            Debug.Log("Fake pressed " + d);
            task.ReceiveInput(GetComponent<PlayerTaskInteraction>(), d, fakeInput.Count - 1);
        }
    }

    // Add an arrow to the next open box. Does NOT check yet — the player presses Enter to submit
    // the whole sequence (see SubmitCurrent), and Backspace to delete the last one (see DeleteLast).
    public void SubmitDirection(Direction direction)
    {
        int slots = uiElement != null ? uiElement.transform.childCount : 0;
        if (input.Count >= slots) return; // all boxes filled — press Enter to check

        input.Add(direction);
        Transform arrow = uiElement.transform.GetChild(input.Count - 1).GetChild(0);
        arrow.gameObject.SetActive(true);
        arrow.GetComponent<RectTransform>().localRotation = Quaternion.Euler(0f, 0f, map[direction]);

        if (task != null) task.OnInputChanged(input.Count);
    }

    /// <summary>Backspace — remove the most recently entered arrow.</summary>
    public void DeleteLast()
    {
        if (input.Count == 0) return;
        uiElement.transform.GetChild(input.Count - 1).GetChild(0).gameObject.SetActive(false);
        input.RemoveAt(input.Count - 1);
        if (task != null) task.OnInputChanged(input.Count);
    }

    /// <summary>Enter — submit the entered sequence to be checked all at once.</summary>
    public void SubmitCurrent()
    {
        if (task != null) task.SubmitSequence(this, input);
    }
    void ProcessInput(Vector2 input)
    {
        if (input.y > 0)
            SubmitDirection(Direction.Up);
        else if (input.y < 0)
            SubmitDirection(Direction.Down);
        else if (input.x < 0)
            SubmitDirection(Direction.Left);
        else if (input.x > 0)
            SubmitDirection(Direction.Right);
    }

    /// <summary>Freeze/unfreeze the player while it works on a task. Called by MoveObjectsTask.
    /// Disables steering, movement and (on the shark) the bite, so the task's WASD/left-click input
    /// doesn't also drive the character.</summary>
    public void LockMovement(bool locked)
    {
        if (fishMotor == null) fishMotor = GetComponent<FishMotor>();
        if (fishMotor != null) fishMotor.enabled = !locked;
        if (TryGetComponent<FishController>(out var fc)) fc.enabled = !locked;
        if (TryGetComponent<SharkAbilities>(out var sa)) sa.enabled = !locked;
        if (locked && TryGetComponent<Rigidbody>(out var rb)) rb.linearVelocity = Vector3.zero;
    }

    // (The task is started by the TaskStation via E, and its movement lock is released by
    // MoveObjectsTask when the task finishes — so no per-frame trigger handling is needed here.)


}
