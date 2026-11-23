using System;
using UnityEngine;
using Alesa.Core;

namespace Alesa.Runtime
{
    /// <summary>
    /// Runtime gameplay lifecycle adapter for Agent 1 (Alesa).
    /// Detects game pause (Time.timeScale == 0, pause menu), modal screens (reading notes, dialpad),
    /// and scene teardown. Safely suspends follower locomotion, suppresses voice callouts during modals,
    /// and unhooks events on scene unload to prevent memory leaks.
    /// </summary>
    [DisallowMultipleComponent]
    public class AlesaGameplayLifecycle : MonoBehaviour
    {
        [Header("Target References")]
        [Tooltip("Nathan's PlayerGameplay component. If unassigned, automatically finds Player in scene.")]
        public PlayerGameplay playerGameplay;

        [Tooltip("Alesa's follower movement component.")]
        public AlesaFollower follower;

        [Tooltip("Alesa's route navigator.")]
        public AlesaNavigator navigator;

        [Tooltip("Alesa's junction guidance decision controller.")]
        public AlesaGuidanceController guidanceController;

        [Tooltip("Alesa's voice guidance speaker.")]
        public AlesaVoiceSpeaker voiceSpeaker;

        [Header("Debug Status (Read-Only)")]
        [SerializeField] private GameplayLifecycleState currentState = GameplayLifecycleState.Playing;
        [SerializeField] private bool isTimePaused = false;
        [SerializeField] private bool isInModalScreen = false;
        [SerializeField] private float totalPausedTime = 0f;

        private CompanionLifecycleManager _manager;

        public CompanionLifecycleManager Manager => _manager ??= new CompanionLifecycleManager();
        public GameplayLifecycleState CurrentState => _manager != null ? _manager.CurrentState : currentState;
        public bool CanMove => _manager == null || _manager.CanMove;
        public bool CanProcessGuidance => _manager == null || _manager.CanProcessGuidance;
        public bool CanPlayAudio => _manager == null || _manager.CanPlayAudio;

        public event Action<GameplayLifecycleState, GameplayLifecycleState> OnLifecycleStateChanged;

        private void Awake()
        {
            _manager = new CompanionLifecycleManager();
            _manager.OnStateChanged += HandleStateChanged;
            FindMissingReferences();
        }

        private void Start()
        {
            FindMissingReferences();
        }

        private void OnEnable()
        {
            if (_manager == null)
            {
                _manager = new CompanionLifecycleManager();
                _manager.OnStateChanged += HandleStateChanged;
            }
        }

        private void OnDisable()
        {
            if (_manager != null)
            {
                _manager.SetState(GameplayLifecycleState.Disabled, Time.unscaledTime);
            }

            // Pause voice speaker if playing
            if (voiceSpeaker != null && voiceSpeaker.audioSource != null && voiceSpeaker.audioSource.isPlaying)
            {
                voiceSpeaker.audioSource.Stop();
            }
        }

        private void OnDestroy()
        {
            if (_manager != null)
            {
                _manager.OnStateChanged -= HandleStateChanged;
            }
        }

        private void Update()
        {
            if (playerGameplay == null)
            {
                FindMissingReferences();
            }

            float unscaledTime = Time.unscaledTime;
            bool timePaused = Time.timeScale <= 0f;
            bool modalActive = playerGameplay != null && (playerGameplay.Mode == PlayerGameplay.ScreenMode.Note || playerGameplay.Mode == PlayerGameplay.ScreenMode.Keypad);
            bool gamePaused = timePaused || (playerGameplay != null && playerGameplay.Mode == PlayerGameplay.ScreenMode.Paused);

            // Synchronize with core lifecycle manager
            if (gamePaused)
            {
                if (_manager.CurrentState != GameplayLifecycleState.Paused)
                {
                    _manager.Pause(unscaledTime);
                }
            }
            else if (modalActive)
            {
                if (_manager.CurrentState != GameplayLifecycleState.ModalScreen)
                {
                    _manager.OpenModal(unscaledTime);
                }
            }
            else
            {
                if (_manager.CurrentState == GameplayLifecycleState.Paused)
                {
                    _manager.Resume(unscaledTime);
                }
                else if (_manager.CurrentState == GameplayLifecycleState.ModalScreen)
                {
                    _manager.CloseModal(unscaledTime);
                }
            }

            isTimePaused = timePaused;
            isInModalScreen = modalActive;
            totalPausedTime = _manager.TotalPausedDuration;
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (_manager == null) return;

            if (pauseStatus)
            {
                _manager.Pause(Time.unscaledTime);
            }
            else
            {
                _manager.Resume(Time.unscaledTime);
            }
        }

        private void HandleStateChanged(GameplayLifecycleState oldState, GameplayLifecycleState newState)
        {
            currentState = newState;

            // Follower locomotion handling
            if (follower != null && follower.agent != null && follower.agent.isOnNavMesh)
            {
                if (newState == GameplayLifecycleState.Paused || newState == GameplayLifecycleState.ModalScreen)
                {
                    follower.agent.isStopped = true;
                }
                else if (newState == GameplayLifecycleState.Playing)
                {
                    follower.agent.isStopped = false;
                }
            }

            // Voice audio handling
            if (voiceSpeaker != null && voiceSpeaker.audioSource != null)
            {
                if (newState == GameplayLifecycleState.Paused)
                {
                    if (voiceSpeaker.audioSource.isPlaying)
                    {
                        voiceSpeaker.audioSource.Pause();
                    }
                }
                else if (oldState == GameplayLifecycleState.Paused)
                {
                    voiceSpeaker.audioSource.UnPause();
                }
            }

            OnLifecycleStateChanged?.Invoke(oldState, newState);
        }

        public void FindMissingReferences()
        {
            if (follower == null) follower = GetComponent<AlesaFollower>() ?? FindFirstObjectByType<AlesaFollower>();
            if (navigator == null) navigator = GetComponent<AlesaNavigator>() ?? FindFirstObjectByType<AlesaNavigator>();
            if (guidanceController == null) guidanceController = GetComponent<AlesaGuidanceController>() ?? FindFirstObjectByType<AlesaGuidanceController>();
            if (voiceSpeaker == null) voiceSpeaker = GetComponent<AlesaVoiceSpeaker>() ?? FindFirstObjectByType<AlesaVoiceSpeaker>();

            if (playerGameplay == null)
            {
                var playerObj = GameObject.FindWithTag("Player");
                if (playerObj != null) playerGameplay = playerObj.GetComponent<PlayerGameplay>();
            }
        }
    }
}
