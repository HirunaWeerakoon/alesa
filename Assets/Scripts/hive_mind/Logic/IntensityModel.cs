using System;

namespace Alesa.HiveMind
{
    /// <summary>
    /// What the director can currently observe about the player's situation. Filled in by the
    /// Unity adapter once per tick; every field is something the game already knows.
    /// </summary>
    public struct PacingInputs
    {
        /// <summary>Metres to the nearest active minion. Use <see cref="float.PositiveInfinity"/> for none.</summary>
        public float NearestThreatDistance;

        /// <summary>True while the possessed boss is present and hunting.</summary>
        public bool BossActive;

        /// <summary>Player health, 1 = untouched, 0 = dead.</summary>
        public float PlayerHealthFraction;

        /// <summary>Hottest value from the <see cref="HiveHeatMap"/>: how well the hive has the player located.</summary>
        public float Heat;

        /// <summary>True on the frames the player is firing. A firefight is its own kind of pressure.</summary>
        public bool PlayerFiring;

        /// <summary>Sensible defaults for "nothing is happening".</summary>
        public static PacingInputs Calm => new PacingInputs
        {
            NearestThreatDistance = float.PositiveInfinity,
            BossActive = false,
            PlayerHealthFraction = 1f,
            Heat = 0f,
            PlayerFiring = false
        };
    }

    /// <summary>
    /// Tracks how stressful the game is right now, as a single number in 0..1.
    ///
    /// Modelled on Left 4 Dead's director: rather than spawning on a timer, the game measures the
    /// pressure the player is actually under and uses that to decide when to push and when to back
    /// off. The key asymmetry is that intensity rises quickly and falls slowly, because fear
    /// outlasts the thing that caused it. Without that, a player who kills one minion would be
    /// instantly ambushed again, which reads as unfair rather than tense.
    ///
    /// Pure C#; no engine types, fully unit-testable.
    /// </summary>
    public sealed class IntensityModel
    {
        /// <summary>Current stress, 0 (safe) to 1 (overwhelmed).</summary>
        public float Intensity { get; private set; }

        /// <summary>The value <see cref="Intensity"/> is currently moving toward. Exposed for debugging.</summary>
        public float Target { get; private set; }

        /// <summary>Seconds to cover the full 0..1 range while rising. Fast: danger registers at once.</summary>
        public float RiseSeconds { get; set; } = 1.5f;

        /// <summary>Seconds to cover the full range while falling. Slow: the player stays rattled.</summary>
        public float FallSeconds { get; set; } = 12f;

        /// <summary>Distance at which a minion stops contributing any pressure at all.</summary>
        public float ThreatRange { get; set; } = 25f;

        /// <summary>
        /// Folds this tick's observations into the intensity.
        /// </summary>
        public void Tick(PacingInputs inputs, float deltaTime)
        {
            if (deltaTime <= 0f) return;

            Target = ComputeTarget(inputs);

            // Asymmetric approach: pick the rate by direction of travel, then move at most that far
            // this tick. Linear rather than exponential so the time to cross the range is a number
            // that can be reasoned about and tuned, instead of an asymptote that never arrives.
            float seconds = Target > Intensity ? RiseSeconds : FallSeconds;
            float step = seconds <= 0f ? 1f : deltaTime / seconds;

            Intensity += Math.Sign(Target - Intensity) * Math.Min(step, Math.Abs(Target - Intensity));
            Intensity = Clamp01(Intensity);
        }

        /// <summary>
        /// Combines the inputs into the pressure the player *should* be feeling. Contributions are
        /// taken as a maximum rather than a sum: being cornered by the boss is not made worse by
        /// also hearing a footstep, and summing would saturate the moment two things overlapped.
        /// </summary>
        private float ComputeTarget(PacingInputs inputs)
        {
            float target = 0f;

            // Proximity: full pressure when a minion is on top of the player, none beyond ThreatRange.
            if (inputs.NearestThreatDistance < ThreatRange && ThreatRange > 0f)
                target = Math.Max(target, 1f - inputs.NearestThreatDistance / ThreatRange);

            // The boss is a floor, not a bonus: while it hunts, the game is never calm.
            if (inputs.BossActive) target = Math.Max(target, 0.8f);

            // Being hunted at all is mildly stressful even with nothing in sight, which is what
            // makes the heat map felt rather than merely simulated.
            target = Math.Max(target, Clamp01(inputs.Heat) * 0.5f);

            // Low health raises the stakes of everything else.
            float hurt = 1f - Clamp01(inputs.PlayerHealthFraction);
            target = Math.Max(target, hurt * 0.7f);

            // A firefight is loud and committing, so it counts as real pressure.
            if (inputs.PlayerFiring) target = Math.Max(target, 0.6f);

            return Clamp01(target);
        }

        /// <summary>Resets to calm. Used on respawn and scene reload.</summary>
        public void Reset()
        {
            Intensity = 0f;
            Target = 0f;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
