using System;
using UnityEngine;
using UnityEngine.Events;
using Alesa.Core;

namespace Alesa.Runtime
{
    /// <summary>
    /// Runtime rescue activation and lifecycle manager for Agent 1 (Alesa).
    /// Subscribes to DialpadKeypad.onAlesaFreed or monitors PlayerGameplay.AlesaFreed.
    /// Safely transitions companion movement, route planning, and voice guidance from
    /// dormant to active following states upon rescue.
    /// </summary>
    [DisallowMultipleComponent]
    public class AlesaRescueController : MonoBehaviour
    {
        [Header("Trigger References")]
        [Tooltip("The dialpad keypad locking Alesa in the asylum room. Automatically hooks onAlesaFreed if assigned.")]
        public DialpadKeypad targetKeypad;

        [Tooltip("Nathan's PlayerGameplay component. Used as a fallback polling source.")]
        public PlayerGameplay playerGameplay;

        [Header("Agent 1 Subsystem References")]
        [Tooltip("Alesa's physical follower component.")]
        public AlesaFollower follower;

        [Tooltip("Alesa's pathfinding and route coordinator.")]
        public AlesaNavigator navigator;

        [Tooltip("Alesa's junction guidance decision controller.")]
        public AlesaGuidanceController guidanceController;

        [Tooltip("Alesa's voice guidance and subtitle speaker.")]
        public AlesaVoiceSpeaker voiceSpeaker;

        [Header("Rescue Greeting Audio")]
        [Tooltip("Custom voice clip played immediately when Alesa is freed.")]
        public AudioClip rescueGreetingClip;

        [Tooltip("Subtitle displayed when Alesa is freed.")]
        public string rescueGreetingSubtitle = "Nathan! Thank God you opened the door. I know the way out, follow me!";

        [Header("Rescue Events")]
        [Tooltip("Fires when Alesa is rescued and activated.")]
        public UnityEvent onRescueActivated = new UnityEvent();

        [Header("Debug Status (Read-Only)")]
        [SerializeField] private bool isRescued = false;
        [SerializeField] private float rescueTime = -1f;

        private RescueStateController _coreController;

        public RescueStateController CoreController => _coreController ??= new RescueStateController();
        public bool IsRescued => _coreController != null ? _coreController.IsRescued : isRescued;
        public float RescueTime => rescueTime;

        public event Action OnAlesaRescued;

        private void Awake()
        {
            _coreController = new RescueStateController();
            FindMissingReferences();
        }

        private void OnEnable()
        {
            if (targetKeypad != null)
            {
                targetKeypad.onAlesaFreed.AddListener(ActivateAfterRescue);
            }
        }

        private void OnDisable()
        {
            if (targetKeypad != null)
            {
                targetKeypad.onAlesaFreed.RemoveListener(ActivateAfterRescue);
            }
        }

        private void Start()
        {
            FindMissingReferences();

            // Check if player already freed Alesa prior to this component starting
            if (!IsRescued && playerGameplay != null && playerGameplay.AlesaFreed)
            {
                ActivateAfterRescue();
            }
        }

        private void Update()
        {
            // Fallback check in case DialpadKeypad is unlocked via code without triggering UnityEvent
            if (!IsRescued && playerGameplay != null && playerGameplay.AlesaFreed)
            {
                ActivateAfterRescue();
            }
        }

        /// <summary>
        /// External contract: Activates Alesa upon rescue from the dialpad keypad room.
        /// Strictly idempotent: repeated calls are safely ignored without redundant events.
        /// </summary>
        public void ActivateAfterRescue()
        {
            if (_coreController == null) _coreController = new RescueStateController();

            float currentTime = Application.isPlaying ? Time.time : 0f;
            if (!_coreController.TriggerRescue(currentTime))
            {
                // Already rescued, ignore duplicate trigger
                return;
            }

            isRescued = true;
            rescueTime = currentTime;

            // 1. Activate Follower Movement
            if (follower != null)
            {
                follower.ActivateAfterRescue();
            }

            // 2. Activate Route Navigator & Plan Escape Route
            if (navigator != null)
            {
                navigator.ActivateAfterRescue();
            }

            // 3. Play Rescue Greeting Voice / Subtitle
            PlayRescueGreeting();

            // 4. Dispatch Events
            OnAlesaRescued?.Invoke();
            onRescueActivated.Invoke();

            Debug.Log("<color=green>[ALESA RESCUE]</color> Alesa successfully freed! Companion AI transitioned from Dormant to Active Escape Guidance.", this);
        }

        /// <summary>
        /// Plays the initial spoken voice line or renders fallback subtitles upon being rescued.
        /// </summary>
        private void PlayRescueGreeting()
        {
            if (voiceSpeaker == null) return;

            if (rescueGreetingClip != null && voiceSpeaker.audioSource != null)
            {
                voiceSpeaker.audioSource.clip = rescueGreetingClip;
                voiceSpeaker.audioSource.volume = voiceSpeaker.voiceVolume;
                voiceSpeaker.audioSource.Play();
            }

            Debug.Log($"<color=#E066FF>[Alesa Voice]</color> <b>\"{rescueGreetingSubtitle}\"</b> {(rescueGreetingClip == null ? "<i>(AudioClip unassigned - subtitle fallback)</i>" : "")}", this);
        }

        /// <summary>
        /// Resolves references gracefully if not explicitly assigned in the inspector.
        /// </summary>
        public void FindMissingReferences()
        {
            if (follower == null) follower = GetComponent<AlesaFollower>() ?? FindFirstObjectByType<AlesaFollower>();
            if (navigator == null) navigator = GetComponent<AlesaNavigator>() ?? FindFirstObjectByType<AlesaNavigator>();
            if (guidanceController == null) guidanceController = GetComponent<AlesaGuidanceController>() ?? FindFirstObjectByType<AlesaGuidanceController>();
            if (voiceSpeaker == null) voiceSpeaker = GetComponent<AlesaVoiceSpeaker>() ?? FindFirstObjectByType<AlesaVoiceSpeaker>();

            if (targetKeypad == null) targetKeypad = FindFirstObjectByType<DialpadKeypad>();
            if (playerGameplay == null)
            {
                var playerObj = GameObject.FindWithTag("Player");
                if (playerObj != null) playerGameplay = playerObj.GetComponent<PlayerGameplay>();
            }
        }

        /// <summary>
        /// Resets the rescue state. Used for scene restarts or unit testing.
        /// </summary>
        public void ResetRescue()
        {
            _coreController?.Reset();
            isRescued = false;
            rescueTime = -1f;
        }
    }
}
