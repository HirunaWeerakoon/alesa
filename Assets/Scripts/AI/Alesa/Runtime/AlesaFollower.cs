using UnityEngine;
using UnityEngine.AI;
using Alesa.Core;

namespace Alesa.Runtime
{
    /// <summary>
    /// Drives Alesa's physical companion behavior in Unity.
    /// Manages the dynamic follow anchor (~1.75m behind Nathan), resolves NavMesh geometry,
    /// and handles wall-backing fallbacks.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent))]
    public class AlesaFollower : MonoBehaviour
    {
        [Header("Target & References")]
        [Tooltip("Nathan's Transform. If unassigned, automatically finds Player in scene.")]
        public Transform playerTransform;

        [Tooltip("Alesa's NavMeshAgent component.")]
        public NavMeshAgent agent;

        [Header("Anchor Formation Settings")]
        [Tooltip("Target distance to trail behind Nathan (default ~1.75m).")]
        [Range(0.5f, 4.0f)]
        public float followDistance = 1.75f;

        [Tooltip("Minimum personal space buffer (prevents Alesa from pushing into Nathan).")]
        [Range(0.5f, 2.0f)]
        public float minPersonalSpace = 1.0f;

        [Tooltip("Lateral offset from center (positive = right flank, negative = left flank).")]
        [Range(-1.5f, 1.5f)]
        public float lateralOffset = 0.0f;

        [Tooltip("Exponential smoothing speed to prevent anchor whip-around on quick turns.")]
        [Range(2f, 20f)]
        public float anchorSmoothingSpeed = 8.0f;

        [Tooltip("Search radius when projecting anchor onto walkable NavMesh.")]
        [Range(0.5f, 4.0f)]
        public float navMeshSampleRadius = 2.0f;

        [Header("Arrival Steering Settings")]
        [Tooltip("Distance from anchor at which Alesa completely stops (m).")]
        [Range(0.1f, 1.5f)]
        public float stoppingDistance = 0.4f;

        [Tooltip("Distance from anchor at which Alesa begins smooth deceleration (m).")]
        [Range(1.0f, 6.0f)]
        public float slowingRadius = 2.5f;

        [Tooltip("Standard walking top speed (m/s) matching Nathan's walk speed.")]
        public float maxWalkSpeed = 5.0f;

        [Tooltip("Sprint top speed (m/s) matching Nathan's sprint speed.")]
        public float maxSprintSpeed = 8.5f;

        [Tooltip("Acceleration rate (m/s^2).")]
        public float acceleration = 8.0f;

        [Tooltip("Deceleration rate (m/s^2).")]
        public float deceleration = 12.0f;

        [Header("State Synchronization Settings")]
        [Tooltip("When enabled, treats Alesa as freed for isolated test scenes where dialpad puzzle is not present.")]
        public bool autoFreeInTestScene = true;

        [Header("Stuck Detection & Recovery Settings")]
        [Tooltip("Seconds with zero progress before stuck state is flagged.")]
        [Range(0.5f, 3.0f)]
        public float stuckDetectionTime = 1.0f;

        [Tooltip("Minimum movement required per second (m/s) to count as healthy progress.")]
        public float minDisplacementRate = 0.2f;

        [Tooltip("Distance from Nathan at which Alesa triggers emergency catch-up/teleport.")]
        public float extremeSeparationDistance = 18.0f;

        [Tooltip("Seconds stuck before escalating to emergency teleport safeguard.")]
        public float teleportTimeout = 5.5f;

        [Header("Debug Status (Read-Only)")]
        [SerializeField] private CompanionState currentState = CompanionState.Dormant;
        [SerializeField] private Vector3 currentAnchorPosition;
        [SerializeField] private float currentDistanceToAnchor;
        [SerializeField] private float currentTargetSpeed;
        [SerializeField] private bool hasValidAnchor = false;
        [SerializeField] private bool isStuck = false;
        [SerializeField] private RecoveryTier currentRecoveryTier = RecoveryTier.None;
        [SerializeField] private float currentStuckTimer = 0f;
        [SerializeField] private int totalRecoveriesExecuted = 0;

