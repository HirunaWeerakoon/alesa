using System;

namespace Alesa.Core
{
    public enum RecoveryTier
    {
        None = 0,
        Tier1_Repath = 1,
        Tier2_FlankOffset = 2,
        Tier3_WaypointNav = 3,
        Tier4_Teleport = 4
    }

    /// <summary>
    /// Pure C# progressive recovery manager for Alesa.
    /// Detects lack of physical progress despite commanded movement, invalid paths,
    /// or extreme separation, and escalates through progressive recovery tiers.
    /// </summary>
    public sealed class FollowerRecoveryController
    {
        public float StuckDetectionTime { get; set; } = 1.0f;
        public float MinDisplacementRate { get; set; } = 0.2f; // m/s required to count as healthy movement
        public float ExtremeDistanceThreshold { get; set; } = 18.0f;
        public float TeleportTimeout { get; set; } = 5.5f;

        public float StuckTimer { get; private set; }
        public bool IsStuck { get; private set; }
        public RecoveryTier CurrentTier { get; private set; } = RecoveryTier.None;
        public int RecoveryCount { get; private set; }

        public FollowerRecoveryController(
            float stuckDetectionTime = 1.0f,
            float minDisplacementRate = 0.2f,
            float extremeDistanceThreshold = 18.0f)
        {
            StuckDetectionTime = Math.Max(0.5f, stuckDetectionTime);
            MinDisplacementRate = Math.Max(0.05f, minDisplacementRate);
            ExtremeDistanceThreshold = Math.Max(10.0f, extremeDistanceThreshold);
        }

        /// <summary>
        /// Evaluates movement progress each tick.
        /// </summary>
        public RecoveryTier EvaluateProgress(
            float actualDisplacement,
            float commandedSpeed,
            bool hasValidNavPath,
            float distanceToPlayer,
            float deltaTime)
        {
            if (deltaTime <= 0f) return CurrentTier;

            // Immediate Tier 4 trigger if separated beyond extreme distance
            if (distanceToPlayer > ExtremeDistanceThreshold)
            {
                IsStuck = true;
                CurrentTier = RecoveryTier.Tier4_Teleport;
                return CurrentTier;
            }

            // Check if agent is expected to be moving
            bool expectingMovement = commandedSpeed > 0.5f;
            float expectedMinStep = MinDisplacementRate * deltaTime;

            bool isLackingProgress = expectingMovement && (actualDisplacement < expectedMinStep);
            bool isPathCompromised = !hasValidNavPath;

            if (isLackingProgress || isPathCompromised)
            {
                StuckTimer += deltaTime;

                if (StuckTimer >= StuckDetectionTime)
                {
                    IsStuck = true;

                    if (StuckTimer >= TeleportTimeout)
                    {
                        CurrentTier = RecoveryTier.Tier4_Teleport;
                    }
                    else if (StuckTimer >= 3.5f)
                    {
                        CurrentTier = RecoveryTier.Tier3_WaypointNav;
                    }
                    else if (StuckTimer >= 2.0f)
                    {
                        CurrentTier = RecoveryTier.Tier2_FlankOffset;
                    }
                    else
                    {
                        CurrentTier = RecoveryTier.Tier1_Repath;
                    }
                }
            }
            else
            {
                // Making healthy progress - decay stuck timer and clear state when back to normal
                if (StuckTimer > 0f)
                {
                    StuckTimer = Math.Max(0f, StuckTimer - deltaTime * 2f);
                }

                if (StuckTimer <= 0f && IsStuck)
                {
                    Reset();
                }
            }

            return CurrentTier;
        }

        /// <summary>
        /// Records that a recovery action was executed and increments counter.
        /// </summary>
        public void NoteRecoveryActionExecuted()
        {
            RecoveryCount++;
        }

        /// <summary>
        /// Clears all stuck timers and resets to normal non-stuck state.
        /// </summary>
        public void Reset()
        {
            StuckTimer = 0f;
            IsStuck = false;
            CurrentTier = RecoveryTier.None;
        }
    }
}
