using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(BoxCollider))]
public class MaintenanceBreaker : MonoBehaviour, IInteractable
{
    [Tooltip("Only these lights are restored, leaving the rest of the hospital dark.")]
    public Light[] restoredLights = new Light[0];
    public GameObject[] poweredObjects = new GameObject[0];
    public Transform lever;
    public Vector3 offAngles = new Vector3(0, 0, -35);
    public Vector3 onAngles = new Vector3(0, 0, 35);
    public UnityEvent onPowerRestored = new UnityEvent();
    public bool HasPower { get; private set; }
    public string Prompt => "Flip maintenance breaker";
    public bool CanInteract(PlayerGameplay player) => !HasPower && player.State.CanInteract;

    private void Start() { ApplyPower(false); }

    private void ApplyPower(bool on)
    {
        foreach (var light in restoredLights) if (light != null) light.enabled = on;
        foreach (var obj in poweredObjects) if (obj != null) obj.SetActive(on);
        if (lever != null) lever.localEulerAngles = on ? onAngles : offAngles;
    }

    public void Interact(PlayerGameplay player)
    {
        if (HasPower || !player.State.CanInteract) return;
        HasPower = true; ApplyPower(true);
        player.Notify("Partial hospital power restored.");
        onPowerRestored.Invoke();
    }
}
