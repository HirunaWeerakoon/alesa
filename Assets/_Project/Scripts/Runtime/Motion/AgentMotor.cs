using System.Collections.Generic;
using Alesa.Core.AI;
using Alesa.Core.Motion;
using UnityEngine;
using UnityEngine.AI;
using CoreVec3 = System.Numerics.Vector3;

namespace Alesa.Runtime.Motion
{
    /// <summary>
    /// 3D character locomotion and smooth rotation controller (Agent Controller foundation).
    /// Bridges an <see cref="IMobileBrain"/> (or direct waypoint/destination commands) to
    /// <see cref="MotionSolver"/>, Unity <see cref="NavMesh"/> path queries, transform motion,
    /// and optional <see cref="UnityEngine.Animator"/> locomotion parameters.
    /// </summary>
    public sealed class AgentMotor : MonoBehaviour
    {
        private static readonly int SpeedParam = Animator.StringToHash("Speed");
        private static readonly int AngularVelocityParam = Animator.StringToHash("AngularVelocity");
        private static readonly int TurningInPlaceParam = Animator.StringToHash("TurningInPlace");

        [Header("Kinematic Motion")]
        [SerializeField] private float acceleration = 8f;
        [SerializeField] private float deceleration = 12f;
        [SerializeField] private float turnSpeed = 300f;
        [SerializeField] private float turnInPlaceAngle = 75f;
        [SerializeField] private float turnInPlaceSpeed = 220f;
        [SerializeField] private float lookahead = 1.5f;
        [SerializeField] private float stoppingDistance = 0.3f;
        [SerializeField, Range(0f, 1f)] private float cornerSlowdown = 0.6f;

        [Header("Pathing & Grounding")]
        [Tooltip("Queries Unity NavMesh paths when a baked NavMesh is available; falls back to direct steering otherwise.")]
        [SerializeField] private bool useNavMesh = true;
        [SerializeField] private float repathInterval = 0.25f;
        [SerializeField] private float repathTargetThreshold = 0.35f;
        [SerializeField] private float navMeshSampleRadius = 2f;
        [SerializeField] private int navMeshAreaMask = NavMesh.AllAreas;
        [Tooltip("Keeps an attached NavMeshAgent synchronized without letting it override MotionSolver rotation/position.")]
        [SerializeField] private bool syncAttachedNavMeshAgent = true;

        [Header("Animation (optional)")]
        [SerializeField] private Animator animator;

        private readonly List<CoreVec3> _corePath = new List<CoreVec3>(16);
        private readonly List<Vector3> _unityPath = new List<Vector3>(16);
        private readonly HashSet<int> _animatorParameters = new HashSet<int>();

        private NavMeshPath _navMeshPath;
        private NavMeshAgent _navMeshAgent;
        private Animator _cachedAnimator;
        private float _nextRepathTime;
        private Vector3 _lastPathTarget;
        private bool _hasLastPathTarget;

        // Manual command state (used when no IMobileBrain is bound)
        private bool _hasManualDestination;
        private bool _hasManualWaypoints;
        private Vector3 _manualDestination;
        private float _manualMaxSpeed;
        private Vector3? _manualLookAt;

        public Animator Animator
        {
            get
            {
                ResolveAnimatorIfNeeded();
                return animator;
            }
        }

        public IMobileBrain Brain { get; private set; }
        public MotionSolver Solver { get; private set; }

        public float CurrentSpeed => Solver != null ? Solver.Speed : 0f;
        public float AngularVelocity => Solver != null ? Solver.AngularVelocity : 0f;
        public float Heading => Solver != null ? Solver.Heading : transform.eulerAngles.y;
        public Vector3 Velocity => Solver != null ? Solver.Forward.ToUnity() * Solver.Speed : Vector3.zero;
        public bool TurningInPlace => Solver != null && Solver.TurningInPlace;
        public bool Arrived => Solver == null || Solver.Arrived;
        public float RemainingDistance => Solver != null ? Solver.RemainingDistance : 0f;
        public IReadOnlyList<Vector3> CurrentPath => _unityPath;

        private void Awake()
        {
            EnsureInitialized();
        }

        /// <summary>Binds a decision brain whose <see cref="IMobileBrain.Intent"/> will drive this motor.</summary>
        public void Bind(IMobileBrain brain)
        {
            EnsureInitialized();
            Brain = brain;
            _hasManualDestination = false;
            _hasManualWaypoints = false;
            ClearPath();
        }

