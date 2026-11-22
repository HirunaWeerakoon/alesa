using System;
using UnityEngine;
using Alesa.Core;

namespace Alesa.Runtime
{
    /// <summary>
    /// Runtime voice instruction playback component for Alesa.
    /// Drives spoken navigation guidance through an attached Unity AudioSource with 3D spatialization,
    /// priority-based audio interruption, duplicate prevention, and graceful subtitle fallbacks.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public class AlesaVoiceSpeaker : MonoBehaviour
    {
        [Header("Target & References")]
        [Tooltip("The guidance decision coordinator emitting navigation instructions.")]
        public AlesaGuidanceController guidanceController;

        [Tooltip("AudioSource component used for voice playback.")]
        public AudioSource audioSource;

        [Header("Voice Audio Clips")]
        [Tooltip("Voice clip: 'Nathan, turn left!'")]
        public AudioClip clipTurnLeft;

        [Tooltip("Voice clip: 'Take the right corridor!'")]
        public AudioClip clipTurnRight;

        [Tooltip("Voice clip: 'Keep going straight!'")]
        public AudioClip clipContinueStraight;

        [Tooltip("Voice clip: 'Turn around! Wrong way!'")]
        public AudioClip clipUTurn;

        [Tooltip("Voice clip: 'That way is blocked! Take another route!'")]
        public AudioClip clipRouteBlocked;

        [Tooltip("Voice clip: 'Path blocked! Rerouting, follow me this way!'")]
        public AudioClip clipRouteRerouted;

        [Tooltip("Voice clip: 'No safe route available! We're trapped!'")]
        public AudioClip clipNoRoute;

        [Tooltip("Voice clip: 'We reached the exit! Keep moving!'")]
        public AudioClip clipDestinationReached;

        [Header("Playback Settings")]
        [Range(0f, 1f)]
        public float voiceVolume = 1.0f;

        [Tooltip("When enabled, enables 3D spatial sound so Alesa's voice emanates from her physical position.")]
        public bool enable3DSpatialAudio = true;

        [Tooltip("Minimum distance in meters where Alesa's voice is at full volume.")]
        public float min3DDistance = 1.5f;

        [Tooltip("Maximum distance in meters before Alesa's voice fades out.")]
        public float max3DDistance = 25.0f;

        [Header("Debug Status (Read-Only)")]
        [SerializeField] private VoiceCueId activeVoiceCue = VoiceCueId.None;
        [SerializeField] private string lastSpokenSubtitle = string.Empty;
        [SerializeField] private bool isSpeaking = false;
        [SerializeField] private int totalVoiceLinesPlayed = 0;

        public VoiceCueId ActiveVoiceCue => activeVoiceCue;
        public string LastSpokenSubtitle => lastSpokenSubtitle;
        public bool IsSpeaking => isSpeaking;
        public int TotalVoiceLinesPlayed => totalVoiceLinesPlayed;

        public event Action<string, VoiceCueId> OnVoiceSpoken;

        private void Awake()
        {
            InitializeReferences();
            ConfigureAudioSource();
        }

        private void OnEnable()
        {
            InitializeReferences();
            ConfigureAudioSource();

            if (guidanceController != null)
            {
                guidanceController.OnGuidanceEmitted += HandleGuidanceEmitted;
            }
        }

        private void OnDisable()
        {
            if (guidanceController != null)
            {
                guidanceController.OnGuidanceEmitted -= HandleGuidanceEmitted;
            }

            if (audioSource != null && audioSource.isPlaying)
            {
                audioSource.Stop();
            }
        }

        private void Update()
        {
            if (audioSource != null)
            {
                isSpeaking = audioSource.isPlaying;
                if (!isSpeaking && activeVoiceCue != VoiceCueId.None)
                {
                    activeVoiceCue = VoiceCueId.None;
                }
            }
        }

        public void InitializeReferences()
        {
            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
            }

            if (guidanceController == null)
            {
                guidanceController = FindFirstObjectByType<AlesaGuidanceController>();
            }
        }

        public void ConfigureAudioSource()
        {
            if (audioSource == null) return;

            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.volume = voiceVolume;

            if (enable3DSpatialAudio)
            {
                audioSource.spatialBlend = 1.0f; // Full 3D spatialization
                audioSource.minDistance = min3DDistance;
                audioSource.maxDistance = max3DDistance;
                audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
            }
            else
            {
                audioSource.spatialBlend = 0.0f; // 2D flat stereo
            }
        }

        public void HandleGuidanceEmitted(GuidanceDecision decision)
        {
            PlayVoiceGuidance(decision);
        }

        /// <summary>
        /// Plays the voice instruction corresponding to a GuidanceDecision.
        /// Arbitrates interruption priority, sets 3D audio, and provides subtitle callbacks.
        /// </summary>
        public bool PlayVoiceGuidance(GuidanceDecision decision)
        {
            VoiceCueId cue = VoiceClipMapper.MapDecisionToCue(decision);
            if (cue == VoiceCueId.None) return false;

            // Priority arbitration: check if incoming line can interrupt active speech
            if (audioSource != null && audioSource.isPlaying)
            {
                if (VoiceClipMapper.CanInterrupt(activeVoiceCue, cue))
                {
                    audioSource.Stop();
                }
                else
                {
                    // Non-critical line cannot interrupt ongoing speech
                    return false;
                }
            }

            AudioClip clipToPlay = GetClipForCue(cue);
            string subtitle = VoiceClipMapper.GetSubtitleText(cue);

            // Play physical audio clip if assigned
            if (clipToPlay != null && audioSource != null)
            {
                audioSource.clip = clipToPlay;
                audioSource.volume = voiceVolume;
                audioSource.Play();
            }

            // Record state and fire event (graceful fallback when audio clips are not yet imported)
            activeVoiceCue = cue;
            lastSpokenSubtitle = subtitle;
            totalVoiceLinesPlayed++;

            if (Application.isPlaying)
            {
                Debug.Log($"<color=#E066FF>[Alesa Voice]</color> <b>\"{subtitle}\"</b> {(clipToPlay == null ? "<i>(AudioClip unassigned - subtitle fallback)</i>" : "")}", this);
            }

            OnVoiceSpoken?.Invoke(subtitle, cue);
            return true;
        }

        public AudioClip GetClipForCue(VoiceCueId cue)
        {
            return cue switch
            {
                VoiceCueId.TurnLeft => clipTurnLeft,
                VoiceCueId.TurnRight => clipTurnRight,
                VoiceCueId.ContinueStraight => clipContinueStraight,
                VoiceCueId.UTurn => clipUTurn,
                VoiceCueId.RouteBlocked => clipRouteBlocked,
                VoiceCueId.RouteRerouted => clipRouteRerouted,
                VoiceCueId.NoRouteAvailable => clipNoRoute,
                VoiceCueId.DestinationReached => clipDestinationReached,
                _ => null
            };
        }

        /// <summary>
        /// Immediately stops any ongoing speech playback.
        /// </summary>
        public void StopSpeech()
        {
            if (audioSource != null && audioSource.isPlaying)
            {
                audioSource.Stop();
            }
            activeVoiceCue = VoiceCueId.None;
            isSpeaking = false;
        }
    }
}
