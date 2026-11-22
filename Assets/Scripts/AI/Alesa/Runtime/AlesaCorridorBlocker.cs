using UnityEngine;
using UnityEngine.AI;

namespace Alesa.Runtime
{
    /// <summary>
    /// Connects a physical scene obstacle (door, debris, Boss ambush marker) to the
    /// Alesa waypoint graph and Unity NavMesh.
    /// When activated, it blocks the graph edge and carves the NavMesh so agents physically detour.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshObstacle))]
    public class AlesaCorridorBlocker : MonoBehaviour
    {
        [Header("Graph Corridor Connection")]
        [Tooltip("The navigation manager in the scene (auto-discovered if null).")]
        [SerializeField] private AlesaNavigator navigator;

        [Tooltip("First waypoint node of the corridor to block.")]
        public AlesaWaypoint nodeA;

        [Tooltip("Second waypoint node of the corridor to block.")]
        public AlesaWaypoint nodeB;

        [Tooltip("Whether to block travel in both directions (standard for hallways).")]
        public bool bidirectional = true;

        [Tooltip("Descriptive label explaining why this corridor is blocked.")]
        public string blockReason = "Debris / Supernatural Obstacle";

        [Header("State")]
        [Tooltip("Current blockage status. Toggle to open or close the corridor.")]
        [SerializeField] private bool isBlocked = false;

        [Header("Visual Representation")]
        [Tooltip("Optional child mesh or visual renderer. Enabled when blocked.")]
        [SerializeField] private GameObject visualModel;

        private NavMeshObstacle _obstacle;

        public bool IsBlocked => isBlocked;
        public string FromNodeId => nodeA != null ? nodeA.Id : string.Empty;
        public string ToNodeId => nodeB != null ? nodeB.Id : string.Empty;

        private void Awake()
        {
            _obstacle = GetComponent<NavMeshObstacle>();
            if (_obstacle != null)
            {
                _obstacle.carving = true;
            }

            if (navigator == null)
            {
                navigator = FindFirstObjectByType<AlesaNavigator>();
            }

            ApplyBlockState(isBlocked, forceUpdate: true);
        }

        private void OnEnable()
        {
            if (navigator == null)
            {
                navigator = FindFirstObjectByType<AlesaNavigator>();
            }
            ApplyBlockState(isBlocked, forceUpdate: true);
        }

        private void OnValidate()
        {
            if (_obstacle == null) _obstacle = GetComponent<NavMeshObstacle>();
            if (_obstacle != null)
            {
                _obstacle.carving = true;
            }

            ApplyBlockState(isBlocked, forceUpdate: false);
        }

        /// <summary>
        /// Activates or deactivates the corridor blockage across the graph, NavMesh, and visual models.
        /// </summary>
        public void SetBlocked(bool blocked)
        {
            if (isBlocked == blocked) return;

            isBlocked = blocked;
            ApplyBlockState(isBlocked, forceUpdate: true);
        }

        [ContextMenu("Toggle Blocker (Open / Closed)")]
        public void Toggle()
        {
            SetBlocked(!isBlocked);
        }

        private void ApplyBlockState(bool blocked, bool forceUpdate)
        {
            // 1. Update physical NavMeshObstacle carving
            if (_obstacle == null) _obstacle = GetComponent<NavMeshObstacle>();
            if (_obstacle != null)
            {
                _obstacle.enabled = blocked;
                _obstacle.carving = true;
            }

            // 2. Update visual model
            if (visualModel != null)
            {
                visualModel.SetActive(blocked);
            }
            else
            {
                // Fallback: if no separate child model, toggle own MeshRenderer if present
                var renderer = GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.enabled = blocked;
                }
            }

            // 3. Update Waypoint Graph via AlesaNavigator
            if (navigator != null && nodeA != null && nodeB != null)
            {
                navigator.SetCorridorBlocked(nodeA.Id, nodeB.Id, blocked, blockReason, bidirectional);
            }
        }

        private void OnDrawGizmos()
        {
            if (nodeA == null || nodeB == null) return;

            Vector3 pA = nodeA.transform.position;
            Vector3 pB = nodeB.transform.position;
            Vector3 mid = (pA + pB) * 0.5f + Vector3.up * 1f;

            if (isBlocked)
            {
                Gizmos.color = new Color(1f, 0.1f, 0.1f, 0.9f);
                Gizmos.DrawWireCube(mid, new Vector3(2.5f, 2.5f, 2.5f));
                Gizmos.DrawLine(pA + Vector3.up * 0.5f, pB + Vector3.up * 0.5f);
            }
        }
    }
}
