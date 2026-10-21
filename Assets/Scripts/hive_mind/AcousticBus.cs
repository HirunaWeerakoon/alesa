using System;
using UnityEngine;

/// <summary>Physical noise categories; suggested radii are authoring defaults in metres.</summary>
public enum SoundType
{
    /// <summary>Normal walking, approximately 2 metres.</summary>
    Footstep = 0,
    /// <summary>Sprint footsteps, approximately 10 metres.</summary>
    Sprint = 1,
    /// <summary>Gunfire carrying along corridors, approximately 35–40 metres.</summary>
    Gunshot = 2,
    /// <summary>Kicked carts, trays and other physical props, approximately 8 metres.</summary>
    PropCollision = 3,
}

/// <summary>
/// Global acoustic event hub. Dispatch is synchronous and creates no per-broadcast
/// objects. Listeners handle range checks and hearing responses themselves.
/// </summary>
public static class AcousticBus
{
    /// <summary>
    /// Broadcasts world-space origin, hearing radius in metres, and noise category.
    /// Listeners should subscribe in OnEnable and unsubscribe in OnDisable.
    /// </summary>
    public static event Action<Vector3, float, SoundType> OnSoundEmitted;

    /// <summary>Broadcasts a physical noise to currently registered listeners.</summary>
    /// <param name="origin">World-space sound position.</param>
    /// <param name="radius">Hearing radius in metres, supplied by the emitter.</param>
    /// <param name="type">The physical action that produced the noise.</param>
    public static void EmitSound(Vector3 origin, float radius, SoundType type)
    {
        OnSoundEmitted?.Invoke(origin, radius, type);
    }

    // Prevent stale subscriptions when Play mode is entered without domain reload.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSubscribers()
    {
        OnSoundEmitted = null;
    }
}
