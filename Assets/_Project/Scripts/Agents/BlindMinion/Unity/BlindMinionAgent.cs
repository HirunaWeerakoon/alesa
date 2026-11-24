using System;
using Alesa.Runtime;
using Alesa.Runtime.Motion;
using Alesa.Runtime.World;
using UnityEngine;

namespace Alesa.Agents.BlindMinion
{
    /// <summary>
    /// Unity adapter: builds the brain from inspector values, ticks it, plays audio and drives the
    /// minion-specific animation (Scream / Attack triggers, Searching bool) and where its head points.
    /// </summary>
    [RequireComponent(typeof(AgentMotor))]
    public sealed class BlindMinionAgent : MonoBehaviour, ILookTargetSource
    {
        private static readonly int ScreamTrigger = Animator.StringToHash("Scream");
        private static readonly int AttackTrigger = Animator.StringToHash("Attack");
        private static readonly int SearchingBool = Animator.StringToHash("Searching");

        [Tooltip("Height above the floor the head aims at (a noise is at floor level).")]
        [SerializeField] private float lookHeight = 1.4f;

        [SerializeField] private AIBootstrap ai;

        [Header("Hearing (loudness units = metres of open-air reach)")]
        [SerializeField] private float hearingThreshold = 5f;
        [SerializeField] private float huntThreshold = 25f;
        [SerializeField] private float closedDoorLoss = 20f;
        [SerializeField] private float junctionLoss = 1.5f;
        [Tooltip("Perceived loudness at which the minion knows exactly where a noise came from.")]
        [SerializeField] private float clearHearingLoudness = 20f;
        [Tooltip("Largest guessing error (m) for a barely audible noise.")]
        [SerializeField] private float maxHearingError = 6f;

        [Header("Memory")]
        [Tooltip("Seconds for alertness to fade from 1 (just heard something) back to 0.")]
        [SerializeField] private float alertnessMemory = 30f;
        [Tooltip("At full alertness the hearing threshold is multiplied by (1 - this).")]
        [SerializeField, Range(0f, 1f)] private float alertSensitivity = 0.6f;

        [Header("Behaviour")]
        [SerializeField] private float reactionTime = 0.6f;
        [SerializeField] private float huntMemory = 20f;
        [SerializeField] private float searchDuration = 4f;
        [SerializeField] private float investigateSpeed = 1.8f;
        [SerializeField] private float huntSpeed = 5.5f;
        [SerializeField] private float attackRange = 1.8f;
        [Tooltip("Scream loudness when it starts hunting; wakes nearby minions in a chain.")]
        [SerializeField] private float screamLoudness = 30f;

        [Header("Audio (optional)")]
        [SerializeField] private AudioSource voice;
        [SerializeField] private AudioClip alertScream;

        public BlindMinionBrain Brain { get; private set; }

        /// <summary>Raised when this minion hits the player. Hook health / screen effects here.</summary>
        public event Action<BlindMinionAgent> AttackedPlayer;

        private void Awake()
        {
            Brain = new BlindMinionBrain(ai.Context, UnityEngine.Random.Range(1, int.MaxValue))
            {
                CurrentPosition = transform.position.ToCore(),
                HearingThreshold = hearingThreshold,
                HuntThreshold = huntThreshold,
                ReactionTime = reactionTime,
                HuntMemory = huntMemory,
                SearchDuration = searchDuration,
                InvestigateSpeed = investigateSpeed,
                HuntSpeed = huntSpeed,
                AttackRange = attackRange,
                ScreamLoudness = screamLoudness,
                ClearHearingLoudness = clearHearingLoudness,
                MaxHearingError = maxHearingError,
                AlertnessMemory = alertnessMemory,
                AlertSensitivity = alertSensitivity,
            };
            Brain.Acoustics.ClosedDoorLoss = closedDoorLoss;
            Brain.Acoustics.JunctionLoss = junctionLoss;
            Brain.Screamed += OnScreamed;
            Brain.Attacked += OnAttacked;
            _motor = GetComponent<AgentMotor>();
            _motor.Bind(Brain);
            if (GetComponent<HeadLookAt>() == null) gameObject.AddComponent<HeadLookAt>(); // older scenes
        }

        private AgentMotor _motor;

        private void Update()
        {
            Brain.CurrentPosition = transform.position.ToCore();
            Brain.Tick(Time.deltaTime);
            SetBool(SearchingBool, Brain.State == MinionState.Searching);
        }

        /// <summary>
        /// The head shows what the minion believes: its (possibly wrong) guess of where the noise was,
        /// or the player once it has its hands on them. Asleep, it looks nowhere in particular.
        /// </summary>
        public bool TryGetLookTarget(out Vector3 worldPoint)
        {
            worldPoint = default;
            if (Brain == null) return false;
            switch (Brain.State)
            {
                case MinionState.Attacking:
                    worldPoint = ai.Context.World.PlayerPosition.ToUnity() + Vector3.up * lookHeight;
                    return true;
                case MinionState.Alerted:
                case MinionState.Investigating:
                case MinionState.Hunting:
                case MinionState.Searching:
                    if (!Brain.HasFinalTarget) return false;
                    worldPoint = Brain.FinalTarget.ToUnity() + Vector3.up * lookHeight;
                    return true;
                default:
                    return false;
            }
        }

        public void Kill() => Destroy(gameObject);

        private void OnDestroy()
        {
            if (Brain == null) return;
            Brain.Screamed -= OnScreamed;
            Brain.Attacked -= OnAttacked;
            Brain.Dispose();
        }

        private void OnScreamed()
        {
            if (voice != null && alertScream != null) voice.PlayOneShot(alertScream);
            SetTrigger(ScreamTrigger);
        }

        private void OnAttacked()
        {
            SetTrigger(AttackTrigger);
            AttackedPlayer?.Invoke(this);
        }

        private void SetTrigger(int id)
        {
            var animator = _motor != null ? _motor.Animator : null;
            if (HasParameter(animator, id)) animator.SetTrigger(id);
        }

        private void SetBool(int id, bool value)
        {
            var animator = _motor != null ? _motor.Animator : null;
            if (HasParameter(animator, id)) animator.SetBool(id, value);
        }

        // animator.parameters allocates a new array per call, so cache it per Animator.
        private Animator _cachedAnimator;
        private readonly System.Collections.Generic.HashSet<int> _parameters = new System.Collections.Generic.HashSet<int>();

        private bool HasParameter(Animator animator, int id)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return false;
            if (animator != _cachedAnimator)
            {
                _cachedAnimator = animator;
                _parameters.Clear();
                foreach (var p in animator.parameters) _parameters.Add(p.nameHash);
            }
            return _parameters.Contains(id);
        }
    }
}
