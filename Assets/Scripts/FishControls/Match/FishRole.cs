namespace FishGame
{
    /// <summary>Which side a player is on for the round. Assigned by the server at round start.</summary>
    public enum FishRole : byte
    {
        Unassigned = 0,
        Fish = 1,   // prey: survive the timer, do tasks, eat to stave off hunger
        Shark = 2,  // hunter: eat the fish before the clock runs out
    }
}
