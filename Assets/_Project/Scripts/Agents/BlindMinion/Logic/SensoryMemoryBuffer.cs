using System;
using System.Collections.Generic;
using System.Numerics;
using Alesa.Core.AI;

namespace Alesa.Agents.BlindMinion
{
    /// <summary>An individual sensory impression stored in short-term memory.</summary>
    public readonly struct SensoryImpression
    {
        public readonly Vector3 Position;
        public readonly float Loudness;
        public readonly NoiseType Type;
        public readonly float Timestamp;
        public readonly int NodeId;

        public SensoryImpression(Vector3 position, float loudness, NoiseType type, float timestamp, int nodeId)
        {
            Position = position;
            Loudness = loudness;
            Type = type;
            Timestamp = timestamp;
            NodeId = nodeId;
        }
    }

    /// <summary>
    /// Auditory short-term memory buffer retaining sequential noise impressions over time.
    /// Provides cumulative stimulus analysis to distinguish isolated anomalies from continuous patterns.
    /// </summary>
    public sealed class SensoryMemoryBuffer
    {
        private readonly List<SensoryImpression> _impressions = new List<SensoryImpression>();
        private readonly int _maxCapacity;

        public float RetentionWindow { get; set; } = 20.0f;
        public int Count => _impressions.Count;

        public SensoryMemoryBuffer(int capacity = 32)
        {
            _maxCapacity = Math.Max(4, capacity);
        }

        public void AddImpression(Vector3 position, float loudness, NoiseType type, float timestamp, int nodeId)
        {
            if (_impressions.Count >= _maxCapacity)
            {
                _impressions.RemoveAt(0);
            }
            _impressions.Add(new SensoryImpression(position, loudness, type, timestamp, nodeId));
        }

        /// <summary>
        /// Calculates cumulative acoustic excitation within the retention window.
        /// Repeated footsteps or mechanical clatter accumulate pressure, elevating alertness.
        /// </summary>
        public float CalculateAccumulatedExcitation(float currentTime, float halfLife = 6.0f)
        {
            Prune(currentTime);
            float total = 0f;

            foreach (var impression in _impressions)
            {
                float age = Math.Max(0f, currentTime - impression.Timestamp);
                float decay = (float)Math.Exp(-0.693147f * (age / Math.Max(0.1f, halfLife)));
                float weight = NoiseThreatEvaluator.GetCategoryWeight(impression.Type);
                total += impression.Loudness * weight * decay;
            }

            return total;
        }

        /// <summary>
        /// Computes the net velocity/direction vector indicated by recent sequential impressions.
        /// </summary>
        public bool TryGetAcousticHeading(float currentTime, out Vector3 estimatedDirection)
        {
            Prune(currentTime);
            estimatedDirection = Vector3.Zero;
            if (_impressions.Count < 2) return false;

            var oldest = _impressions[0];
            var newest = _impressions[_impressions.Count - 1];
            float dt = newest.Timestamp - oldest.Timestamp;

            if (dt <= 0.05f) return false;

            Vector3 delta = newest.Position - oldest.Position;
            float dist = FlatMath.Distance(newest.Position, oldest.Position);
            if (dist < 0.2f) return false;

            estimatedDirection = Vector3.Normalize(new Vector3(delta.X, 0f, delta.Z));
            return true;
        }

        public void Prune(float currentTime)
        {
            _impressions.RemoveAll(imp => (currentTime - imp.Timestamp) > RetentionWindow);
        }

        public void Clear() => _impressions.Clear();
    }
}
