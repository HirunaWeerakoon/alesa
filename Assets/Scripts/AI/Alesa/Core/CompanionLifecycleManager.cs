using System;

namespace Alesa.Core
{
    public enum GameplayLifecycleState
    {
        Playing,
        Paused,
        ModalScreen,
        Disabled
    }

    /// <summary>
    /// Pure C# companion gameplay lifecycle coordinator for Agent 1.
    /// Manages game pause, note-reading/keypad modal screens, and teardown states.
    /// Ensures companion movement, route planning, cooldown timers, and voice cues are safely
    /// suspended during pause and modal screens, and properly resumed without temporal distortion.
    /// Strictly decoupled from UnityEngine for automated testing.
    /// </summary>
    public sealed class CompanionLifecycleManager
    {
        public GameplayLifecycleState CurrentState { get; private set; } = GameplayLifecycleState.Playing;
        public GameplayLifecycleState PreviousState { get; private set; } = GameplayLifecycleState.Playing;

        public float LastStateChangeTime { get; private set; } = 0f;
        public float PauseStartTime { get; private set; } = -1f;
        public float TotalPausedDuration { get; private set; } = 0f;
        public int StateChangeCount { get; private set; } = 0;

        public bool CanMove => CurrentState == GameplayLifecycleState.Playing;
        public bool CanProcessGuidance => CurrentState == GameplayLifecycleState.Playing;
        public bool CanPlayAudio => CurrentState == GameplayLifecycleState.Playing;
        public bool IsPaused => CurrentState == GameplayLifecycleState.Paused;
        public bool IsInModal => CurrentState == GameplayLifecycleState.ModalScreen;

        public event Action<GameplayLifecycleState, GameplayLifecycleState> OnStateChanged;
        public event Action OnPaused;
        public event Action OnResumed;

        public CompanionLifecycleManager(GameplayLifecycleState initialState = GameplayLifecycleState.Playing)
        {
            CurrentState = initialState;
            PreviousState = initialState;
        }

        /// <summary>
        /// Transitions to a new lifecycle state. Idempotent: transitions to the current state are ignored.
        /// </summary>
        public bool SetState(GameplayLifecycleState newState, float currentTime = 0f)
        {
            if (CurrentState == newState) return false;

            GameplayLifecycleState oldState = CurrentState;

            // Handle pause duration accumulation
            if (oldState == GameplayLifecycleState.Paused && newState != GameplayLifecycleState.Paused)
            {
                if (PauseStartTime >= 0f && currentTime >= PauseStartTime)
                {
                    TotalPausedDuration += (currentTime - PauseStartTime);
                }
                PauseStartTime = -1f;
            }
            else if (newState == GameplayLifecycleState.Paused && oldState != GameplayLifecycleState.Paused)
            {
                PauseStartTime = currentTime;
            }

            PreviousState = oldState;
            CurrentState = newState;
            LastStateChangeTime = currentTime;
            StateChangeCount++;

            OnStateChanged?.Invoke(oldState, newState);

            if (newState == GameplayLifecycleState.Paused)
            {
                OnPaused?.Invoke();
            }
            else if (oldState == GameplayLifecycleState.Paused)
            {
                OnResumed?.Invoke();
            }

            return true;
        }

        public bool Pause(float currentTime = 0f) => SetState(GameplayLifecycleState.Paused, currentTime);

        public bool Resume(float currentTime = 0f)
        {
            if (CurrentState != GameplayLifecycleState.Paused) return false;
            // Restore previous state if not paused/disabled, else return to Playing
            var target = (PreviousState != GameplayLifecycleState.Paused && PreviousState != GameplayLifecycleState.Disabled)
                ? PreviousState
                : GameplayLifecycleState.Playing;
            return SetState(target, currentTime);
        }

        public bool OpenModal(float currentTime = 0f) => SetState(GameplayLifecycleState.ModalScreen, currentTime);

        public bool CloseModal(float currentTime = 0f)
        {
            if (CurrentState != GameplayLifecycleState.ModalScreen) return false;
            return SetState(GameplayLifecycleState.Playing, currentTime);
        }

        /// <summary>
        /// Computes effective game time excluding paused durations, preventing timer drift during long pauses.
        /// </summary>
        public float GetEffectiveTime(float rawTime)
        {
            float activePause = (IsPaused && PauseStartTime >= 0f && rawTime >= PauseStartTime) ? (rawTime - PauseStartTime) : 0f;
            return Math.Max(0f, rawTime - TotalPausedDuration - activePause);
        }

        /// <summary>
        /// Resets the lifecycle state back to initial playing state.
        /// </summary>
        public void Reset()
        {
            CurrentState = GameplayLifecycleState.Playing;
            PreviousState = GameplayLifecycleState.Playing;
            LastStateChangeTime = 0f;
            PauseStartTime = -1f;
            TotalPausedDuration = 0f;
            StateChangeCount = 0;
        }
    }
}
