using System;
using System.Collections.Generic;
using System.Numerics;
using Alesa.Core.AI;

namespace Alesa.Agents.BlindMinion
{
    /// <summary>
    /// Record of an inter-minion vocal screech or alert broadcast.
    /// </summary>
    public readonly struct ScreechAlertRecord
    {
        public readonly int MinionId;
        public readonly Vector3 Position;
        public readonly float Loudness;
        public readonly float Timestamp;

        public ScreechAlertRecord(int minionId, Vector3 position, float loudness, float timestamp)
        {
            MinionId = minionId;
            Position = position;
            Loudness = loudness;
            Timestamp = timestamp;
        }
    }

    /// <summary>
    /// Manages sound propagation relay and inter-minion communication.
    /// Enables acoustic chain reactions: when one minion detects prey and screeches,
    /// neighboring minions evaluate the alert, elevating group suspicion while preventing
    /// infinite recursive echo loops.
    /// </summary>
    public sealed class MinionAlertRelay
    {
        private readonly NoiseBus _noiseBus;
        private readonly List<ScreechAlertRecord> _recentAlerts = new List<ScreechAlertRecord>();

        public float ScreechRelayCooldown { get; set; } = 4.0f;
        public float AlertDecayWindow { get; set; } = 12.0f;

        private float _lastBroadcastTime = float.NegativeInfinity;

        public event Action<ScreechAlertRecord> ScreechRelayed;

        public MinionAlertRelay(NoiseBus noiseBus)
        {
            _noiseBus = noiseBus;
        }

        /// <summary>
        /// Broadcasts an alert screech on the global NoiseBus, stamped with the minion's ID.
        /// </summary>
        public bool TryBroadcastScreech(int minionId, Vector3 position, float loudness, float currentTime)
        {
            if (currentTime - _lastBroadcastTime < ScreechRelayCooldown) return false;

            _lastBroadcastTime = currentTime;
            var record = new ScreechAlertRecord(minionId, position, loudness, currentTime);
            _recentAlerts.Add(record);

            _noiseBus?.Emit(new NoiseEvent(position, loudness, NoiseType.Scream, currentTime, minionId));
            ScreechRelayed?.Invoke(record);
            return true;
        }

        /// <summary>
        /// Registers a heard screech from an external minion into the collective alertness buffer.
        /// </summary>
        public void RegisterHeardScreech(NoiseEvent noise)
        {
            if (noise.Type != NoiseType.Scream) return;
            _recentAlerts.Add(new ScreechAlertRecord(noise.SourceId, noise.Position, noise.Loudness, noise.Time));
        }

        /// <summary>
        /// Calculates collective group agitation [0, 1] based on recent screech count and distance.
        /// </summary>
        public float CalculateGroupSuspicion(Vector3 minionPosition, float currentTime)
        {
            PruneStale(currentTime);
            if (_recentAlerts.Count == 0) return 0f;

            float agitation = 0f;
            foreach (var alert in _recentAlerts)
            {
                float age = Math.Max(0f, currentTime - alert.Timestamp);
                float timeFactor = 1.0f - (age / AlertDecayWindow);
                float dist = FlatMath.Distance(alert.Position, minionPosition);
                float distFactor = 1.0f / (1.0f + 0.08f * dist);

                agitation += timeFactor * distFactor * 0.4f;
            }

            return Math.Min(1.0f, agitation);
        }

        private void PruneStale(float currentTime)
        {
            _recentAlerts.RemoveAll(a => (currentTime - a.Timestamp) > AlertDecayWindow);
        }
    }
}
