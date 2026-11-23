using UnityEngine;

/// <summary>
/// A hinged door the player opens with the interact key. Rotates <see cref="hinge"/> about its
/// local up axis, so the hinge transform must sit on the hinge edge, not in the middle of the leaf.
/// Exposes <see cref="OpenAmount"/> so AI hearing can treat a closed door as a sound barrier.
/// </summary>
[DisallowMultipleComponent]
public class HospitalDoor : MonoBehaviour, IInteractable
{
    [Header("Hinge")]
    [Tooltip("Transform rotated when the door swings. Leave empty to rotate this object.")]
    public Transform hinge;
    [Tooltip("Angle in degrees the door swings open. Negative swings the other way.")]
    [Range(-170f, 170f)] public float openAngle = 95f;
    [Tooltip("Seconds for a full swing.")]
    [Min(0.05f)] public float swingSeconds = 0.9f;

    [Header("Locking")]
    [Tooltip("A locked door refuses to open until something calls Unlock().")]
    public bool locked;
    [Tooltip("Shown when the player tries a locked door.")]
    public string lockedMessage = "The door is locked.";
    [Tooltip("Prompt shown when the door can be used.")]
    public string openPrompt = "Open door";
    public string closePrompt = "Close door";

    [Header("Audio")]
    public AudioSource audioSource;
    public AudioClip openClip, closeClip, lockedClip;

    [Header("Noise (hive mind)")]
    [Tooltip("Hearing radius in metres when the door is swung. 0 disables door noise.")]
    [Min(0f)] public float noiseRadius = 12f;

    /// <summary>True once the door has been swung open.</summary>
    public bool IsOpen { get; private set; }
    /// <summary>True while the door is mid-swing.</summary>
    public bool IsMoving => !Mathf.Approximately(swing, IsOpen ? 1f : 0f);
    /// <summary>0 when fully shut, 1 when fully open. AI hearing can attenuate by this.</summary>
    public float OpenAmount => swing;
    /// <summary>Whether the door is currently locked.</summary>
    public bool IsLocked => locked;

    /// <summary>Raised whenever the door starts opening or closing. True means opening.</summary>
    public event System.Action<HospitalDoor, bool> Toggled;

    private float swing;
    private Quaternion shutRotation;
    private Transform pivot;

    public string Prompt => locked ? lockedMessage : (IsOpen ? closePrompt : openPrompt);
    public bool CanInteract(PlayerGameplay player) => player != null && player.State.CanInteract;

    private void Awake()
    {
        pivot = hinge != null ? hinge : transform;
        shutRotation = pivot.localRotation;
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
    }

    public void Interact(PlayerGameplay player)
    {
        if (!CanInteract(player)) return;
        if (locked)
        {
            player.Notify(lockedMessage, 2f);
            Play(lockedClip);
            return;
        }
        Toggle();
    }

    /// <summary>Swings the door the other way. Safe to call while it is still moving.</summary>
    public void Toggle() { SetOpen(!IsOpen); }

    /// <summary>Opens or shuts the door. Does nothing if it is already in that state.</summary>
    public void SetOpen(bool open)
    {
        if (locked && open) return;
        if (IsOpen == open) return;
        IsOpen = open;
        Play(open ? openClip : closeClip);
        if (noiseRadius > 0f)
            AcousticBus.EmitSound(pivot != null ? pivot.position : transform.position, noiseRadius, SoundType.PropCollision);
        Toggled?.Invoke(this, open);
    }

    /// <summary>Unlocks the door, and optionally swings it open straight away.</summary>
    public void Unlock(bool alsoOpen = false)
    {
        locked = false;
        if (alsoOpen) SetOpen(true);
    }

    /// <summary>Locks the door. A door that is standing open stays open.</summary>
    public void Lock() { locked = true; }

    private void Update()
    {
        float target = IsOpen ? 1f : 0f;
        if (Mathf.Approximately(swing, target)) return;
        // Unscaled time so a door already swinging keeps moving sensibly after a pause.
        swing = Mathf.MoveTowards(swing, target, Time.deltaTime / Mathf.Max(0.05f, swingSeconds));
        pivot.localRotation = shutRotation * Quaternion.Euler(0f, openAngle * Mathf.SmoothStep(0f, 1f, swing), 0f);
    }

    private void Play(AudioClip clip)
    {
        if (audioSource != null && clip != null) audioSource.PlayOneShot(clip);
    }

    private void OnDrawGizmosSelected()
    {
        Transform p = hinge != null ? hinge : transform;
        Gizmos.color = locked ? Color.red : Color.green;
        Gizmos.DrawWireSphere(p.position, 0.12f);
        Gizmos.DrawLine(p.position, p.position + p.up * 2f);
        // Preview of the open position.
        Gizmos.color = new Color(0f, 1f, 0f, 0.4f);
        Vector3 shut = p.rotation * Vector3.forward;
        Vector3 opened = p.rotation * Quaternion.Euler(0f, openAngle, 0f) * Vector3.forward;
        Gizmos.DrawLine(p.position, p.position + shut);
        Gizmos.DrawLine(p.position, p.position + opened);
    }
}
