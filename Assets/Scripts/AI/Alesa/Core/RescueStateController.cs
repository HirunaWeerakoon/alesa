using System;

namespace Alesa.Core
{
    /// <summary>
    /// Pure C# companion rescue coordinator for Agent 1.
    /// Manages the activation lifecycle when Nathan rescues Alesa from her locked room.
    /// Strictly decoupled from UnityEngine for automated testability and zero merge conflicts.
    /// </summary>
    public sealed class RescueStateController
    {
        public bool IsRescued { get; private set; }
        public float RescueTimestamp { get; private set; }
        public int ActivationCount { get; private set; }

        public event Action OnRescued;

        public RescueStateController(bool initiallyRescued = false)
        {
            IsRescued = initiallyRescued;
            RescueTimestamp = initiallyRescued ? 0f : -1f;
            ActivationCount = initiallyRescued ? 1 : 0;
        }

        /// <summary>
        /// Triggers rescue activation. Strictly idempotent: subsequent invocations
        /// are ignored without firing redundant events.
        /// </summary>
        /// <param name="currentTime">The current game time.</param>
        /// <returns>True if this call transitioned Alesa from dormant to rescued; false if already rescued.</returns>
        public bool TriggerRescue(float currentTime = 0f)
        {
            if (IsRescued) return false;

            IsRescued = true;
            RescueTimestamp = currentTime;
            ActivationCount++;

            OnRescued?.Invoke();
            return true;
        }

        /// <summary>
        /// Resets the rescue state back to dormant. Used for scene restarts, checkpoints, or unit testing.
        /// </summary>
        public void Reset()
        {
            IsRescued = false;
            RescueTimestamp = -1f;
            ActivationCount = 0;
        }
    }
}
