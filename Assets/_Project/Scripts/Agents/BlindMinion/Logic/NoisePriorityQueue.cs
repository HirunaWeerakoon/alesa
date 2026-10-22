using System;
using System.Collections.Generic;
using System.Numerics;
using Alesa.Core.AI;

namespace Alesa.Agents.BlindMinion
{
    /// <summary>
    /// Evaluates and prioritizes competing acoustic events for the Blind Minion.
    /// Categorizes noise sources by inherent threat level (e.g. violent gunshots vs. soft footsteps)
    /// and weights them against perceived loudness, proximity, and temporal decay.
    /// </summary>
    public static class NoiseThreatEvaluator
    {
        /// <summary>
        /// Baseline category multipliers:
        /// - Gunshots &amp; Screams: violent acoustic spikes, maximum threat.
        /// - Thrown items &amp; slamming doors: abrupt environmental impacts.
        /// - Sprinting: rapid moving disturbance.
        /// - Footsteps: subtle, lower threat.
        /// </summary>
        public static float GetCategoryWeight(NoiseType type)
        {
            switch (type)
            {
                case NoiseType.Gunshot: return 3.0f;
                case NoiseType.Scream: return 2.5f;
                case NoiseType.DoorSlam: return 1.8f;
                case NoiseType.ThrownObject: return 1.6f;
                case NoiseType.Trolley: return 1.4f;
                case NoiseType.Sprint: return 1.3f;
                case NoiseType.Footstep: return 1.0f;
                default: return 1.0f;
            }
        }

        /// <summary>
        /// Computes a composite threat score from perceived loudness, sound type, and distance decay.
        /// Higher scores preempt ongoing investigations.
        /// </summary>
        public static float CalculateThreatScore(NoiseEvent noise, float perceivedLoudness, Vector3 minionPosition)
        {
            float weight = GetCategoryWeight(noise.Type);
            float dist = FlatMath.Distance(noise.Position, minionPosition);

            // Perceived loudness scaled by category weight, tempered by spatial distance attenuation
            float rawThreat = perceivedLoudness * weight;
            float attenuation = 1.0f / (1.0f + 0.05f * dist);
            return rawThreat * attenuation;
        }
    }

    /// <summary>Acoustic event processed and ranked by threat evaluation.</summary>
    public readonly struct PrioritizedNoise : IComparable<PrioritizedNoise>
    {
        public readonly NoiseEvent Noise;
        public readonly float PerceivedLoudness;
        public readonly float ThreatScore;
        public readonly float Timestamp;
        public readonly int HeardThroughNode;

        public PrioritizedNoise(NoiseEvent noise, float perceivedLoudness, float threatScore, float timestamp, int heardThroughNode)
        {
            Noise = noise;
            PerceivedLoudness = perceivedLoudness;
            ThreatScore = threatScore;
            Timestamp = timestamp;
            HeardThroughNode = heardThroughNode;
        }

        public int CompareTo(PrioritizedNoise other)
        {
            // Highest threat score comes first
            int cmp = other.ThreatScore.CompareTo(ThreatScore);
            return cmp != 0 ? cmp : other.Timestamp.CompareTo(Timestamp);
        }
    }

    /// <summary>
    /// Bounded dynamic priority queue for acoustic events heard by a minion.
    /// Filters stale noises and presents the most dangerous or immediate stimulus.
    /// </summary>
    public sealed class NoisePriorityQueue
    {
        private readonly List<PrioritizedNoise> _items = new List<PrioritizedNoise>();
        private readonly int _maxCapacity;

        public int Count => _items.Count;

        public NoisePriorityQueue(int maxCapacity = 16)
        {
            _maxCapacity = Math.Max(1, maxCapacity);
        }

        public void Enqueue(PrioritizedNoise item)
        {
            int insertIndex = _items.BinarySearch(item);
            if (insertIndex < 0) insertIndex = ~insertIndex;

            _items.Insert(insertIndex, item);

            if (_items.Count > _maxCapacity)
            {
                _items.RemoveAt(_items.Count - 1);
            }
        }

        public bool TryPeek(out PrioritizedNoise highestThreat)
        {
            if (_items.Count == 0)
            {
                highestThreat = default;
                return false;
            }

            highestThreat = _items[0];
            return true;
        }

        public bool TryDequeue(out PrioritizedNoise highestThreat)
        {
            if (_items.Count == 0)
            {
                highestThreat = default;
                return false;
            }

            highestThreat = _items[0];
            _items.RemoveAt(0);
            return true;
        }

        /// <summary>Purges events older than <paramref name="maxAge"/> seconds relative to <paramref name="currentTime"/>.</summary>
        public void PruneExpired(float currentTime, float maxAge)
        {
            _items.RemoveAll(item => (currentTime - item.Timestamp) > maxAge);
        }

        public void Clear() => _items.Clear();
    }
}