        private FollowAnchorCalculator _calculator;
        private ArrivalSteeringCalculator _steeringCalculator;
        private FollowerRecoveryController _recoveryController;
        private CompanionStateMachine _stateMachine;
        private PlayerMotor _playerMotor;
        private PlayerGameplay _playerGameplay;
        private Vector3 _lastSampledPosition;
        private float _lastRecoveryActionTime;
        private bool _manualRescued;

        public CompanionStateMachine StateMachine => _stateMachine ??= new CompanionStateMachine(autoFreeInTestScene ? CompanionState.Following : CompanionState.Dormant);
        public ArrivalSteeringCalculator SteeringCalculator => _steeringCalculator ??= new ArrivalSteeringCalculator(stoppingDistance, slowingRadius, acceleration, deceleration);
        public FollowerRecoveryController RecoveryController => _recoveryController ??= new FollowerRecoveryController(stuckDetectionTime, minDisplacementRate, extremeSeparationDistance);
        public Vector3 CurrentAnchorPosition => currentAnchorPosition;
        public bool HasValidAnchor => hasValidAnchor;
        public bool IsStuck => isStuck;
        public RecoveryTier CurrentRecoveryTier => currentRecoveryTier;
        public CompanionState CurrentState => _stateMachine != null ? _stateMachine.CurrentState : currentState;

        private void Awake()
        {
            if (agent == null) agent = GetComponent<NavMeshAgent>();
            InitializeReferences();
            _calculator = new FollowAnchorCalculator(followDistance, minPersonalSpace, lateralOffset);
            _steeringCalculator = new ArrivalSteeringCalculator(stoppingDistance, slowingRadius, acceleration, deceleration);
            _recoveryController = new FollowerRecoveryController(stuckDetectionTime, minDisplacementRate, extremeSeparationDistance)
            {
                TeleportTimeout = teleportTimeout
            };
            _stateMachine = new CompanionStateMachine(autoFreeInTestScene ? CompanionState.Following : CompanionState.Dormant);
            _stateMachine.WalkSpeed = maxWalkSpeed;
            _stateMachine.SprintSpeed = maxSprintSpeed;
            _stateMachine.StopDistanceThreshold = stoppingDistance;
            _stateMachine.OnStateChanged += (prev, next) => currentState = next;
        }

        private void OnEnable()
        {
            InitializeReferences();
            _lastSampledPosition = transform.position;
            if (playerTransform != null)
            {
                currentAnchorPosition = ComputeRawAnchor(playerTransform.position, playerTransform.forward);
            }
        }

        private void OnDisable()
        {
            if (agent != null && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.ResetPath();
            }
        }

        private void Update()
        {
            if (playerTransform == null)
            {
                InitializeReferences();
                if (playerTransform == null) return;
            }

            if (Time.timeScale <= 0f)
            {
                if (agent != null && agent.isOnNavMesh && !agent.isStopped)
                {
                    agent.isStopped = true;
                }
                return;
            }

            UpdateFollowAnchor(Time.deltaTime);

            if (Application.isPlaying)
            {
                UpdateCompanionState();
                ApplySteering(Time.deltaTime);
            }
        }

        public void InitializeReferences()
        {
            if (agent == null) agent = GetComponent<NavMeshAgent>();

            if (playerTransform == null)
            {
                var playerObj = GameObject.FindWithTag("Player");
                if (playerObj == null)
                {
                    // Fallback: search for Player_Nathan in hierarchy
                    var nathan = GameObject.Find("Player_Nathan");
                    if (nathan != null) playerObj = nathan;
                }

                if (playerObj != null)
                {
                    playerTransform = playerObj.transform;
                    _playerMotor = playerObj.GetComponent<PlayerMotor>();
                    _playerGameplay = playerObj.GetComponent<PlayerGameplay>();
                }
            }
            else
            {
                if (_playerMotor == null) _playerMotor = playerTransform.GetComponent<PlayerMotor>();
                if (_playerGameplay == null) _playerGameplay = playerTransform.GetComponent<PlayerGameplay>();
            }

            if (_calculator == null)
            {
                _calculator = new FollowAnchorCalculator(followDistance, minPersonalSpace, lateralOffset);
            }
        }

