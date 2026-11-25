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

        [Header("How much she talks")]
        [Tooltip("Guidance lines (which way to turn) allowed in one playthrough. Blocked-route warnings get a couple more.")]
        [Min(0)] public int maxGuidanceLines = 8;
        [Tooltip("Seconds that must pass after any line before another guidance line.")]
        [Min(0f)] public float minGapSeconds = 8f;
        [Tooltip("Seconds before the same line can be said again.")]
        [Min(0f)] public float repeatGapSeconds = 40f;
        [Tooltip("Most times one line is said in a playthrough.")]
        [Min(1)] public int maxPerLine = 2;
        [Tooltip("Chance a guidance line that passes every other check is actually said.")]
        [Range(0f, 1f)] public float guidanceChance = 0.85f;

        [Header("Playback Settings")]
        [Range(0f, 1f)]
        public float voiceVolume = 1.0f;

        [Tooltip("When enabled, enables 3D spatial sound so Alesa's voice emanates from her physical position.")]
        public bool enable3DSpatialAudio = true;

        [Tooltip("Minimum distance in meters where Alesa's voice is at full volume.")]
        public float min3DDistance = 4f;

        [Tooltip("Maximum distance in meters before Alesa's voice fades out.")]
        public float max3DDistance = 40f;

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

        private DialogueBudget _budget;

        /// <summary>Decides whether a line may be spoken, so she does not talk non-stop.</summary>
        public DialogueBudget Budget
        {
            get
            {
                if (_budget == null) _budget = new DialogueBudget(UnityEngine.Random.Range(1, int.MaxValue));
                _budget.MaxGuidanceLines = maxGuidanceLines;
                _budget.MinGapSeconds = minGapSeconds;
                _budget.RepeatGapSeconds = repeatGapSeconds;
                _budget.MaxPerCue = maxPerLine;
                _budget.GuidanceChance = guidanceChance;
                return _budget;
            }
        }

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
        public bool PlayVoiceGuidance(GuidanceDecision decision) => PlayCue(VoiceClipMapper.MapDecisionToCue(decision));

        /// <summary>
        /// Speaks one line, if the budget allows it. The exit line is always spoken; the blocked, rerouting and
        /// trapped warnings may cut through the gap between lines; everything else is rationed.
        /// </summary>
        public bool PlayCue(VoiceCueId cue)
        {
            if (cue == VoiceCueId.None) return false;

            if (Application.isPlaying)
            {
                bool always = cue == VoiceCueId.DestinationReached;
                bool critical = cue == VoiceCueId.RouteBlocked || cue == VoiceCueId.RouteRerouted || cue == VoiceCueId.NoRouteAvailable;
                if (always) Budget.Register((int)cue, Time.time);
                else if (!Budget.TryAllow((int)cue, critical, Time.time)) return false;
            }

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

        /// <summary>Plays a non-dialogue sound from her, such as a scream. It never interrupts a line in progress.</summary>
        public bool PlayReaction(AudioClip clip)
        {
            if (clip == null || audioSource == null || audioSource.isPlaying) return false;
            audioSource.PlayOneShot(clip, voiceVolume);
            return true;
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
