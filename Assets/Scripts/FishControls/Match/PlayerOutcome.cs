namespace FishGame
{
    /// <summary>How a player's round is going. Everyone starts Alive; a fish is out of the round once it
    /// escapes through the open wall, gets eaten, or dies some other way (starving, cuts). The shark is out
    /// once it starves (Died) or quits (Left), and every fish still in the water then Survived.</summary>
    public enum PlayerOutcome : byte
    {
        Alive = 0,
        Escaped = 1,  // swam out through the lifted wall
        Eaten = 2,    // caught by the shark
        Died = 3,     // starved, or cut to death
        Left = 4,     // quit the game mid-round (not counted either way)
        Survived = 5, // still in the water when the round ended because no shark was left (counts as escaped)
    }

    /// <summary>One player's line in the round results. MatchManager keeps one per player, synced to
    /// everyone, so the end screen can say who escaped and who was eaten.</summary>
    public struct RoundPlayer
    {
        public string name;
        public FishRole role;
        public PlayerOutcome outcome;
    }
}
