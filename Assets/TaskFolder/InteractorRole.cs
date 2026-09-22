namespace FishGame
{
    /// <summary>
    /// What kind of player an interactor is, and which kinds a station accepts. It's a [Flags] enum
    /// so a station can allow more than one (e.g. Fish + Shark while testing). Sharks and fish
    /// interact with different things, so stations filter by this.
    /// </summary>
    [System.Flags]
    public enum InteractorRole
    {
        None  = 0,
        Fish  = 1 << 0,
        Shark = 1 << 1,
        All   = Fish | Shark,
    }
}
