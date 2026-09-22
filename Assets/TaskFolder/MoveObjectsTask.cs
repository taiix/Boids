using System.Collections;
using System.Collections.Generic;
using FishGame;
using UnityEngine;

public class MoveObjectsTask : TaskBase
{
    public int rounds = 8;
    public int sequenceLength = 6;
    private int currentRound;
    private List<Direction> currentSequence;

    private PlayerTaskInteraction caller;
    private PlayerTaskInteraction receiver;

    public bool taskActive, isWorkedOn;
    public bool offline = true;

    [Header("Single-player test")]
    [Tooltip("One player both SEES and INPUTS the sequence (memorize-then-repeat). For real 2-player, leave off.")]
    public bool solo = false;
    [Tooltip("Solo mode: seconds the sequence is shown before you repeat it from memory.")]
    public float showSeconds = 2.5f;

    [Header("Feedback & completion")]
    [Tooltip("On-screen status / correct-wrong text (a SequenceTaskDisplay on the task Canvas).")]
    [SerializeField] SequenceTaskDisplay display;
    [Tooltip("Objects removed from the map when the WHOLE task is completed (e.g. the sea urchins).")]
    [SerializeField] GameObject[] propsToRemove;
    [Tooltip("Pause between rounds / before finishing so the feedback is readable.")]
    [SerializeField] float betweenRounds = 1.2f;

    Coroutine _showCo;

    public override void Begin(TaskInteractor interactor)
    {
        if (IsActive) return;

        var callerPti = interactor != null ? interactor.GetComponent<PlayerTaskInteraction>() : null;
        if (callerPti == null)
        {
            Debug.LogWarning("[MoveObjectsTask] interactor has no PlayerTaskInteraction — started in demo mode (no sequence).");
            IsActive = true;
            return;
        }

        // Solo test mode: the SAME player both sees and inputs — no second player / fake needed.
        var receiverPti = solo ? callerPti : callerPti.otherFake;
        if (receiverPti == null)
        {
            Debug.LogWarning("[MoveObjectsTask] no receiver — enable 'solo', or assign caller.otherFake.");
            return;
        }

        IsActive = true;
        callerPti.LockMovement(true);
        StartTask(callerPti, receiverPti);
    }

    public void PrepareTask(PlayerTaskInteraction p1)
    {
        if (offline)
        {

        }
        else
        {

        }
    }

    public void StartTask(PlayerTaskInteraction p1, PlayerTaskInteraction p2)
    {
        caller = p1;
        receiver = p2;

        currentRound = 0;
        taskActive = true;
        if (caller.uiElement != null) caller.uiElement.SetActive(true);

        GenerateSequence();
        SendSeqToReceiver();
    }

    void GenerateSequence()
    {
        currentSequence = new List<Direction>();
        for (int i = 0; i < sequenceLength; i++)
            currentSequence.Add((Direction)Random.Range(0, 4));
    }

    void SendSeqToReceiver()
    {
        if (solo)
        {
            // Memorize-then-repeat: show the sequence, then (after showSeconds) clear it and let the
            // same player input it from memory. Input is gated off (isWorkedOn=false) while it's shown.
            isWorkedOn = false;
            if (display != null) display.SetStatus("Memorize the sequence…");
            caller.ShowSequence(currentSequence);
            if (_showCo != null) StopCoroutine(_showCo);
            _showCo = StartCoroutine(SoloShowThenInput());
        }
        else
        {
            isWorkedOn = true;
            caller.ShowSequence(currentSequence);
            receiver.PrepareInput(currentSequence);
        }
        Debug.Log("Sequence Sent");
    }

    IEnumerator SoloShowThenInput()
    {
        yield return new WaitForSeconds(showSeconds);
        receiver.PrepareInput(currentSequence); // clear the slots + reset input
        isWorkedOn = true;                       // now accept the player's input
        if (display != null)
        {
            display.SetStatus($"Repeat the sequence  (0/{sequenceLength})");
            display.SetControls("Backspace = delete last arrow     •     Enter = submit");
        }
    }

