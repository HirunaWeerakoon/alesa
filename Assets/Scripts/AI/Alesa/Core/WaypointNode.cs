using System;

namespace Alesa.Core
{
    /// <summary>
    /// Represents a discrete waypoint or intersection point in the hospital navigation graph.
    /// </summary>
    public sealed class WaypointNode
    {
        public string Id { get; }
        public NodePosition Position { get; }
        public bool IsJunction { get; }
        public string Label { get; }

        public WaypointNode(string id, NodePosition position, bool isJunction = false, string label = null)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Waypoint node ID cannot be null or whitespace.", nameof(id));

            Id = id.Trim();
            Position = position;
            IsJunction = isJunction;
            Label = label ?? Id;
        }

        public override string ToString() => $"Node[{Id}] @ {Position}{(IsJunction ? " (Junction)" : "")}";
    }
}

