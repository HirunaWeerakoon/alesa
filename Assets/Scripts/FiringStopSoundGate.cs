using System;

// A single pending sound is postponed by every successful shot.
public sealed class FiringStopSoundGate
{
    private bool pending;
    private float due;
    public void RecordShot(float now, float quietSeconds) { pending = true; due = now + Math.Max(0.01f, quietSeconds); }
    public bool TryConsume(float now, bool triggerHeld)
    {
        if (!pending || triggerHeld || now < due) return false;
        pending = false;
        return true;
    }
    public void Clear() { pending = false; }
}
