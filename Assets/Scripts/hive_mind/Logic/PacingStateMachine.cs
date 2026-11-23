using System;

namespace Alesa.HiveMind
{
    /// <summary>Where the game currently sits in the tension cycle.</summary>
    public enum PacingState
    {
        /// <summary>Pressure is being applied and intensity is climbing toward the peak.</summary>
        BuildUp,

        /// <summary>Intensity has peaked. Held briefly so the high point actually registers.</summary>
        SustainPeak,

        /// <summary>Pressure withdrawn; waiting for the player to calm down.</summary>
        PeakFade,

        /// <summary>Enforced quiet. The player gets to breathe, loot and move before it starts again.</summary>
        Relax
    }

    /// <summary>
    /// Turns a continuous intensity signal into a four-phase tension cycle, and from that into a
    /// single number the director can spend: <see cref="PressureBudget"/>.
    ///
    /// The cycle is the reason the game has a rhythm instead of a difficulty level. Left to itself,
    /// a reactive director either never stops (exhausting) or settles into a steady trickle
    /// (boring). Forcing a quiet phase after every peak is what makes the next peak land.
    ///
    /// Pure C#; no engine types, fully unit-testable.
    /// </summary>
    public sealed class PacingStateMachine
    {
        /// <summary>Intensity at which BuildUp is considered to have peaked.</summary>
        public float PeakThreshold { get; set; } = 0.85f;

        /// <summary>Intensity the player must fall back to before the quiet phase begins.</summary>
        public float CalmThreshold { get; set; } = 0.3f;

        /// <summary>How long the peak is held before pressure is withdrawn.</summary>
        public float SustainSeconds { get; set; } = 4f;

        /// <summary>Minimum quiet between encounters. The single strongest pacing control.</summary>
        public float RelaxSeconds { get; set; } = 35f;

        /// <summary>
        /// Safety valve: PeakFade cannot last forever. Without it, a player who parks in a corner
        /// with intensity stuck just above CalmThreshold would never reach Relax, and the director
        /// would stay switched off indefinitely.
        /// </summary>
        public float MaxFadeSeconds { get; set; } = 25f;

        public PacingState State { get; private set; } = PacingState.BuildUp;

        /// <summary>Seconds spent in the current state.</summary>
        public float TimeInState { get; private set; }

        /// <summary>Number of complete cycles finished. Useful for escalating difficulty over a run.</summary>
        public int CompletedCycles { get; private set; }

        /// <summary>Raised on every state change, with the state just entered.</summary>
        public event Action<PacingState> StateChanged;

        /// <summary>
        /// How hard the director may push right now, 0 (leave them alone) to 1 (everything you have).
        /// The director multiplies its minion cap by this.
        /// </summary>
        public float PressureBudget
        {
            get
            {
                switch (State)
                {
                    // Ramp in over the first few seconds rather than slamming the door: a wave that
                    // appears all at once reads as a spawn, not as something that found you.
                    case PacingState.BuildUp:
                        return Clamp01(0.25f + TimeInState / 10f);

                    case PacingState.SustainPeak:
                        return 1f;

                    // Taper off instead of vanishing, so retreating minions look like they lost the
                    // scent rather than being switched off.
                    case PacingState.PeakFade:
                        return Clamp01(1f - TimeInState / Math.Max(0.01f, MaxFadeSeconds));

                    case PacingState.Relax:
                    default:
                        return 0f;
                }
            }
        }

        public void Tick(float intensity, float deltaTime)
        {
            if (deltaTime <= 0f) return;
            TimeInState += deltaTime;

            switch (State)
            {
                case PacingState.BuildUp:
                    if (intensity >= PeakThreshold) Enter(PacingState.SustainPeak);
                    break;

                case PacingState.SustainPeak:
                    if (TimeInState >= SustainSeconds) Enter(PacingState.PeakFade);
                    break;

                case PacingState.PeakFade:
                    // Either the player genuinely calmed down, or the safety valve fires.
                    if (intensity <= CalmThreshold || TimeInState >= MaxFadeSeconds)
                        Enter(PacingState.Relax);
                    break;

                case PacingState.Relax:
                    if (TimeInState >= RelaxSeconds)
                    {
                        CompletedCycles++;
                        Enter(PacingState.BuildUp);
                    }
                    break;
            }
        }

        /// <summary>
        /// Cuts the quiet phase short. The Unity adapter calls this when the player walks into a
        /// scripted beat, so a set piece is never muted by the director happening to be resting.
        /// </summary>
        public void ForceBuildUp()
        {
            if (State == PacingState.Relax) CompletedCycles++;
            Enter(PacingState.BuildUp);
        }

        public void Reset()
        {
            State = PacingState.BuildUp;
            TimeInState = 0f;
            CompletedCycles = 0;
        }

        private void Enter(PacingState next)
        {
            State = next;
            TimeInState = 0f;
            StateChanged?.Invoke(next);
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