    // The player fills / clears boxes; update the prompt (and tell them when they can press Enter).
    public void OnInputChanged(int count)
    {
        if (display == null || !isWorkedOn) return;
        display.SetStatus(count >= sequenceLength
            ? "All boxes filled — press ENTER to check"
            : $"Repeat the sequence  ({count}/{sequenceLength})");
    }

    // Player pressed Enter — judge the WHOLE entered sequence at once.
    public void SubmitSequence(PlayerTaskInteraction player, List<Direction> input)
    {
        if (player != receiver || !isWorkedOn || input.Count < sequenceLength) return;
        isWorkedOn = false; // stop taking input while we judge

        bool correct = true;
        for (int i = 0; i < sequenceLength; i++)
            if (input[i] != currentSequence[i]) { correct = false; break; }

        if (correct) SequenceComplete();
        else FailTask();
    }

    public void ReceiveInput(PlayerTaskInteraction player, Direction input, int position)
    {
        if (player != receiver || !isWorkedOn)
            return;

        if (CheckInput(input, position))
        {
            Debug.Log("Correct direction");
            if (position >= sequenceLength - 1)
                SequenceComplete();
        }
        else
        {
            // Wrong direction → the whole task fails; the player must start over (press E again).
            Debug.Log("Wrong Input");
            FailTask();
        }
    }

    bool CheckInput(Direction input, int position)
    {
        if(input != currentSequence[position])
        {
            return false;
        }

        return true;
    }

    void SequenceComplete()
    {
        Debug.Log("Whole sequence complete");
        isWorkedOn = false; // stop taking input during the transition
        if (display != null) { display.SetStatus(null); display.SetControls(null); }
        currentRound++;

        if (currentRound >= rounds)
        {
            if (display != null) display.ShowFeedback("Correct!", new Color(0.35f, 0.9f, 0.45f));
            StartCoroutine(FinishAfter(betweenRounds));
        }
        else
        {
            if (display != null) display.ShowFeedback("Correct! Next…", new Color(0.35f, 0.9f, 0.45f));
            StartCoroutine(NextRoundAfter(betweenRounds));
        }
    }

    IEnumerator NextRoundAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        SwapRoles();
        GenerateSequence();
        SendSeqToReceiver();
    }

    IEnumerator FinishAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        FinishTask();
    }

    // Wrong input: reset everything and let the player start over.
    void FailTask()
    {
        taskActive = false;
        isWorkedOn = false;
        IsActive = false;
        StopAllCoroutines();
        if (display != null) { display.SetStatus(null); display.SetControls(null); display.ShowFeedback("Wrong! Start over.", new Color(0.95f, 0.4f, 0.4f)); }
        if (caller != null)
        {
            caller.LockMovement(false);
            if (caller.uiElement != null) caller.uiElement.SetActive(false);
        }
        if (receiver != null && receiver != caller) receiver.LockMovement(false);
    }

    void SwapRoles()
    {
        PlayerTaskInteraction temp = caller;
        caller = receiver;
        receiver = temp;

        Debug.Log("Roles Swapped");
    }

    void FinishTask()
    {
        taskActive = false;
        isWorkedOn = false;
        StopAllCoroutines();
        if (caller != null)
        {
            caller.LockMovement(false);
            if (caller.uiElement != null) caller.uiElement.SetActive(false);
        }
        if (receiver != null && receiver != caller) receiver.LockMovement(false);

     
        if (propsToRemove != null)
            foreach (var p in propsToRemove)
                if (p != null) Destroy(p);

        if (display != null) { display.SetStatus(null); display.SetControls(null); display.ShowFeedback("Reef repaired!", new Color(0.35f, 0.9f, 0.45f)); }
        Debug.Log("Task Finished");
        CompleteTask(); // shaves TimeReward off the survival clock + raises Completed
    }
}
