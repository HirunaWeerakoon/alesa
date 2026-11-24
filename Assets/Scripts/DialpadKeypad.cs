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
    [Tooltip("When true (legacy behaviour) a correct code also frees Alesa. Turn off when Alesa is freed some other way, e.g. a cage.")]
    public bool freesAlesaOnUnlock = true;
    [Tooltip("Raised on a correct code, e.g. to open a door.")]
    public UnityEvent onUnlocked = new UnityEvent();
    public bool IsUnlocked { get; private set; }
    public string Prompt => requiredBreaker != null && !requiredBreaker.HasPower ? "Keypad has no power" : "Use access keypad";
    public bool CanInteract(PlayerGameplay player) => !IsUnlocked && player.State.CanInteract;

    private void Awake() { if (stationaryCamera != null) stationaryCamera.enabled = false; }

    public void Interact(PlayerGameplay player)
    {
        if (IsUnlocked || !player.State.CanInteract) return;
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
        onUnlocked.Invoke();
        if (!freesAlesaOnUnlock) { player.Notify("Access granted. A door has unlocked.", 4f); return; }
        player.MarkAlesaFreed();
        onAlesaFreed.Invoke();
    }
}
