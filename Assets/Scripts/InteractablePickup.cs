using UnityEngine;

[RequireComponent(typeof(BoxCollider))]
public class InteractablePickup : MonoBehaviour, IInteractable
{
    public enum PickupKind { Revolver, Ammo, Note }
    public PickupKind kind;
    [Min(0)] public int rounds = 6;
    public string noteTitle = "Written clue";
    [TextArea(3, 12)] public string noteText;
    private bool collected;
    public string Prompt => kind == PickupKind.Note ? "Read " + noteTitle : "Pick up " + kind;
    public bool CanInteract(PlayerGameplay player) => !collected && player.State.CanInteract;

    public void Interact(PlayerGameplay player)
    {
        if (collected || !player.State.CanInteract) return;
        collected = true;
        player.GetComponent<PlayerMovementAudio>()?.PlayPickup(kind);
        if (kind == PickupKind.Revolver) player.Combat.Equip(rounds);
        else if (kind == PickupKind.Ammo) { player.Combat.AddAmmo(rounds); player.Notify($"Picked up {rounds} spare rounds."); }
        else player.ReadNote(noteTitle, noteText);
        gameObject.SetActive(false);
    }
}
