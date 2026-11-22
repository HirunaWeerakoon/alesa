using System;

namespace Alesa.Core
{
    public enum TurnDirection
    {
        None = 0,
        Straight = 1,
        Left = 2,
        Right = 3,
        UTurn = 4
    }

    /// <summary>
    /// Pure C# geometric direction classification engine.
    /// Calculates signed horizontal angles between incoming approach vectors and
    /// outgoing route corridors around the vertical axis to classify turns as
    /// Left, Right, Straight, or U-Turn.
    /// </summary>
    public sealed class DirectionClassifier
    {
        public float StraightThresholdAngle { get; set; } = 30.0f;
        public float UTurnThresholdAngle { get; set; } = 150.0f;

        public DirectionClassifier(float straightThreshold = 30.0f, float uTurnThreshold = 150.0f)
        {
            StraightThresholdAngle = Math.Clamp(straightThreshold, 5.0f, 60.0f);
            UTurnThresholdAngle = Math.Clamp(uTurnThreshold, 120.0f, 175.0f);
        }

        /// <summary>
        /// Calculates the signed horizontal angle in degrees from vector A to vector B on the X-Z plane.
        /// Conforms to Unity's standard coordinate convention (around Vector3.up):
        /// - Positive angle (0° to +180°): Turning RIGHT (clockwise).
        /// - Negative angle (0° to -180°): Turning LEFT (counter-clockwise).
        /// </summary>
        public static float CalculateSignedAngle2D(float fromX, float fromZ, float toX, float toZ)
        {
            float magFrom = (float)Math.Sqrt(fromX * fromX + fromZ * fromZ);
            float magTo = (float)Math.Sqrt(toX * toX + toZ * toZ);

            if (magFrom < 1e-5f || magTo < 1e-5f)
            {
                return 0f;
            }

            fromX /= magFrom;
            fromZ /= magFrom;
            toX /= magTo;
            toZ /= magTo;

            // Dot product: cos(theta)
            float dot = fromX * toX + fromZ * toZ;
            dot = Math.Clamp(dot, -1f, 1f);

            // 2D determinant (Y-component of 3D cross product): sin(theta) around +Y axis
            // In Unity coordinates where X is East and Z is North,
            // cross(from, to).y = from.z * to.x - from.x * to.z
            float crossY = fromZ * toX - fromX * toZ;

            // atan2(sin, cos) yields signed angle in radians [-pi, +pi]
            double angleRad = Math.Atan2(crossY, dot);
            return (float)(angleRad * (180.0 / Math.PI));
        }

        public static float CalculateSignedAngle2D(NodePosition fromDir, NodePosition toDir)
        {
            return CalculateSignedAngle2D(fromDir.X, fromDir.Z, toDir.X, toDir.Z);
        }

        /// <summary>
        /// Classifies a signed angle in degrees into a discrete turn direction.
        /// </summary>
        public TurnDirection ClassifyAngle(float signedAngleDegrees)
        {
            float absAngle = Math.Abs(signedAngleDegrees);

            if (absAngle <= StraightThresholdAngle)
            {
                return TurnDirection.Straight;
            }

            if (absAngle >= UTurnThresholdAngle)
            {
                return TurnDirection.UTurn;
            }

            // In Unity coordinates around Vector3.up:
            // Positive angle = turning RIGHT
            // Negative angle = turning LEFT
            return signedAngleDegrees > 0f ? TurnDirection.Right : TurnDirection.Left;
        }

        /// <summary>
        /// Classifies turn direction at a junction using route geometry:
        /// Incoming vector: Node(junctionIndex - 1) -> Node(junctionIndex)
        /// Outgoing vector: Node(junctionIndex) -> Node(junctionIndex + 1)
        /// </summary>
        public TurnDirection ClassifyRouteTurn(
            WaypointGraph graph,
            RouteResult route,
            int junctionIndex,
            out float signedAngle)
        {
            signedAngle = 0f;

            if (graph == null || route == null || !route.IsValid)
                return TurnDirection.None;

            if (junctionIndex <= 0 || junctionIndex >= route.NodeCount - 1)
                return TurnDirection.None;

            string prevNodeId = route.NodeIds[junctionIndex - 1];
            string juncNodeId = route.NodeIds[junctionIndex];
            string nextNodeId = route.NodeIds[junctionIndex + 1];

            if (!graph.TryGetNode(prevNodeId, out var prevNode) ||
                !graph.TryGetNode(juncNodeId, out var juncNode) ||
                !graph.TryGetNode(nextNodeId, out var nextNode))
            {
                return TurnDirection.None;
            }

            return ClassifyTurnFromWaypoints(prevNode.Position, juncNode.Position, nextNode.Position, out signedAngle);
        }

        /// <summary>
        /// Classifies turn direction from three 3D waypoint positions (incoming -> junction -> outgoing).
        /// </summary>
        public TurnDirection ClassifyTurnFromWaypoints(
            NodePosition incomingPos,
            NodePosition junctionPos,
            NodePosition outgoingPos,
            out float signedAngle)
        {
            float inDx = junctionPos.X - incomingPos.X;
            float inDz = junctionPos.Z - incomingPos.Z;

            float outDx = outgoingPos.X - junctionPos.X;
            float outDz = outgoingPos.Z - junctionPos.Z;

            signedAngle = CalculateSignedAngle2D(inDx, inDz, outDx, outDz);
            return ClassifyAngle(signedAngle);
        }

        /// <summary>
        /// Classifies turn direction relative to Nathan's physical approach vector to the junction.
        /// Useful if Nathan approaches from an off-path position.
        /// </summary>
        public TurnDirection ClassifyTurnFromPlayerApproach(
            NodePosition playerPos,
            NodePosition junctionPos,
            NodePosition outgoingPos,
            out float signedAngle)
        {
            float inDx = junctionPos.X - playerPos.X;
            float inDz = junctionPos.Z - playerPos.Z;

            float outDx = outgoingPos.X - junctionPos.X;
            float outDz = outgoingPos.Z - junctionPos.Z;

            signedAngle = CalculateSignedAngle2D(inDx, inDz, outDx, outDz);
            return ClassifyAngle(signedAngle);
        }
    }
}
