using System;
using System.Numerics;

namespace Alesa.Agents.BlindMinion
{
    /// <summary>
    /// Tracks and extrapolates the Last Known Position (LKP) of an acoustic target.
    /// When the player falls silent, this tracker maintains spatial uncertainty inflation:
    /// the confidence radius expands outward over time proportional to presumed movement speed.
    /// </summary>
    public sealed class LastKnownPositionTracker
    {
        public bool HasLKP { get; private set; }
        public Vector3 Position { get; private set; }
        public Vector3 VelocityEstimate { get; private set; }
        public float RecordedTime { get; private set; }
        public float InitialUncertaintyRadius { get; private set; }

        /// <summary>Rate (m/s) at which the uncertainty boundary expands as time elapses.</summary>
        public float UncertaintyExpansionRate { get; set; } = 1.5f;

        /// <summary>Upper limit for the search radius so the minion doesn't try searching an entire level.</summary>
        public float MaxSearchRadius { get; set; } = 15.0f;

        public void Record(Vector3 position, float time, float initialUncertainty = 1.0f, Vector3 velocity = default)
        {
            Position = position;
            RecordedTime = time;
            InitialUncertaintyRadius = Math.Max(0.5f, initialUncertainty);
            VelocityEstimate = velocity;
            HasLKP = true;
        }

        /// <summary>Returns the estimated centroid of the target, factoring in velocity extrapolation.</summary>
        public Vector3 GetEstimatedCentroid(float currentTime)
        {
            if (!HasLKP) return Vector3.Zero;
            float dt = Math.Max(0f, currentTime - RecordedTime);
            return Position + VelocityEstimate * Math.Min(dt, 2.0f); // cap linear extrapolation to avoid runaway
        }

        /// <summary>Current radius of the search area encompassing probable target movement.</summary>
        public float GetCurrentUncertaintyRadius(float currentTime)
        {
            if (!HasLKP) return 0f;
            float elapsed = Math.Max(0f, currentTime - RecordedTime);
            float radius = InitialUncertaintyRadius + elapsed * UncertaintyExpansionRate;
            return Math.Min(radius, MaxSearchRadius);
        }

        /// <summary>Determines if a candidate position falls within the current probability circle.</summary>
        public bool IsWithinUncertainty(Vector3 candidate, float currentTime)
        {
            if (!HasLKP) return false;
            float dist = FlatMath.Distance(candidate, GetEstimatedCentroid(currentTime));
            return dist <= GetCurrentUncertaintyRadius(currentTime);
        }

        public void Clear()
        {
            HasLKP = false;
            Position = Vector3.Zero;
            VelocityEstimate = Vector3.Zero;
            RecordedTime = 0f;
            InitialUncertaintyRadius = 0f;
        }
    }
}
