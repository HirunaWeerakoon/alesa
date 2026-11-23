using System;
using System.Numerics;

namespace Alesa.BlindMinion
{
    public enum NoiseType { Footstep, Sprint, Gunshot, DoorSlam, ThrownObject }

    /// <summary>A sound made somewhere in the level.</summary>
    public readonly struct NoiseEvent
    {
        public readonly Vector3 Position;

        /// <summary>How loud it is, in "metres of reach in open air": a loudness-8 footstep carries about 8 m.</summary>
        public readonly float Loudness;

        public readonly NoiseType Type;

        public NoiseEvent(Vector3 position, float loudness, NoiseType type)
        {
            Position = position;
            Loudness = loudness;
            Type = type;
        }
    }

    /// <summary>
    /// Anything that makes noise reports it here; every minion listening hears about it.
    /// Emitters don't need to know minions exist.
    /// </summary>
    public sealed class NoiseBus
    {
        public event Action<NoiseEvent> NoiseEmitted;

        public void Emit(NoiseEvent noise) => NoiseEmitted?.Invoke(noise);
    }
}
