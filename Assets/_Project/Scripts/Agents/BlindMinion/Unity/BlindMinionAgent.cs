using System;
using System.Collections;
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
        private static readonly int NeckBiteTrigger = Animator.StringToHash("NeckBite");
        private static readonly int DeathTorsoTrigger = Animator.StringToHash("DeathTorso");
        private static readonly int DeathHeadTrigger = Animator.StringToHash("DeathHead");
        private static readonly int ChargingBool = Animator.StringToHash("Charging");
        private static readonly int CrawlBool = Animator.StringToHash("Crawl");

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
        [Tooltip("Distance at which it feels the player and hunts without hearing anything. 0 = hearing only.")]
        [SerializeField] private float presenceRange = 6f;
        [Tooltip("Damage dealt to the player on each hit. 0 = harmless.")]
        [SerializeField] private float attackDamage = 20f;

        [Header("Contact (gaps are body surface to body surface, not centre to centre)")]
        [Tooltip("Gap at which it stops walking and swings.")]
        [SerializeField] private float attackGap = 0.3f;
        [Tooltip("Gap at which the swing becomes a neck bite. A motionless player is bitten at any contact gap.")]
        [SerializeField] private float biteGap = 0.2f;
        [Tooltip("Nathan's CharacterController radius, used to turn the gaps above into centre distances.")]
        [SerializeField] private float playerRadius = 0.325f;
        [Tooltip("Player speed (m/s) below which they count as standing still, which invites a neck bite.")]
        [SerializeField] private float playerIdleSpeed = 0.15f;
        [Tooltip("Seconds between the swing starting and the blow landing. The player can back out of reach during it.")]
        [SerializeField] private float attackWindUp = 0.35f;
        [Tooltip("Slack beyond attackGap still counted as a hit, so a hair's breadth does not read as a miss.")]
        [SerializeField] private float missTolerance = 0.35f;

        [Tooltip("Off by default so a gunshot is walked to, not sprinted at. Running is reserved for being shot at and for feeling the player.")]
        [SerializeField] private bool huntOnLoudNoise;
        [Tooltip("Scream loudness when it starts hunting; wakes nearby minions in a chain.")]
        [SerializeField] private float screamLoudness = 30f;

        [Header("Search & memory")]
        [Tooltip("Places checked after losing the trail, nearest the last known position first. 0 = stand still for Search Duration.")]
        [SerializeField, Min(0)] private int sweepNodeCount = 4;
        [Tooltip("Longest a search lasts before the minion gives up and goes back to sleep.")]
        [SerializeField, Min(1f)] private float sweepDuration = 18f;
        [Tooltip("Seconds spent looking around at each place it checks.")]
        [SerializeField, Min(0f)] private float sweepDwellSeconds = 1.2f;
        [Tooltip("Smallest area searched, however sure it was where the noise came from (m).")]
        [SerializeField, Min(0f)] private float sweepMinRadius = 4f;
        [Tooltip("How suspicion cools off. Sigmoidal holds it high for a while, then rolls off; Linear is the original.")]
        [SerializeField] private DecayProfile alertDecayProfile = DecayProfile.SigmoidalTension;
        [Tooltip("Total noise over the last few seconds at which an investigation becomes a hunt. A run of footsteps adds up. 0 = off.")]
        [SerializeField, Min(0f)] private float excitationHuntThreshold = 60f;
        [Tooltip("A far less alarming noise does not pull it off a gunshot; it is checked afterwards.")]
        [SerializeField] private bool prioritizeThreats = true;

        [Header("Audio (optional)")]
        [SerializeField] private AudioSource voice;
        [SerializeField] private AudioClip alertScream;

        public BlindMinionBrain Brain { get; private set; }

        /// <summary>Raised when this minion hits the player. Hook health / screen effects here.</summary>
        public event Action<BlindMinionAgent> AttackedPlayer;

        /// <summary>Damage this minion deals per landed hit.</summary>
        public float AttackDamage => attackDamage;

        private Transform _headBone;
        private bool _headBoneResolved;

        /// <summary>Where to look to meet its face. The Head bone when the rig has one, else an estimate.</summary>
        public Vector3 HeadPoint
        {
            get
            {
                if (!_headBoneResolved)
                {
                    _headBoneResolved = true;
                    var animator = _motor != null ? _motor.Animator : GetComponentInChildren<Animator>();
                    if (animator != null && animator.isHuman) _headBone = animator.GetBoneTransform(HumanBodyBones.Head);
                }
                return _headBone != null ? _headBone.position : transform.position + Vector3.up * lookHeight;
            }
        }

        /// <summary>Raised when the minion screams on starting a hunt. For audio.</summary>
        public event Action Screamed;

        /// <summary>Raised when a blow lands. True when it was a neck bite rather than a swing.</summary>
        public event Action<bool> Struck;

        /// <summary>Raised the moment a swing begins, before it is known whether it will land.</summary>
        public event Action<bool> SwingStarted;

        /// <summary>Raised when a swing finished with the player out of reach.</summary>
        public event Action Whiffed;

        /// <summary>
        /// Raised when a neck bite lands, carrying the minion that landed it. Separate from
        /// <see cref="Struck"/> because the camera needs to know *who* has hold of the player,
        /// and audio does not.
        /// </summary>
        public event Action<BlindMinionAgent> NeckBitPlayer;

        private void Awake()
        {
            // The prefab leaves this empty; it was only ever filled in as a scene override, so a
            // minion instantiated at runtime had no context at all and threw here. The bootstrap
            // is a scene singleton, which is exactly what it is for.
            if (ai == null) ai = AIBootstrap.Instance;
            if (ai == null) ai = FindFirstObjectByType<AIBootstrap>();
            if (ai == null)
            {
                Debug.LogError("BlindMinionAgent needs an AIBootstrap in the scene; disabling this minion.", this);
                enabled = false;
                return;
            }

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
                PresenceRange = presenceRange,
                HuntOnLoudNoise = huntOnLoudNoise,
                TouchRange = BodyRadius + playerRadius + attackGap,
                AttackStandoff = BodyRadius + playerRadius + attackGap,
                ScreamLoudness = screamLoudness,
                ClearHearingLoudness = clearHearingLoudness,
                MaxHearingError = maxHearingError,
                AlertnessMemory = alertnessMemory,
                AlertSensitivity = alertSensitivity,
                SweepNodeCount = sweepNodeCount,
                SweepDuration = sweepDuration,
                SweepDwellSeconds = sweepDwellSeconds,
                SweepMinRadius = sweepMinRadius,
                AlertDecayProfile = alertDecayProfile,
                ExcitationHuntThreshold = excitationHuntThreshold,
                PrioritizeThreats = prioritizeThreats,
            };
            // The golden angle spreads consecutive ids almost evenly round a circle without any
            // minion needing to know about the others, so they close in from different sides
            // instead of filing in behind one another.
            Brain.AttackAngle = (Brain.Id * 137.507f) % 360f;

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
            Screamed?.Invoke();
        }

        /// <summary>
        /// The minion's own radius in world units, so the contact gaps hold whatever the prefab is
        /// scaled to. NavMeshAgent.radius is a local value, so the lossy scale has to be applied.
        /// </summary>
        private float BodyRadius
        {
            get
            {
                var nav = GetComponent<UnityEngine.AI.NavMeshAgent>();
                float local = nav != null ? nav.radius : 0.35f;
                var s = transform.lossyScale;
                return local * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z));
            }
        }

        /// <summary>
        /// Swing or bite. A neck bite is the close, committed kill: it lands when the minion is
        /// right on top of the player, or when the player is standing still long enough to be
        /// taken hold of. Anything looser is an ordinary swipe.
        /// </summary>
        private void OnAttacked()
        {
            var world = ai.Context.World;
            bool neckBite = GapToPlayer() <= biteGap || world.PlayerVelocity.Length() <= playerIdleSpeed;

            SetTrigger(neckBite ? NeckBiteTrigger : AttackTrigger);
            SwingStarted?.Invoke(neckBite);
            StartCoroutine(ResolveSwing(neckBite));
        }

        /// <summary>
        /// The blow lands at the end of the wind-up, not the instant the animation starts.
        /// Damage used to apply on frame one of the swing, which made the attack undodgeable and
        /// left no moment at which a miss could happen — so there was nothing for a whiff sound to
        /// mark. Backing off during the wind-up now gets you out of it.
        /// </summary>
        private IEnumerator ResolveSwing(bool neckBite)
        {
            yield return new WaitForSeconds(attackWindUp);
            if (this == null || !isActiveAndEnabled) yield break;

            if (GapToPlayer() > attackGap + missTolerance)
            {
                Whiffed?.Invoke();
                yield break;
            }

            Struck?.Invoke(neckBite);
            // Before AttackedPlayer, which is what applies the damage: the camera should already be
            // locked on when a fatal bite hands over to the death sequence.
            if (neckBite) NeckBitPlayer?.Invoke(this);
            AttackedPlayer?.Invoke(this);
        }

        /// <summary>Surface-to-surface distance to the player.</summary>
        private float GapToPlayer()
        {
            Vector3 player = ai.Context.World.PlayerPosition.ToUnity();
            Vector3 a = transform.position; a.y = 0f;
            player.y = 0f;
            return Vector3.Distance(a, player) - (BodyRadius + playerRadius);
        }

        /// <summary>
        /// Switches the minion between walking locomotion and an all-out charge. The charge clip is
        /// picked once per charge, so it does not flicker between upright and crawling.
        /// </summary>
        public void SetCharging(bool charging, bool onAllFours)
        {
            SetBool(ChargingBool, charging);
            if (charging) SetBool(CrawlBool, onAllFours);
        }

        /// <summary>Plays the death clip for the shot that killed it and stops the brain driving the body.</summary>
        public void PlayDeath(bool headshot)
        {
            SetTrigger(headshot ? DeathHeadTrigger : DeathTorsoTrigger);
            enabled = false;
            var motor = GetComponent<AgentMotor>();
            if (motor != null) { motor.Stop(); motor.enabled = false; }
            var nav = GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (nav != null) nav.enabled = false;
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
