using Alesa.Core.AI;
using UnityEngine;

namespace Alesa.Runtime.World
{
    /// <summary>
    /// Bridges gameplay sound broadcasts from <see cref="AcousticBus"/> (Assembly-CSharp) into
    /// the shared <see cref="AIBootstrap"/> <see cref="NoiseBus"/> consumed by GV-backed agents
    /// such as <c>BlindMinionAgent</c>.
    /// Lives alongside <see cref="AcousticBus"/> in Assembly-CSharp so it can reference both
    /// <see cref="AcousticBus"/> and the auto-referenced <c>Alesa.Runtime</c>/<c>Alesa.Core</c>
    /// assemblies without introducing a circular dependency.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AcousticBusBridge : MonoBehaviour
    {
        [Tooltip("Optional explicit AIBootstrap reference. Falls back to AIBootstrap.Instance when unassigned.")]
        [SerializeField] private AIBootstrap aiBootstrap;

        [Tooltip("Multiplier applied when forwarding AcousticBus hearing radius (metres) as NoiseBus loudness.")]
        [SerializeField, Min(0f)] private float loudnessScale = 1f;

        private void OnEnable()
        {
            AcousticBus.OnSoundEmitted += HandleSoundEmitted;
        }

        private void OnDisable()
        {
            AcousticBus.OnSoundEmitted -= HandleSoundEmitted;
        }

        private void HandleSoundEmitted(Vector3 origin, float radius, SoundType type)
        {
            if (float.IsNaN(radius) || float.IsInfinity(radius) || radius <= 0f || loudnessScale <= 0f)
            {
                return;
            }

            AIBootstrap target = aiBootstrap != null ? aiBootstrap : AIBootstrap.Instance;
            if (target == null)
            {
                return;
            }

            target.EmitNoise(origin, radius * loudnessScale, MapSoundType(type));
        }

        /// <summary>Maps gameplay <see cref="SoundType"/> categories to <see cref="NoiseType"/>.</summary>
        public static NoiseType MapSoundType(SoundType type)
        {
            switch (type)
            {
                case SoundType.Footstep:
                    return NoiseType.Footstep;
                case SoundType.Sprint:
                    return NoiseType.Sprint;
                case SoundType.Gunshot:
                    return NoiseType.Gunshot;
                case SoundType.PropCollision:
                    return NoiseType.Trolley;
                default:
                    return NoiseType.Footstep;
            }
        }
    }
}