        /// <summary>
        /// Calculates the dynamic anchor, projects onto the NavMesh, and handles wall-backing fallbacks.
        /// </summary>
        public Vector3 UpdateFollowAnchor(float deltaTime)
        {
            if (playerTransform == null) return transform.position;

            _calculator.PreferredDistance = followDistance;
            _calculator.MinSeparation = minPersonalSpace;
            _calculator.LateralOffset = lateralOffset;

            // 1. Calculate ideal geometric anchor behind Nathan
            Vector3 rawTarget = ComputeRawAnchor(playerTransform.position, playerTransform.forward);

            // 2. Validate and project onto NavMesh
            Vector3 resolvedNavMeshTarget = ResolveNavMeshPosition(rawTarget, playerTransform.position, playerTransform.right);

            // 3. Smooth anchor position to eliminate whip-around jitter during quick turns
            if (currentAnchorPosition == Vector3.zero || !Application.isPlaying)
            {
                currentAnchorPosition = resolvedNavMeshTarget;
            }
            else
            {
                NodePosition currentPos = currentAnchorPosition.ToNodePosition();
                NodePosition targetPos = resolvedNavMeshTarget.ToNodePosition();
                NodePosition smoothed = _calculator.SmoothAnchor(currentPos, targetPos, deltaTime, anchorSmoothingSpeed);
                currentAnchorPosition = smoothed.ToVector3();
            }

            return currentAnchorPosition;
        }

        private Vector3 ComputeRawAnchor(Vector3 playerPos, Vector3 playerForward)
        {
            NodePosition pPos = playerPos.ToNodePosition();
            NodePosition pFwd = playerForward.ToNodePosition();

            NodePosition ideal = _calculator.CalculateIdealAnchor(pPos, pFwd);
            NodePosition clamped = _calculator.ClampPersonalSpace(pPos, ideal);

            return clamped.ToVector3();
        }

        /// <summary>
        /// Projects the target onto the NavMesh. If Nathan's back is against a wall,
        /// tests side flank positions so Alesa waits beside him rather than inside the wall.
        /// </summary>
        public Vector3 ResolveNavMeshPosition(Vector3 idealPos, Vector3 playerPos, Vector3 playerRight)
        {
            // Primary check: is ideal position on NavMesh?
            if (NavMesh.SamplePosition(idealPos, out NavMeshHit hit, navMeshSampleRadius, NavMesh.AllAreas))
            {
                hasValidAnchor = true;
                return hit.position;
            }

            // Wall-backing fallback 1: Test right flank
            Vector3 rightFlank = playerPos + playerRight * followDistance;
            if (NavMesh.SamplePosition(rightFlank, out hit, navMeshSampleRadius, NavMesh.AllAreas))
            {
                hasValidAnchor = true;
                return hit.position;
            }

            // Wall-backing fallback 2: Test left flank
            Vector3 leftFlank = playerPos - playerRight * followDistance;
            if (NavMesh.SamplePosition(leftFlank, out hit, navMeshSampleRadius, NavMesh.AllAreas))
            {
                hasValidAnchor = true;
                return hit.position;
            }

            // Last resort fallback: Sample directly at Nathan's position
            if (NavMesh.SamplePosition(playerPos, out hit, navMeshSampleRadius, NavMesh.AllAreas))
            {
                hasValidAnchor = true;
                return hit.position;
            }

            hasValidAnchor = false;
            return idealPos;
        }

        /// <summary>
        /// Explicit rescue trigger called by AlesaRescueController or dialpad events.
        /// </summary>
        public void ActivateAfterRescue()
        {
            _manualRescued = true;
        }

