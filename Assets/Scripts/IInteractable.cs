public interface IInteractable
{
    string Prompt { get; }
    bool CanInteract(PlayerGameplay player);
    void Interact(PlayerGameplay player);
}
