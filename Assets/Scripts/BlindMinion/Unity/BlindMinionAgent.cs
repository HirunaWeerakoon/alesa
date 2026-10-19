using UnityEngine;
using UnityEngine.AI;

namespace Alesa.BlindMinion
{
    /// <summary>
    /// Puts a <see cref="BlindMinionBrain"/> in the scene: feeds it the minion's position, and walks
    /// the NavMeshAgent to whatever the brain wants to investigate.
    /// Needs a baked NavMesh under the minion.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class BlindMinionAgent : MonoBehaviour
    {
        [Header("Hearing")]
        [Tooltip("Quietest perceived loudness it reacts to.")]
        [SerializeField] private float hearingThreshold = 5f;

        [Header("Behaviour")]
        [SerializeField] private float reactionTime = 0.6f;
        [SerializeField] private float searchDuration = 4f;
        [SerializeField] private float investigateSpeed = 1.8f;

        private NavMeshAgent _nav;
        private Vector3 _lastDestination = new Vector3(float.NaN, 0f, 0f);

        public BlindMinionBrain Brain { get; private set; }

        private void Awake()
        {
            _nav = GetComponent<NavMeshAgent>();
            CreateBrain();
        }

        private void CreateBrain()
        {
            Brain = new BlindMinionBrain(NoiseSystem.Bus)
            {
                HearingThreshold = hearingThreshold,
                ReactionTime = reactionTime,
                SearchDuration = searchDuration,
                InvestigateSpeed = investigateSpeed,
                CurrentPosition = ToNumerics(transform.position),
            };
        }

        private void Update()
        {
            if (Brain == null) CreateBrain(); // scripts recompiled during Play wipe plain C# objects

            Brain.CurrentPosition = ToNumerics(transform.position);
            Brain.Tick(Time.deltaTime);

            if (!_nav.isOnNavMesh) return;
            if (Brain.WantsToMove)
            {
                var target = ToUnity(Brain.NoisePosition);
                _nav.speed = Brain.DesiredSpeed;
                _nav.isStopped = false;
                if (float.IsNaN(_lastDestination.x) || (target - _lastDestination).sqrMagnitude > 0.25f)
                {
                    _nav.SetDestination(target);
                    _lastDestination = target;
                }
            }
            else
            {
                _nav.isStopped = true;
                _lastDestination = new Vector3(float.NaN, 0f, 0f);
            }
        }

        private void OnDestroy() => Brain?.Dispose();

        private void OnDrawGizmos()
        {
            if (Brain == null) return;
            Gizmos.color = StateColour(Brain.State);
            Gizmos.DrawSphere(transform.position + Vector3.up * 2.2f, 0.2f);
            if (Brain.State == MinionState.Dormant) return;
            Gizmos.DrawLine(transform.position, ToUnity(Brain.NoisePosition));
            Gizmos.DrawWireCube(ToUnity(Brain.NoisePosition), Vector3.one * 0.4f);
        }

        public static Color StateColour(MinionState state)
        {
            switch (state)
            {
                case MinionState.Alerted: return new Color(1f, 0.6f, 0f);
                case MinionState.Investigating: return Color.yellow;
                case MinionState.Searching: return new Color(0.6f, 0.4f, 1f);
                default: return Color.grey;
            }
        }

        private static System.Numerics.Vector3 ToNumerics(Vector3 v) => new System.Numerics.Vector3(v.x, v.y, v.z);
        private static Vector3 ToUnity(System.Numerics.Vector3 v) => new Vector3(v.X, v.Y, v.Z);
    }
}
