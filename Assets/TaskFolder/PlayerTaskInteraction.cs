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
    }
    private void OnPuzzleMove(InputAction.CallbackContext ctx)
    {
        if (!IsHolding || !task.isWorkedOn || isFake)
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

    public void SubmitDirection(Direction direction)
    {
        input.Add(direction);
        Transform currentChild = uiElement.transform.GetChild(input.Count-1).GetChild(0);
        currentChild.gameObject.SetActive(true);

        RectTransform rect = currentChild.GetComponent<RectTransform>();
        rect.localRotation = Quaternion.Euler(0f, 0f, map[direction]);

        task.ReceiveInput(this, direction, input.Count-1);
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

    private void OnTriggerStay(Collider other)
    {
        if (other.CompareTag("Finish"))
        {

            if (IsHolding && !task.isWorkedOn)
            {
                fishMotor = GetComponent<FishMotor>();
                task.StartTask(this, otherFake);
                GetComponent<Rigidbody>().linearVelocity = Vector3.zero;
                fishMotor.enabled = false;
            }
            else if (!IsHolding && task.isWorkedOn)
            {
                fishMotor = GetComponent<FishMotor>();
                fishMotor.enabled = true;
                task.isWorkedOn = false;

            }
            else if (IsHolding && task.isWorkedOn)
            {
                //Vector2 input = _puzzleMove.ReadValue<Vector2>();
                //ProcessInput(input);
            }
        }
    }


}