        /// <summary>
        /// Gathers live player motor, gameplay, and spatial signals and feeds them into the CompanionStateMachine.
        /// </summary>
        public void UpdateCompanionState()
        {
            if (_stateMachine == null) return;

            bool isRescued = autoFreeInTestScene || _manualRescued || (_playerGameplay != null && _playerGameplay.AlesaFreed);
            bool controlsEnabled = _playerMotor == null || _playerMotor.ControlsEnabled;
            bool isAiming = _playerGameplay != null && _playerGameplay.State.CurrentAction == PlayerState.Action.Aiming;
            bool isSprinting = _playerMotor != null && _playerMotor.IsSprinting;
            bool hasMoveInput = _playerMotor != null && _playerMotor.HasMovementInput;

            float distToPlayer = Vector3.Distance(transform.position, playerTransform.position);
            float distToAnchor = Vector3.Distance(transform.position, currentAnchorPosition);

            bool hasValidPath = true;
            if (agent != null && agent.isOnNavMesh && agent.hasPath)
            {
                hasValidPath = agent.pathStatus != NavMeshPathStatus.PathInvalid;
            }

            // Evaluate movement progress and stuck conditions
            float displacement = Vector3.Distance(transform.position, _lastSampledPosition);
            _lastSampledPosition = transform.position;

            RecoveryTier tier = _recoveryController.EvaluateProgress(
                displacement,
                currentTargetSpeed,
                hasValidPath,
                distToPlayer,
                Time.deltaTime);

            isStuck = _recoveryController.IsStuck;
            currentRecoveryTier = tier;
            currentStuckTimer = _recoveryController.StuckTimer;

            if (isStuck)
            {
                ExecuteRecoveryAction(tier);
            }

            CompanionContext context = new CompanionContext
            {
                IsAlesaRescued = isRescued,
                IsPlayerControlsEnabled = controlsEnabled,
                IsPlayerAiming = isAiming,
                IsPlayerSprinting = isSprinting,
                HasPlayerMovementInput = hasMoveInput,
                DistanceToPlayer = distToPlayer,
                DistanceToFollowAnchor = distToAnchor,
                HasValidNavPath = hasValidPath,
                IsStuck = isStuck
            };

            _stateMachine.WalkSpeed = maxWalkSpeed;
            _stateMachine.SprintSpeed = maxSprintSpeed;
            _stateMachine.StopDistanceThreshold = stoppingDistance;

            _stateMachine.EvaluateState(in context);
            currentState = _stateMachine.CurrentState;
        }

        private void ExecuteRecoveryAction(RecoveryTier tier)
        {
            if (Time.time - _lastRecoveryActionTime < 0.75f) return;
            _lastRecoveryActionTime = Time.time;

            switch (tier)
            {
                case RecoveryTier.Tier1_Repath:
                    if (agent != null && agent.isOnNavMesh)
                    {
                        agent.ResetPath();
                        agent.SetDestination(currentAnchorPosition);
                        _recoveryController.NoteRecoveryActionExecuted();
                        totalRecoveriesExecuted = _recoveryController.RecoveryCount;
                    }
                    break;

                case RecoveryTier.Tier2_FlankOffset:
                    // Invert or alternate lateral flank to maneuver around obstruction
                    lateralOffset = (Mathf.Abs(lateralOffset) < 0.1f) ? 1.25f : -lateralOffset;
                    UpdateFollowAnchor(0.1f);
                    if (agent != null && agent.isOnNavMesh)
                    {
                        agent.ResetPath();
                        agent.SetDestination(currentAnchorPosition);
                        _recoveryController.NoteRecoveryActionExecuted();
                        totalRecoveriesExecuted = _recoveryController.RecoveryCount;
                    }
                    break;

                case RecoveryTier.Tier3_WaypointNav:
                    // Request sprint catch-up
                    if (agent != null && agent.isOnNavMesh)
                    {
                        agent.speed = maxSprintSpeed;
                        agent.SetDestination(currentAnchorPosition);
                        _recoveryController.NoteRecoveryActionExecuted();
                        totalRecoveriesExecuted = _recoveryController.RecoveryCount;
                    }
                    break;

                case RecoveryTier.Tier4_Teleport:
                    // Emergency warp safeguard behind player out of camera view
                    if (agent != null && agent.isOnNavMesh)
                    {
                        Vector3 warpTarget = currentAnchorPosition;
                        if (NavMesh.SamplePosition(warpTarget, out NavMeshHit hit, 3.0f, NavMesh.AllAreas))
                        {
                            agent.Warp(hit.position);
                            _recoveryController.Reset();
                            _recoveryController.NoteRecoveryActionExecuted();
                            totalRecoveriesExecuted = _recoveryController.RecoveryCount;
                            isStuck = false;
                            currentRecoveryTier = RecoveryTier.None;
                        }
                    }
                    break;
            }
        }

