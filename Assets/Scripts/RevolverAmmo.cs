using System;

// Kept independent of Unity so ammunition conservation can be tested directly.
[Serializable]
public sealed class RevolverAmmo
{
    public int capacity = 6;
    public int loaded;
    public int spare;

    public void Normalize()
    {
        capacity = Math.Max(1, capacity);
        loaded = Math.Max(0, Math.Min(capacity, loaded));
        spare = Math.Max(0, spare);
    }

    public void LoadPickup(int rounds)
    {
        Normalize();
        rounds = Math.Max(0, rounds);
        loaded = Math.Min(capacity, rounds);
        AddSpare(rounds - loaded);
    }

    public void AddSpare(int rounds) { spare = (int)Math.Min(int.MaxValue, (long)spare + Math.Max(0, rounds)); }

    public bool TryConsume()
    {
        if (loaded <= 0) return false;
        loaded--;
        return true;
    }

    public int Reload()
    {
        Normalize();
        int transferred = Math.Min(capacity - loaded, spare);
        loaded += transferred;
        spare -= transferred;
        return transferred;
    }
}
