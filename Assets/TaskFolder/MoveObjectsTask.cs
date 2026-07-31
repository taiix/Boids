using System.Collections.Generic;
using UnityEngine;

public class MoveObjectsTask : MonoBehaviour
{
    public int rounds = 8;
    public int sequenceLength = 5;
    private int currentRound;
    private List<Direction> currentSequence;

    private PlayerTaskInteraction caller;
    private PlayerTaskInteraction receiver;

    public bool taskActive, isWorkedOn;
    public bool offline = true;



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
        isWorkedOn = true;

        GenerateSequence();
        SendSeqToReceiver();
    }

    void GenerateSequence()
    {
        currentSequence = new List<Direction>();

        for(int i =0; i < 6; i++)
        {
            currentSequence.Add((Direction)Random.Range(0,4));
        }
    }

    void SendSeqToReceiver()
    {
        caller.ShowSequence(currentSequence);
        receiver.PrepareInput(currentSequence);
        Debug.Log("Sequence Sent");
    }

    public void ReceiveInput(PlayerTaskInteraction player, Direction input, int position)
    {
        if (player != receiver)
            return;

        if (CheckInput(input, position))
        {
            Debug.Log("Correct direction");
            if (position >= sequenceLength)
            {
                SequenceComplete();
            }
        }
        else
        {
            GenerateSequence();
            SendSeqToReceiver();
            Debug.Log("Wrong Input");
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
        currentRound++;
        if(currentRound>= rounds)
        {
            FinishTask();
            return;
        }

        SwapRoles();
        GenerateSequence();
        SendSeqToReceiver();
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
        Debug.Log("Task Finished");
    }
}