        /// <summary>Detaches any bound brain and clears active movement targets.</summary>
        public void Unbind()
        {
            Brain = null;
            Stop();
        }

        /// <summary>Directly commands the motor to move to a world-space point (when not driven by a bound brain).</summary>
        public void SetDestination(Vector3 worldTarget, float maxSpeed, Vector3? lookAt = null)
        {
            EnsureInitialized();
            _hasManualDestination = true;
            _hasManualWaypoints = false;
            _manualDestination = worldTarget;
            _manualMaxSpeed = Mathf.Max(0f, maxSpeed);
            _manualLookAt = lookAt;
            _hasLastPathTarget = false;
        }

        /// <summary>Directly feeds an explicit polyline of world-space waypoints into <see cref="MotionSolver"/>.</summary>
        public void SetWaypoints(IReadOnlyList<Vector3> waypoints, float maxSpeed, Vector3? lookAt = null)
        {
            EnsureInitialized();
            _hasManualDestination = false;
            _hasManualWaypoints = waypoints != null && waypoints.Count > 0;
            _manualMaxSpeed = Mathf.Max(0f, maxSpeed);
            _manualLookAt = lookAt;

            _corePath.Clear();
            _unityPath.Clear();
            if (_hasManualWaypoints)
            {
                _unityPath.Add(transform.position);
                _corePath.Add(transform.position.ToCore());
                for (int i = 0; i < waypoints.Count; i++)
                {
                    _unityPath.Add(waypoints[i]);
                    _corePath.Add(waypoints[i].ToCore());
                }
            }
        }

        /// <summary>Clears any manual destination/waypoints so the agent decelerates cleanly to a stop.</summary>
        public void Stop()
        {
            _hasManualDestination = false;
            _hasManualWaypoints = false;
            _manualMaxSpeed = 0f;
            _manualLookAt = null;
            ClearPath();
        }

        /// <summary>Teleports the agent to a new position and optional heading without interpolating through space.</summary>
        public void Warp(Vector3 worldPosition, float? yawDegrees = null)
        {
            EnsureInitialized();
            transform.position = worldPosition;
            if (yawDegrees.HasValue)
            {
                transform.rotation = Quaternion.Euler(0f, yawDegrees.Value, 0f);
                Solver.Heading = yawDegrees.Value;
            }

            Solver.Position = worldPosition.ToCore();
            if (_navMeshAgent != null && _navMeshAgent.isOnNavMesh)
            {
                _navMeshAgent.Warp(worldPosition);
            }

            ClearPath();
        }

        public MotionSettings BuildMotionSettings() => new MotionSettings
        {
            Acceleration = Mathf.Max(0.01f, acceleration),
            Deceleration = Mathf.Max(0.01f, deceleration),
            TurnSpeed = Mathf.Max(1f, turnSpeed),
            TurnInPlaceAngle = Mathf.Clamp(turnInPlaceAngle, 1f, 180f),
            TurnInPlaceSpeed = Mathf.Max(1f, turnInPlaceSpeed),
            Lookahead = Mathf.Max(0.1f, lookahead),
            StoppingDistance = Mathf.Max(0.05f, stoppingDistance),
            CornerSlowdown = Mathf.Clamp01(cornerSlowdown),
        };

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            EnsureInitialized();
            Solver.Settings = BuildMotionSettings();
            Solver.Position = transform.position.ToCore();

            bool hasDestination = false;
            Vector3 destination = default;
            float maxSpeed = 0f;
            CoreVec3? lookAt = null;

            if (Brain != null)
            {
                MovementIntent intent = Brain.Intent;
                hasDestination = intent.HasTarget;
                destination = intent.Target.ToUnity();
                maxSpeed = Mathf.Max(0f, intent.Speed);
                if (intent.HasLookAt) lookAt = intent.LookAt;
            }
            else if (_hasManualDestination)
            {
                hasDestination = true;
                destination = _manualDestination;
                maxSpeed = _manualMaxSpeed;
                if (_manualLookAt.HasValue) lookAt = _manualLookAt.Value.ToCore();
            }
            else if (_hasManualWaypoints)
            {
                maxSpeed = _manualMaxSpeed;
                if (_manualLookAt.HasValue) lookAt = _manualLookAt.Value.ToCore();
            }

            if (Brain != null || !_hasManualWaypoints)
            {
                if (hasDestination)
                {
                    RefreshPathIfNeeded(destination);
                }
                else
                {
                    ClearPath();
                }
            }

