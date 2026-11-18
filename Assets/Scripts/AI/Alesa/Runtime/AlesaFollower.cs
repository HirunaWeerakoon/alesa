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

        [Header("Debug Status (Read-Only)")]
        [SerializeField] private CompanionState currentState = CompanionState.Dormant;
        [SerializeField] private Vector3 currentAnchorPosition;
        [SerializeField] private bool hasValidAnchor = false;

        private FollowAnchorCalculator _calculator;
        private CompanionStateMachine _stateMachine;
        private PlayerMotor _playerMotor;
        private PlayerGameplay _playerGameplay;

        public CompanionStateMachine StateMachine => _stateMachine ??= new CompanionStateMachine(CompanionState.Following);
        public Vector3 CurrentAnchorPosition => currentAnchorPosition;
        public bool HasValidAnchor => hasValidAnchor;
        public CompanionState CurrentState => _stateMachine != null ? _stateMachine.CurrentState : currentState;

        private void Awake()
        {
            if (agent == null) agent = GetComponent<NavMeshAgent>();
            InitializeReferences();
            _calculator = new FollowAnchorCalculator(followDistance, minPersonalSpace, lateralOffset);
            _stateMachine = new CompanionStateMachine(CompanionState.Following);
            _stateMachine.OnStateChanged += (prev, next) => currentState = next;
        }

        private void OnEnable()
        {
            InitializeReferences();
            if (playerTransform != null)
            {
                currentAnchorPosition = ComputeRawAnchor(playerTransform.position, playerTransform.forward);
            }
        }

        private void Update()
        {
            if (playerTransform == null)
            {
                InitializeReferences();
                if (playerTransform == null) return;
            }

            UpdateFollowAnchor(Time.deltaTime);
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

        private void OnDrawGizmos()
        {
            if (playerTransform == null) return;

            Vector3 playerPos = playerTransform.position;

            // Draw Personal Space Ring around Nathan
            Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.25f);
            Gizmos.DrawWireSphere(playerPos + Vector3.up * 0.1f, minPersonalSpace);

            // Draw Follow Anchor
            if (currentAnchorPosition != Vector3.zero)
            {
                Gizmos.color = hasValidAnchor ? new Color(0f, 0.9f, 1f, 0.85f) : new Color(1f, 0.2f, 0.2f, 0.85f);
                Gizmos.DrawWireSphere(currentAnchorPosition + Vector3.up * 0.2f, 0.35f);
                Gizmos.DrawLine(playerPos + Vector3.up * 0.5f, currentAnchorPosition + Vector3.up * 0.2f);
            }
        }
    }
}
