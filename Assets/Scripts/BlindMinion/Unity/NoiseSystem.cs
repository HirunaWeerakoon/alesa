using UnityEngine;

namespace Alesa.BlindMinion
{
    /// <summary>
    /// The scene-wide noise bus. Any script can make the minions hear something with one line:
    ///     NoiseSystem.Emit(transform.position, 60f, NoiseType.Gunshot);
    /// </summary>
    public static class NoiseSystem
    {
        public static NoiseBus Bus { get; private set; } = new NoiseBus();

        public static void Emit(Vector3 position, float loudness, NoiseType type) =>
            Bus.Emit(new NoiseEvent(new System.Numerics.Vector3(position.x, position.y, position.z), loudness, type));

        // Enter Play Mode without a domain reload keeps statics alive: start each session with a fresh bus.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => Bus = new NoiseBus();
    }
}
