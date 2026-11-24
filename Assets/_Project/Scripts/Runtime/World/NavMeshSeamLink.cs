using UnityEngine;
using UnityEngine.AI;

namespace Alesa.Runtime.World
{
    /// <summary>
    /// Registers a runtime <see cref="NavMeshLinkData"/> bridge via <see cref="NavMesh.AddLink"/>
    /// without requiring the external <c>com.unity.ai.navigation</c> package.
    /// Mirrors <see cref="HospitalNavMeshLoader"/> and <c>FinalBossNavigation</c> so adjacent
    /// baked <see cref="NavMeshData"/> assets connect seamlessly in Editor and Play Mode.
    /// </summary>
    [ExecuteAlways]
    public sealed class NavMeshSeamLink : MonoBehaviour
    {
        [Tooltip("Link start point in local space relative to this Transform.")]
        [SerializeField] private Vector3 startPoint = new Vector3(0f, 0f, -3.25f);

        [Tooltip("Link end point in local space relative to this Transform.")]
        [SerializeField] private Vector3 endPoint = new Vector3(0f, 0f, 3.25f);

        [SerializeField, Min(0f)] private float width = 2.5f;
        [SerializeField] private bool bidirectional = true;
        [SerializeField] private int area;
        [SerializeField] private int agentTypeID;
        [SerializeField] private float costModifier = -1f;

        private NavMeshLinkInstance _linkInstance;

        public Vector3 WorldStart => transform.TransformPoint(startPoint);
        public Vector3 WorldEnd => transform.TransformPoint(endPoint);
        public float Width => width;
        public bool Bidirectional => bidirectional;
        public bool IsActive => _linkInstance.valid;

        private void OnEnable()
        {
            RegisterLink();
        }

        private void OnDisable()
        {
            UnregisterLink();
        }

        private void OnDestroy()
        {
            UnregisterLink();
        }

        private void OnValidate()
        {
            if (isActiveAndEnabled)
            {
                RefreshLink();
            }
        }

        public void ConfigureWorldEndpoints(Vector3 worldStart, Vector3 worldEnd, float linkWidth, bool isBidirectional = true)
        {
            Vector3 midpoint = (worldStart + worldEnd) * 0.5f;
            transform.position = midpoint;
            transform.rotation = Quaternion.identity;
            startPoint = transform.InverseTransformPoint(worldStart);
            endPoint = transform.InverseTransformPoint(worldEnd);
            width = Mathf.Max(0f, linkWidth);
            bidirectional = isBidirectional;
            RefreshLink();
        }

        public void RefreshLink()
        {
            UnregisterLink();
            if (isActiveAndEnabled)
            {
                RegisterLink();
            }
        }

        private void RegisterLink()
        {
            if (_linkInstance.valid || !gameObject.scene.IsValid()) return;

            var data = new NavMeshLinkData
            {
                startPosition = startPoint,
                endPosition = endPoint,
                width = Mathf.Max(0f, width),
                costModifier = costModifier,
                bidirectional = bidirectional,
                area = area,
                agentTypeID = agentTypeID,
            };

            _linkInstance = NavMesh.AddLink(data, transform.position, transform.rotation);
        }

        private void UnregisterLink()
        {
            if (_linkInstance.valid)
            {
                _linkInstance.Remove();
                _linkInstance = default;
            }
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 a = WorldStart;
            Vector3 b = WorldEnd;
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(a, b);
            Gizmos.DrawWireSphere(a, 0.25f);
            Gizmos.DrawWireSphere(b, 0.25f);
        }
    }
}
