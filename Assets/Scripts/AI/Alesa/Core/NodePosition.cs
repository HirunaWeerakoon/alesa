using System;

namespace Alesa.Core
{
    /// <summary>
    /// Pure C# 3D spatial coordinate representation decoupled from UnityEngine.Vector3.
    /// Used for node positions, Euclidean distance calculations, and turn geometry.
    /// </summary>
    public readonly struct NodePosition : IEquatable<NodePosition>
    {
        public float X { get; }
        public float Y { get; }
        public float Z { get; }

        public NodePosition(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static NodePosition Zero => new NodePosition(0f, 0f, 0f);

        /// <summary>
        /// Calculates full 3D Euclidean straight-line distance between two coordinates.
        /// </summary>
        public float DistanceTo(NodePosition other)
        {
            float dx = X - other.X;
            float dy = Y - other.Y;
            float dz = Z - other.Z;
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>
        /// Calculates 2D Euclidean distance on the X-Z ground plane (standard for Unity ground navigation).
        /// </summary>
        public float FlatDistanceTo(NodePosition other)
        {
            float dx = X - other.X;
            float dz = Z - other.Z;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        public bool Equals(NodePosition other)
        {
            const float epsilon = 1e-5f;
            return Math.Abs(X - other.X) < epsilon &&
                   Math.Abs(Y - other.Y) < epsilon &&
                   Math.Abs(Z - other.Z) < epsilon;
        }

        public override bool Equals(object obj) => obj is NodePosition other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + X.GetHashCode();
                hash = hash * 31 + Y.GetHashCode();
                hash = hash * 31 + Z.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(NodePosition left, NodePosition right) => left.Equals(right);
        public static bool operator !=(NodePosition left, NodePosition right) => !left.Equals(right);

        public override string ToString() => $"({X:F2}, {Y:F2}, {Z:F2})";
    }
}

