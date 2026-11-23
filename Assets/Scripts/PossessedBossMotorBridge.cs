using System.Reflection;
using Alesa.Runtime.Motion;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Non-invasive bridge connecting Agent 3's <see cref="PossessedBoss"/> (state machine,
/// intercept/pursuit destination planning, and <see cref="PossessedBoss.AppearAt"/> warps)
/// to GV's <see cref="AgentMotor"/> and <see cref="HeadLookAt"/> without modifying
/// <see cref="PossessedBoss"/>.
/// Lives in Assembly-CSharp (global namespace) alongside <see cref="PossessedBoss"/>.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PossessedBoss), typeof(NavMeshAgent), typeof(AgentMotor))]
[RequireComponent(typeof(HeadLookAt))]
public sealed class PossessedBossMotorBridge : MonoBehaviour, ILookTargetSource
{
    private const BindingFlags PrivateFieldFlags = BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly FieldInfo MotorUseNavMeshField = typeof(AgentMotor).GetField("useNavMesh", PrivateFieldFlags);
    private static readonly FieldInfo MotorSyncAgentField = typeof(AgentMotor).GetField("syncAttachedNavMeshAgent", PrivateFieldFlags);
    private static readonly FieldInfo MotorAnimatorField = typeof(AgentMotor).GetField("animator", PrivateFieldFlags);
    private static readonly FieldInfo HeadUseAnimatorIkField = typeof(HeadLookAt).GetField("useAnimatorIK", PrivateFieldFlags);

    [Header("Head Look-At")]
    [Tooltip("Height offset above the player's feet that the Possessed Boss aims its head toward.")]
    [SerializeField] private float lookHeight = 1.55f;

    [Header("Animation & NavMesh Sync")]
    [SerializeField] private Animator animator;
    [SerializeField] private bool syncNavMeshAgent = true;

    [Header("Tuning")]
    [Tooltip("Yaw rotation speed (deg/s) while facing the player during Grabbing windup.")]
    [SerializeField, Min(30f)] private float grabTurnSpeed = 540f;

    [Tooltip("Minimum destination delta (m) before re-issuing AgentMotor.SetDestination.")]
    [SerializeField, Min(0f)] private float destinationUpdateThreshold = 0.05f;

    private PossessedBoss _boss;
    private NavMeshAgent _agent;
    private AgentMotor _motor;
    private HeadLookAt _headLookAt;

    private bool _wasPresent;
    private PossessedBoss.BossState _lastState;
    private Vector3 _lastSyncedPos;
    private bool _hasActiveCommand;
    private Vector3 _lastSentDestination;
    private float _lastSentSpeed;

    private void Awake()
    {
        ResolveReferences();

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        ConfigureMotorAndHeadLook();

        _wasPresent = _boss != null && _boss.IsPresent;
        _lastState = _boss != null ? _boss.currentState : PossessedBoss.BossState.Hidden;
        _lastSyncedPos = transform.position;

        // While hidden/disabled, keep updatePosition enabled so PossessedBoss.AppearAt's
        // NavMeshAgent.Warp immediately moves transform.position before LateUpdate syncs AgentMotor.
        if (_agent != null)
        {
            _agent.updateRotation = false;
            _agent.updatePosition = !_wasPresent;
        }
    }

    private void OnDisable()
    {
        _hasActiveCommand = false;
    }

    /// <summary>
    /// Supplies the player's head position for <see cref="HeadLookAt"/> whenever the boss is present.
    /// </summary>
    public bool TryGetLookTarget(out Vector3 target)
    {
        if (_boss == null)
        {
            _boss = GetComponent<PossessedBoss>();
        }

        if (_boss != null && _boss.IsPresent && _boss.player != null)
        {
            target = _boss.player.position + Vector3.up * lookHeight;
            return true;
        }

        target = default;
        return false;
    }

