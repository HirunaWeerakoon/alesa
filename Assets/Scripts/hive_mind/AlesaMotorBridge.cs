using Alesa.Core;
using Alesa.Runtime.Motion;
using UnityEngine;
using UnityEngine.AI;

namespace Alesa.Runtime
{
    /// <summary>
    /// Non-invasive bridge connecting Agent 1's <see cref="AlesaFollower"/> (which computes follow
    /// anchors, companion states, and stuck recovery) to GV's <see cref="AgentMotor"/> and
    /// <see cref="HeadLookAt"/> without modifying any Agent 1 scripts.
    /// Lives in Assembly-CSharp alongside <see cref="AcousticBusBridge"/> so it can reference both
    /// <see cref="AlesaFollower"/> and the auto-referenced <c>Alesa.Runtime</c> assembly.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AgentMotor), typeof(AlesaFollower))]
    public sealed class AlesaMotorBridge : MonoBehaviour, ILookTargetSource
    {
        [Header("Head Look-At")]
        [Tooltip("Height offset above the player's feet that Alesa's head aims toward.")]
        [SerializeField] private float lookHeight = 1.55f;

        [Header("Motor Sync")]
        [Tooltip("Squared distance (m^2) between NavMeshAgent.nextPosition and transform.position that triggers AgentMotor.Warp.")]
        [SerializeField, Min(0.25f)] private float teleportSyncThresholdSqr = 4f;

        [Tooltip("Minimum anchor movement (m) before re-issuing AgentMotor.SetDestination to avoid redundant per-frame repaths.")]
        [SerializeField, Min(0f)] private float destinationUpdateThreshold = 0.05f;

        [Header("Optional References")]
        [SerializeField] private AlesaGameplayLifecycle lifecycle;

        private AgentMotor _motor;
        private AlesaFollower _follower;
        private NavMeshAgent _navMeshAgent;

        private bool _hasActiveCommand;
        private Vector3 _lastSentDestination;
        private float _lastSentSpeed;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnDisable()
        {
            _hasActiveCommand = false;
        }

        /// <summary>
        /// Supplies Nathan's head position as Alesa's look target for <see cref="HeadLookAt"/>
        /// once Alesa has been rescued (i.e. not <see cref="CompanionState.Dormant"/>).
        /// </summary>
        public bool TryGetLookTarget(out Vector3 worldPoint)
        {
            worldPoint = default;
            if (_follower == null)
            {
                _follower = GetComponent<AlesaFollower>();
            }

            if (_follower == null ||
                _follower.playerTransform == null ||
                _follower.CurrentState == CompanionState.Dormant)
            {
                return false;
            }

            worldPoint = _follower.playerTransform.position + Vector3.up * lookHeight;
            return true;
        }

        private void LateUpdate()
        {
            if (_motor == null || _follower == null)
            {
                ResolveReferences();
                if (_motor == null || _follower == null) return;
            }

            // 1. Sync emergency teleports triggered by AlesaFollower (Tier4_Teleport calls agent.Warp).
            if (_navMeshAgent != null &&
                _navMeshAgent.enabled &&
                _navMeshAgent.isOnNavMesh &&
                Vector3.SqrMagnitude(_navMeshAgent.nextPosition - transform.position) > teleportSyncThresholdSqr)
            {
                _motor.Warp(_navMeshAgent.nextPosition);
                _hasActiveCommand = false;
            }

            // 2. Halt locomotion when paused, in modal UI, lacking a valid anchor, or holding position.
            CompanionState state = _follower.CurrentState;
            bool blockedByLifecycle = Time.timeScale <= 0f || (lifecycle != null && !lifecycle.CanMove);
            bool holdPosition = state == CompanionState.Dormant ||
                                state == CompanionState.Idle ||
                                state == CompanionState.WaitingAim ||
                                _follower.IsHoldingPosition;

            if (blockedByLifecycle || !_follower.HasValidAnchor || holdPosition)
            {
                if (_hasActiveCommand || !_motor.Arrived || _motor.CurrentSpeed > 0.01f)
                {
                    _motor.Stop();
                }
                _hasActiveCommand = false;
                KeepOutOfPlayer();
                return;
            }

            KeepOutOfPlayer();

            // 3. Forward AlesaFollower's computed follow anchor and speed to AgentMotor.
            float targetSpeed = _follower.StateMachine != null
                ? _follower.StateMachine.GetTargetSpeed()
                : (state == CompanionState.Sprinting || state == CompanionState.Recovering
                    ? _follower.maxSprintSpeed
                    : _follower.maxWalkSpeed);

            if (targetSpeed <= 0f)
            {
                _motor.Stop();
                _hasActiveCommand = false;
                return;
            }

            Vector3 anchor = _follower.CurrentAnchorPosition;
            float thresholdSqr = destinationUpdateThreshold * destinationUpdateThreshold;
            bool destinationChanged = !_hasActiveCommand ||
                                      Vector3.SqrMagnitude(anchor - _lastSentDestination) > thresholdSqr ||
                                      Mathf.Abs(targetSpeed - _lastSentSpeed) > 0.05f;

            if (destinationChanged)
            {
                _motor.SetDestination(anchor, targetSpeed);
                _lastSentDestination = anchor;
                _lastSentSpeed = targetSpeed;
                _hasActiveCommand = true;
            }
        }

        /// <summary>
        /// Pushes Alesa back out when she and Nathan end up inside one another.
        /// </summary>
        /// <remarks>
        /// Nothing in the navigation layer prevents it. Her NavMeshAgent has <c>updatePosition</c>
        /// off because <see cref="AgentMotor"/> owns the transform, so the agent's own avoidance
        /// never moves her — and Nathan is a CharacterController, which NavMesh avoidance cannot see
        /// at all. This runs in LateUpdate, after the motor has already written this frame's
        /// position, and nudges rather than snaps so the correction is not visible as a jump.
        /// </remarks>
        private void KeepOutOfPlayer()
        {
            Transform player = _follower.playerTransform;
            float space = _follower.minPersonalSpace;
            if (player == null || space <= 0f || _follower.separationPushSpeed <= 0f) return;

            Vector3 offset = transform.position - player.position;
            offset.y = 0f;
            float distance = offset.magnitude;
            if (distance >= space) return;

            Vector3 away = distance > 0.01f ? offset / distance : -player.forward;
            float step = Mathf.Min(space - distance, _follower.separationPushSpeed * Time.deltaTime);
            Vector3 target = transform.position + away * step;

            // Never push her into geometry to get her out of Nathan: if the only way out is through
            // a wall, leave the overlap alone and let the follow anchor resolve it.
            if (NavMesh.Raycast(transform.position, target, out _, NavMesh.AllAreas)) return;
            if (!NavMesh.SamplePosition(target, out NavMeshHit hit, 0.25f, NavMesh.AllAreas)) return;

            // Move the transform only. AgentMotor re-reads it into the solver at the top of its next
            // Update, so this needs no Warp — and a Warp here would clear her path every frame she
            // happened to be touching Nathan.
            transform.position = hit.position;
        }

        private void ResolveReferences()
        {
            if (_motor == null) _motor = GetComponent<AgentMotor>();
            if (_follower == null) _follower = GetComponent<AlesaFollower>();
            if (_navMeshAgent == null) _navMeshAgent = GetComponent<NavMeshAgent>();
            if (lifecycle == null) lifecycle = GetComponent<AlesaGameplayLifecycle>();
        }
    }
}
