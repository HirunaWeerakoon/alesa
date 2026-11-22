using System;

namespace Alesa.Agents.BlindMinion
{
    public enum DecayProfile
    {
        Linear,
        Exponential,
        SigmoidalTension
    }

    /// <summary>
    /// Evaluates non-linear attention and suspicion decay curves for the Blind Minion.
    /// Provides mathematical decay formulations to model realistic cognitive cool-down:
    /// heightened tension persists after violent sounds before relaxing into calm dormancy.
    /// </summary>
    public static class SuspicionDecayCurve
    {
        /// <summary>
        /// Evaluates current alertness [0, 1] given elapsed silent duration and half-life.
        /// </summary>
        public static float Evaluate(float peakAlertness, float elapsedSilence, float duration, DecayProfile profile = DecayProfile.SigmoidalTension)
        {
            if (elapsedSilence <= 0f) return Math.Min(1.0f, Math.Max(0f, peakAlertness));
            if (elapsedSilence >= duration) return 0f;

            float t = elapsedSilence / Math.Max(0.1f, duration);

            switch (profile)
            {
                case DecayProfile.Linear:
                    return Math.Max(0f, peakAlertness * (1.0f - t));

                case DecayProfile.Exponential:
                    // Half-life is at t = 0.35
                    return Math.Max(0f, peakAlertness * (float)Math.Exp(-2.5 * t));

                case DecayProfile.SigmoidalTension:
                default:
                    // Holds alertness high during initial inspection (plateau), then rolls off smoothly
                    // S-curve formulation: 1 / (1 + (t / (1 - t))^3)
                    float s = (float)(1.0 / (1.0 + Math.Pow(t / (1.0001 - t), 2.5)));
                    return Math.Max(0f, peakAlertness * s);
            }
        }

        /// <summary>
        /// Calculates adaptive reaction delay. When alertness is high, the minion's reaction
        /// to secondary noises is accelerated.
        /// </summary>
        public static float ComputeReactionDelay(float baseReactionTime, float alertness, float maxSpeedup = 0.5f)
        {
            float speedup = Math.Min(1.0f, Math.Max(0f, alertness)) * Math.Min(0.9f, Math.Max(0f, maxSpeedup));
            return baseReactionTime * (1.0f - speedup);
        }
    }
}
