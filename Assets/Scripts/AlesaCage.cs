using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// The cage Alesa is held in. Interacting swings both cage doors outwards and frees Alesa
/// (PlayerGameplay.AlesaFreed), which activates her rescue controller and follower.
/// </summary>
public sealed class AlesaCage : MonoBehaviour, IInteractable
{
    [Tooltip("The door hinged on the right and the one hinged on the left, as seen from the front.")]
    public Transform rightDoor, leftDoor;
    [Tooltip("Degrees each door swings. The right door turns by -angle, the left by +angle.")]
    public float openAngle = 105f;
    [Min(0.1f)] public float openSeconds = 1.2f;
    public AudioSource openSound;
    public UnityEvent onOpened = new UnityEvent();

    public bool IsOpen { get; private set; }
    public string Prompt => "Open cage";
    public bool CanInteract(PlayerGameplay player) => !IsOpen && player.State.CanInteract;

    public void Interact(PlayerGameplay player)
    {
        if (IsOpen || !player.State.CanInteract) return;
        IsOpen = true;
        if (openSound != null) openSound.Play();
        StartCoroutine(OpenThenFree(player));
    }

    // Alesa is only freed once the doors are fully open, so she walks out through the gap instead of the swinging doors.
    private IEnumerator OpenThenFree(PlayerGameplay player)
    {
        yield return Swing();
        yield return new WaitForSeconds(0.25f);
        player.MarkAlesaFreed();
        onOpened.Invoke();
    }

    private IEnumerator Swing()
    {
        float done = 0f;
        while (done < 1f)
        {
            float next = Mathf.Min(1f, done + Time.deltaTime / openSeconds);
            float step = Mathf.SmoothStep(0f, 1f, next) - Mathf.SmoothStep(0f, 1f, done);
            if (rightDoor != null) rightDoor.RotateAround(rightDoor.position, transform.up, -openAngle * step);
            if (leftDoor != null) leftDoor.RotateAround(leftDoor.position, transform.up, openAngle * step);
            done = next;
            yield return null;
        }
    }
}