        /// <summary>
        /// Applies Craig Reynolds Arrival steering: smoothly slows down within slowingRadius
        /// and comes to a complete halt at stoppingDistance to eliminate jitter and overshooting.
        /// </summary>
        public void ApplySteering(float deltaTime)
        {
            if (agent == null || !agent.isOnNavMesh) return;

            currentDistanceToAnchor = Vector3.Distance(transform.position, currentAnchorPosition);

            // Update calculator properties
            _steeringCalculator.StoppingDistance = stoppingDistance;
            _steeringCalculator.SlowingRadius = slowingRadius;
            _steeringCalculator.Acceleration = acceleration;
            _steeringCalculator.Deceleration = deceleration;

            // Determine target speed depending on companion state
            float maxSpeed = _stateMachine != null ? _stateMachine.GetTargetSpeed() : ((currentState == CompanionState.Sprinting) ? maxSprintSpeed : maxWalkSpeed);

            if (currentState == CompanionState.WaitingAim || currentState == CompanionState.Idle || currentState == CompanionState.Dormant)
            {
                currentTargetSpeed = 0f;
                agent.isStopped = true;
                agent.velocity = Vector3.zero;
                return;
            }

            currentTargetSpeed = _steeringCalculator.CalculateDesiredSpeed(currentDistanceToAnchor, maxSpeed);

            if (currentDistanceToAnchor <= stoppingDistance)
            {
                agent.isStopped = true;
                agent.velocity = Vector3.zero;
                return;
            }

            agent.isStopped = false;
            agent.speed = _steeringCalculator.SmoothSpeed(agent.speed, currentTargetSpeed, deltaTime);
            agent.acceleration = acceleration;
            agent.SetDestination(currentAnchorPosition);
        }

        private void OnDrawGizmos()
        {
            if (playerTransform == null) return;

            Vector3 playerPos = playerTransform.position;

            // Draw Personal Space Ring around Nathan
            Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.25f);
            Gizmos.DrawWireSphere(playerPos + Vector3.up * 0.1f, minPersonalSpace);

            // Draw Follow Anchor & Steering Rings
            if (currentAnchorPosition != Vector3.zero)
            {
                Gizmos.color = hasValidAnchor ? new Color(0f, 0.9f, 1f, 0.85f) : new Color(1f, 0.2f, 0.2f, 0.85f);
                Gizmos.DrawWireSphere(currentAnchorPosition + Vector3.up * 0.2f, 0.35f);
                Gizmos.DrawLine(playerPos + Vector3.up * 0.5f, currentAnchorPosition + Vector3.up * 0.2f);

                // Arrival deceleration zone (light green)
                Gizmos.color = new Color(0.2f, 1f, 0.3f, 0.25f);
                Gizmos.DrawWireSphere(currentAnchorPosition + Vector3.up * 0.1f, slowingRadius);

                // Complete stop ring (light red)
                Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.35f);
                Gizmos.DrawWireSphere(currentAnchorPosition + Vector3.up * 0.1f, stoppingDistance);
            }

            // Draw WaitingAim protective aura
            if (currentState == CompanionState.WaitingAim)
            {
                Gizmos.color = new Color(1f, 0.6f, 0f, 0.9f);
                Gizmos.DrawWireSphere(transform.position + Vector3.up * 1f, 0.6f);
            }

            // Draw Stuck warning marker and recovery line
            if (isStuck)
            {
                Gizmos.color = new Color(1f, 0.15f, 0.15f, 0.95f);
                Gizmos.DrawWireSphere(transform.position + Vector3.up * 1.2f, 0.8f);
                Gizmos.DrawLine(transform.position + Vector3.up * 1.2f, currentAnchorPosition + Vector3.up * 0.2f);
            }
        }
    }
}
