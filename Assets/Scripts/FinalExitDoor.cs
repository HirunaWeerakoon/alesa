using UnityEngine;

/// <summary>
/// The door at the end of the maze. Pressing the interact key on it ends the run as an escape.
/// Only usable once the final encounter has begun, so it cannot be used to skip the maze.
/// </summary>
public sealed class FinalExitDoor : MonoBehaviour, IInteractable
{
    public FinalBossEncounter encounter;
    [SerializeField] private string prompt = "Open the exit door";

    public string Prompt => prompt;

    public bool CanInteract(PlayerGameplay player) =>
        encounter != null && encounter.EncounterActive && !encounter.Escaped && player != null && player.State.CanInteract;

    public void Interact(PlayerGameplay player)
    {
        if (!CanInteract(player)) return;
        encounter.ReachExit();
    }
}
