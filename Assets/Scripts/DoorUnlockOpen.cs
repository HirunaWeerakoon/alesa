using UnityEngine;

/// <summary>
/// Lets a parameterless UnityEvent (such as DialpadKeypad.onUnlocked) unlock a <see cref="HospitalDoor"/>
/// and swing it open in one step. HospitalDoor.Unlock takes a bool, which UnityEvent cannot supply.
/// </summary>
public sealed class DoorUnlockOpen : MonoBehaviour
{
    public HospitalDoor door;

    public void UnlockAndOpen()
    {
        if (door != null) door.Unlock(true);
    }
}