    private void LateUpdate()
    {
        if (_boss == null || _agent == null || _motor == null)
        {
            ResolveReferences();
            if (_boss == null || _agent == null || _motor == null) return;
        }

        if (Time.timeScale <= 0f) return;

        PossessedBoss.BossState state = _boss.currentState;
        bool isPresent = _boss.IsPresent;

        // 1. Detect warps / teleports triggered by PossessedBoss.AppearAt(...)
        bool enteredAppearing = state == PossessedBoss.BossState.Appearing && _lastState != PossessedBoss.BossState.Appearing;
        bool becamePresent = isPresent && !_wasPresent;
        bool transformJumped = _agent.enabled && (transform.position - _lastSyncedPos).sqrMagnitude > 1.5f;
        bool agentDesynced = _agent.enabled && _agent.isOnNavMesh && (_agent.nextPosition - transform.position).sqrMagnitude > 1.5f;

        if (enteredAppearing || becamePresent || transformJumped || agentDesynced)
        {
            Vector3 warpPosition = agentDesynced ? _agent.nextPosition : transform.position;
            if (syncNavMeshAgent)
            {
                _agent.updatePosition = false;
                _agent.updateRotation = false;
            }

            _motor.Warp(warpPosition, transform.eulerAngles.y);
            _hasActiveCommand = false;
        }

        _wasPresent = isPresent;
        _lastState = state;

        // 2. Halted states: Hidden, Despawned, Finished, or Appearing
        if (!isPresent || state == PossessedBoss.BossState.Appearing)
        {
            if (_hasActiveCommand || !_motor.Arrived || _motor.CurrentSpeed > 0.01f)
            {
                _motor.Stop();
            }
            _hasActiveCommand = false;

            // Prepare NavMeshAgent so the next AppearAt() Warp moves transform.position directly.
            if (state != PossessedBoss.BossState.Appearing && !_agent.enabled)
            {
                _agent.updatePosition = true;
                _agent.updateRotation = false;
            }

            _lastSyncedPos = transform.position;
            return;
        }

        // 3. Grabbing: halt movement and smoothly rotate yaw toward the player during grab windup
        if (state == PossessedBoss.BossState.Grabbing)
        {
            if (_hasActiveCommand || !_motor.Arrived || _motor.CurrentSpeed > 0.01f)
            {
                _motor.Stop();
            }
            _hasActiveCommand = false;

            if (_boss.player != null && Time.deltaTime > 0f)
            {
                Vector3 toPlayer = _boss.player.position - transform.position;
                toPlayer.y = 0f;
                if (toPlayer.sqrMagnitude > 0.0001f)
                {
                    float targetYaw = Mathf.Atan2(toPlayer.x, toPlayer.z) * Mathf.Rad2Deg;
                    float currentYaw = _motor.Solver != null ? _motor.Solver.Heading : transform.eulerAngles.y;
                    float nextYaw = Mathf.MoveTowardsAngle(currentYaw, targetYaw, grabTurnSpeed * Time.deltaTime);
                    float normalizedYaw = nextYaw < 0f ? nextYaw + 360f : nextYaw % 360f;

                    transform.rotation = Quaternion.Euler(0f, normalizedYaw, 0f);
                    if (_motor.Solver != null)
                    {
                        _motor.Solver.Heading = normalizedYaw;
                    }
                }
            }

            _lastSyncedPos = transform.position;
            return;
        }

        // 4. Intercepting or Pursuing: feed NavMeshAgent destination and speed into AgentMotor
        if (syncNavMeshAgent)
        {
            _agent.updatePosition = false;
            _agent.updateRotation = false;
        }

        if ((state == PossessedBoss.BossState.Intercepting || state == PossessedBoss.BossState.Pursuing) &&
            _agent.enabled &&
            _agent.isOnNavMesh &&
            !_agent.isStopped &&
            (_agent.hasPath || _agent.pathPending))
        {
            Vector3 destination = _agent.destination;
            float speed = Mathf.Max(0.1f, _boss.movementSpeed);
            float thresholdSqr = destinationUpdateThreshold * destinationUpdateThreshold;

            bool changed = !_hasActiveCommand ||
                           (destination - _lastSentDestination).sqrMagnitude > thresholdSqr ||
                           Mathf.Abs(speed - _lastSentSpeed) > 0.05f;

            if (changed)
            {
                _motor.SetDestination(destination, speed);
                _lastSentDestination = destination;
                _lastSentSpeed = speed;
                _hasActiveCommand = true;
            }
        }
        else if (!_agent.enabled || _agent.isStopped)
        {
            if (_hasActiveCommand || !_motor.Arrived || _motor.CurrentSpeed > 0.01f)
            {
                _motor.Stop();
            }
            _hasActiveCommand = false;
        }

        _lastSyncedPos = transform.position;
    }

    private void ResolveReferences()
    {
        if (_boss == null) _boss = GetComponent<PossessedBoss>();
        if (_agent == null) _agent = GetComponent<NavMeshAgent>();
        if (_motor == null) _motor = GetComponent<AgentMotor>();
        if (_headLookAt == null) _headLookAt = GetComponent<HeadLookAt>();
    }

    private void ConfigureMotorAndHeadLook()
    {
        if (_motor != null)
        {
            MotorUseNavMeshField?.SetValue(_motor, true);
            MotorSyncAgentField?.SetValue(_motor, syncNavMeshAgent);
            if (animator != null)
            {
                MotorAnimatorField?.SetValue(_motor, animator);
            }
        }

        if (_headLookAt == null)
        {
            _headLookAt = gameObject.AddComponent<HeadLookAt>();
        }

        if (_headLookAt != null)
        {
            HeadUseAnimatorIkField?.SetValue(_headLookAt, true);
            _headLookAt.SetSource(this);
        }
    }
}
