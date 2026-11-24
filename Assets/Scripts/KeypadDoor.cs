using System.Collections;
using UnityEngine;

/// <summary>
/// A hinged door that swings open once when <see cref="Open"/> is called (for example from a keypad's
/// onUnlocked event). The hinge is an offset in the door's local space, so it works for any door mesh
/// whose pivot is at its centre.
/// </summary>
public sealed class KeypadDoor : MonoBehaviour
{
    [Tooltip("Hinge position in the door's local space. For a door whose pivot is its centre this is half the door width, to the hinge side.")]
    public Vector3 hingeLocalOffset = new Vector3(-0.6f, 0f, 0f);
    [Tooltip("Degrees about the vertical axis. The sign picks the swing direction.")]
    public float openAngle = -100f;
    [Min(0.1f)] public float openSeconds = 1.6f;
    public AudioSource openSound;

    public bool IsOpen { get; private set; }

    public void Open()
    {
        if (IsOpen) return;
        IsOpen = true;
        if (openSound != null) openSound.Play();
        StartCoroutine(Swing());
    }

    private IEnumerator Swing()
    {
        Vector3 hinge = transform.TransformPoint(hingeLocalOffset);
        float done = 0f;
        while (done < 1f)
        {
            float next = Mathf.Min(1f, done + Time.deltaTime / openSeconds);
            transform.RotateAround(hinge, Vector3.up, openAngle * (Mathf.SmoothStep(0f, 1f, next) - Mathf.SmoothStep(0f, 1f, done)));
            done = next;
            yield return null;
        }
    }
}
