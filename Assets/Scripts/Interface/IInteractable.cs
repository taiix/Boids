/// <summary>Something the local fish can press E on while it's inside its trigger (see FishInteract).</summary>
public interface IInteractable
{
    /// <summary>The local fish is inside its trigger.</summary>
    bool OnFocus { get; }
    /// <summary>It can be used right now (e.g. the trash hasn't sunk yet).</summary>
    bool CanInteract { get; }
    /// <summary>In use by the local player: keep sending it E even after leaving its trigger
    /// (parking the fish makes its body kinematic, which Unity reports as a trigger exit).</summary>
    bool IsBusy { get; }

    /// <summary>The local fish entered its trigger.</summary>
    void Focus();
    /// <summary>E was pressed while focused.</summary>
    void Interact();
    /// <summary>The local fish left its trigger.</summary>
    void LoseFocus();
}
