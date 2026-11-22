using System;

namespace Alesa.Core
{
    public enum CompanionState
    {
        /// <summary>Before Nathan rescues Alesa at the dialpad.</summary>
        Dormant,

        /// <summary>Nathan is stationary and Alesa has reached her follow anchor.</summary>
        Idle,

        /// <summary>Alesa is walking to stay within formation behind Nathan.</summary>
        Following,

        /// <summary>Nathan is sprinting; Alesa sprints to keep up.</summary>
        Sprinting,

        /// <summary>Nathan is aiming his weapon; Alesa holds safely behind him.</summary>
        WaitingAim,

        /// <summary>Separated from Nathan, stuck, or path invalid; executing recovery.</summary>
        Recovering
    }

    /// <summary>
    /// Pure C# context containing player signals and spatial measurements needed for locomotion transitions.
    /// Decoupled from Unity components for unit testing.
    /// </summary>
    public struct CompanionContext
    {
        public bool IsAlesaRescued;
        public bool IsPlayerControlsEnabled;
        public bool IsPlayerAiming;
        public bool IsPlayerSprinting;
        public bool HasPlayerMovementInput;
        public float DistanceToPlayer;
        public float DistanceToFollowAnchor;
        public bool HasValidNavPath;
        public bool IsStuck;
    }

    /// <summary>
    /// Pure C# companion locomotion finite state machine.
    /// Evaluates player state, spatial distance, and navigation health to deterministically
    /// transition between companion states without race conditions or conflicting behaviors.
    /// </summary>
    public sealed class CompanionStateMachine
    {
        public CompanionState CurrentState { get; private set; }
        public CompanionState PreviousState { get; private set; }

        public float WalkSpeed { get; set; } = 5.0f;
        public float SprintSpeed { get; set; } = 8.5f;
        public float StopDistanceThreshold { get; set; } = 0.35f;
        public float RecoveryDistanceThreshold { get; set; } = 18.0f;

        public event Action<CompanionState, CompanionState> OnStateChanged;

        public CompanionStateMachine(CompanionState initialState = CompanionState.Dormant)
        {
            CurrentState = initialState;
            PreviousState = initialState;
        }

        public float GetTargetSpeed()
        {
            return CurrentState switch
            {
                CompanionState.Dormant => 0f,
                CompanionState.Idle => 0f,
                CompanionState.WaitingAim => 0f,
                CompanionState.Following => WalkSpeed,
                CompanionState.Sprinting => SprintSpeed,
                CompanionState.Recovering => SprintSpeed,
                _ => WalkSpeed
            };
        }

        /// <summary>
        /// Evaluates player context and triggers state transition if required.
        /// </summary>
        public CompanionState EvaluateState(in CompanionContext context)
        {
            CompanionState nextState = DetermineNextState(context);

            if (nextState != CurrentState)
            {
                PreviousState = CurrentState;
                CurrentState = nextState;
                OnStateChanged?.Invoke(PreviousState, CurrentState);
            }

            return CurrentState;
        }

        private CompanionState DetermineNextState(in CompanionContext context)
        {
            // Priority 1: Rescue status
            if (!context.IsAlesaRescued)
            {
                return CompanionState.Dormant;
            }

            // Priority 2: Game modal / pause / controls disabled
            if (!context.IsPlayerControlsEnabled)
            {
                return CompanionState.Idle;
            }

            // Priority 3: Recovery conditions (lost path, stuck, or extreme separation)
            if (!context.HasValidNavPath || context.IsStuck || context.DistanceToPlayer > RecoveryDistanceThreshold)
            {
                return CompanionState.Recovering;
            }

            // Priority 4: Player aiming weapon (hold position safely behind Nathan)
            if (context.IsPlayerAiming)
            {
                return CompanionState.WaitingAim;
            }

            // Priority 5: Locomotion based on distance and player sprint
            bool needsMovement = context.DistanceToFollowAnchor > StopDistanceThreshold || context.HasPlayerMovementInput;

            if (needsMovement)
            {
                if (context.IsPlayerSprinting)
                {
                    return CompanionState.Sprinting;
                }

                return CompanionState.Following;
            }

            // Priority 6: Close to anchor and player not moving -> Idle
            return CompanionState.Idle;
        }

        /// <summary>
        /// Explicit transition for lifecycle events (e.g. keypad rescue).
        /// </summary>
        public void SetState(CompanionState newState)
        {
            if (CurrentState == newState) return;

            PreviousState = CurrentState;
            CurrentState = newState;
            OnStateChanged?.Invoke(PreviousState, CurrentState);
        }
    }
}
