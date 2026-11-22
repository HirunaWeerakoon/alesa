using System;
using System.Numerics;

namespace Alesa.BlindMinion
{
    public enum MinionState { Dormant, Alerted, Investigating, Searching }

    /// <summary>
    /// Decision-making for one blind minion. No Unity types: the Unity component feeds it its
    /// position and reads back where it wants to go.
    ///
    /// State machine:
    ///   Dormant        asleep; any noise it can hear wakes it
    ///   Alerted        short reaction pause (turns its head to the sound)
    ///   Investigating  walks to where the noise was; a newer noise re-targets it
    ///   Searching      reached the spot, found nothing, listens for a while
    ///   → back to Dormant
    /// </summary>
    public sealed class BlindMinionBrain : IDisposable
    {
        private readonly NoiseBus _noise;

        // --- Tunables ---
        public float HearingThreshold { get; set; } = 5f;
        public float ReactionTime { get; set; } = 0.6f;
        public float SearchDuration { get; set; } = 4f;
        public float InvestigateSpeed { get; set; } = 1.8f;
        public float ArriveRadius { get; set; } = 1f;

        // --- State ---
        public MinionState State { get; private set; } = MinionState.Dormant;

        /// <summary>Set by the Unity component every frame.</summary>
        public Vector3 CurrentPosition { get; set; }

        /// <summary>Where it last heard something.</summary>
        public Vector3 NoisePosition { get; private set; }

        public float LastPerceivedLoudness { get; private set; } = float.NegativeInfinity;

        /// <summary>True while it wants to walk to <see cref="NoisePosition"/>.</summary>
        public bool WantsToMove => State == MinionState.Investigating;
        public float DesiredSpeed => WantsToMove ? InvestigateSpeed : 0f;

        public event Action<MinionState> StateChanged;

        private float _clock;
        private float _stateEnteredAt;

        public BlindMinionBrain(NoiseBus noise)
        {
            _noise = noise;
            _noise.NoiseEmitted += OnNoise;
        }

        public void Dispose() => _noise.NoiseEmitted -= OnNoise;

        public void Tick(float deltaTime)
        {
            _clock += deltaTime;
            float timeInState = _clock - _stateEnteredAt;

            switch (State)
            {
                case MinionState.Alerted:
                    if (timeInState >= ReactionTime) Enter(MinionState.Investigating);
                    break;
                case MinionState.Investigating:
                    if (Hearing.FlatDistance(CurrentPosition, NoisePosition) <= ArriveRadius) Enter(MinionState.Searching);
                    break;
                case MinionState.Searching:
                    if (timeInState >= SearchDuration) Enter(MinionState.Dormant);
                    break;
            }
        }

        private void OnNoise(NoiseEvent noise)
        {
            float perceived = Hearing.Perceived(noise, CurrentPosition);
            if (perceived < HearingThreshold) return;

            NoisePosition = noise.Position;
            LastPerceivedLoudness = perceived;

            if (State == MinionState.Dormant) Enter(MinionState.Alerted);
            else if (State == MinionState.Searching) Enter(MinionState.Investigating);
            // Alerted / Investigating: keep going, now towards the newer noise.
        }

        private void Enter(MinionState next)
        {
            if (State == next) return;
            State = next;
            _stateEnteredAt = _clock;
            StateChanged?.Invoke(next);
        }
    }
}
