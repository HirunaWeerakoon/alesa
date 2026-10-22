using System;
using System.Numerics;

namespace Alesa.Core.AI
{
    public enum NoiseType { Footstep, Sprint, Gunshot, DoorSlam, Trolley, ThrownObject, Scream }

    public readonly struct NoiseEvent
    {
        public readonly Vector3 Position;

        /// <summary>Source loudness in arbitrary units (roughly "metres of reach in open air").</summary>
        public readonly float Loudness;

        public readonly NoiseType Type;
        public readonly float Time;

        /// <summary>Who made the noise; 0 = the player / world. Lets an agent ignore its own screams.</summary>
        public readonly int SourceId;

        public NoiseEvent(Vector3 position, float loudness, NoiseType type, float time, int sourceId = 0)
        {
            Position = position;
            Loudness = loudness;
            Type = type;
            Time = time;
            SourceId = sourceId;
        }
    }

    /// <summary>
    /// Integration point between player interactions (gunshots, doors, trolleys, throwables)
    /// and the sound-hunting minions. Emitters don't know who listens.
    /// </summary>
    public sealed class NoiseBus
    {
        public event Action<NoiseEvent> NoiseEmitted;

        public void Emit(NoiseEvent noise) => NoiseEmitted?.Invoke(noise);
    }
}
