using System;
using UnityEngine;

public static class AcousticBusChecks
{
    private static int calls;
    private static Vector3 lastOrigin;
    private static float lastRadius;
    private static SoundType lastType;
    private static void Hear(Vector3 origin, float radius, SoundType type)
    {
        calls++; lastOrigin = origin; lastRadius = radius; lastType = type;
    }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }

    public static void Main()
    {
        var position = new Vector3(1, 2, 3);
        AcousticBus.EmitSound(position, 2, SoundType.Footstep); // No listeners is safe.
        AcousticBus.OnSoundEmitted += Hear;
        try
        {
            foreach (SoundType type in new[] { SoundType.Footstep, SoundType.Sprint, SoundType.Gunshot, SoundType.PropCollision })
            {
                int before = calls;
                AcousticBus.EmitSound(position, 37.5f, type);
                Check(calls == before + 1 && lastType == type && lastRadius == 37.5f, "Sound payload was not delivered exactly once");
                Check(lastOrigin.x == 1 && lastOrigin.y == 2 && lastOrigin.z == 3, "World-space origin changed");
            }
            for (int i = 0; i < 1000; i++) AcousticBus.EmitSound(position, 10, SoundType.Sprint);
            long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++) AcousticBus.EmitSound(position, 10, SoundType.Sprint);
            long bytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
            Check(bytes == 0, "Broadcast allocated managed memory: " + bytes);
        }
        finally { AcousticBus.OnSoundEmitted -= Hear; }
        int afterUnsubscribe = calls;
        AcousticBus.EmitSound(position, 40, SoundType.Gunshot);
        Check(calls == afterUnsubscribe, "Unsubscribed listener still received events");
        Console.WriteLine("Passed acoustic bus: no-listener broadcast, all payload types, unsubscribe, and 1000 broadcasts with zero managed allocation.");
    }
}
