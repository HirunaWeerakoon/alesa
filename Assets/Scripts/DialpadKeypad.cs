using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(BoxCollider))]
public class DialpadKeypad : MonoBehaviour, IInteractable
{
    [SerializeField] private string accessCode = "1950";
    public Camera stationaryCamera;
    public MaintenanceBreaker requiredBreaker;
    [Tooltip("Disable this barrier when the correct code frees Alesa.")]
    public GameObject lockedBarrier;
    public UnityEvent onAlesaFreed = new UnityEvent();
    public bool IsUnlocked { get; private set; }
    public string Prompt => requiredBreaker != null && !requiredBreaker.HasPower ? "Keypad has no power" : "Use access keypad";
    public bool CanInteract(PlayerGameplay player) => !IsUnlocked;

    private void Awake() { if (stationaryCamera != null) stationaryCamera.enabled = false; }

    public void Interact(PlayerGameplay player)
    {
        if (IsUnlocked || player.BlocksControls) return;
        if (requiredBreaker != null && !requiredBreaker.HasPower) { player.Notify("Restore maintenance power first."); return; }
        if (stationaryCamera == null) { player.Notify("Keypad camera is not configured."); return; }
        player.OpenKeypad(this);
    }

    public bool CodeMatches(string entered) => !string.IsNullOrEmpty(accessCode) && entered == accessCode;

    public void Unlock(PlayerGameplay player)
    {
        if (IsUnlocked) return;
        IsUnlocked = true;
        if (lockedBarrier != null) lockedBarrier.SetActive(false);
        player.MarkAlesaFreed();
        onAlesaFreed.Invoke();
    }
}
