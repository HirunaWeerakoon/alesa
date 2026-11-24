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

        /// <summary>Distance kept inside a NavMesh boundary edge, so the next raycast does not flicker on it.</summary>
        private const float NavMeshEdgeInset = 0.05f;

        /// <summary>Snap radius used for grounding. Must stay below the thinnest wall in the level.</summary>
        private const float NavMeshSnapRadius = 0.25f;

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

        [Header("Separation")]
        [Tooltip("Centre-to-centre distance (m) at which two agents start pushing each other apart. Bodies are 0.7 m across, so this needs to exceed that by the gap you want to see between them. 0 disables it.")]
        [SerializeField] private float separationRadius = 1.4f;

        [Tooltip("How hard the push is, in metres per second at full overlap.")]
        [SerializeField] private float separationStrength = 2.5f;

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

        /// <summary>Every enabled motor, so agents can push off each other without a physics query.</summary>
        private static readonly List<AgentMotor> ActiveMotors = new List<AgentMotor>();

        private void Awake()
        {
            EnsureInitialized();
        }

        private void OnEnable() => ActiveMotors.Add(this);

        private void OnDisable() => ActiveMotors.Remove(this);

        /// <summary>
        /// Pushes this agent out of any other agent it is standing inside.
        /// </summary>
        /// <remarks>
        /// The NavMeshAgent's own avoidance cannot do this here. Avoidance works by steering
        /// <c>NavMeshAgent.nextPosition</c>, and this motor sets <c>updatePosition = false</c> and
        /// writes the transform itself, so the avoidance result is computed and then thrown away —
        /// which is why the agents' HighQualityObstacleAvoidance setting had no effect and two
        /// minions walking to the same place merged into one silhouette.
        ///
        /// This is a plain separation force instead: cheap, order-independent, and enough to keep
        /// three abreast in a three-metre corridor. It is clamped per frame so a crowd cannot
        /// catapult anyone, and the result still goes through <see cref="ConstrainToNavMesh"/>, so
        /// being shoved can never push an agent through a wall.
        /// </remarks>
        private Vector3 Separate(Vector3 position, float dt)
        {
            if (separationRadius <= 0f || separationStrength <= 0f || ActiveMotors.Count < 2) return position;

            Vector3 push = Vector3.zero;
            for (int i = 0; i < ActiveMotors.Count; i++)
            {
                AgentMotor other = ActiveMotors[i];
                if (other == null || other == this) continue;

                Vector3 away = position - other.transform.position;
                away.y = 0f;
                float distance = away.magnitude;
                if (distance >= separationRadius) continue;

                // Two agents spawned on the exact same spot have no direction to separate along;
                // break the tie deterministically by instance id so they do not both pick the same way.
                if (distance < 0.001f)
                {
                    float angle = (GetInstanceID() % 360) * Mathf.Deg2Rad;
                    away = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    distance = 0.001f;
                }

                push += (away / distance) * (1f - distance / separationRadius);
            }

            if (push.sqrMagnitude < 1e-6f) return position;

            float step = Mathf.Min(separationStrength * dt, separationRadius * 0.5f);
            return position + Vector3.ClampMagnitude(push, 1f) * step;
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

            Vector3 nextPosition = Separate(Solver.Position.ToUnity(), dt);
            nextPosition = ConstrainToNavMesh(transform.position, nextPosition);

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

        /// <summary>
        /// Keeps the step from <paramref name="from"/> to <paramref name="to"/> on the walkable
        /// surface, and returns the ground height there.
        /// </summary>
        /// <remarks>
        /// <see cref="MotionSolver"/> steers toward a point <c>Lookahead</c> metres along the path
        /// rather than to the next corner, so it deliberately cuts corners. In a two-metre hospital
        /// corridor a cut corner puts the body inside the wall, and nothing else pulls it back:
        /// an attached <see cref="NavMeshAgent"/> has <c>updatePosition</c> off, so its own
        /// containment never runs.
        ///
        /// <see cref="NavMesh.SamplePosition"/> alone cannot fix that. It finds the nearest point on
        /// the mesh within a radius and ignores walls entirely, so from inside a partition it is just
        /// as likely to answer with the room on the far side. <see cref="NavMesh.Raycast"/> is the
        /// only query that reports crossing a boundary edge, so the step is clipped at the edge and
        /// then allowed to slide along it — the agent hugs the corner instead of stopping dead.
        /// </remarks>
        private Vector3 ConstrainToNavMesh(Vector3 from, Vector3 to)
        {
            if (!useNavMesh)
            {
                to.y = from.y;
                return to;
            }

            if (NavMesh.Raycast(from, to, out NavMeshHit wall, navMeshAreaMask))
            {
                Vector3 normal = wall.normal;
                normal.y = 0f;
                normal = normal.sqrMagnitude > 1e-6f ? normal.normalized : (from - to).normalized;

                // Slide the blocked remainder along the wall rather than discarding it.
                Vector3 slide = Vector3.ProjectOnPlane(to - wall.position, normal);
                Vector3 slid = wall.position + slide + normal * NavMeshEdgeInset;
                to = NavMesh.Raycast(wall.position, slid, out NavMeshHit corner, navMeshAreaMask)
                    ? corner.position + normal * NavMeshEdgeInset
                    : slid;
            }

            // A tight radius here is deliberate: a wide one would happily snap to the far side of a
            // thin wall and undo the clipping above.
            if (NavMesh.SamplePosition(to, out NavMeshHit surface, NavMeshSnapRadius, navMeshAreaMask))
            {
                return surface.position;
            }

            // Off the mesh entirely (spawned badly, or the mesh was rebaked under us): fall back to
            // the configured search radius so the agent recovers instead of freezing forever.
            if (NavMesh.SamplePosition(to, out NavMeshHit recovered, navMeshSampleRadius, navMeshAreaMask))
            {
                return recovered.position;
            }

            to.y = from.y;
            return to;
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