            Solver.Step(_corePath, maxSpeed, lookAt, dt);

            Vector3 nextPosition = Solver.Position.ToUnity();
            nextPosition.y = ResolveGroundHeight(nextPosition, transform.position.y);

            transform.position = nextPosition;
            transform.rotation = Quaternion.Euler(0f, Solver.Heading, 0f);
            Solver.Position = nextPosition.ToCore();

            if (syncAttachedNavMeshAgent && _navMeshAgent != null && _navMeshAgent.enabled && _navMeshAgent.isOnNavMesh)
            {
                _navMeshAgent.nextPosition = nextPosition;
            }

            UpdateAnimatorParameters();
        }

        private void EnsureInitialized()
        {
            if (Solver == null)
            {
                Solver = new MotionSolver(BuildMotionSettings(), transform.position.ToCore(), transform.eulerAngles.y);
            }

            if (_navMeshPath == null)
            {
                _navMeshPath = new NavMeshPath();
            }

            if (_navMeshAgent == null)
            {
                _navMeshAgent = GetComponent<NavMeshAgent>();
                if (_navMeshAgent != null && syncAttachedNavMeshAgent)
                {
                    _navMeshAgent.updatePosition = false;
                    _navMeshAgent.updateRotation = false;
                }
            }

            ResolveAnimatorIfNeeded();
        }

        private void RefreshPathIfNeeded(Vector3 target)
        {
            bool targetMoved = !_hasLastPathTarget ||
                               (target - _lastPathTarget).sqrMagnitude >= repathTargetThreshold * repathTargetThreshold;
            bool timerExpired = Time.time >= _nextRepathTime;

            if (!targetMoved && !timerExpired && _corePath.Count >= 2) return;

            _lastPathTarget = target;
            _hasLastPathTarget = true;
            _nextRepathTime = Time.time + Mathf.Max(0.05f, repathInterval);

            _corePath.Clear();
            _unityPath.Clear();

            Vector3 origin = transform.position;
            bool usedNavMesh = false;

            if (useNavMesh &&
                NavMesh.SamplePosition(origin, out NavMeshHit startHit, navMeshSampleRadius, navMeshAreaMask) &&
                NavMesh.SamplePosition(target, out NavMeshHit endHit, navMeshSampleRadius, navMeshAreaMask) &&
                NavMesh.CalculatePath(startHit.position, endHit.position, navMeshAreaMask, _navMeshPath) &&
                _navMeshPath.status != NavMeshPathStatus.PathInvalid &&
                _navMeshPath.corners.Length >= 2)
            {
                Vector3[] corners = _navMeshPath.corners;
                for (int i = 0; i < corners.Length; i++)
                {
                    _unityPath.Add(corners[i]);
                    _corePath.Add(corners[i].ToCore());
                }
                usedNavMesh = true;
            }

            if (!usedNavMesh)
            {
                _unityPath.Add(origin);
                _unityPath.Add(target);
                _corePath.Add(origin.ToCore());
                _corePath.Add(target.ToCore());
            }
        }

        private float ResolveGroundHeight(Vector3 position, float fallbackY)
        {
            if (useNavMesh && NavMesh.SamplePosition(position, out NavMeshHit hit, navMeshSampleRadius, navMeshAreaMask))
            {
                return hit.position.y;
            }
            return fallbackY;
        }

        private void ClearPath()
        {
            _corePath.Clear();
            _unityPath.Clear();
            _hasLastPathTarget = false;
        }

        private void ResolveAnimatorIfNeeded()
        {
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }
        }

        private void UpdateAnimatorParameters()
        {
            Animator anim = Animator;
            if (anim == null || anim.runtimeAnimatorController == null) return;

            if (anim != _cachedAnimator)
            {
                _cachedAnimator = anim;
                _animatorParameters.Clear();
                foreach (AnimatorControllerParameter p in anim.parameters)
                {
                    _animatorParameters.Add(p.nameHash);
                }
            }

            if (_animatorParameters.Contains(SpeedParam)) anim.SetFloat(SpeedParam, Solver.Speed);
            if (_animatorParameters.Contains(AngularVelocityParam)) anim.SetFloat(AngularVelocityParam, Solver.AngularVelocity);
            if (_animatorParameters.Contains(TurningInPlaceParam)) anim.SetBool(TurningInPlaceParam, Solver.TurningInPlace);
        }
    }
}
