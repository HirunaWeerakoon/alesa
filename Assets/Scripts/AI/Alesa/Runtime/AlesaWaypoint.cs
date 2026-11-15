using System;
using System.Collections.Generic;
using UnityEngine;
using Alesa.Core;

namespace Alesa.Runtime
{
    [Serializable]
    public class WaypointConnection
    {
        [Tooltip("Target neighbor waypoint to connect to.")]
        public AlesaWaypoint targetWaypoint;

        [Tooltip("Traversal cost override. Set to -1 to automatically use 3D Euclidean distance.")]
        public float costOverride = -1f;

        [Tooltip("Additional risk or hazard penalty (>= 0).")]
        [Min(0f)]
        public float riskPenalty = 0f;

        [Tooltip("If true, a matching reverse edge will automatically be created.")]
        public bool bidirectional = true;

        [Tooltip("If true, this edge will be registered as blocked (e.g. closed door or debris).")]
        public bool isBlocked = false;

        [Tooltip("Optional reason for blockage (e.g. 'Debris', 'Locked Door').")]
        public string blockReason = string.Empty;
    }

    /// <summary>
    /// Scene-authored waypoint component. Represents a discrete node in the hospital maze.
    /// Level designers can place these in Unity and wire connections in the Inspector.
    /// </summary>
    [SelectionBase]
    [DisallowMultipleComponent]
    public class AlesaWaypoint : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("Unique ID for graph search. If empty, uses GameObject name.")]
        [SerializeField] private string waypointId = string.Empty;

        [Tooltip("Marks this waypoint as a corridor intersection (candidate for voice guidance).")]
        public bool isJunction = false;

        [Header("Corridor Connections")]
        [Tooltip("List of connected neighbor waypoints.")]
        public List<WaypointConnection> connections = new List<WaypointConnection>();

        public string Id
        {
            get
            {
                if (string.IsNullOrWhiteSpace(waypointId))
                    return gameObject.name.Trim();
                return waypointId.Trim();
            }
        }

        public NodePosition Position => transform.position.ToNodePosition();

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(waypointId))
            {
                waypointId = gameObject.name.Trim();
            }
        }

        public void AddConnection(AlesaWaypoint target, float costOverride = -1f, bool bidirectional = true, float riskPenalty = 0f)
        {
            if (target == null || target == this) return;

            // Avoid duplicate Inspector connections
            for (int i = 0; i < connections.Count; i++)
            {
                if (connections[i].targetWaypoint == target)
                {
                    connections[i].costOverride = costOverride;
                    connections[i].bidirectional = bidirectional;
                    connections[i].riskPenalty = riskPenalty;
                    return;
                }
            }

            connections.Add(new WaypointConnection
            {
                targetWaypoint = target,
                costOverride = costOverride,
                bidirectional = bidirectional,
                riskPenalty = riskPenalty
            });
        }
    }
}

